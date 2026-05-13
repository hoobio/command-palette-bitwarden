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
    private int? _pendingTwoFactorMethod;
    private bool _twoFactorRequired;
    private bool _deviceVerificationRequired;
    private ForegroundContext? _context;
    private DateTime _lastContextCapture = DateTime.MinValue;
    private Timer? _totpTimer;
    private List<(ListItem ListItem, BitwardenItem VaultItem, Tag[] BaseTags)>? _totpItems;
    private Timer? _syncTimer;
    private ListItem? _syncItem;
    private readonly Timer _iconRefreshTimer;
    private readonly Timer _searchDebounceTimer;
    // Debounce window for the unlocked-vault search rebuild. Each keystroke
    // resets the timer; the rebuild only runs after the user pauses for this
    // long. BuildListItems allocates 5-15 commands per result, so doing it
    // per keystroke (with no debounce) makes typing feel laggy on larger
    // vaults. 100ms is the sweet spot used by most palette UIs (VS Code,
    // Spotlight): below the threshold of perceived input delay, but enough
    // to collapse a typed word into a single rebuild.
    private const int SearchDebounceMs = 100;
    // IconCached can fire many times per second during a cold vault load (each of
    // ~580 logins resolves to its own callback). A pure trailing-edge debounce
    // would defer the rebuild until the entire load finishes, hiding progress for
    // ~10-30s on a slow connection. Instead, coalesce within IconRefreshDebounceMs,
    // but force a rebuild every IconRefreshMaxWaitMs so the user sees icons appear
    // incrementally during a long fetch run.
    private const int IconRefreshDebounceMs = 500;
    private const int IconRefreshMaxWaitMs = 2000;
    private DateTime _iconRefreshFirstQueuedAt;
    private readonly Lock _iconRefreshLock = new();
    private StatusMessage? _lastBiometricStatus;
    private volatile bool _biometricClickFailed;
    private volatile bool _autoBiometricTriggered;

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
        RepromptPage.BiometricRequested += OnRepromptBiometricRequested;
        _iconRefreshTimer = new Timer(OnIconRefreshTick, null, Timeout.Infinite, Timeout.Infinite);
        _searchDebounceTimer = new Timer(OnSearchDebounceTick, null, Timeout.Infinite, Timeout.Infinite);
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
                            _ = Task.Run(TryAutoTriggerBiometric);
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

            // Use the throttled capture (default 500ms) so repeated GetItems
            // calls from the host don't trigger a fresh window enumeration
            // every time. The first GetItems above already forces a refresh.
            if (CaptureContext() && !_handlingAction && _service.LastStatus == VaultStatus.Unlocked && _service.IsCacheLoaded)
            {
                _currentItems = BuildListItems(Search(_currentSearchText));
            }

            if (_initComplete && !_handlingAction)
                IsLoading = false;

            if (_service.LastStatus == VaultStatus.Locked && !_handlingAction)
                _ = Task.Run(TryAutoTriggerBiometric);

            return _currentItems;
        }
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        // Don't recapture foreground context here. The user is focused on the
        // palette while typing, so the context can't have changed since the
        // last GetItems call. CaptureContext does Win32 window enumeration and
        // a UIA/COM round-trip per browser window, which adds visible lag to
        // every keystroke.
        _currentSearchText = newSearch;

        if (_service.IsUnlocked)
            _service.ResetAutoLockTimer();

        if (_handlingAction || _service.LastStatus != VaultStatus.Unlocked)
        {
            DebugLogService.Log("Page", $"UpdateSearchText skipped: handlingAction={_handlingAction}, status={_service.LastStatus}, search='{newSearch}'");
        }

        if ((_twoFactorRequired || _deviceVerificationRequired) && !_handlingAction)
        {
            _currentItems = BuildUnauthenticatedItems();
            RaiseItemsChanged();
            return;
        }

        if (_handlingAction || _service.LastStatus != VaultStatus.Unlocked)
            return;

        if (_service.IsCacheLoaded)
        {
            // Debounce: each keystroke just (re)arms the timer. The actual
            // rebuild + RaiseItemsChanged runs once the user pauses typing.
            _searchDebounceTimer.Change(SearchDebounceMs, Timeout.Infinite);
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

    private void OnSearchDebounceTick(object? _)
    {
        // Re-check state at fire time: the vault may have locked, an action
        // may be in flight, or the cache may have been invalidated since the
        // keystroke that armed the timer.
        if (_handlingAction || _service.LastStatus != VaultStatus.Unlocked || !_service.IsCacheLoaded)
            return;
        _currentItems = BuildListItems(Search(_currentSearchText));
        RaiseItemsChanged();
    }

    private void OnCacheUpdated()
    {
        if (_handlingAction) return;
        CaptureContext();
        var results = Search(_currentSearchText);
        DebugLogService.Log("Page", $"OnCacheUpdated: {results.Count} items");
        _currentItems = BuildListItems(results);
        _initComplete = true;
        IsLoading = false;
        RaiseItemsChanged();
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
                _biometricClickFailed = false;
                _autoBiometricTriggered = false;
                HideBiometricStatus();
                _currentItems = BuildListItems(Search(_currentSearchText));
                break;
            case VaultStatus.Unauthenticated:
                _biometricClickFailed = false;
                _autoBiometricTriggered = false;
                HideBiometricStatus();
                _currentItems = BuildUnauthenticatedItems();
                break;
            case VaultStatus.Locked:
                _currentItems = BuildLockedItems();
                TryAutoTriggerBiometric();
                break;
            case VaultStatus.CliNotFound:
                _currentItems = BuildCliNotFoundItems();
                break;
            default:
                _currentItems = [];
                break;
        }

        RaiseItemsChanged();

        // Re-raise after a short delay in case the first event fired before the SDK 
        // subscribed to ItemsChanged (before the palette was first opened).
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
                    TryAutoTriggerBiometric();
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
            var (placeholder, typedNoun, emptyHint, validator) = DescribeTwoFactorMethod(_pendingTwoFactorMethod);
            PlaceholderText = placeholder;
            var code = _currentSearchText.Trim();
            var canSubmit = validator(code);
            ICommand command = canSubmit
                ? new AnonymousCommand(() => OnTwoFactorSubmitted(code)) { Name = "Submit", Result = CommandResult.KeepOpen() }
                : new NoOpCommand();
            var hint = new ListItem(command)
            {
                Title = canSubmit ? $"Submit {typedNoun}" : "Two-Factor Authentication Required",
                Subtitle = _errorMessage ?? (canSubmit ? "Press Enter to submit" : emptyHint),
                Icon = new IconInfo("\uE8D7"),
            };
            if (_errorMessage != null)
                hint.Tags = [new Tag("Error") { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) }];
            return WithDebugLog([hint, BuildBackToLoginItem()]);
        }

        if (_deviceVerificationRequired && _pendingEmail != null && _pendingPassword != null)
        {
            PlaceholderText = "Enter device verification code...";
            var code = _currentSearchText.Trim();
            var canSubmit = code.Length >= 6 && code.Length <= 8 && long.TryParse(code, out _);
            ICommand command = canSubmit
                ? new AnonymousCommand(() => OnDeviceVerificationSubmitted(code)) { Name = "Submit", Result = CommandResult.KeepOpen() }
                : new NoOpCommand();
            var hint = new ListItem(command)
            {
                Title = canSubmit ? "Submit verification code" : "New Device Verification Required",
                Subtitle = _errorMessage ?? (canSubmit
                    ? "Press Enter to submit"
                    : "Enter the OTP code sent to your login email"),
                Icon = new IconInfo("\uE8D7"),
            };
            if (_errorMessage != null)
                hint.Tags = [new Tag("Error") { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) }];
            return WithDebugLog([hint, BuildBackToLoginItem()]);
        }

        PlaceholderText = "Search your vault... (try is:fav, is:protected, folder:Work, has:totp, has:passkey, url:github)";

        var item = new ListItem(new Pages.LoginPage(_service, _settings, OnLoginSubmitted))
        {
            Title = "Login to Bitwarden",
            Subtitle = _errorMessage ?? "Sign in with your email and master password",
            Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png"),
        };

        if (_errorMessage != null)
            item.Tags = [new Tag("Error") { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) }];

        return WithDebugLog([item, BuildSetServerItem()]);
    }

    private IListItem[] BuildLockedItems() => WithDebugLog(
    [
        BuildUnlockItem(),
        BuildSetServerItem(),
        BuildLogoutItem(),
    ]);

    internal static (string Placeholder, string TypedNoun, string EmptyHint, Func<string, bool> Validator) DescribeTwoFactorMethod(int? method) => method switch
    {
        0 => ("Enter your authenticator code...", "authenticator code", "Type the 6-digit code from your authenticator app", IsNumericCode),
        1 => ("Enter the code sent to your email...", "email code", "Check your inbox for the code Bitwarden just sent", IsNumericCode),
        3 => ("Touch your YubiKey...", "YubiKey OTP", "Touch your YubiKey to insert its one-time code", IsYubiKeyOtp),
        _ => ("Enter your 2FA code...", "2FA code", "Type your 6-8 digit code above and press Enter", IsNumericCode),
    };

    private static bool IsNumericCode(string code) =>
        code.Length >= 6 && code.Length <= 8 && long.TryParse(code, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _);

    private static bool IsYubiKeyOtp(string code) =>
        code.Length >= 32 && code.Length <= 64 && code.All(c => char.IsLetterOrDigit(c));

    private ListItem BuildBackToLoginItem() => new(new AnonymousCommand(OnBackToLoginRequested)
    {
        Name = "Back",
        Result = CommandResult.KeepOpen(),
    })
    {
        Title = "Back to login",
        Subtitle = "Pick a different 2FA method or re-enter your credentials",
        Icon = new IconInfo(""),
    };

    private void OnBackToLoginRequested()
    {
        DebugLogService.Log("Action", "User returned to login screen from 2FA prompt");
        ClearSearchText();
        _twoFactorRequired = false;
        _deviceVerificationRequired = false;
        _pendingEmail = null;
        _pendingPassword = null;
        _pendingTwoFactorMethod = null;
        _errorMessage = null;
        _currentItems = BuildUnauthenticatedItems();
        RaiseItemsChanged();
    }

    private ListItem BuildUnlockItem()
    {
        var biometricEnabled = _settings?.UseDesktopIntegration.Value == true && _settings?.AutoBiometricUnlock.Value == true;
        ICommand command = biometricEnabled && !_biometricClickFailed
            ? new AnonymousCommand(OnBiometricUnlockRequested) { Name = "Unlock with Windows Hello", Result = CommandResult.KeepOpen() }
            : new Pages.UnlockVaultPage(_service, _settings, OnUnlockSubmitted, OnBiometricUnlockRequested);

        var item = new ListItem(command)
        {
            Title = "Unlock Vault",
            Subtitle = _errorMessage ?? "Vault is locked, select to unlock your Bitwarden vault",
            Icon = new IconInfo("\uE785"),
        };

        if (_errorMessage != null)
            item.Tags = [new Tag("Error") { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) }];

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
        Subtitle = $"{DebugLogService.Count} entries captured",
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
        var totpTracked = new List<(ListItem, BitwardenItem, Tag[])>();

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
            // Item index is passed to FaviconService as the download priority so the
            // first items in the visible list fetch their icons before the tail. The
            // command palette host renders results in this exact order.
            var iconPriority = 0;
            foreach (var item in items)
            {
                var allowContextTag = showContextTag;
                if (capContextTags)
                {
                    var isContextMatch = _context != null && ContextAwarenessService.ContextScore(_context, item) > 0;
                    allowContextTag = isContextMatch && contextTagsUsed < contextLimit;
                    if (allowContextTag) contextTagsUsed++;
                }
                // When capContextTags is true, ContextScore was already evaluated above — pass context: null
                // and showContextTag: allowContextTag to avoid a redundant ContextScore call in BuildTags/BuildBaseTags.
                var contextForTags = capContextTags ? null : _context;
                var listItem = BuildListItem(item, showWatchtower, allowContextTag, totpTagStyle, showPasskeyTag, showProtectedTag, showWebsiteIcons, contextForTags, iconPriority++);
                list.Add(listItem);
                if (totpTagStyle == "live" && item.HasTotp)
                {
                    var baseTags = VaultItemHelper.BuildBaseTags(item, showWatchtower, contextForTags, allowContextTag, showPasskeyTag, showProtectedTag);
                    totpTracked.Add((listItem, item, baseTags));
                }
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

    private ListItem BuildListItem(BitwardenItem item, bool showWatchtower, bool showContextTag, string totpTagStyle, bool showPasskeyTag, bool showProtectedTag, bool showWebsiteIcons = true, ForegroundContext? context = null, int iconPriority = int.MaxValue)
    {
        var listItem = new ListItem(VaultItemHelper.GetDefaultCommand(item, _service))
        {
            Title = item.Name,
            Subtitle = item.Subtitle,
            Icon = VaultItemHelper.GetIcon(item, showWebsiteIcons, iconPriority),
            MoreCommands = VaultItemHelper.BuildContextItems(item, _service),
        };

        var tags = VaultItemHelper.BuildTags(item, showWatchtower, context, showContextTag, totpTagStyle, showPasskeyTag, showProtectedTag);
        if (tags.Length > 0)
            listItem.Tags = tags;

        return listItem;
    }

    public void Dispose()
    {
        _totpTimer?.Dispose();
        _syncTimer?.Dispose();
        _iconRefreshTimer.Dispose();
        _searchDebounceTimer.Dispose();
        _service.CacheUpdated -= OnCacheUpdated;
        _service.StatusChanged -= OnStatusChanged;
        _service.WarmupCompleted -= OnWarmupCompleted;
        _service.AutoLocking -= OnAutoLocking;
        _service.AutoLocked -= OnAutoLocked;
        _service.CliConfigChanged -= OnCliConfigChanged;
        AccessTracker.ItemAccessed -= OnItemAccessed;
        FaviconService.IconCached -= OnIconCached;
        RepromptPage.GraceStarted -= OnRepromptGraceStarted;
        RepromptPage.BiometricRequested -= OnRepromptBiometricRequested;
    }

    private void OnIconCached()
    {
        lock (_iconRefreshLock)
        {
            if (_iconRefreshFirstQueuedAt == default)
                _iconRefreshFirstQueuedAt = DateTime.UtcNow;
            var elapsedMs = (int)(DateTime.UtcNow - _iconRefreshFirstQueuedAt).TotalMilliseconds;
            var dueIn = Math.Max(0, Math.Min(IconRefreshDebounceMs, IconRefreshMaxWaitMs - elapsedMs));
            _iconRefreshTimer.Change(dueIn, Timeout.Infinite);
        }
    }

    private void OnIconRefreshTick(object? _)
    {
        lock (_iconRefreshLock)
            _iconRefreshFirstQueuedAt = default;

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

        foreach (var (listItem, vaultItem, baseTags) in items)
        {
            var totpTag = VaultItemHelper.BuildTotpTag(vaultItem.TotpSecret!);
            listItem.Tags = totpTag != null ? [.. baseTags, totpTag] : baseTags;
        }
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
        _pendingTwoFactorMethod = null;
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

    // Biometric reprompt is handled here (not in RepromptForm) so the WinHello
    // prompt can come to the foreground. With the form returned via GoBack
    // first, the items list is showing -- the same UI state where the unlock
    // biometric path already prompts visibly.
    private void OnRepromptBiometricRequested(BiometricVerificationRequest request)
    {
        DebugLogService.Log("Reprompt", $"Biometric verification requested for item {request.ItemId}");

        _ = Task.Run(async () =>
        {
            var connectingStatus = new StatusMessage { Message = "Connecting to Bitwarden Desktop...", State = MessageState.Info };
            ExtensionHost.ShowStatus(connectingStatus, StatusContext.Page);

            (bool success, string? error) result;
            try
            {
                result = await request.Service.VerifyWithBiometricsAsync(
                    onStatus: msg => connectingStatus.Message = msg);
            }
            catch (Exception ex)
            {
                DebugLogService.Log("Reprompt", $"Biometric verify exception: {ex.GetType().Name}: {ex.Message}");
                result = (false, ex.Message);
            }
            finally
            {
                try { ExtensionHost.HideStatus(connectingStatus); } catch { }
            }

            if (!result.success)
            {
                RepromptPage.RecordFailure();
                var cooldown = RepromptPage.GetCooldownSecondsRemaining();
                var msg = cooldown > 0
                    ? $"Too many failed attempts. Try again in {cooldown}s."
                    : result.error ?? "Biometric verification failed";
                var failStatus = new StatusMessage { Message = msg, State = MessageState.Error };
                ExtensionHost.ShowStatus(failStatus, StatusContext.Page);
                _ = Task.Delay(3000).ContinueWith(_ => { try { ExtensionHost.HideStatus(failStatus); } catch { } }, TaskScheduler.Default);
                return;
            }

            RepromptPage.RecordVerification(request.ItemId);
            try { request.InnerAction(); }
            catch (Exception ex)
            {
                DebugLogService.Log("Reprompt", $"Inner action exception: {ex.GetType().Name}: {ex.Message}");
                var errStatus = new StatusMessage { Message = "Action failed after verification.", State = MessageState.Error };
                ExtensionHost.ShowStatus(errStatus, StatusContext.Page);
                _ = Task.Delay(3000).ContinueWith(_ => { try { ExtensionHost.HideStatus(errStatus); } catch { } }, TaskScheduler.Default);
                return;
            }

            var successStatus = new StatusMessage { Message = $"Copied {request.ActionLabel} to clipboard", State = MessageState.Success };
            ExtensionHost.ShowStatus(successStatus, StatusContext.Page);
            _ = Task.Delay(3000).ContinueWith(_ => { try { ExtensionHost.HideStatus(successStatus); } catch { } }, TaskScheduler.Default);

            // Refresh items so any per-item grace tag updates immediately.
            if (_service.LastStatus == VaultStatus.Unlocked && _service.IsCacheLoaded)
            {
                lock (_itemsLock)
                    _currentItems = BuildListItems(Search(_currentSearchText));
                RaiseItemsChanged();
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
                await _service.LockAsync(userInitiated: true);
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

    private void TryAutoTriggerBiometric()
    {
        if (_autoBiometricTriggered || _biometricClickFailed || _handlingAction)
            return;

        var biometricEnabled = _settings?.UseDesktopIntegration.Value == true
                            && _settings?.AutoBiometricUnlock.Value == true;
        var rememberSession = _settings?.RememberSession.Value == true;

        if (!biometricEnabled && !rememberSession)
            return;

        _autoBiometricTriggered = true;

        _ = Task.Run(async () =>
        {
            // Prefer silent restore from the saved credential so RememberSession
            // actually saves the user a prompt after a soft auto-lock.
            if (rememberSession && await _service.TryRestoreSessionAsync())
            {
                DebugLogService.Log("Page", "Restored session silently; biometric not needed");
                return;
            }

            if (biometricEnabled)
            {
                DebugLogService.Log("Page", "Auto-triggering biometric unlock");
                OnBiometricUnlockRequested();
            }
            else
            {
                // RememberSession was on but the stored credential was gone or
                // invalid; reset so the manual unlock paths work.
                _autoBiometricTriggered = false;
            }
        });
    }

    private void HideBiometricStatus()
    {
        if (_lastBiometricStatus != null)
        {
            try { ExtensionHost.HideStatus(_lastBiometricStatus); } catch { }
            _lastBiometricStatus = null;
        }
    }

    private void OnBiometricUnlockRequested()
    {
        DebugLogService.Log("Action", "Windows Hello unlock requested by user");
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
                ShowLoadingStatus("Connecting to Bitwarden Desktop...", "Windows Hello");
                var (success, error) = await _service.UnlockWithBiometricsAsync(
                    onStatus: msg => ShowLoadingStatus(msg, "Windows Hello"));
                if (!success)
                {
                    _biometricClickFailed = true;
                    _errorMessage = error ?? "Windows Hello unlock failed";
                    // Settle flags before notifying the host (see comment on
                    // the success path below).
                    _handlingAction = false;
                    IsLoading = false;
                    _currentItems = BuildLockedItems();
                    RaiseItemsChanged();

                    if (BitwardenSettingsManager.HasBiometricSuccess)
                    {
                        HideBiometricStatus();
                        _lastBiometricStatus = new StatusMessage { Message = _errorMessage, State = MessageState.Warning };
                        ExtensionHost.ShowStatus(_lastBiometricStatus, StatusContext.Page);
                        _ = Task.Delay(5000).ContinueWith(_ => HideBiometricStatus(), TaskScheduler.Default);
                    }
                    return;
                }

                _biometricClickFailed = false;
                HideBiometricStatus();
                ShowLoadingStatus("Retrieving items from vault...", "bw list items");
                await _service.RefreshCacheAsync();
                // Clear handling/loading flags BEFORE raising ItemsChanged so
                // the host's GetItems callback sees the fully-settled state.
                // If we raise first and clear in `finally`, the host fetches
                // items while _handlingAction/IsLoading are still true and
                // keeps showing the loading placeholder until the next
                // hide/show cycle forces a fresh GetItems.
                _handlingAction = false;
                IsLoading = false;
                _currentItems = BuildListItems(Search(_currentSearchText));
                RaiseItemsChanged();
            }
            finally
            {
                // Defensive: ensure flags are cleared on any exit path.
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

    private void OnLoginSubmitted(string email, string password, int? twoFactorMethod)
    {
        DebugLogService.Log("Action", $"Login submitted by user (2FA method: {(twoFactorMethod?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "auto")})");
        _handlingAction = true;
        ClearSearchText();
        _errorMessage = null;
        _twoFactorRequired = false;
        _deviceVerificationRequired = false;
        _pendingEmail = null;
        _pendingPassword = null;
        _pendingTwoFactorMethod = null;
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
                        _pendingTwoFactorMethod = twoFactorMethod;
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
                var (success, error, _, _) = await _service.LoginAsync(email!, password!, twoFactorCode, _pendingTwoFactorMethod);
                if (!success)
                {
                    _errorMessage = error?.Contains("Code", StringComparison.OrdinalIgnoreCase) == true
                        ? "Invalid 2FA code - try again"
                        : error ?? "Verification failed";
                    _currentItems = BuildUnauthenticatedItems();
                    RaiseItemsChanged();
                    return;
                }

                _twoFactorRequired = false;
                _pendingEmail = null;
                _pendingPassword = null;
                _pendingTwoFactorMethod = null;
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
                    _errorMessage = error ?? "Verification failed - try again";
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

    private static IListItem[] BuildLoadingPlaceholder(string title, string command) => WithDebugLog(
    [
        new ListItem(new NoOpCommand())
        {
            Title = title,
            Subtitle = $"Running: {command}",
            Icon = new IconInfo("\uE895"),
        },
    ]);
}
