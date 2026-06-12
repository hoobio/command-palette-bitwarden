using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace HoobiBitwardenCompanion.Services;

// Foreground clipboard copy with optional auto-clear. The extension's SecureClipboardService runs in
// the COM-server process; clipboard ops are reliable from the foreground (the companion), so the
// companion owns its own copies and mirrors the auto-clear behaviour. Clears only if no later Copy
// has happened since (a sequence token), so we don't wipe a value the user copied afterwards.
internal static class ClipboardHelper
{
    private static long _sequence;

    public static void Copy(string value, bool autoClear = true, int clearAfterSeconds = 30)
    {
        var mine = Interlocked.Increment(ref _sequence);

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(value);
        Clipboard.SetContent(package);

        if (!autoClear || clearAfterSeconds <= 0) return;

        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue == null) return;

        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(clearAfterSeconds);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (Interlocked.Read(ref _sequence) != mine) return; // superseded by a later copy
            try { Clipboard.Clear(); }
            catch { /* clipboard may be locked by another app */ }
        };
        timer.Start();
    }
}
