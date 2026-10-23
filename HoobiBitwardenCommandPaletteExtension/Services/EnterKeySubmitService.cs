using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace HoobiBitwardenCommandPaletteExtension.Services;

// Workaround for https://github.com/microsoft/PowerToys/issues/46003: PowerToys 0.101+ submits Enter
// in a single-line TextBox but not in a PasswordBox, so Enter in a password field invokes the submit button via UIA.
// If PowerToys runs elevated and this extension does not, UIPI blocks the hook and UIA, and Enter does nothing.
internal static unsafe partial class EnterKeySubmitService
{
  private static volatile string? _armedButtonName;
  private static nint _keyboardHook;
  private static uint _pumpThreadId;
  private static readonly ManualResetEventSlim _pumpReady = new(false);
  private static readonly Lock _startLock = new();
  private static bool _pumpStarted;
  private static long _lastInvokeTimestamp;
  private static uint _lastPid;
  private static bool _lastPidIsCmdPal;

  internal static void Arm(string submitButtonName)
  {
    _armedButtonName = submitButtonName;
    try
    {
      EnsurePumpThread();
      PostThreadMessageW(_pumpThreadId, WM_APP_INSTALL_HOOK, 0, 0);
    }
    catch (Exception ex)
    {
      DebugLogService.Log("EnterSubmit", $"Arm failed: {ex.GetType().Name}: {ex.Message}");
    }
  }

  internal static void Disarm()
  {
    _armedButtonName = null;
    if (_pumpStarted)
      PostThreadMessageW(_pumpThreadId, WM_APP_UNINSTALL_HOOK, 0, 0);
  }

  private static void EnsurePumpThread()
  {
    if (_pumpStarted)
      return;

    lock (_startLock)
    {
      if (_pumpStarted)
        return;

      var thread = new Thread(PumpProc) { IsBackground = true, Name = "EnterKeySubmitHook" };
      thread.Start();
      _pumpReady.Wait(TimeSpan.FromSeconds(5));
      _pumpStarted = true;
    }
  }

  private static void PumpProc()
  {
    _pumpThreadId = GetCurrentThreadId();
    PeekMessageW(out _, 0, WM_USER, WM_USER, PM_NOREMOVE); // creates the queue before other threads post to it
    _pumpReady.Set();

    while (GetMessageW(out var msg, 0, 0, 0) > 0)
    {
      switch (msg.message)
      {
        case WM_APP_INSTALL_HOOK:
          if (_keyboardHook == 0 && _armedButtonName != null)
          {
            _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, &HookProc, GetModuleHandleW(null), 0);
            DebugLogService.Log("EnterSubmit", _keyboardHook != 0 ? "Keyboard hook installed" : $"SetWindowsHookEx failed: {Marshal.GetLastPInvokeError()}");
          }
          break;
        case WM_APP_UNINSTALL_HOOK:
          RemoveKeyboardHook();
          break;
        default:
          TranslateMessage(in msg);
          DispatchMessageW(in msg);
          break;
      }
    }

