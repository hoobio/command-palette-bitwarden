using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace HoobiBitwardenCommandPaletteExtension.Services;

// Workaround for https://github.com/microsoft/PowerToys/issues/46003: the Command
// Palette host doesn't wire Enter to Action.Submit in rendered Adaptive Card forms.
// The extension runs out-of-process, so we can't touch the host's UI thread. Instead,
// while one of our forms is armed, a low-level keyboard hook watches for Enter; when
// pressed with a password box focused inside the Command Palette window, the form's
// submit button is invoked through UI Automation, which drives the host's normal
// SubmitForm pipeline.
//
// Privacy/safety constraints baked into the design:
// - The hook callback inspects the virtual-key code only, acts only on VK_RETURN, and
//   never records or forwards key data.
// - The hook is installed only while one of our forms armed it, and is removed as soon
//   as Command Palette loses foreground.
// - The UIA invoke only fires when the focused element is a password field belonging
//   to the Command Palette process, so a stale armed state cannot click anything else.
//
// Known limitation: if PowerToys runs elevated and this extension does not, UIPI blocks
// both the hook and UIA access, and Enter falls back to doing nothing (upstream status quo).
//
// Failsafe layering, so a global keyboard hook can never outlive a live form:
//   1. The OS uninstalls the hook automatically if this process dies.
//   2. The EVENT_SYSTEM_FOREGROUND WinEvent removes it the instant focus leaves
//      Command Palette (covers the palette closing or being killed).
//   3. A watchdog timer is the backstop for (2): out-of-context WinEvents can be
//      dropped under load, so every tick it removes the hook if it's still
//      installed while Command Palette is no longer the foreground window.
internal static unsafe partial class EnterKeySubmitService
{
  // Tests toggle this off so page GetContent calls don't install a real hook.
  internal static bool HookingEnabled { get; set; } = true;

  internal static string? ArmedButtonName => _armedButtonName;

  private static volatile string? _armedButtonName;
  private static nint _keyboardHook; // pump thread only
  private static uint _pumpThreadId;
  private static readonly ManualResetEventSlim _pumpReady = new(false);
  private static readonly Lock _startLock = new();
  private static bool _pumpStarted;
  private static long _lastInvokeTimestamp;
  private static readonly ConcurrentDictionary<uint, bool> _pidIsCmdPal = new();
  private static Timer? _watchdog;
  private const int WatchdogIntervalMs = 2000;

