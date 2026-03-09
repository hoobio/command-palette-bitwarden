using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCommandPaletteExtension.Helpers;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension;

internal sealed partial class BitwardenFallbackItem : FallbackCommandItem, IDisposable
{
  private readonly BitwardenCliService _service;
  private readonly Lock _lock = new();
  private CancellationTokenSource? _cts;

  public BitwardenFallbackItem(BitwardenCliService service)
      : base(new NoOpCommand(), "Search Bitwarden", "hoobi.bitwarden.fallback")
  {
    _service = service;
    Title = string.Empty;
    Subtitle = string.Empty;
    Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
  }

  public override void UpdateQuery(string query)
  {
    CancellationToken ct;
    lock (_lock)
    {
      _cts?.Cancel();
      _cts?.Dispose();
      _cts = new CancellationTokenSource();
      ct = _cts.Token;
    }

    if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
    {
      ClearResult(ct);
      return;
    }

    if (_service.IsCacheLoaded)
    {
      var items = _service.SearchCached(query);
      HandleResults(items, query, ct);
      return;
    }

    _ = Task.Run(async () =>
    {
      try
      {
        if (!_service.IsUnlocked)
        {
          var status = await _service.GetVaultStatusAsync();
          if (status != VaultStatus.Unlocked)
          {
            ClearResult(ct);
            return;
          }
        }

        await _service.RefreshCacheAsync();
        if (ct.IsCancellationRequested)
          return;

        var items = _service.SearchCached(query);
        HandleResults(items, query, ct);
      }
      catch (OperationCanceledException) { }
      catch
      {
        if (!ct.IsCancellationRequested)
          ClearResult(ct);
      }
    }, ct);
  }

  private void HandleResults(List<BitwardenItem> items, string query, CancellationToken ct)
  {
    if (items.Count == 0)
      ClearResult(ct);
    else if (items.Count == 1)
      SetSingleResult(items[0], ct);
    else
      SetMultipleResult(query, items, ct);
  }

  private void ClearResult(CancellationToken ct)
  {
    lock (_lock)
    {
      if (ct.IsCancellationRequested)
        return;

      Title = string.Empty;
      Subtitle = string.Empty;
      Command = new NoOpCommand();
      MoreCommands = null!;
    }
  }

  private void SetSingleResult(BitwardenItem item, CancellationToken ct)
  {
    lock (_lock)
    {
      if (ct.IsCancellationRequested)
        return;

      Title = item.Name;
      Subtitle = item.Subtitle;
      Icon = VaultItemHelper.GetIcon(item);
      Command = VaultItemHelper.GetDefaultCommand(item);
      MoreCommands = VaultItemHelper.BuildContextItems(item);
    }
  }

  private void SetMultipleResult(string query, List<BitwardenItem> items, CancellationToken ct)
  {
    lock (_lock)
    {
      if (ct.IsCancellationRequested)
        return;

      Title = $"Bitwarden: {items.Count} results for \"{query}\"";
      Subtitle = "View all results";
      Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
      Command = new HoobiBitwardenCommandPaletteExtensionPage(_service);
      MoreCommands = null!;
    }
  }

  public void Dispose()
  {
    _cts?.Cancel();
    _cts?.Dispose();
  }
}
