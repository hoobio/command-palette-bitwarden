using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;
using Windows.ApplicationModel.Store.Preview.InstallControl;
using Windows.Services.Store;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal interface IStoreUpdates
{
  Task<bool> CheckAsync();

  Task<string> InstallAsync();
}

internal sealed class StoreUpdates : IStoreUpdates
{
  private const string ProductId = "9P5KS8T80MV3";

  private StoreContext? _context;
  private IReadOnlyList<StorePackageUpdate> _updates = [];

  public async Task<bool> CheckAsync()
  {
    await ScanAsync();
    _context ??= StoreContext.GetDefault();
    _updates = await _context.GetAppAndOptionalStorePackageUpdatesAsync();
    DebugLogService.Log("Updates", $"Store check: {_updates.Count} update(s), silent install {_context.CanSilentlyDownloadStorePackageUpdates}");
    return _updates.Count > 0;
  }

  public async Task<string> InstallAsync()
  {
    if (_context is not { CanSilentlyDownloadStorePackageUpdates: true } context || _updates.Count == 0)
      return InstallStates.OpenStore;

    try
    {
      var result = await context.TrySilentDownloadAndInstallStorePackageUpdatesAsync(_updates);
      DebugLogService.Log("Updates", $"Store install finished: {result.OverallState}");
      return result.OverallState == StorePackageUpdateState.Completed ? InstallStates.Installed : InstallStates.Failed;
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Updates", $"Store install failed: {ex.GetType().Name}: {ex.Message}");
      throw;
    }
  }

  private static async Task ScanAsync()
  {
    AppInstallManager manager;
    try
    {
      manager = new AppInstallManager();
      var item = await manager.SearchForUpdatesAsync(ProductId, string.Empty);
      DebugLogService.Log("Updates", $"Store scan: {item?.GetCurrentStatus().InstallState.ToString() ?? "no install item"}");
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Updates", $"Store scan failed: {ex.GetType().Name}: {ex.Message}");
      return;
    }

    try
    {
      await manager.SearchForAllUpdatesAsync();
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Updates", $"Store scan of all apps failed: {ex.GetType().Name}: {ex.Message}");
    }
  }
}
