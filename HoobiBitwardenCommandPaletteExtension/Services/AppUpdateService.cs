using System;
using System.Threading;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed class AppUpdateService(AppInfo app, IStoreUpdates? store, GitHubReleaseClient github)
{
  private int _checking;

  internal static AppUpdateService CreateDefault()
  {
    var app = AppInfo.Current;
    return new AppUpdateService(app, app.IsStore ? new StoreUpdates() : null, new GitHubReleaseClient(GitHubReleaseClient.CreateHttpClient(app.Version)));
  }

  internal AboutResponse About() => new(app.Version, app.Commit, app.Channel, app.IsStore);

  internal async Task<UpdateResponse> CheckAsync(CancellationToken cancellationToken)
  {
    if (app.Channel == AppChannels.Dev)
      return new UpdateResponse(UpdateStates.Off, null, null, app.IsStore);

    if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
      throw new IpcException(IpcErrors.Conflict);

    try
    {
      if (store is not null)
        return new UpdateResponse(await store.CheckAsync() ? UpdateStates.Available : UpdateStates.UpToDate, null, null, true);
      return await github.CheckAsync(app, cancellationToken);
    }
    catch (Exception ex) when (ex is not (OperationCanceledException or IpcException))
    {
      DebugLogService.Log("Updates", $"Check failed: {ex.GetType().Name}: {ex.Message}");
      return new UpdateResponse(UpdateStates.Failed, null, null, app.IsStore);
    }
    finally
    {
      Volatile.Write(ref _checking, 0);
    }
  }

  internal async Task InstallAsync()
  {
    if (store is null)
      throw new IpcException(IpcErrors.Invalid);

    try
    {
      await store.InstallAsync();
    }
    catch (Exception ex)
    {
      throw new IpcException(IpcErrors.Failed, ex);
    }
  }
}
