using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed class WindowContext
{
  public string? ProcessName { get; init; }
  public string? WindowTitle { get; init; }
  public string? BrowserUrl { get; init; }
  public bool IsBrowser { get; init; }
}

internal sealed class ForegroundContext
{
  public List<WindowContext> Windows { get; init; } = [];
}

internal static partial class ContextAwarenessService
{
  private const int MaxWindows = 5;

  internal static ForegroundContext? CaptureContext()
  {
    try
    {
      var windows = CollectTopWindows();
      return windows.Count > 0 ? new ForegroundContext { Windows = windows } : null;
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Context", $"CaptureContext failed: {ex.GetType().Name}: {ex.Message}");
      return null;
    }
  }

  private static List<WindowContext> CollectTopWindows()
  {
    var result = new List<WindowContext>(MaxWindows);
    var seen = new HashSet<uint>();

    var foreground = GetForegroundWindow();
    if (foreground == nint.Zero)
      return result;

    TryAddWindow(foreground, result, seen);

    var hwnd = GetWindow(foreground, GW_HWNDNEXT);
    while (hwnd != nint.Zero && result.Count < MaxWindows)
    {
      if (IsWindowVisible(hwnd))
        TryAddWindow(hwnd, result, seen);
      hwnd = GetWindow(hwnd, GW_HWNDNEXT);
    }
    return result;
  }

  private static void TryAddWindow(nint hwnd, List<WindowContext> result, HashSet<uint> seen)
  {
    GetWindowThreadProcessId(hwnd, out var pid);
    if (pid == 0 || !seen.Add(pid))
      return;

    var processName = GetProcessNameForPid(pid);
    if (processName == null || IsCommandPaletteProcess(processName))
      return;

    var title = GetWindowTitle(hwnd);
    if (title == null)
      return;

    var isBrowser = IsBrowserProcess(processName);
    string? browserUrl = null;
    if (isBrowser)
      browserUrl = ExtractBrowserUrl(hwnd) ?? ExtractUrlFromTitle(title);

    result.Add(new WindowContext
    {
      ProcessName = processName,
      WindowTitle = title,
      BrowserUrl = browserUrl,
      IsBrowser = isBrowser,
    });
  }

  private static bool IsCommandPaletteProcess(string processName) =>
      processName.Contains("CmdPal", StringComparison.OrdinalIgnoreCase);

  internal static string? ExtractUrlFromTitle(string? title)
  {
    if (string.IsNullOrEmpty(title))
      return null;

    var parts = title.Split([" - ", " — ", " | "], StringSplitOptions.RemoveEmptyEntries);
    foreach (var part in parts)
    {
      var trimmed = part.Trim();
      if (trimmed.Contains('.') && !trimmed.Contains(' ') && trimmed.Length < 100)
        return trimmed;
    }
    return null;
  }

  internal static int ContextScore(ForegroundContext context, Models.BitwardenItem item)
  {
    var windows = context.Windows;
    for (var i = 0; i < windows.Count; i++)
    {
      if (WindowMatchesItem(windows[i], item))
        return MaxWindows - i; // Higher score = closer to front
    }
    return 0;
  }

  private static bool WindowMatchesItem(WindowContext window, Models.BitwardenItem item)
  {
    if (window.IsBrowser)
      return BrowserContextMatchesItem(window, item);

    return ProcessNameMatchesItem(window.ProcessName, window.WindowTitle, item);
  }

