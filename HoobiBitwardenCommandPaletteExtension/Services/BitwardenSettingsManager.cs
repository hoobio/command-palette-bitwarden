using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed class BitwardenSettingsManager : JsonSettingsManager
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HoobiBitwardenCommandPalette");

    public ToggleSetting RememberSession { get; } = new(
        "rememberSession",
        "Remember Session",
        "Securely store your session key using Windows Credential Manager so you don't need to unlock each launch",
        false);

    public ToggleSetting ShowWebsiteIcons { get; } = new(
        "showWebsiteIcons",
        "Show Website Icons",
        "Download and display website icons for login items. Disable for privacy — a generic icon will be shown instead",
        true);

    public ToggleSetting ShowWatchtowerTags { get; } = new(
        "showWatchtowerTags",
        "Show Watchtower Tags",
        "Display warning tags for weak, old, or insecure passwords on vault items",
        true);

    public ToggleSetting ContextAwareness { get; } = new(
        "contextAwareness",
        "Context Awareness",
        "Detect the foreground application to boost relevant vault items to the top of results",
        true);

    public ToggleSetting ShowContextTag { get; } = new(
        "showContextTag",
        "Show Context Tag",
        "Display a 'Context' tag on vault items that match the foreground application",
        false);

    public ToggleSetting ShowProtectedTag { get; } = new(
        "showProtectedTag",
        "Show Protected Tag",
        "Display a \"\uD83D\uDD12\" tag on vault items that require master password re-prompt",
        true);

    public ToggleSetting ShowPasskeyTag { get; } = new(
        "showPasskeyTag",
        "Show Passkey Tag",
        "Display a 'Passkey' tag on vault items that support passwordless sign-in",
        true);

    public ChoiceSetSetting TotpTagStyle { get; } = new(
        "totpTagStyle",
        "TOTP Tag Style",
        "Display a TOTP tag on vault items with an authenticator secret",
        [
            new("Off", "off"),
            new("Static (show 2FA badge only)", "static"),
            new("Live (show live code + countdown)", "live"),
        ]);

    public ChoiceSetSetting AutoLockTimeout { get; } = new(
        "autoLockTimeout",
        "Auto-Lock Timeout",
        "Automatically lock the vault after a period of inactivity",
        [
            new("Never", "0"),
            new("1 minute", "1"),
            new("2 minutes", "2"),
            new("5 minutes", "5"),
            new("15 minutes", "15"),
            new("30 minutes", "30"),
            new("1 hour", "60"),
        ]);

    public ToggleSetting AutoClearClipboard { get; } = new(
        "autoClearClipboard",
        "Auto-Clear Clipboard",
        "Automatically clear sensitive data from clipboard after a delay",
        true);

    public ChoiceSetSetting ContextItemLimit { get; } = new(
        "contextItemLimit",
        "Context Items Limit",
        "Maximum number of context-matched vault items to show when no search is typed (0 = unlimited)",
        [
            new("1 item", "1"),
            new("2 items", "2"),
            new("3 items", "3"),
            new("5 items", "5"),
            new("10 items", "10"),
            new("Unlimited", "0"),
        ]);
    public ChoiceSetSetting BackgroundRefresh { get; } = new(
        "backgroundRefresh",
        "Background Refresh Interval",
        "How often to automatically sync vault items in the background while the vault is open",
        [
            new("Never", "0"),
            new("5 minutes", "5"),
            new("15 minutes", "15"),
            new("30 minutes", "30"),
            new("1 hour", "60"),
        ]);
    public ChoiceSetSetting ClipboardClearDelay { get; } = new(
        "clipboardClearDelay",
        "Clipboard Clear Delay",
        "How long to wait before clearing sensitive clipboard data",
        [
            new("10 seconds", "10"),
            new("15 seconds", "15"),
            new("30 seconds", "30"),
            new("60 seconds", "60"),
            new("2 minutes", "120"),
        ]);

    public TextSetting CliDirectoryOverride { get; } = new(
        "cliDirectoryOverride",
        "CLI Path Override",
        "Path to the Bitwarden CLI. Accepts a directory containing bw, or a direct path to bw/bw.exe. Leave empty to use the system PATH",
        "");

    public ToggleSetting UsePortableDataDirectory { get; } = new(
        "usePortableDataDirectory",
        "Use CLI Path as Data Directory",
        "Store Bitwarden CLI data (data.json) alongside the CLI executable instead of the default location. Requires CLI Path Override to be set",
        false);

    public TextSetting CliDataDirectoryOverride { get; } = new(
        "cliDataDirectoryOverride",
        "CLI Data Directory Override",
        "Custom directory for Bitwarden CLI data (data.json). Overrides both the default location and the portable directory toggle. Leave empty to use the default or portable location",
        "");

    public ChoiceSetSetting RepromptGracePeriod { get; } = new(
        "repromptGracePeriod",
        "Re-prompt Grace Period",
        "After verifying your master password for a protected item, skip re-verification for this duration",
        [
            new("No grace period", "0"),
            new("30 seconds", "30"),
            new("1 minute", "60"),
            new("2 minutes", "120"),
            new("5 minutes", "300"),
        ]);

    public ToggleSetting DebugLogging { get; } = new(
        "debugLogging",
        "Debug Logging",
        "Enable debug logging to help diagnose issues. When enabled, a 'Copy Debug Log' command appears in the vault browser. Logs are kept in memory only and cleared when the extension restarts",
        false);

    public BitwardenSettingsManager(string? settingsFilePath = null)
    {
        Directory.CreateDirectory(SettingsDir);
        FilePath = settingsFilePath ?? Path.Combine(SettingsDir, "settings.json");
        ContextItemLimit.Value = "3";
        TotpTagStyle.Value = "static";
        BackgroundRefresh.Value = "5";
        RepromptGracePeriod.Value = "60";
        Settings.Add(RememberSession);
        Settings.Add(ShowWebsiteIcons);
        Settings.Add(AutoLockTimeout);
        Settings.Add(ShowWatchtowerTags);
        Settings.Add(ContextAwareness);
        Settings.Add(ShowContextTag);
        Settings.Add(ShowProtectedTag);
        Settings.Add(ShowPasskeyTag);
        Settings.Add(TotpTagStyle);
        Settings.Add(ContextItemLimit);
        Settings.Add(BackgroundRefresh);
        Settings.Add(AutoClearClipboard);
        Settings.Add(ClipboardClearDelay);
        Settings.Add(CliDirectoryOverride);
        Settings.Add(UsePortableDataDirectory);
        Settings.Add(CliDataDirectoryOverride);
        Settings.Add(RepromptGracePeriod);
        Settings.Add(DebugLogging);
        CaptureDefaults();
        Settings.SettingsChanged += OnSettingsChanged;
        LoadSettings();
        SyncClipboardSettings();
        SyncRepromptSettings();
        DebugLogService.Enabled = DebugLogging.Value;
        LogConfig("startup");
    }

    private void OnSettingsChanged(object sender, Settings e)
    {
        SaveSettings();
        SyncClipboardSettings();
        SyncRepromptSettings();
        DebugLogService.Enabled = DebugLogging.Value;
        LogConfig("settings changed");

        if (!RememberSession.Value)
            SessionStore.Clear();
    }

    private void SyncClipboardSettings()
    {
        SecureClipboardService.AutoClearEnabled = AutoClearClipboard.Value;
        if (int.TryParse(ClipboardClearDelay.Value, out var delay))
            SecureClipboardService.ClearDelaySeconds = delay;
    }

    private void SyncRepromptSettings()
    {
        var seconds = int.TryParse(RepromptGracePeriod.Value, out var gp) ? gp : 60;
        Pages.RepromptPage.GracePeriodSeconds = Math.Clamp(seconds, 0, 300);
    }

    private readonly Dictionary<string, object?> _defaults = [];

    private void CaptureDefaults()
    {
        foreach (var setting in AllSettings())
            _defaults[setting.Key] = setting.Value;
    }

    private void LogConfig(string reason)
    {
        if (!DebugLogService.Enabled) return;

        var parts = new List<string>();
        foreach (var setting in AllSettings())
        {
            _defaults.TryGetValue(setting.Key, out var def);
            var val = setting.Value;
            if (Equals(val, def)) continue;
            var display = setting.Key is "cliDirectoryOverride" or "cliDataDirectoryOverride"
                ? "[set]"
                : val?.ToString() ?? "";
            parts.Add($"{setting.Key}={display}");
        }

        var changed = parts.Count > 0 ? string.Join(" ", parts) : "(all defaults)";
        DebugLogService.Log("Config", $"[{reason}] {changed}");
    }

    private IEnumerable<(string Key, object? Value)> AllSettings()
    {
        yield return (RememberSession.Key, (object?)RememberSession.Value);
        yield return (ShowWebsiteIcons.Key, ShowWebsiteIcons.Value);
        yield return (ShowWatchtowerTags.Key, ShowWatchtowerTags.Value);
        yield return (ContextAwareness.Key, ContextAwareness.Value);
        yield return (ShowContextTag.Key, ShowContextTag.Value);
        yield return (ShowProtectedTag.Key, (object?)ShowProtectedTag.Value);
        yield return (ShowPasskeyTag.Key, ShowPasskeyTag.Value);
        yield return (TotpTagStyle.Key, TotpTagStyle.Value);
        yield return (AutoLockTimeout.Key, AutoLockTimeout.Value);
        yield return (AutoClearClipboard.Key, AutoClearClipboard.Value);
        yield return (ClipboardClearDelay.Key, ClipboardClearDelay.Value);
        yield return (ContextItemLimit.Key, ContextItemLimit.Value);
        yield return (BackgroundRefresh.Key, BackgroundRefresh.Value);
        yield return (CliDirectoryOverride.Key, CliDirectoryOverride.Value);
        yield return (UsePortableDataDirectory.Key, (object?)UsePortableDataDirectory.Value);
        yield return (CliDataDirectoryOverride.Key, CliDataDirectoryOverride.Value);
        yield return (RepromptGracePeriod.Key, RepromptGracePeriod.Value);
        yield return (DebugLogging.Key, (object?)DebugLogging.Value);
    }
}
