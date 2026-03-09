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
    private bool _initialLoadStarted;
    private bool _handlingAction;
    private string _currentSearchText = string.Empty;
    private string? _errorMessage;

    public HoobiBitwardenCommandPaletteExtensionPage(BitwardenCliService service, BitwardenSettingsManager? settings = null)
    {
        _service = service;
        _settings = settings;
        _service.CacheUpdated += OnCacheUpdated;
        _service.StatusChanged += OnStatusChanged;
        _service.WarmupCompleted += OnWarmupCompleted;
        AccessTracker.ItemAccessed += OnItemAccessed;
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Bitwarden";
        Name = "Open";
        PlaceholderText = "Search your vault...";
    }

    public override IListItem[] GetItems()
    {
        if (_currentItems.Length > 0)
        {
            IsLoading = false;
            return _currentItems;
        }

        if (!_initialLoadStarted)
        {
            _initialLoadStarted = true;
            IsLoading = true;
            _currentItems = BuildLoadingPlaceholder("Checking vault status...", "bw status");
            _ = Task.Run(InitializeAsync);
        }

        return _currentItems;
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (oldSearch == newSearch && _currentItems.Length > 0)
            return;

        _currentSearchText = newSearch;

        if (_service.LastStatus != VaultStatus.Unlocked)
            return;

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
        if (_handlingAction) return;
        var results = _service.SearchCached(_currentSearchText);
        _currentItems = BuildListItems(results);
        RaiseItemsChanged();
        IsLoading = false;
    }

    private void OnStatusChanged()
    {
        if (!_handlingAction) RebuildForCurrentStatus();
    }

    private void OnWarmupCompleted()
    {
        if (!_handlingAction) RebuildForCurrentStatus();
    }

    private void RebuildForCurrentStatus()
    {
        IsLoading = false;

        switch (_service.LastStatus)
        {
            case VaultStatus.Unlocked when _service.IsCacheLoaded:
                _currentItems = BuildListItems(_service.SearchCached(_currentSearchText));
                break;
            case VaultStatus.Unauthenticated:
                _currentItems = BuildUnauthenticatedItems();
                break;
            case VaultStatus.Locked:
                _currentItems = BuildLockedItems();
                break;
            case VaultStatus.CliNotFound:
                _currentItems = BuildCliNotFoundItems();
                break;
            default:
                _currentItems = [];
                break;
        }

        RaiseItemsChanged();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var status = _service.LastStatus;
            if (status is null)
                status = await _service.GetVaultStatusAsync();

            switch (status)
            {
                case VaultStatus.CliNotFound:
                    _currentItems = BuildCliNotFoundItems();
                    break;
                case VaultStatus.Unauthenticated:
                    _currentItems = BuildUnauthenticatedItems();
                    break;
                case VaultStatus.Locked:
                    _currentItems = BuildLockedItems();
                    break;
                case VaultStatus.Unlocked:
                    if (!_service.IsCacheLoaded)
                    {
                        ShowLoadingStatus("Retrieving items from vault...", "bw list items");
                        await _service.RefreshCacheAsync();
                    }
                    _currentItems = BuildListItems(_service.SearchCached(null));
                    break;
            }

            RaiseItemsChanged();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static IListItem[] BuildCliNotFoundItems() =>
    [
        new ListItem(new OpenUrlCommand("https://bitwarden.com/help/cli/#download-and-install"))
        {
            Title = "Bitwarden CLI not found",
            Subtitle = "Install the Bitwarden CLI (bw) and ensure it's in your PATH",
            Icon = new IconInfo("\uE783"),
        },
    ];

    private IListItem[] BuildUnauthenticatedItems()
    {
        var item = new ListItem(new Pages.LoginPage(_service, _settings, OnLoginSubmitted))
        {
            Title = "Login to Bitwarden",
            Subtitle = _errorMessage ?? "Sign in with your email and master password",
            Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png"),
        };

        if (_errorMessage != null)
            item.Tags = [new Tag(_errorMessage) { Foreground = ColorHelpers.FromRgb(0xFF, 0x44, 0x44) }];

        return [item, BuildSetServerItem()];
    }

    private IListItem[] BuildLockedItems() =>
    [
        BuildUnlockItem(),
        BuildSetServerItem(),
        BuildLogoutItem(),
    ];

    private ListItem BuildUnlockItem()
    {
        var item = new ListItem(new Pages.UnlockVaultPage(_service, _settings, OnUnlockSubmitted))
        {
            Title = "Vault is locked",
            Subtitle = _errorMessage ?? "Click to unlock your Bitwarden vault",
            Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png"),
        };

        if (_errorMessage != null)
            item.Tags = [new Tag(_errorMessage) { Foreground = ColorHelpers.FromRgb(0xFF, 0x44, 0x44) }];

        return item;
    }

    private ListItem BuildSetServerItem() => new(new Pages.SetServerPage(_service, OnSetServerSubmitted))
    {
        Title = "Set Bitwarden Server",
        Subtitle = BitwardenCliService.ServerUrl ?? "https://vault.bitwarden.com",
        Icon = new IconInfo("\uE774"),
    };

    private ListItem BuildLogoutItem() => new(new AnonymousCommand(OnLogoutRequested)
    { Name = "Logout", Result = CommandResult.KeepOpen() })
    {
        Title = "Logout of Bitwarden",
        Subtitle = "Log out and clear session",
        Icon = new IconInfo("\uEA56"),
    };

    private ListItem BuildLockItem() => new(new AnonymousCommand(OnLockRequested)
    { Name = "Lock", Result = CommandResult.KeepOpen() })
    {
        Title = "Lock Bitwarden",
        Subtitle = "Lock the vault and clear cached items",
        Icon = new IconInfo("\uE72E"),
    };

    private IListItem[] BuildListItems(List<BitwardenItem> items)
    {
        var list = new List<IListItem>();

        if (items.Count == 0)
            list.Add(new ListItem(new NoOpCommand()) { Title = "No results found" });
        else
            list.AddRange(items.Select(BuildListItem));

        list.Add(BuildSetServerItem());
        list.Add(BuildLockItem());
        list.Add(BuildLogoutItem());
        return list.ToArray();
    }

    private IListItem BuildListItem(BitwardenItem item) => new ListItem(VaultItemHelper.GetDefaultCommand(item))
    {
        Title = item.Name,
        Subtitle = item.Subtitle,
        Icon = VaultItemHelper.GetIcon(item),
        MoreCommands = VaultItemHelper.BuildContextItems(item),
    };

    public void Dispose()
    {
        _service.CacheUpdated -= OnCacheUpdated;
        _service.StatusChanged -= OnStatusChanged;
        _service.WarmupCompleted -= OnWarmupCompleted;
        AccessTracker.ItemAccessed -= OnItemAccessed;
    }

    private void OnItemAccessed()
    {
        if (_service.LastStatus == VaultStatus.Unlocked && _service.IsCacheLoaded)
            _currentItems = BuildListItems(_service.SearchCached(_currentSearchText));
    }

    private void OnLockRequested()
    {
        _handlingAction = true;
        _errorMessage = null;
        IsLoading = true;
        ShowLoadingStatus("Locking vault...", "bw lock");
        _ = Task.Run(async () =>
        {
            try
            {
                await _service.LockAsync();
                _currentItems = BuildLockedItems();
                RaiseItemsChanged();
            }
            finally
            {
                _handlingAction = false;
                IsLoading = false;
            }
        });
    }

    private void OnLogoutRequested()
    {
        _handlingAction = true;
        _errorMessage = null;
        IsLoading = true;
        ShowLoadingStatus("Logging out...", "bw logout");
        _ = Task.Run(async () =>
        {
            try
            {
                await _service.LogoutAsync();
                _currentItems = BuildUnauthenticatedItems();
                RaiseItemsChanged();
            }
            finally
            {
                _handlingAction = false;
                IsLoading = false;
            }
        });
    }

    private void OnSetServerSubmitted(string url)
    {
        _handlingAction = true;
        _errorMessage = null;
        IsLoading = true;
        ShowLoadingStatus("Setting server URL...", "bw config server");
        _ = Task.Run(async () =>
        {
            try
            {
                var error = await _service.SetServerUrlAsync(url);
                if (error != null)
                {
                    _errorMessage = error;
                    RebuildForCurrentStatus();
                    return;
                }

                ShowLoadingStatus("Checking vault status...", "bw status");
                await _service.GetVaultStatusAsync();
                RebuildForCurrentStatus();
            }
            finally
            {
                _handlingAction = false;
                IsLoading = false;
            }
        });
    }

    private void OnUnlockSubmitted(string password)
    {
        _errorMessage = null;
        _currentItems = [];
        IsLoading = true;
        RaiseItemsChanged();

        _ = Task.Run(async () =>
        {
            ShowLoadingStatus("Unlocking vault...", "bw unlock");
            var (success, error) = await _service.UnlockAsync(password);
            if (!success)
            {
                if (_service.LastStatus == VaultStatus.Unauthenticated)
                {
                    _errorMessage = "You are not logged in";
                    _currentItems = BuildUnauthenticatedItems();
                }
                else
                {
                    _errorMessage = error?.Contains("key", StringComparison.OrdinalIgnoreCase) == true
                        ? "Invalid password entered"
                        : error ?? "Unlock failed";
                    _currentItems = BuildLockedItems();
                }

                RaiseItemsChanged();
                IsLoading = false;
                return;
            }

            ShowLoadingStatus("Retrieving items from vault...", "bw list items");
            await _service.RefreshCacheAsync();
            _currentItems = BuildListItems(_service.SearchCached(null));
            RaiseItemsChanged();
            IsLoading = false;
        });
    }

    private void OnLoginSubmitted(string email, string password, string? twoFactorCode)
    {
        _errorMessage = null;
        _currentItems = [];
        IsLoading = true;
        RaiseItemsChanged();

        _ = Task.Run(async () =>
        {
            ShowLoadingStatus("Logging in...", "bw login");
            var (success, error, twoFactorRequired) = await _service.LoginAsync(email, password, twoFactorCode);
            if (!success)
            {
                if (twoFactorRequired && string.IsNullOrEmpty(twoFactorCode))
                    _errorMessage = "Two-factor authentication required - enter your 2FA code";
                else
                    _errorMessage = error ?? "Login failed";

                _currentItems = BuildUnauthenticatedItems();
                RaiseItemsChanged();
                IsLoading = false;
                return;
            }

            ShowLoadingStatus("Retrieving items from vault...", "bw list items");
            await _service.RefreshCacheAsync();
            _currentItems = BuildListItems(_service.SearchCached(null));
            RaiseItemsChanged();
            IsLoading = false;
        });
    }

    private void ShowLoadingStatus(string title, string command)
    {
        _currentItems = BuildLoadingPlaceholder(title, command);
        RaiseItemsChanged();
    }

    private static IListItem[] BuildLoadingPlaceholder(string title, string command) =>
    [
        new ListItem(new NoOpCommand())
        {
            Title = title,
            Subtitle = $"Running: {command}",
            Icon = new IconInfo("\uE895"),
        },
    ];
}