  private static bool BrowserContextMatchesItem(WindowContext window, Models.BitwardenItem item)
  {
    if (item.Type != Models.BitwardenItemType.Login)
      return false;

    var title = StripBrowserSuffix(window.WindowTitle, window.ProcessName);
    var urlHost = !string.IsNullOrEmpty(window.BrowserUrl) ? ExtractHost(window.BrowserUrl) : null;

    foreach (var entry in item.Uris)
    {
      if (entry.Match == Models.UriMatchType.Never)
        continue;

      if (UriMatchesBrowserContext(entry, window.BrowserUrl, urlHost))
        return true;
    }

    if (!string.IsNullOrEmpty(title) && item.Name.Length >= 3 && title.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
      return true;

    return false;
  }

  internal static bool UriMatchesBrowserContext(Models.ItemUri entry, string? browserUrl, string? browserHost)
  {
    var itemHost = entry.Host;

    return entry.Match switch
    {
      Models.UriMatchType.Exact =>
          !string.IsNullOrEmpty(browserUrl) && NormalizeUrl(browserUrl) == NormalizeUrl(entry.Uri),

      Models.UriMatchType.StartsWith =>
          !string.IsNullOrEmpty(browserUrl) && NormalizeUrl(browserUrl).StartsWith(NormalizeUrl(entry.Uri), StringComparison.OrdinalIgnoreCase),

      Models.UriMatchType.Host =>
          browserHost != null && itemHost != null && browserHost.Equals(itemHost, StringComparison.OrdinalIgnoreCase),

      // Vault URI used as regex pattern (set by vault owner, not external input). 100ms timeout mitigates ReDoS.
      Models.UriMatchType.RegularExpression =>
          !string.IsNullOrEmpty(browserUrl) && System.Text.RegularExpressions.Regex.IsMatch(browserUrl, entry.Uri, System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)),

      _ => // Default and Domain: subdomain-inclusive host matching
          browserHost != null && itemHost != null && HostsMatch(browserHost, itemHost),
    };
  }

  internal static string NormalizeUrl(string url)
  {
    if (!url.Contains("://"))
      url = "https://" + url;
    return url.TrimEnd('/');
  }

  // Keep in sync with BrowserProcessNames below (display names for title stripping vs exe names for detection).
  private static readonly string[] BrowserDisplayNames =
  [
    "Google Chrome", "Mozilla Firefox", "Microsoft Edge", "Brave", "Opera", "Vivaldi", "Arc", "Thorium", "Waterfox", "LibreWolf"
  ];

  internal static string? StripBrowserSuffix(string? title, string? processName)
  {
    if (string.IsNullOrEmpty(title))
      return null;

    // Browsers append " - Browser Name" or " — Browser Name" to the title
    foreach (var name in BrowserDisplayNames)
    {
      var dashSuffix = " - " + name;
      if (title.EndsWith(dashSuffix, StringComparison.OrdinalIgnoreCase))
        return title[..^dashSuffix.Length];

      var emDashSuffix = " — " + name;
      if (title.EndsWith(emDashSuffix, StringComparison.OrdinalIgnoreCase))
        return title[..^emDashSuffix.Length];
    }
    return title;
  }

  internal static bool ProcessNameMatchesItem(string? processName, string? windowTitle, Models.BitwardenItem item)
  {
    if (string.IsNullOrEmpty(processName) && string.IsNullOrEmpty(windowTitle))
      return false;

    // Match process name ↔ item name via prefix (handles compound names like "steamwebhelper" → "Steam")
    if (!string.IsNullOrEmpty(processName) && item.Name.Length >= 3 && NamesSimilar(processName, item.Name))
      return true;

    // Match window title against item name (whole-word only to avoid partial matches in long titles)
    if (!string.IsNullOrEmpty(windowTitle) && item.Name.Length >= 4
        && ContainsWholeWord(windowTitle, item.Name))
      return true;

    // Match URI host domain base against process name (e.g., process "discord" → host "discord.com")
    if (item.Type == Models.BitwardenItemType.Login && !string.IsNullOrEmpty(processName))
    {
      foreach (var entry in item.Uris)
      {
        if (entry.Match == Models.UriMatchType.Never)
          continue;

        var host = entry.Host;
        if (host != null)
        {
          var domainBase = host.Split('.')[0];
          if (domainBase.Length >= 3 && domainBase.Equals(processName, StringComparison.OrdinalIgnoreCase))
            return true;
        }
      }
    }

    return false;
  }

