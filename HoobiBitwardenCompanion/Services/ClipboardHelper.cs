using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace HoobiBitwardenCompanion.Services;

// Foreground clipboard copy mirroring the extension's secure-clipboard behaviour. The extension's
// SecureClipboardService runs in the background COM-server process where clipboard ops are flaky, so
// the companion (foreground) owns its copies but shares the extension's settings (auto-clear on/off
// and delay, configured at launch) and the same protections: excluded from Windows clipboard history
// and cloud roaming, and auto-cleared only if our value is still on the clipboard.
internal static class ClipboardHelper
{
    private static long _sequence;
    private static bool _autoClear = true;
    private static int _clearSeconds = 30;

    // Raised after a successful copy (with the field label, e.g. "Username") so the active window can
    // show an in-app "Copied X to clipboard" toast.
    public static event Action<string>? Copied;

    // Apply the clipboard settings shared from the extension (see CompanionLauncher / LaunchOptions).
    public static void Configure(bool autoClear, int clearSeconds)
    {
        _autoClear = autoClear;
        _clearSeconds = clearSeconds > 0 ? clearSeconds : 30;
    }

    public static void Copy(string value, string label = "")
    {
        var mine = Interlocked.Increment(ref _sequence);

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(value);
        // Keep secrets out of clipboard history and cloud sync, matching the extension's secure copy.
        var options = new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false };
        Clipboard.SetContentWithOptions(package, options);
        Copied?.Invoke(label);

        if (!_autoClear || _clearSeconds <= 0) return;

        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue == null) return;

        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(_clearSeconds);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => _ = ClearIfStillOursAsync(mine, value);
        timer.Start();
    }

    private static async System.Threading.Tasks.Task ClearIfStillOursAsync(long mine, string value)
    {
        if (Interlocked.Read(ref _sequence) != mine) return; // superseded by a later copy
        try
        {
            // Only clear if our value is still there - don't wipe something the user copied since.
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text) && await content.GetTextAsync() == value)
                Clipboard.Clear();
        }
        catch { /* clipboard may be locked by another app */ }
    }
}