    RemoveKeyboardHook();
  }

  private static void RemoveKeyboardHook()
  {
    if (_keyboardHook != 0)
    {
      UnhookWindowsHookEx(_keyboardHook);
      _keyboardHook = 0;
      DebugLogService.Log("EnterSubmit", "Keyboard hook removed");
    }
  }

  [UnmanagedCallersOnly]
  private static nint HookProc(int nCode, nuint wParam, nint lParam)
  {
    try
    {
      if (nCode >= 0 && wParam == WM_KEYDOWN)
      {
        var hwnd = GetForegroundWindow();
        if (!IsCommandPaletteWindow(hwnd))
        {
          _armedButtonName = null;
          PostThreadMessageW(_pumpThreadId, WM_APP_UNINSTALL_HOOK, 0, 0);
        }
        else if (_armedButtonName is string armed && *(uint*)lParam == VK_RETURN && TryClaimInvoke())
        {
          ThreadPool.QueueUserWorkItem(static state =>
          {
            var (h, name) = ((nint, string))state!;
            TryInvokeSubmit(h, name);
          }, (hwnd, armed));
        }
      }
    }
    catch { }
    return CallNextHookEx(0, nCode, wParam, lParam);
  }

  // Debounces our own invokes against key auto-repeat.
  private static bool TryClaimInvoke()
  {
    var now = Stopwatch.GetTimestamp();
    var last = Interlocked.Read(ref _lastInvokeTimestamp);
    if (last != 0 && Stopwatch.GetElapsedTime(last, now).TotalMilliseconds < 500)
      return false;
    return Interlocked.CompareExchange(ref _lastInvokeTimestamp, now, last) == last;
  }

  private static bool IsCommandPaletteWindow(nint hwnd)
  {
    if (hwnd == 0)
      return false;
    GetWindowThreadProcessId(hwnd, out var pid);
    if (pid == 0)
      return false;

    if (pid == _lastPid)
      return _lastPidIsCmdPal;

    try
    {
      using var process = Process.GetProcessById((int)pid);
      _lastPidIsCmdPal = process.ProcessName.Contains("CmdPal", StringComparison.OrdinalIgnoreCase);
      _lastPid = pid;
      return _lastPidIsCmdPal;
    }
    catch
    {
      return false;
    }
  }

  private static void TryInvokeSubmit(nint hwnd, string buttonName)
  {
    IUIAutomation? automation = null;
    IUIAutomationElement? focused = null;
    IUIAutomationElement? root = null;
    nint typeCondition = 0;
    nint nameCondition = 0;
    nint condition = 0;
    IUIAutomationElement? button = null;
    IUIAutomationInvokePattern? invoke = null;
    var nameBstr = Marshal.StringToBSTR(buttonName);
    try
    {
      automation = UiaInterop.CreateAutomation();
      if (automation == null)
        return;

      if (automation.GetFocusedElement(out var focusedPtr) != 0
          || (focused = UiaInterop.Wrap<IUIAutomationElement>(focusedPtr)) == null)
        return;

      if (!FocusedElementIsOurPasswordField(focused, hwnd))
        return;

      if (automation.ElementFromHandle(hwnd, out var rootPtr) != 0
          || (root = UiaInterop.Wrap<IUIAutomationElement>(rootPtr)) == null)
        return;

      if (automation.CreatePropertyCondition(UiaInterop.UIA_ControlTypePropertyId, UiaInterop.Variant.FromInt(UiaInterop.UIA_ButtonControlTypeId), out typeCondition) != 0 || typeCondition == 0)
        return;
      if (automation.CreatePropertyCondition(UiaInterop.UIA_NamePropertyId, UiaInterop.Variant.FromBstr(nameBstr), out nameCondition) != 0 || nameCondition == 0)
        return;
      if (automation.CreateAndCondition(typeCondition, nameCondition, out condition) != 0 || condition == 0)
        return;

      if (root.FindFirst(UiaInterop.TreeScope_Descendants, condition, out var buttonPtr) != 0
          || (button = UiaInterop.Wrap<IUIAutomationElement>(buttonPtr)) == null)
      {
        DebugLogService.Log("EnterSubmit", $"Submit button '{buttonName}' not found");
        return;
      }

      if (button.GetCurrentPattern(UiaInterop.UIA_InvokePatternId, out var patternPtr) != 0
          || (invoke = UiaInterop.Wrap<IUIAutomationInvokePattern>(patternPtr)) == null)
        return;

      invoke.Invoke();
      DebugLogService.Log("EnterSubmit", $"Invoked '{buttonName}' via Enter");
    }
    catch (Exception ex)
    {
      DebugLogService.Log("EnterSubmit", $"Invoke failed: {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
      UiaInterop.Release(invoke);
      UiaInterop.Release(button);
      UiaInterop.Release(condition);
      UiaInterop.Release(nameCondition);
      UiaInterop.Release(typeCondition);
      UiaInterop.Release(root);
      UiaInterop.Release(focused);
      UiaInterop.Release(automation);
      Marshal.FreeBSTR(nameBstr);
    }
  }

  private static bool FocusedElementIsOurPasswordField(IUIAutomationElement focused, nint cmdPalHwnd)
  {
    GetWindowThreadProcessId(cmdPalHwnd, out var cmdPalPid);
    return focused.get_CurrentProcessId(out var focusedPid) == 0
        && focusedPid == (int)cmdPalPid
        && focused.get_CurrentIsPassword(out var isPassword) == 0
        && isPassword != 0;
  }

  private const uint WM_USER = 0x0400;
  private const uint WM_APP_INSTALL_HOOK = 0x8001;
  private const uint WM_APP_UNINSTALL_HOOK = 0x8002;
  private const uint WM_KEYDOWN = 0x0100;
  private const uint VK_RETURN = 0x0D;
  private const int WH_KEYBOARD_LL = 13;
  private const uint PM_NOREMOVE = 0;

  [StructLayout(LayoutKind.Sequential)]
  private struct MSG
  {
    public nint hwnd;
    public uint message;
    public nuint wParam;
    public nint lParam;
    public uint time;
    public int ptX;
    public int ptY;
  }

  [LibraryImport("user32.dll")]
  private static partial nint GetForegroundWindow();

  [LibraryImport("user32.dll")]
  private static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

  [LibraryImport("user32.dll", SetLastError = true)]
  private static partial nint SetWindowsHookExW(int idHook, delegate* unmanaged<int, nuint, nint, nint> lpfn, nint hMod, uint dwThreadId);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool UnhookWindowsHookEx(nint hhk);

  [LibraryImport("user32.dll")]
  private static partial nint CallNextHookEx(nint hhk, int nCode, nuint wParam, nint lParam);

  [LibraryImport("user32.dll")]
  private static partial int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool PeekMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool TranslateMessage(in MSG lpMsg);

  [LibraryImport("user32.dll")]
  private static partial nint DispatchMessageW(in MSG lpMsg);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool PostThreadMessageW(uint idThread, uint msg, nuint wParam, nint lParam);

  [LibraryImport("kernel32.dll")]
  private static partial uint GetCurrentThreadId();

  [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
  private static partial nint GetModuleHandleW(string? lpModuleName);
}
