using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCommandPaletteExtension.Helpers;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension;

internal sealed partial class HoobiBitwardenCommandPaletteExtensionPage : DynamicListPage, IDisposable
{
    private readonly BitwardenCliService _service;
    private readonly BitwardenSettingsManager? _settings;
    private IListItem[] _currentItems = [];
    private bool _initialized;
    private string _currentSearchText = string.Empty;

    public HoobiBitwardenCommandPaletteExtensionPage(BitwardenCliService service, BitwardenSettingsManager? settings = null)
    {
        _service = service;
        _settings = settings;
        _service.CacheUpdated += OnCacheUpdated;
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Bitwarden";
        Name = "Open";
        PlaceholderText = "Search your vault...";
    }

    public override IListItem[] GetItems()
    {
        if (!_initialized)
        {
            _initialized = true;
            _ = Task.Run(InitializeAsync);
        }

        return _currentItems;
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        _currentSearchText = newSearch;

        if (_service.IsCacheLoaded)
        {
            _currentItems = BuildListItems(_service.SearchCached(newSearch));
            RaiseItemsChanged();
            _service.TriggerBackgroundRefreshIfStale();
        }
        else
        {
            _ = Task.Run(async () =>
            {
                IsLoading = true;
                await _service.RefreshCacheAsync();
            });
        }
    }

    private void OnCacheUpdated()
    {
        var results = _service.SearchCached(_currentSearchText);
        _currentItems = BuildListItems(results);
        RaiseItemsChanged();
        IsLoading = false;
    }

    private async Task InitializeAsync()
    {
        IsLoading = true;

        var unlocked = await _service.CheckStatusAsync();
        if (!unlocked)
        {
            _currentItems = [BuildUnlockItem()];
            RaiseItemsChanged();
            IsLoading = false;
            return;
        }

        await _service.RefreshCacheAsync();
        _currentItems = BuildListItems(_service.SearchCached(null));
        RaiseItemsChanged();
        IsLoading = false;
    }

    private ListItem BuildUnlockItem() => new(new Pages.UnlockVaultPage(_service, _settings))
    {
        Title = "Vault is locked",
        Subtitle = "Click to unlock your Bitwarden vault",
        Icon = new IconInfo("\uE72E"),
    };

    private IListItem[] BuildListItems(List<BitwardenItem> items)
    {
        if (items.Count == 0)
            return [new ListItem(new NoOpCommand()) { Title = "No results found" }];

        return items.Select(BuildListItem).ToArray();
    }

    private IListItem BuildListItem(BitwardenItem item) => new ListItem(VaultItemHelper.GetDefaultCommand(item))
    {
        Title = item.Name,
        Subtitle = item.Subtitle,
        Icon = VaultItemHelper.GetIcon(item),
        MoreCommands = VaultItemHelper.BuildContextItems(item),
    };

    public void Dispose() => _service.CacheUpdated -= OnCacheUpdated;
}