  internal static bool NamesSimilar(string a, string b) =>
      a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);

  internal static bool ContainsWholeWord(string text, string word)
  {
    var idx = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
    while (idx >= 0)
    {
      var before = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
      var after = idx + word.Length >= text.Length || !char.IsLetterOrDigit(text[idx + word.Length]);
      if (before && after)
        return true;
      idx = text.IndexOf(word, idx + 1, StringComparison.OrdinalIgnoreCase);
    }
    return false;
  }

  internal static string? ExtractHost(string url) => Models.ItemUri.ExtractHost(url);

  // Suffix-based subdomain matching (not true eTLD+1). Can false-positive on public suffixes
  // like .co.uk, but true eTLD+1 requires a public suffix list dependency. Acceptable since
  // this only affects context boosting priority, not authentication or access control.
  internal static bool HostsMatch(string a, string b) =>
      a.Equals(b, StringComparison.OrdinalIgnoreCase)
      || a.EndsWith("." + b, StringComparison.OrdinalIgnoreCase)
      || b.EndsWith("." + a, StringComparison.OrdinalIgnoreCase);

  private static string? GetProcessNameForPid(uint pid)
  {
    try
    {
      using var process = Process.GetProcessById((int)pid);
      return process.ProcessName;
    }
    catch { return null; }
  }

  private static string? GetWindowTitle(nint hwnd)
  {
    var length = GetWindowTextLengthW(hwnd);
    if (length == 0) return null;
    var buffer = new char[length + 1];
    GetWindowTextW(hwnd, buffer, buffer.Length);
    var title = new string(buffer, 0, length);
    return string.IsNullOrWhiteSpace(title) ? null : title;
  }

  private static readonly string[] BrowserProcessNames =
  [
    "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "thorium", "waterfox", "librewolf"
  ];

  internal static bool IsBrowserProcess(string? processName) =>
      processName != null && BrowserProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase);

  [LibraryImport("user32.dll")]
  private static partial nint GetForegroundWindow();

  [LibraryImport("user32.dll")]
  private static partial int GetWindowTextLengthW(nint hWnd);

  [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
  private static partial int GetWindowTextW(nint hWnd, [Out] char[] lpString, int nMaxCount);

  [LibraryImport("user32.dll")]
  private static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

  [LibraryImport("user32.dll")]
  private static partial nint GetWindow(nint hWnd, uint uCmd);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool IsWindowVisible(nint hWnd);

  private const uint GW_HWNDNEXT = 2;

  private static string? ExtractBrowserUrl(nint hwnd)
  {
    IUIAutomation? automation = null;
    IUIAutomationElement? element = null;
    nint condition = 0;
    IUIAutomationElement? edit = null;
    try
    {
      automation = UiaInterop.CreateAutomation();
      if (automation == null)
        return null;

      if (automation.ElementFromHandle(hwnd, out var elementPtr) != 0
          || (element = UiaInterop.Wrap<IUIAutomationElement>(elementPtr)) == null)
        return null;

      if (automation.CreatePropertyCondition(UiaInterop.UIA_ControlTypePropertyId, UiaInterop.Variant.FromInt(UiaInterop.UIA_EditControlTypeId), out condition) != 0 || condition == 0)
        return null;

      if (element.FindFirst(UiaInterop.TreeScope_Descendants, condition, out var editPtr) != 0
          || (edit = UiaInterop.Wrap<IUIAutomationElement>(editPtr)) == null)
        return null;

      if (edit.GetCurrentPropertyValue(UiaInterop.UIA_ValueValuePropertyId, out var value) == 0)
      {
        var url = value.AsString()?.Trim();
        UiaInterop.VariantClear(ref value);
        if (url != null && LooksLikeUrl(url))
          return url;
      }
    }
    catch { }
    finally
    {
      UiaInterop.Release(edit);
      UiaInterop.Release(condition);
      UiaInterop.Release(element);
      UiaInterop.Release(automation);
    }
    return null;
  }

  internal static bool LooksLikeUrl(string value)
  {
    if (string.IsNullOrWhiteSpace(value) || value.Length > 2048)
      return false;
    if (value.Contains("://"))
      return true;
    return value.IndexOf('.') > 0 && !value.Contains(' ');
  }
}
