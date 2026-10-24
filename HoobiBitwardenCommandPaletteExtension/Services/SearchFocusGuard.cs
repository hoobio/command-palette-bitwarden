using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HoobiBitwardenCommandPaletteExtension.Services;

// Command Palette sometimes shows its window without taking the foreground, so typing goes to the previous window; this moves focus back to the search box.
internal static partial class SearchFocusGuard
{
  private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(20);
  private static readonly TimeSpan StableDuration = TimeSpan.FromMilliseconds(500);
  private static readonly TimeSpan MaxGuardDuration = TimeSpan.FromSeconds(5);
  private static readonly Lock _startLock = new();
  private static bool _started;
  private static int _generation;
  private static volatile nint _lastShownHwnd;

  internal static void Start()
  {
    lock (_startLock)
    {
      if (_started)
        return;
      _started = true;
    }
    new Thread(PumpProc) { IsBackground = true, Name = "SearchFocusGuard" }.Start();
  }

  private static unsafe void PumpProc()
  {
    var showHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, 0, &WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    var uncloakHook = SetWinEventHook(EVENT_OBJECT_UNCLOAKED, EVENT_OBJECT_UNCLOAKED, 0, &WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    DebugLogService.Log("FocusGuard", showHook != 0 && uncloakHook != 0 ? "Watching Command Palette window" : "SetWinEventHook failed");

    while (GetMessageW(out var msg, 0, 0, 0) > 0)
      DispatchMessageW(in msg);

    if (showHook != 0)
      UnhookWinEvent(showHook);
    if (uncloakHook != 0)
      UnhookWinEvent(uncloakHook);
  }

  [UnmanagedCallersOnly]
  private static void WinEventProc(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
  {
    try
    {
      if (idObject != OBJID_WINDOW || idChild != 0 || hwnd == 0 || GetAncestor(hwnd, GA_ROOT) != hwnd)
        return;
      if (!EnterKeySubmitService.IsCommandPaletteWindow(hwnd))
        return;
      _lastShownHwnd = hwnd;
      StartGuard(hwnd);
    }
    catch { }
  }

  // The Windows Hello prompt belongs to Bitwarden Desktop and takes the foreground; when it closes, focus goes to the previous app.
  internal static void RestoreFocus()
  {
    var hwnd = _lastShownHwnd;
    if (hwnd != 0 && IsWindowVisible(hwnd) && !IsCloaked(hwnd))
      StartGuard(hwnd);
  }

  private static void StartGuard(nint hwnd)
  {
    var generation = Interlocked.Increment(ref _generation);
    _ = Task.Run(() => GuardFocusAsync(hwnd, generation));
  }

  private static async Task GuardFocusAsync(nint hwnd, int generation)
  {
    var started = Stopwatch.GetTimestamp();
    long stableSince = 0;
    while ((stableSince == 0 || Stopwatch.GetElapsedTime(stableSince) < StableDuration)
        && Stopwatch.GetElapsedTime(started) < MaxGuardDuration
        && generation == Volatile.Read(ref _generation))
    {
      await Task.Delay(CheckInterval);
      if (!IsWindowVisible(hwnd) || IsCloaked(hwnd))
        return;
      var foreground = GetForegroundWindow();
      if (foreground == hwnd || EnterKeySubmitService.IsCommandPaletteWindow(foreground))
      {
        if (stableSince == 0)
          stableSince = Stopwatch.GetTimestamp();
        continue;
      }
      stableSince = 0;
      if (FocusSearchBox(hwnd))
        DebugLogService.Log("FocusGuard", $"Command Palette was visible without focus; focused the search box after {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}ms");
    }
  }

  private static bool FocusSearchBox(nint hwnd)
  {
    IUIAutomation? automation = null;
    IUIAutomationElement? root = null;
    IUIAutomationElement? searchBox = null;
    nint condition = 0;
    try
    {
      automation = UiaInterop.CreateAutomation();
      if (automation == null
          || automation.ElementFromHandle(hwnd, out var rootPtr) != 0
          || (root = UiaInterop.Wrap<IUIAutomationElement>(rootPtr)) == null)
        return false;

      if (automation.CreatePropertyCondition(UiaInterop.UIA_ControlTypePropertyId, UiaInterop.Variant.FromInt(UiaInterop.UIA_EditControlTypeId), out condition) == 0 && condition != 0
          && root.FindFirst(UiaInterop.TreeScope_Descendants, condition, out var boxPtr) == 0)
        searchBox = UiaInterop.Wrap<IUIAutomationElement>(boxPtr);

      var hr = (searchBox ?? root).SetFocus();
      if (hr != 0)
        DebugLogService.Log("FocusGuard", $"SetFocus failed: 0x{hr:X8}");
      return hr == 0;
    }
    catch (Exception ex)
    {
      DebugLogService.Log("FocusGuard", $"Focus failed: {ex.GetType().Name}: {ex.Message}");
      return false;
    }
    finally
    {
      UiaInterop.Release(condition);
      UiaInterop.Release(searchBox);
      UiaInterop.Release(root);
      UiaInterop.Release(automation);
    }
  }

  private static bool IsCloaked(nint hwnd) =>
    DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

  private const uint EVENT_OBJECT_SHOW = 0x8002;
  private const uint EVENT_OBJECT_UNCLOAKED = 0x8018;
  private const uint WINEVENT_OUTOFCONTEXT = 0;
  private const uint WINEVENT_SKIPOWNPROCESS = 2;
  private const int OBJID_WINDOW = 0;
  private const uint GA_ROOT = 2;
  private const uint DWMWA_CLOAKED = 14;

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
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool IsWindowVisible(nint hWnd);

  [LibraryImport("user32.dll")]
  private static partial nint GetAncestor(nint hwnd, uint gaFlags);

  [LibraryImport("dwmapi.dll")]
  private static partial int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);

  [LibraryImport("user32.dll")]
  private static partial int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

  [LibraryImport("user32.dll")]
  private static partial nint DispatchMessageW(in MSG lpMsg);

  [LibraryImport("user32.dll")]
  private static unsafe partial nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool UnhookWinEvent(nint hWinEventHook);
}