  internal static void Arm(string submitButtonName)
  {
    if (!HookingEnabled)
      return;

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
      _watchdog ??= new Timer(static _ => WatchdogTick(), null, WatchdogIntervalMs, WatchdogIntervalMs);
    }
  }

  // Backstop for a dropped foreground WinEvent (see class header): if the hook is
  // still installed but Command Palette isn't foreground anymore, tear it down.
  private static void WatchdogTick()
  {
    try
    {
      if (_keyboardHook == 0 || IsCommandPaletteWindow(GetForegroundWindow()))
        return;

      _armedButtonName = null;
      if (_pumpStarted)
        PostThreadMessageW(_pumpThreadId, WM_APP_UNINSTALL_HOOK, 0, 0);
    }
    catch
    {
      // Backstop must never throw on a timer thread.
    }
  }

  private static void PumpProc()
  {
    _pumpThreadId = GetCurrentThreadId();
    // Force message queue creation before other threads post to us.
    PeekMessageW(out _, 0, WM_USER, WM_USER, PM_NOREMOVE);
    _pumpReady.Set();

    // Foreground tracking so the keyboard hook never outlives a visible palette.
    var winEventHook = SetWinEventHook(
        EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
        0, &WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

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
    if (winEventHook != 0)
      UnhookWinEvent(winEventHook);
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
      if (nCode >= 0
          && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN)
          && _armedButtonName is string armed
          && *(uint*)lParam == VK_RETURN)
      {
        var hwnd = GetForegroundWindow();
        if (IsCommandPaletteWindow(hwnd) && TryClaimInvoke())
          ThreadPool.QueueUserWorkItem(static state =>
          {
            var (h, name) = ((nint, string))state!;
            TryInvokeSubmit(h, name);
          }, (hwnd, armed));
      }
    }
    catch
    {
      // Never let an exception escape a hook callback.
    }
    return CallNextHookEx(0, nCode, wParam, lParam);
  }

  [UnmanagedCallersOnly]
  private static void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
  {
    try
    {
      if (eventType == EVENT_SYSTEM_FOREGROUND && _keyboardHook != 0 && !IsCommandPaletteWindow(hwnd))
      {
        _armedButtonName = null;
        RemoveKeyboardHook(); // runs on the pump thread (out-of-context win event)
      }
    }
    catch { }
  }

  // Suppresses bursts (key auto-repeat, queued work) and the double-submit race
  // if upstream ever starts handling Enter natively.
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

    if (_pidIsCmdPal.TryGetValue(pid, out var cached))
      return cached;

    bool isCmdPal;
    try
    {
      using var process = Process.GetProcessById((int)pid);
      isCmdPal = process.ProcessName.Contains("CmdPal", StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
      return false;
    }

    if (_pidIsCmdPal.Count > 64)
      _pidIsCmdPal.Clear();
    _pidIsCmdPal[pid] = isCmdPal;
    return isCmdPal;
  }

  private static void TryInvokeSubmit(nint hwnd, string buttonName)
  {
    IUIAutomation? automation = null;
    IUIAutomationElement? focused = null;
    IUIAutomationElement? root = null;
    IUIAutomationCondition? typeCondition = null;
    IUIAutomationCondition? nameCondition = null;
    IUIAutomationCondition? condition = null;
    IUIAutomationElement? button = null;
    object? patternObj = null;
    try
    {
#pragma warning disable IL2072 // COM activation via CLSID is inherently dynamic
      automation = (IUIAutomation)Activator.CreateInstance(
          Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"))!)!;
#pragma warning restore IL2072

      if (automation.GetFocusedElement(out focused) != 0 || focused == null)
        return;

      if (!FocusedElementIsOurTextInput(focused, hwnd))
        return;

      if (automation.ElementFromHandle(hwnd, out root) != 0 || root == null)
        return;

      if (automation.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_ButtonControlTypeId, out typeCondition) != 0 || typeCondition == null)
        return;
      if (automation.CreatePropertyCondition(UIA_NamePropertyId, buttonName, out nameCondition) != 0 || nameCondition == null)
        return;
      if (automation.CreateAndCondition(typeCondition, nameCondition, out condition) != 0 || condition == null)
        return;

      if (root.FindFirst(TreeScope_Descendants, condition, out button) != 0 || button == null)
      {
        DebugLogService.Log("EnterSubmit", $"Submit button '{buttonName}' not found");
        return;
      }

      if (button.GetCurrentPattern(UIA_InvokePatternId, out patternObj) != 0 || patternObj is not IUIAutomationInvokePattern invoke)
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
      if (patternObj != null) Marshal.ReleaseComObject(patternObj);
      if (button != null) Marshal.ReleaseComObject(button);
      if (condition != null) Marshal.ReleaseComObject(condition);
      if (nameCondition != null) Marshal.ReleaseComObject(nameCondition);
      if (typeCondition != null) Marshal.ReleaseComObject(typeCondition);
      if (root != null) Marshal.ReleaseComObject(root);
      if (focused != null) Marshal.ReleaseComObject(focused);
      if (automation != null) Marshal.ReleaseComObject(automation);
    }
  }

  // Only invoke when focus sits in a text input owned by the Command Palette
  // process: a password box (unlock/verify) or a plain edit (the login email
  // field, which the renderer focuses first). This stops a stale armed state
  // from clicking anything when focus is on a button or another window.
  private static bool FocusedElementIsOurTextInput(IUIAutomationElement focused, nint cmdPalHwnd)
  {
    GetWindowThreadProcessId(cmdPalHwnd, out var cmdPalPid);
    if (focused.GetCurrentPropertyValue(UIA_ProcessIdPropertyId, out var pidValue) != 0
        || pidValue is not int focusedPid
        || focusedPid != (int)cmdPalPid)
      return false;

    if (focused.GetCurrentPropertyValue(UIA_IsPasswordPropertyId, out var isPassword) == 0
        && isPassword is bool password && password)
      return true;

    return focused.GetCurrentPropertyValue(UIA_ControlTypePropertyId, out var controlType) == 0
        && controlType is int type
        && type == UIA_EditControlTypeId;
  }

  private const uint WM_USER = 0x0400;
  private const uint WM_APP_INSTALL_HOOK = 0x8001;
  private const uint WM_APP_UNINSTALL_HOOK = 0x8002;
  private const uint WM_KEYDOWN = 0x0100;
  private const uint WM_SYSKEYDOWN = 0x0104;
  private const uint VK_RETURN = 0x0D;
  private const int WH_KEYBOARD_LL = 13;
  private const uint PM_NOREMOVE = 0;
  private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
  private const uint WINEVENT_OUTOFCONTEXT = 0;

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

  [LibraryImport("user32.dll")]
  private static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool UnhookWinEvent(nint hWinEventHook);

  [LibraryImport("kernel32.dll")]
  private static partial uint GetCurrentThreadId();

  [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
  private static partial nint GetModuleHandleW(string? lpModuleName);

  private const int UIA_ProcessIdPropertyId = 30002;
  private const int UIA_ControlTypePropertyId = 30003;
  private const int UIA_NamePropertyId = 30005;
  private const int UIA_IsPasswordPropertyId = 30019;
  private const int UIA_ButtonControlTypeId = 50000;
  private const int UIA_EditControlTypeId = 50004;
  private const int UIA_InvokePatternId = 10000;
  private const int TreeScope_Descendants = 4;

  // Minimal COM interface definitions — only the methods we call are properly declared;
  // unused vtable slots use parameterless void stubs for slot positioning.
  // (Same pattern as ContextAwarenessService.)

  [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IUIAutomation
  {
    void _ReservedCompareElements();
    void _ReservedCompareRuntimeIds();
    void _ReservedGetRootElement();

    [PreserveSig]
    int ElementFromHandle(nint hwnd, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);

    void _ReservedElementFromPoint();

    [PreserveSig]
    int GetFocusedElement([MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);

    void _ReservedGetRootElementBuildCache();
    void _ReservedElementFromHandleBuildCache();
    void _ReservedElementFromPointBuildCache();
    void _ReservedGetFocusedElementBuildCache();
    void _ReservedCreateTreeWalker();
    void _ReservedControlViewWalker();
    void _ReservedContentViewWalker();
    void _ReservedRawViewWalker();
    void _ReservedRawViewCondition();
    void _ReservedControlViewCondition();
    void _ReservedContentViewCondition();
    void _ReservedCreateCacheRequest();
    void _ReservedCreateTrueCondition();
    void _ReservedCreateFalseCondition();

    [PreserveSig]
    int CreatePropertyCondition(
        int propertyId,
        [In, MarshalAs(UnmanagedType.Struct)] object value,
        [MarshalAs(UnmanagedType.Interface)] out IUIAutomationCondition condition);

    void _ReservedCreatePropertyConditionEx();

    [PreserveSig]
    int CreateAndCondition(
        [MarshalAs(UnmanagedType.Interface)] IUIAutomationCondition condition1,
        [MarshalAs(UnmanagedType.Interface)] IUIAutomationCondition condition2,
        [MarshalAs(UnmanagedType.Interface)] out IUIAutomationCondition condition);
  }

  [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IUIAutomationElement
  {
    void _ReservedSetFocus();
    void _ReservedGetRuntimeId();

    [PreserveSig]
    int FindFirst(
        int scope,
        [MarshalAs(UnmanagedType.Interface)] IUIAutomationCondition condition,
        [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement found);

    void _ReservedFindAll();
    void _ReservedFindFirstBuildCache();
    void _ReservedFindAllBuildCache();
    void _ReservedBuildUpdatedCache();

    [PreserveSig]
    int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object value);

    void _ReservedGetCurrentPropertyValueEx();
    void _ReservedGetCachedPropertyValue();
    void _ReservedGetCachedPropertyValueEx();
    void _ReservedGetCurrentPatternAs();
    void _ReservedGetCachedPatternAs();

    [PreserveSig]
    int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object pattern);
  }

  [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IUIAutomationCondition { }

  [ComImport, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IUIAutomationInvokePattern
  {
    [PreserveSig]
    int Invoke();
  }
}
