using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    private readonly Lock _itemsLock = new();
    private IListItem[] _currentItems = [];
    private bool _initialLoadStarted;
    private volatile bool _initComplete;
    private volatile bool _handlingAction;
    private string _currentSearchText = string.Empty;
    private string? _errorMessage;
    private string? _pendingEmail;
    private string? _pendingPassword;
    private bool _twoFactorRequired;
    private ForegroundContext? _context;
    private DateTime _lastContextCapture = DateTime.MinValue;
    private Timer? _totpTimer;
    private List<(ListItem ListItem, BitwardenItem VaultItem, bool AllowContextTag)>? _totpItems;
    private Timer? _syncTimer;
    private ListItem? _syncItem;
    private readonly Timer _iconRefreshTimer;

    public HoobiBitwardenCommandPaletteExtensionPage(BitwardenCliService service, BitwardenSettingsManager? settings = null)
    {
        _service = service;
        _settings = settings;
        _service.CacheUpdated += OnCacheUpdated;
        _service.StatusChanged += OnStatusChanged;
        _service.WarmupCompleted += OnWarmupCompleted;
        _service.AutoLocking += OnAutoLocking;
        _service.AutoLocked += OnAutoLocked;
        AccessTracker.ItemAccessed += OnItemAccessed;
        FaviconService.IconCached += OnIconCached;
        _iconRefreshTimer = new Timer(OnIconRefreshTick, null, Timeout.Infinite, Timeout.Infinite);
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Bitwarden";
        Name = "Open";
        PlaceholderText = "Search your vault... (try is:fav, folder:Work, has:totp, has:passkey, url:github)";
        CaptureContext();
    }

    private bool CaptureContext(bool force = false)
    {
        if (!force && (DateTime.UtcNow - _lastContextCapture).TotalMilliseconds < 500)
            return false;

        try
        {
            _lastContextCapture = DateTime.UtcNow;
            _context = _settings?.ContextAwareness.Value != false
                ? ContextAwarenessService.CaptureContext()
                : null;
            return true;
        }
        catch { return false; }
    }

    public override IListItem[] GetItems()
    {
        lock (_itemsLock)
        {
            if (!_initialLoadStarted)
            {
                _initialLoadStarted = true;
                IsLoading = true;
                CaptureContext(force: true);
                _currentItems = BuildLoadingPlaceholder("Checking vault status...", "bw status");
                _ = Task.Run(InitializeAsync);
                return _currentItems;
            }

            if (CaptureContext(force: true) && !_handlingAction && _service.LastStatus == VaultStatus.Unlocked && _service.IsCacheLoaded)
            {
                _currentItems = BuildListItems(Search(_currentSearchText));
            }

            if (_initComplete && !_handlingAction)
                IsLoading = false;
            return _currentItems;
        }
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        CaptureContext(force: true);
        _currentSearchText = newSearch;

        if (_service.IsUnlocked)
            _service.ResetAutoLockTimer();

        if (_handlingAction || _service.LastStatus != VaultStatus.Unlocked)
            return;

        if (_service.IsCacheLoaded)
        {
            _currentItems = BuildListItems(Search(newSearch));
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
        CaptureContext();
        var results = Search(_currentSearchText);
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
                _currentItems = BuildListItems(Search(_currentSearchText));
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
                    if (_service.IsCacheLoaded)
                        _currentItems = BuildListItems(Search(_currentSearchText));
                    else
                        // RefreshCacheAsync returned early because warmup held the lock.
                        // CacheUpdated/WarmupCompleted will fire shortly and show items.
                        return;
                    break;
            }

            RaiseItemsChanged();
        }
        catch (InvalidOperationException)
        {
            RebuildForCurrentStatus();
        }
        finally
        {
            _initComplete = true;
            // If awaiting warmup (early-return path), keep IsLoading=true so the spinner
            // stays visible until CacheUpdated/WarmupCompleted fires.
            if (_service.IsCacheLoaded || _service.LastStatus != VaultStatus.Unlocked)
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
        if (_twoFactorRequired && _pendingEmail != null && _pendingPassword != null)
        {
            var twoFactorItem = new ListItem(new Pages.TwoFactorPage(OnTwoFactorSubmitted))
            {
                Title = "Two-Factor Authentication Required",
                Subtitle = _errorMessage ?? "Enter the code from your authenticator app",
                Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png"),
            };
            if (_errorMessage != null)
                twoFactorItem.Tags = [new Tag(_errorMessage) { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) }];
            return [twoFactorItem];
        }

        var item = new ListItem(new Pages.LoginPage(_service, _settings, OnLoginSubmitted))
        {
            Title = "Login to Bitwarden",
            Subtitle = _errorMessage ?? "Sign in with your email and master password",
            Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png"),
        };

        if (_errorMessage != null)
            item.Tags = [new Tag(_errorMessage) { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) }];

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
            item.Tags = [new Tag(_errorMessage) { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) }];

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

    private ListItem BuildSyncItem()
    {
        var item = new ListItem(new AnonymousCommand(OnSyncRequested)
        { Name = "Sync", Result = CommandResult.KeepOpen() })
        {
            Title = "Sync Vault",
            Subtitle = GetSyncSubtitle(),
            Icon = new IconInfo("\uE895"),
        };
        _syncItem = item;
        return item;
    }

    private string GetSyncSubtitle()
    {
        var last = _service.LastRefresh;
        if (last == default) return "Force sync and refresh vault items from server";
        return $"Last synced: {FormatAge(DateTime.UtcNow - last)}";
    }

    internal static string FormatAge(TimeSpan age) => age.TotalSeconds switch
    {
        < 5 => "just now",
        < 60 => $"{(int)age.TotalSeconds} seconds ago",
        < 120 => "1 minute ago",
        < 3600 => $"{(int)age.TotalMinutes} minutes ago",
        < 7200 => "1 hour ago",
        _ => $"{(int)age.TotalHours} hours ago",
    };

    private List<BitwardenItem> Search(string? query = null)
    {
        var limit = int.TryParse(_settings?.ContextItemLimit.Value, out var v) ? v : 3;
        return _service.SearchCached(query, _context, limit);
    }

    internal static bool MatchesCommand(string search, string command)
        => search.Length >= 2 && command.StartsWith(search, StringComparison.OrdinalIgnoreCase);

    private IListItem[] BuildListItems(List<BitwardenItem> items)
    {
        var list = new List<IListItem>();
        var showWatchtower = _settings?.ShowWatchtowerTags.Value != false;
        var showContextTag = _settings?.ShowContextTag.Value != false;
        var totpTagStyle = _settings?.TotpTagStyle.Value ?? "off";
        var showPasskeyTag = _settings?.ShowPasskeyTag.Value != false;
        var showWebsiteIcons = _settings?.ShowWebsiteIcons.Value != false;
        var totpTracked = new List<(ListItem, BitwardenItem, bool)>();

        var contextLimit = int.TryParse(_settings?.ContextItemLimit.Value, out var lv) ? lv : 3;
        var contextTagsUsed = 0;
        var capContextTags = showContextTag && string.IsNullOrWhiteSpace(_currentSearchText) && contextLimit > 0;

        var search = (_currentSearchText ?? "").Trim();
        var boostSync = MatchesCommand(search, "sync");
        var boostLock = MatchesCommand(search, "lock");
        var boostLogout = MatchesCommand(search, "logout");

        if (boostSync) list.Add(BuildSyncItem());
        if (boostLock) list.Add(BuildLockItem());
        if (boostLogout) list.Add(BuildLogoutItem());

        if (items.Count == 0 && !boostSync && !boostLock && !boostLogout)
            list.Add(new ListItem(new NoOpCommand()) { Title = "No results found" });
        else
        {
            foreach (var item in items)
            {
                var allowContextTag = showContextTag;
                if (capContextTags)
                {
                    var isContextMatch = _context != null && ContextAwarenessService.ContextScore(_context, item) > 0;
                    allowContextTag = isContextMatch && contextTagsUsed < contextLimit;
                    if (allowContextTag) contextTagsUsed++;
                }
                var listItem = BuildListItem(item, showWatchtower, allowContextTag, totpTagStyle, showPasskeyTag, showWebsiteIcons);
                list.Add(listItem);
                if (totpTagStyle == "live" && item.HasTotp)
                    totpTracked.Add((listItem, item, allowContextTag));
            }
        }

        if (!boostSync) list.Add(BuildSyncItem());
        list.Add(BuildSetServerItem());
        if (!boostLock) list.Add(BuildLockItem());
        if (!boostLogout) list.Add(BuildLogoutItem());

        _totpItems = totpTracked.Count > 0 ? totpTracked : null;
        if (_totpItems != null)
            _totpTimer ??= new Timer(OnTotpTimerTick, null, 1000, 1000);
        else
        {
            _totpTimer?.Dispose();
            _totpTimer = null;
        }

        _syncTimer ??= new Timer(OnSyncTimerTick, null, 10000, 10000);

        return list.ToArray();
    }

    private ListItem BuildListItem(BitwardenItem item, bool showWatchtower, bool showContextTag, string totpTagStyle, bool showPasskeyTag, bool showWebsiteIcons = true)
    {
        var listItem = new ListItem(VaultItemHelper.GetDefaultCommand(item))
        {
            Title = item.Name,
            Subtitle = item.Subtitle,
            Icon = VaultItemHelper.GetIcon(item, showWebsiteIcons),
            MoreCommands = VaultItemHelper.BuildContextItems(item),
        };

        var tags = VaultItemHelper.BuildTags(item, showWatchtower, _context, showContextTag, totpTagStyle, showPasskeyTag);
        if (tags.Length > 0)
            listItem.Tags = tags;

        return listItem;
    }

    public void Dispose()
    {
        _totpTimer?.Dispose();
        _syncTimer?.Dispose();
        _iconRefreshTimer.Dispose();
        _service.CacheUpdated -= OnCacheUpdated;
        _service.StatusChanged -= OnStatusChanged;
        _service.WarmupCompleted -= OnWarmupCompleted;
        _service.AutoLocking -= OnAutoLocking;
        _service.AutoLocked -= OnAutoLocked;
        AccessTracker.ItemAccessed -= OnItemAccessed;
        FaviconService.IconCached -= OnIconCached;
    }

    private void OnIconCached() => _iconRefreshTimer.Change(500, Timeout.Infinite);

    private void OnIconRefreshTick(object? _)
    {
        if (_handlingAction || _service.LastStatus != VaultStatus.Unlocked || !_service.IsCacheLoaded)
            return;
        _currentItems = BuildListItems(Search(_currentSearchText));
        RaiseItemsChanged();
    }

    private void OnSyncTimerTick(object? state)
    {
        if (_syncItem is { } item)
            item.Subtitle = GetSyncSubtitle();
    }

    private void OnTotpTimerTick(object? state)
    {
        var items = _totpItems;
        if (items == null) return;

        var showWatchtower = _settings?.ShowWatchtowerTags.Value != false;
        var showPasskeyTag = _settings?.ShowPasskeyTag.Value != false;
        foreach (var (listItem, vaultItem, allowContextTag) in items)
            listItem.Tags = VaultItemHelper.BuildTags(vaultItem, showWatchtower, _context, allowContextTag, "live", showPasskeyTag);
    }

    private void OnAutoLocking()
    {
        _handlingAction = true;
        ShowLoadingStatus("Locking vault...", "bw lock");
    }

    private void OnAutoLocked()
    {
        _currentItems = BuildLockedItems();
        RaiseItemsChanged();
        _handlingAction = false;
    }

    private void OnItemAccessed()
    {
        if (_service.LastStatus == VaultStatus.Unlocked && _service.IsCacheLoaded)
        {
            lock (_itemsLock)
                _currentItems = BuildListItems(Search(_currentSearchText));
            RaiseItemsChanged();
        }
    }

    private void OnLockRequested()
    {
        _handlingAction = true;
        ClearSearchText();
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
        ClearSearchText();
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

    private void OnSyncRequested()
    {
        _handlingAction = true;
        ClearSearchText();
        IsLoading = true;
        ShowLoadingStatus("Syncing vault...", "bw sync");
        _ = Task.Run(async () =>
        {
            try
            {
                await _service.SyncVaultAsync();
                _currentItems = BuildListItems(Search(_currentSearchText));
                RaiseItemsChanged();
            }
            catch (TimeoutException)
            {
                _currentItems = BuildListItems(Search(_currentSearchText));
                RaiseItemsChanged();
            }
            catch (InvalidOperationException)
            {
                RebuildForCurrentStatus();
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
        ClearSearchText();
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
        _handlingAction = true;
        ClearSearchText();
        _errorMessage = null;
        _currentItems = [];
        IsLoading = true;
        RaiseItemsChanged();

        _ = Task.Run(async () =>
        {
            try
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
                    return;
                }

                ShowLoadingStatus("Retrieving items from vault...", "bw list items");
                await _service.RefreshCacheAsync();
                _currentItems = BuildListItems(Search(_currentSearchText));
                RaiseItemsChanged();
            }
            finally
            {
                _handlingAction = false;
                IsLoading = false;
            }
        });
    }

    private void OnLoginSubmitted(string email, string password)
    {
        _handlingAction = true;
        ClearSearchText();
        _errorMessage = null;
        _twoFactorRequired = false;
        _pendingEmail = null;
        _pendingPassword = null;
        _currentItems = [];
        IsLoading = true;
        RaiseItemsChanged();

        _ = Task.Run(async () =>
        {
            try
            {
                ShowLoadingStatus("Logging in...", "bw login");
                var (success, error, twoFactorRequired) = await _service.LoginAsync(email, password, null);
                if (!success)
                {
                    if (twoFactorRequired)
                    {
                        _twoFactorRequired = true;
                        _pendingEmail = email;
                        _pendingPassword = password;
                        _errorMessage = null;
                    }
                    else
                    {
                        _errorMessage = error ?? "Login failed";
                    }

                    _currentItems = BuildUnauthenticatedItems();
                    RaiseItemsChanged();
                    return;
                }

                ShowLoadingStatus("Retrieving items from vault...", "bw list items");
                await _service.RefreshCacheAsync();
                _currentItems = BuildListItems(Search(_currentSearchText));
                RaiseItemsChanged();
            }
            finally
            {
                _handlingAction = false;
                IsLoading = false;
            }
        });
    }

    private void OnTwoFactorSubmitted(string twoFactorCode)
    {
        _handlingAction = true;
        var email = _pendingEmail;
        var password = _pendingPassword;
        _errorMessage = null;
        _currentItems = [];
        IsLoading = true;
        RaiseItemsChanged();

        _ = Task.Run(async () =>
        {
            try
            {
                ShowLoadingStatus("Verifying 2FA code...", "bw login");
                var (success, error, _) = await _service.LoginAsync(email!, password!, twoFactorCode);
                if (!success)
                {
                    _errorMessage = error?.Contains("Code", StringComparison.OrdinalIgnoreCase) == true
                        ? "Invalid 2FA code — try again"
                        : error ?? "Verification failed";
                    _currentItems = BuildUnauthenticatedItems();
                    RaiseItemsChanged();
                    return;
                }

                _twoFactorRequired = false;
                _pendingEmail = null;
                _pendingPassword = null;
                ShowLoadingStatus("Retrieving items from vault...", "bw list items");
                await _service.RefreshCacheAsync();
                _currentItems = BuildListItems(Search(_currentSearchText));
                RaiseItemsChanged();
            }
            finally
            {
                _handlingAction = false;
                IsLoading = false;
            }
        });
    }

    private void ShowLoadingStatus(string title, string command)
    {
        _currentItems = BuildLoadingPlaceholder(title, command);
        RaiseItemsChanged();
    }

    // DynamicListPage.SearchText setter calls SetSearchNoUpdate (no PropertyChanged event).
    // Call OnPropertyChanged explicitly so the host updates the visible search box.
    private void ClearSearchText()
    {
        SetSearchNoUpdate(string.Empty);
        _currentSearchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
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
