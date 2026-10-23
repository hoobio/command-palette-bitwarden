using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static partial class SecureClipboardService
{
  private static Timer? _clearTimer;
  private static ProtectedSecret? _lastCopied;
  private static readonly Lock _lock = new();
  private static int _clearDelaySeconds = 30;

  internal static bool AutoClearEnabled { get; set; } = true;

  internal static int ClearDelaySeconds
  {
    get => _clearDelaySeconds;
    set => _clearDelaySeconds = value > 0 ? value : 30;
  }

  internal static void CopySensitive(string text) => WriteSensitive(text);

  internal static void CopySensitive(ProtectedSecret secret) => secret.Use(static text => WriteSensitive(text));

  private static bool WriteSensitive(ReadOnlySpan<char> text)
  {
    lock (_lock)
    {
      ReplaceLastCopied(ProtectedSecret.From(text));
      SetClipboardExcludedFromHistory(text);
      if (AutoClearEnabled)
        ScheduleClear();
    }
    return true;
  }

  private static void ReplaceLastCopied(ProtectedSecret? secret)
  {
    _lastCopied?.Dispose();
    _lastCopied = secret;
  }

  internal static void CopyNonSensitive(string text)
  {
    lock (_lock)
    {
      ReplaceLastCopied(null);
      _clearTimer?.Dispose();
      _clearTimer = null;
    }
    Microsoft.CommandPalette.Extensions.Toolkit.ClipboardHelper.SetText(text);
  }

  private static void ScheduleClear()
  {
    _clearTimer?.Dispose();
    _clearTimer = new Timer(OnTimerElapsed, null, _clearDelaySeconds * 1000, Timeout.Infinite);
  }

  private static void OnTimerElapsed(object? state)
  {
    lock (_lock)
    {
      if (_lastCopied == null)
        return;

      _clearTimer?.Dispose();
      _clearTimer = null;

      if (!_lastCopied.Use(static expected => TryClearIfUnchanged(expected)))
      {
        if (state != ClearRetried)
        {
          _clearTimer = new Timer(OnTimerElapsed, ClearRetried, ClearRetryDelayMs, Timeout.Infinite);
          return;
        }
        DebugLogService.Log("Clipboard", "Could not open the clipboard to clear copied secret");
      }

      ReplaceLastCopied(null);
    }
  }

  internal static bool RetryOpen(Func<bool> open, int attempts, int delayMs)
  {
    for (var attempt = 1; ; attempt++)
    {
      if (open())
        return true;
      if (attempt >= attempts)
        return false;
      Thread.Sleep(delayMs);
    }
  }

  private static bool TryClearIfUnchanged(ReadOnlySpan<char> expected)
  {
    if (!RetryOpen(() => OpenClipboard(nint.Zero), OpenAttempts, OpenRetryDelayMs))
      return false;

    try
    {
      if (ClipboardTextEquals(expected))
        EmptyClipboard();
      return true;
    }
    finally
    {
      CloseClipboard();
    }
  }

  private static unsafe void SetClipboardExcludedFromHistory(ReadOnlySpan<char> text)
  {
    if (!OpenClipboard(nint.Zero))
      return;

    try
    {
      EmptyClipboard();

      var size = (text.Length + 1) * sizeof(char);
      var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (nuint)size);
      if (hGlobal == nint.Zero)
        return;

      var ptr = GlobalLock(hGlobal);
      if (ptr == nint.Zero)
      {
        GlobalFree(hGlobal);
        return;
      }

      var destination = new Span<char>((void*)ptr, text.Length + 1);
      text.CopyTo(destination);
      destination[^1] = '\0';
      GlobalUnlock(hGlobal);

      if (SetClipboardData(CF_UNICODETEXT, hGlobal) == nint.Zero)
        GlobalFree(hGlobal);

      var excludeFormat = RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
      if (excludeFormat != 0)
      {
        var hFlag = GlobalAlloc(GMEM_MOVEABLE, (nuint)sizeof(int));
        if (hFlag != nint.Zero)
        {
          var flagPtr = GlobalLock(hFlag);
          if (flagPtr != nint.Zero)
          {
            Marshal.WriteInt32(flagPtr, 1);
            GlobalUnlock(hFlag);
            if (SetClipboardData(excludeFormat, hFlag) == nint.Zero)
              GlobalFree(hFlag);
          }
          else
          {
            GlobalFree(hFlag);
          }
        }
      }
    }
    finally
    {
      CloseClipboard();
    }
  }

  private static unsafe bool ClipboardTextEquals(ReadOnlySpan<char> expected)
  {
    var hData = GetClipboardData(CF_UNICODETEXT);
    if (hData == nint.Zero)
      return false;

    var ptr = GlobalLock(hData);
    if (ptr == nint.Zero)
      return false;

    try
    {
      return MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)ptr).SequenceEqual(expected);
    }
    finally
    {
      GlobalUnlock(hData);
    }
  }

  private const int OpenAttempts = 5;
  private const int OpenRetryDelayMs = 50;
  private const int ClearRetryDelayMs = 2000;
  private static readonly object ClearRetried = new();
  private const uint CF_UNICODETEXT = 13;
  private const uint GMEM_MOVEABLE = 0x0002;

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool OpenClipboard(nint hWndNewOwner);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool CloseClipboard();

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool EmptyClipboard();

  [LibraryImport("user32.dll")]
  private static partial nint SetClipboardData(uint uFormat, nint hMem);

  [LibraryImport("user32.dll")]
  private static partial nint GetClipboardData(uint uFormat);

  [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
  private static partial uint RegisterClipboardFormatW(string lpszFormat);

  [LibraryImport("kernel32.dll")]
  private static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

  [LibraryImport("kernel32.dll")]
  private static partial nint GlobalFree(nint hMem);

  [LibraryImport("kernel32.dll")]
  private static partial nint GlobalLock(nint hMem);

  [LibraryImport("kernel32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool GlobalUnlock(nint hMem);
}
