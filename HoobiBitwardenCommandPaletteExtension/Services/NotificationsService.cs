using System;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed partial class NotificationsService : IDisposable
{
    private enum ListenOutcome { Failed, Unauthorized, Dropped }

    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinHealthyUptime = TimeSpan.FromMinutes(1);
    private static readonly string UserAgent = $"HoobiBitwardenCommandPalette/{typeof(NotificationsService).Assembly.GetName().Version}";

    private readonly string _dataFile;
    private readonly Func<Task> _sync;
    private readonly Action _vaultChanged;
    private readonly CancellationTokenSource _cts = new();

    public NotificationsService(string dataFile, Func<Task> sync, Action vaultChanged)
    {
        _dataFile = dataFile;
        _sync = sync;
        _vaultChanged = vaultChanged;
        var token = _cts.Token;
        _ = Task.Run(() => RunAsync(token));
    }

    public void Dispose() => _cts.Cancel();

    private async Task RunAsync(CancellationToken ct)
    {
        var failures = 0;
        var syncFirst = false;
        while (!ct.IsCancellationRequested)
        {
            var outcome = ListenOutcome.Failed;
            try
            {
                var info = HubProtocol.ReadConnectionInfo(_dataFile);
                // bw sync refreshes the access token in data.json, and after a dropped connection it also fetches changes missed while offline.
                if (info is { } current && (syncFirst || HubProtocol.TokenExpiresWithin(current.AccessToken, TokenRefreshMargin)))
                {
                    await _sync();
                    info = HubProtocol.ReadConnectionInfo(_dataFile);
                }

                if (info is { } fresh)
                    outcome = await ListenAsync(fresh.HubUri, fresh.AccessToken, ct);
                else
                    DebugLogService.Log("LiveSync", "No access token in the CLI data file");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                DebugLogService.Log("LiveSync", $"Connection attempt failed: {ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break;
            }

            syncFirst = outcome != ListenOutcome.Failed;
            failures = outcome == ListenOutcome.Dropped ? 0 : failures + 1;
            try { await Task.Delay(RetryDelay(failures), ct); }
            catch (OperationCanceledException) { break; }
        }
        _cts.Dispose();
        DebugLogService.Log("LiveSync", "Stopped");
    }

    private async Task<ListenOutcome> ListenAsync(Uri hubUri, string accessToken, CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
        // Cloudflare answers 403 to a WebSocket upgrade with no User-Agent (verified against a Cloudflare-fronted Vaultwarden).
        socket.Options.SetRequestHeader("User-Agent", UserAgent);
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.KeepAliveInterval = PingInterval;
        socket.Options.KeepAliveTimeout = KeepAliveTimeout;
        using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reader = new HubFrameReader();
        var connectedAt = 0L;
        Task pinging = Task.CompletedTask;
        try
        {
            await socket.ConnectAsync(hubUri, ct);
            await socket.SendAsync(Encoding.UTF8.GetBytes(HubProtocol.Handshake), WebSocketMessageType.Text, true, ct);
            pinging = PingAsync(socket, pingCts.Token);
            var buffer = new byte[8192];
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                var wasConnected = reader.HandshakeCompleted;
                var frames = reader.Append(buffer.AsSpan(0, result.Count));
                if (!wasConnected && reader.HandshakeCompleted)
                {
                    connectedAt = Stopwatch.GetTimestamp();
                    DebugLogService.Log("LiveSync", "Connected to the notifications hub");
                }
                if (frames.Contains(HubFrameKind.VaultChanged))
                    _vaultChanged();
                if (frames.Contains(HubFrameKind.Close)) break;
            }
            DebugLogService.Log("LiveSync", "Notifications hub closed the connection");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            DebugLogService.Log("LiveSync", $"Connection lost: {ex.GetType().Name}: {ex.Message}");
            if (!reader.HandshakeCompleted && socket.HttpStatusCode == HttpStatusCode.Unauthorized)
                return ListenOutcome.Unauthorized;
        }
        finally
        {
            await pingCts.CancelAsync();
            try { await pinging; }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException) { }
        }
        return reader.HandshakeCompleted && Stopwatch.GetElapsedTime(connectedAt) >= MinHealthyUptime
            ? ListenOutcome.Dropped
            : ListenOutcome.Failed;
    }

    // ASP.NET SignalR drops a client after 30 seconds without a hub message; WebSocket-level pongs do not count.
    private static async Task PingAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PingInterval);
        while (await timer.WaitForNextTickAsync(ct))
            await socket.SendAsync(HubProtocol.PingFrame, WebSocketMessageType.Binary, true, ct);
    }

    private static TimeSpan RetryDelay(int failures) =>
        TimeSpan.FromSeconds(Math.Min(600, 10 * Math.Pow(2, failures)) * (0.5 + (Random.Shared.NextDouble() / 2)));
}
