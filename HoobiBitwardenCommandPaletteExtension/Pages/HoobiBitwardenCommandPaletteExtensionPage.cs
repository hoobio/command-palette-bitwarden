using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCommandPaletteExtension.Helpers;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Pages;
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
    private bool _deviceVerificationRequired;
    private ForegroundContext? _context;
    private DateTime _lastContextCapture = DateTime.MinValue;
    private Timer? _totpTimer;
    private List<(ListItem ListItem, BitwardenItem VaultItem, bool AllowContextTag)>? _totpItems;
    private Timer? _syncTimer;
    private ListItem? _syncItem;
    private readonly Timer _iconRefreshTimer;
    private int _repromptFailures;
    private DateTime _repromptCooldownUntil;

    public HoobiBitwardenCommandPaletteExtensionPage(BitwardenCliService service, BitwardenSettingsManager? settings = null)
    {
        _service = service;
        _settings = settings;
        _service.CacheUpdated += OnCacheUpdated;
        _service.StatusChanged += OnStatusChanged;
        _service.WarmupCompleted += OnWarmupCompleted;
        _service.AutoLocking += OnAutoLocking;
        _service.AutoLocked += OnAutoLocked;
        _service.CliConfigChanged += OnCliConfigChanged;
        AccessTracker.ItemAccessed += OnItemAccessed;
        FaviconService.IconCached += OnIconCached;
        RepromptPage.GraceStarted += OnRepromptGraceStarted;
        RepromptPage.VerificationRequested += OnVerificationRequested;
        _iconRefreshTimer = new Timer(OnIconRefreshTick, null, Timeout.Infinite, Timeout.Infinite);
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        var v = Windows.ApplicationModel.Package.Current.Id.Version;
        var version = $"{v.Major}.{v.Minor}.{v.Build}";
#if DEBUG
        Title = $"Bitwarden {version} (Dev)";
#else
        Title = $"Bitwarden {version}";
#endif
        Name = "Open";
        PlaceholderText = "Search your vault... (try is:fav, is:protected, folder:Work, has:totp, has:passkey, url:github)";
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
                CaptureContext(force: true);
                DebugLogService.Log("Page", $"GetItems: first call, LastStatus={_service.LastStatus}");

                // If warmup already ran, skip async init and show results immediately.
                if (_service.LastStatus != null)
                {
                    _initComplete = true;
                    switch (_service.LastStatus)
                    {
                        case VaultStatus.Unlocked when _service.IsCacheLoaded:
                            DebugLogService.Log("Page", $"GetItems: unlocked + cache loaded, {Search(_currentSearchText).Count} items");
                            _currentItems = BuildListItems(Search(_currentSearchText));
                            IsLoading = false;
                            break;
                        case VaultStatus.Unlocked:
                            // Vault open but warmup is still loading the cache.
                            _currentItems = BuildLoadingPlaceholder("Retrieving items from vault...", "bw list items");
                            IsLoading = true;
                            break;
                        case VaultStatus.Unauthenticated:
                            _currentItems = BuildUnauthenticatedItems();
                            IsLoading = false;
                            break;
                        case VaultStatus.Locked:
                            _currentItems = BuildLockedItems();
                            IsLoading = false;
                            break;
                        case VaultStatus.CliNotFound:
                            _currentItems = BuildCliNotFoundItems();
                            IsLoading = false;
                            break;
                        default:
                            _currentItems = [];
                            IsLoading = false;
                            break;
                    }
                    return _currentItems;
                }

                IsLoading = true;
                _currentItems = BuildLoadingPlaceholder("Checking vault status...", "bw status");
                DebugLogService.Log("Page", "GetItems: warmup not complete, starting InitializeAsync");
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

        if ((_twoFactorRequired || _deviceVerificationRequired) && !_handlingAction)
        {
            var code = newSearch.Trim();
            if (code.Length >= 6 && code.Length <= 8 && long.TryParse(code, out _))
            {
                if (_deviceVerificationRequired)
                    OnDeviceVerificationSubmitted(code);
                else
                    OnTwoFactorSubmitted(code);
                return;
            }

            _currentItems = BuildUnauthenticatedItems();
            RaiseItemsChanged();
            return;
        }

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
        DebugLogService.Log("Page", $"OnCacheUpdated: {results.Count} items");
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
        _initComplete = true;
        DebugLogService.Log("Page", $"RebuildForCurrentStatus: status={_service.LastStatus}, cacheLoaded={_service.IsCacheLoaded}");

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

        // Belt-and-suspenders: if the first RaiseItemsChanged fired before the SDK had
        // subscribed to ItemsChanged (i.e. before the palette was first opened), it lands
        // in the void. Re-raise after a short delay so the palette picks it up.
        _ = Task.Delay(300).ContinueWith(_ => RaiseItemsChanged(), TaskScheduler.Default);
    }

    private async Task InitializeAsync()
    {
        try
        {
            DebugLogService.Log("Page", "InitializeAsync: awaiting warmup");
            // Await the ongoing warmup instead of firing a concurrent bw status call,
            // which can cause the CLI to hang when both run simultaneously.
#pragma warning disable VSTHRD003 // WarmupTask is a ThreadPool task; InitializeAsync is already on ThreadPool via Task.Run
            await _service.WarmupTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003

            var status = _service.LastStatus;
            if (status is null)
                status = await _service.GetVaultStatusAsync().ConfigureAwait(false);
            DebugLogService.Log("Page", $"InitializeAsync: vault status={status}");

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

    private static IListItem[] WithDebugLog(IListItem[] items) =>
        DebugLogService.Enabled ? [.. items, BuildCopyDebugLogItem()] : items;

    private static IListItem[] BuildCliNotFoundItems() => WithDebugLog(
    [
        new ListItem(new OpenUrlCommand("https://bitwarden.com/help/cli/#download-and-install"))
        {
            Title = "Bitwarden CLI not found",
            Subtitle = "Install the Bitwarden CLI (bw) and ensure it's in your PATH",
            Icon = new IconInfo("\uE783"),
        },
    ]);

    private IListItem[] BuildUnauthenticatedItems()
    {
        if (_twoFactorRequired && _pendingEmail != null && _pendingPassword != null)
        {
            PlaceholderText = "Enter your 2FA code...";
            var hint = new ListItem(new NoOpCommand())
            {
                Title = "Two-Factor Authentication Required",
                Subtitle = _errorMessage ?? "Type your 6-digit code above and press Enter",
                Icon = new IconInfo("\uE8D7"),
            };
            if (_errorMessage != null)
                hint.Tags = [new Tag(_errorMessage) { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) }];
            return WithDebugLog([hint]);
        }

        if (_deviceVerificationRequired && _pendingEmail != null && _pendingPassword != null)
        {
            PlaceholderText = "Enter device verification code...";
            var hint = new ListItem(new NoOpCommand())
            {
                Title = "New Device Verification Required",
                Subtitle = _errorMessage ?? "Enter the OTP code sent to your login email",
                Icon = new IconInfo("\uE8D7"),
            };
            if (_errorMessage != null)
                hint.Tags = [new Tag(_errorMessage) { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) }];
            return WithDebugLog([hint]);
        }

        PlaceholderText = "Search your vault... (try is:fav, is:protected, folder:Work, has:totp, has:passkey, url:github)";

        var item = new ListItem(new Pages.LoginPage(_service, _settings, OnLoginSubmitted))
        {
            Title = "Login to Bitwarden",
            Subtitle = _errorMessage ?? "Sign in with your email and master password",
            Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png"),
        };

        if (_errorMessage != null)
            item.Tags = [new Tag(_errorMessage) { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) }];

        return WithDebugLog([item, BuildSetServerItem()]);
    }

    private IListItem[] BuildLockedItems() => WithDebugLog(
    [
        BuildUnlockItem(),
        BuildSetServerItem(),
        BuildLogoutItem(),
    ]);

    private ListItem BuildUnlockItem()
    {
        var item = new ListItem(new Pages.UnlockVaultPage(_service, _settings, OnUnlockSubmitted))
        {
            Title = "Unlock Vault",
            Subtitle = _errorMessage ?? "Vault is locked, select to unlock your Bitwarden vault",
            Icon = new IconInfo("\uE785"),
        };

        if (_errorMessage != null)
            item.Tags = [new Tag(_errorMessage) { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) }];

        return item;
    }

    private ListItem BuildSetServerItem() => new(new Pages.SetServerPage(_service, OnSetServerSubmitted))
    {
        Title = "Set Bitwarden Server",
        Subtitle = BitwardenCliService.ServerUrl ?? "bitwarden.com",
        Icon = new IconInfo("\uE774"),
    };

    private ListItem BuildLogoutItem() => new(new AnonymousCommand(OnLogoutRequested)
    { Name = "Logout", Result = CommandResult.KeepOpen() })
    {
        Title = "Logout of Bitwarden",
        Subtitle = "Log out and clear session",
        Icon = new IconInfo("\uEA56"),
    };

    private static ListItem BuildCopyDebugLogItem() => new(new AnonymousCommand(() =>
    {
        ClipboardHelper.SetText(DebugLogService.Export());
        Process.Start(new ProcessStartInfo("https://github.com/hoobio/command-palette-bitwarden/issues") { UseShellExecute = true });
    })
    { Name = "Copy Debug Log", Result = CommandResult.ShowToast("Copied debug log to clipboard") })
    {
        Title = "Copy Debug Log",
        Subtitle = $"Debug logging is ON | {DebugLogService.Count} entries captured",
        Icon = new IconInfo("\uE9D9"),
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
        var showProtectedTag = _settings?.ShowProtectedTag.Value != false;
        var showWebsiteIcons = _settings?.ShowWebsiteIcons.Value != false;
        var totpTracked = new List<(ListItem, BitwardenItem, bool)>();

        var contextLimit = int.TryParse(_settings?.ContextItemLimit.Value, out var lv) ? lv : 3;
        var contextTagsUsed = 0;
        var capContextTags = showContextTag && string.IsNullOrWhiteSpace(_currentSearchText) && contextLimit > 0;

        var search = (_currentSearchText ?? "").Trim();
        var boostSync = MatchesCommand(search, "sync");
        var boostLock = MatchesCommand(search, "lock");
        var boostLogout = MatchesCommand(search, "logout");
        var boostDebug = DebugLogService.Enabled && MatchesCommand(search, "debug");

        if (boostSync) list.Add(BuildSyncItem());
        if (boostLock) list.Add(BuildLockItem());
        if (boostLogout) list.Add(BuildLogoutItem());
        if (boostDebug) list.Add(BuildCopyDebugLogItem());

        if (items.Count == 0 && !boostSync && !boostLock && !boostLogout && !boostDebug)
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
                var listItem = BuildListItem(item, showWatchtower, allowContextTag, totpTagStyle, showPasskeyTag, showProtectedTag, showWebsiteIcons);
                list.Add(listItem);
                if (totpTagStyle == "live" && item.HasTotp)
                    totpTracked.Add((listItem, item, allowContextTag));
            }
        }

        if (!boostSync) list.Add(BuildSyncItem());
        list.Add(BuildSetServerItem());
        if (!boostLock) list.Add(BuildLockItem());
        if (!boostLogout) list.Add(BuildLogoutItem());
        if (DebugLogService.Enabled && !boostDebug)
            list.Add(BuildCopyDebugLogItem());

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

    private ListItem BuildListItem(BitwardenItem item, bool showWatchtower, bool showContextTag, string totpTagStyle, bool showPasskeyTag, bool showProtectedTag, bool showWebsiteIcons = true)
    {
        var listItem = new ListItem(VaultItemHelper.GetDefaultCommand(item, _service))
        {
            Title = item.Name,
            Subtitle = item.Subtitle,
            Icon = VaultItemHelper.GetIcon(item, showWebsiteIcons),
            MoreCommands = VaultItemHelper.BuildContextItems(item, _service),
        };

        var tags = VaultItemHelper.BuildTags(item, showWatchtower, _context, showContextTag, totpTagStyle, showPasskeyTag, showProtectedTag);
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
        _service.CliConfigChanged -= OnCliConfigChanged;
        AccessTracker.ItemAccessed -= OnItemAccessed;
        FaviconService.IconCached -= OnIconCached;
        RepromptPage.GraceStarted -= OnRepromptGraceStarted;
        RepromptPage.VerificationRequested -= OnVerificationRequested;
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
        var showProtectedTag = _settings?.ShowProtectedTag.Value != false;
        foreach (var (listItem, vaultItem, allowContextTag) in items)
            listItem.Tags = VaultItemHelper.BuildTags(vaultItem, showWatchtower, _context, allowContextTag, "live", showPasskeyTag, showProtectedTag);
    }

    private void OnAutoLocking()
    {
        _handlingAction = true;
        ShowLoadingStatus("Locking vault...", "bw lock");
    }

    private void OnCliConfigChanged()
    {
        _handlingAction = false;
        _initialLoadStarted = true;
        _initComplete = false;
        _twoFactorRequired = false;
        _deviceVerificationRequired = false;
        _pendingEmail = null;
        _pendingPassword = null;
        _errorMessage = null;
        IsLoading = true;
        ShowLoadingStatus("Checking vault status...", "bw status");
        _ = Task.Run(InitializeAsync);
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

    private void OnRepromptGraceStarted()
    {
        if (_service.LastStatus == VaultStatus.Unlocked && _service.IsCacheLoaded)
        {
            lock (_itemsLock)
                _currentItems = BuildListItems(Search(_currentSearchText));
            RaiseItemsChanged();
        }
    }

    private void OnVerificationRequested(VerificationRequest request)
    {
        if (DateTime.UtcNow < _repromptCooldownUntil)
        {
            var remaining = (int)(_repromptCooldownUntil - DateTime.UtcNow).TotalSeconds + 1;
            var status = new StatusMessage { Message = $"Too many failed attempts. Try again in {remaining}s.", State = MessageState.Error };
            ExtensionHost.ShowStatus(status, StatusContext.Page);
            _ = Task.Delay(3000).ContinueWith(_ => { try { ExtensionHost.HideStatus(status); } catch { } }, TaskScheduler.Default);
            return;
        }

        _handlingAction = true;
        IsLoading = true;
        ShowLoadingStatus("Verifying master password...", "bw unlock");

        _ = Task.Run(async () =>
        {
            try
            {
                var verified = await request.Service.VerifyMasterPasswordAsync(request.Password);
                if (verified)
                {
                    _repromptFailures = 0;
                    RepromptPage.RecordVerification();
                    request.InnerAction();
                    var status = new StatusMessage { Message = $"Copied {request.ActionLabel} to clipboard", State = MessageState.Success };
                    ExtensionHost.ShowStatus(status, StatusContext.Page);
                    _ = Task.Delay(3000).ContinueWith(_ => { try { ExtensionHost.HideStatus(status); } catch { } }, TaskScheduler.Default);
                }
                else
                {
                    _repromptFailures++;
                    if (_repromptFailures >= 5)
                        _repromptCooldownUntil = DateTime.UtcNow.AddSeconds(30);
                    var status = new StatusMessage { Message = "Incorrect master password", State = MessageState.Error };
                    ExtensionHost.ShowStatus(status, StatusContext.Page);
                    _ = Task.Delay(3000).ContinueWith(_ => { try { ExtensionHost.HideStatus(status); } catch { } }, TaskScheduler.Default);
                }

                lock (_itemsLock)
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

    private void OnLockRequested()
    {
        DebugLogService.Log("Action", "Lock requested by user");
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
        DebugLogService.Log("Action", "Logout requested by user");
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
        DebugLogService.Log("Action", "Sync requested by user");
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

    private void OnSetServerSubmitted(Models.ServerConfig config)
    {
        DebugLogService.Log("Action", "Set server URL submitted by user");
        _handlingAction = true;
        ClearSearchText();
        _errorMessage = null;
        IsLoading = true;
        ShowLoadingStatus("Setting server URL...", "bw config server");
        _ = Task.Run(async () =>
        {
            try
            {
                var status = _service.LastStatus;
                if (status == Services.VaultStatus.Locked || status == Services.VaultStatus.Unlocked)
                {
                    ShowLoadingStatus("Logging out before server change...", "bw logout");
                    await _service.LogoutAsync();
                }

                var error = await _service.SetServerUrlAsync(config);
                if (error != null)
                {
                    _errorMessage = error;
                    RebuildForCurrentStatus();
                    return;
                }

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
        DebugLogService.Log("Action", "Unlock submitted by user");
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
        DebugLogService.Log("Action", "Login submitted by user");
        _handlingAction = true;
        ClearSearchText();
        _errorMessage = null;
        _twoFactorRequired = false;
        _deviceVerificationRequired = false;
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
                var (success, error, twoFactorRequired, deviceVerificationRequired) = await _service.LoginAsync(email, password, null);
                if (!success)
                {
                    if (twoFactorRequired)
                    {
                        _twoFactorRequired = true;
                        _pendingEmail = email;
                        _pendingPassword = password;
                        _errorMessage = null;
                    }
                    else if (deviceVerificationRequired)
                    {
                        _deviceVerificationRequired = true;
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

                ShowLoadingStatus("Syncing vault...", "bw sync");
                await _service.SyncVaultAsync();
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
        DebugLogService.Log("Action", "2FA code submitted by user");
        _handlingAction = true;
        ClearSearchText();
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
                var (success, error, _, _) = await _service.LoginAsync(email!, password!, twoFactorCode);
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
                PlaceholderText = "Search your vault... (try is:fav, is:protected, folder:Work, has:totp, has:passkey, url:github)";
                ShowLoadingStatus("Syncing vault...", "bw sync");
                await _service.SyncVaultAsync();
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

    private void OnDeviceVerificationSubmitted(string otpCode)
    {
        DebugLogService.Log("Action", "Device verification OTP submitted by user");
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
                ShowLoadingStatus("Verifying device...", "bw login");
                var (success, error) = await _service.SubmitDeviceVerificationAsync(otpCode);
                if (!success)
                {
                    _errorMessage = error ?? "Verification failed — try again";
                    _currentItems = BuildUnauthenticatedItems();
                    RaiseItemsChanged();
                    return;
                }

                _deviceVerificationRequired = false;
                _pendingEmail = null;
                _pendingPassword = null;
                PlaceholderText = "Search your vault... (try is:fav, is:protected, folder:Work, has:totp, has:passkey, url:github)";
                ShowLoadingStatus("Syncing vault...", "bw sync");
                await _service.SyncVaultAsync();
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
