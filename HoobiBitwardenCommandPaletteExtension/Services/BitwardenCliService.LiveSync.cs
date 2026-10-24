using System;
using System.Threading;
using System.Threading.Tasks;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed partial class BitwardenCliService
{
    private static readonly TimeSpan LiveSyncDebounce = TimeSpan.FromSeconds(2);
    private readonly Lock _liveSyncLock = new();
    private NotificationsService? _notifications;
    private Timer? _liveSyncTimer;
    private int _liveSyncRunning;

    internal bool IsLiveSyncActive => _notifications != null;

    internal bool IsLiveSyncQueued => _liveSyncTimer != null;

    private void UpdateLiveSync()
    {
        lock (_liveSyncLock)
        {
            var wanted = _settings?.LiveSync.Value == true && _lastStatus == VaultStatus.Unlocked && IsUnlocked;
            if (wanted == (_notifications != null)) return;
            if (wanted)
            {
                DebugLogService.Log("LiveSync", "Starting");
                _notifications = new NotificationsService(HubProtocol.CliDataFile(DataDirectory), SyncVaultAsync, QueueLiveSync);
                return;
            }

            _notifications!.Dispose();
            _notifications = null;
            _liveSyncTimer?.Dispose();
            _liveSyncTimer = null;
        }
    }

    internal void QueueLiveSync()
    {
        lock (_liveSyncLock)
        {
            if (_notifications == null) return;
            _liveSyncTimer ??= new Timer(state => _ = LiveSyncAsync());
            _liveSyncTimer.Change(LiveSyncDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    internal async Task LiveSyncAsync()
    {
        if (!IsUnlocked) return;
        if (Interlocked.Exchange(ref _liveSyncRunning, 1) == 1)
        {
            QueueLiveSync();
            return;
        }

        try
        {
            DebugLogService.Log("LiveSync", "Vault changed on the server; syncing");
            await SyncVaultAsync();
        }
        catch (Exception ex)
        {
            DebugLogService.Log("LiveSync", $"Sync failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _liveSyncRunning, 0);
        }
    }
}
