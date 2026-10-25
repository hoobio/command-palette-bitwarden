using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed class BitwardenSettingsManager : JsonSettingsManager
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HoobiBitwardenCommandPalette");

    private static readonly string BiometricSuccessMarker = Path.Combine(SettingsDir, ".biometric_success");

    public static bool HasBiometricSuccess => File.Exists(BiometricSuccessMarker);

    public static void RecordBiometricSuccess()
    {
        if (!File.Exists(BiometricSuccessMarker))
        {
            try { File.WriteAllText(BiometricSuccessMarker, string.Empty); }
            catch { }
        }
    }

    public ToggleSetting RememberSession { get; } = new(
        "rememberSession",
        "Remember Session",
        "Keeps your session key in Windows Credential Manager, so the vault stays unlocked when the extension restarts",
        false);

    public ToggleSetting UseApiKeyAuthentication { get; } = new(
        "useApiKeyAuthentication",
        "Use API Key Authentication",
        "Log in with your personal API key instead of your email and master password. Use it for FIDO2 or Duo two-step login. The key is kept in Windows Credential Manager, and you still unlock with your master password. Changing this setting logs you out",
        false);

    public ToggleSetting UseDesktopIntegration { get; } = new(
        "useDesktopIntegration",
        "Integrate with Bitwarden Desktop App",
        "Unlock with Windows Hello through the Bitwarden Desktop app. The Desktop app must be running with 'Allow browser integration' turned on",
        true);

    public ToggleSetting AutoBiometricUnlock { get; } = new(
        "autoBiometricUnlock",
        "Automatically Prompt for Biometrics",
        "Prompt for Windows Hello when you open the Bitwarden page on a locked vault, without selecting Unlock first. If Windows Hello is not available, the password form opens",
        true);

    public ToggleSetting ShowWebsiteIcons { get; } = new(
        "showWebsiteIcons",
        "Show Website Icons",
        "Download and show website icons for login items. Turn this off for privacy: items then show a generic icon",
        true);

    public ToggleSetting ShowWatchtowerTags { get; } = new(
        "showWatchtowerTags",
        "Show Watchtower Tags",
        "Show warning tags on vault items with weak, old or insecure passwords",
        true);

    public ToggleSetting ContextAwareness { get; } = new(
        "contextAwareness",
        "Context Awareness",
        "Detect the foreground app and move matching vault items to the top of the results",
        true);

    public ToggleSetting ShowContextTag { get; } = new(
        "showContextTag",
        "Show Context Tag",
        "Show a 'Context' tag on vault items that match the foreground app",
        false);

    public ToggleSetting ShowProtectedTag { get; } = new(
        "showProtectedTag",
        "Show Protected Tag",
        "Show a \"\uD83D\uDD12\" tag on vault items that need the master password re-prompt",
        true);

    public ToggleSetting ShowPasskeyTag { get; } = new(
        "showPasskeyTag",
        "Show Passkey Tag",
        "Show a 'Passkey' tag on vault items that support passwordless sign-in",
        true);

    public ChoiceSetSetting TotpTagStyle { get; } = new(
        "totpTagStyle",
        "TOTP Tag Style",
        "Show a TOTP tag on vault items that have an authenticator key",
        [
            new("Off", "off"),
            new("Static (show 2FA badge only)", "static"),
            new("Live (show live code + countdown)", "live"),
        ]);

    public ChoiceSetSetting OrganizationTagStyle { get; } = new(
        "organizationTagStyle",
        "Organization Tag Style",
        "Show an organisation tag on vault items owned by a Bitwarden organisation. Each organisation keeps the same colour, based on its ID",
        [
            new("Initials (compact 1-3 char badge)", "initials"),
            new("Name (full organization name)", "name"),
            new("Off", "off"),
        ]);

    public ChoiceSetSetting AutoLockTimeout { get; } = new(
        "autoLockTimeout",
        "Auto-Lock Timeout",
        "Lock the vault after this period of inactivity",
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
        "Clear sensitive data from the clipboard after a delay",
        true);

    public ChoiceSetSetting ContextItemLimit { get; } = new(
        "contextItemLimit",
        "Context Items Limit",
        "The number of context-matched vault items shown before you type a search",
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
        "How often the vault syncs in the background while it is unlocked",
        [
            new("Never", "0"),
            new("5 minutes", "5"),
            new("15 minutes", "15"),
            new("30 minutes", "30"),
            new("1 hour", "60"),
        ]);
    public ToggleSetting LiveSync { get; } = new(
        "liveSync",
        "Live Sync",
        "Sync the vault as soon as it changes on another device, as the Bitwarden apps do. While the vault is unlocked, the extension keeps a connection open to your server's notifications service. It signs in with the access token that the Bitwarden CLI stores in data.json",
        true);
    public ChoiceSetSetting ClipboardClearDelay { get; } = new(
        "clipboardClearDelay",
        "Clipboard Clear Delay",
        "How long to wait before the clipboard is cleared",
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
        "Path to the Bitwarden CLI: a folder that contains bw, or the full path to bw.exe. Leave empty to use the system PATH",
        "");

    public ToggleSetting UsePortableDataDirectory { get; } = new(
        "usePortableDataDirectory",
        "Use CLI Path as Data Directory",
        "Keep Bitwarden CLI data (data.json) next to the CLI instead of in the default location. Needs CLI Path Override",
        false);

    public TextSetting CliDataDirectoryOverride { get; } = new(
        "cliDataDirectoryOverride",
        "CLI Data Directory Override",
        "Folder for Bitwarden CLI data (data.json). It takes priority over the default location and the portable setting. Leave empty to use one of those",
        "");

    public ChoiceSetSetting RepromptGracePeriod { get; } = new(
        "repromptGracePeriod",
        "Re-prompt Grace Period",
        "After you verify your master password for a protected item, the extension does not ask again for this long",
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
        "Show a 'Copy Debug Log' item in the vault browser and write the log to %LOCALAPPDATA%\\HoobiBitwardenCommandPalette\\logs to help diagnose problems. Log files are deleted after 7 days. Logs are collected in memory whether or not this is on",
        false);

    // Settings.RaiseSettingsChanged is internal to the SDK, and the public Settings.Update(json) sets values without raising it.
    private static readonly MethodInfo RaiseSettingsChangedMethod = typeof(Microsoft.CommandPalette.Extensions.Toolkit.Settings)
        .GetMethod("RaiseSettingsChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly List<object> _ordered = [];

    internal IReadOnlyList<object> OrderedSettings => _ordered;

    internal void RaiseChanged() => RaiseSettingsChangedMethod.Invoke(Settings, null);

    private readonly Dictionary<string, (string Key, string Value)> _parents = [];

    internal bool TryGetParent(string key, out (string Key, string Value) parent) => _parents.TryGetValue(key, out parent);

    private void AddSetting<T>(Setting<T> setting, Setting<bool>? parent = null)
    {
        Settings.Add(setting);
        _ordered.Add(setting);
        if (parent is not null)
            _parents[setting.Key] = (parent.Key, "true");
    }

    public BitwardenSettingsManager(string? settingsFilePath = null)
    {
        Directory.CreateDirectory(SettingsDir);
        FilePath = settingsFilePath ?? Path.Combine(SettingsDir, "settings.json");
        ContextItemLimit.Value = "3";
        TotpTagStyle.Value = "static";
        OrganizationTagStyle.Value = "name";
        BackgroundRefresh.Value = "5";
        RepromptGracePeriod.Value = "60";
        AddSetting(new SectionHeaderSetting("_section_auth", "Authentication", separator: false));
        AddSetting(RememberSession);
        AddSetting(UseApiKeyAuthentication);
        AddSetting(UseDesktopIntegration);
        AddSetting(AutoBiometricUnlock, UseDesktopIntegration);
        AddSetting(AutoLockTimeout);
        AddSetting(RepromptGracePeriod);

        AddSetting(new SectionHeaderSetting("_section_display", "Vault Display"));
        AddSetting(ShowWebsiteIcons);
        AddSetting(ShowWatchtowerTags);
        AddSetting(ShowProtectedTag);
        AddSetting(ShowPasskeyTag);
        AddSetting(TotpTagStyle);
        AddSetting(OrganizationTagStyle);
        AddSetting(BackgroundRefresh);
        AddSetting(LiveSync);

        AddSetting(new SectionHeaderSetting("_section_context", "Context Awareness"));
        AddSetting(ContextAwareness);
        AddSetting(ShowContextTag, ContextAwareness);
        AddSetting(ContextItemLimit, ContextAwareness);

        AddSetting(new SectionHeaderSetting("_section_clipboard", "Clipboard"));
        AddSetting(AutoClearClipboard);
        AddSetting(ClipboardClearDelay, AutoClearClipboard);

        AddSetting(new SectionHeaderSetting("_section_cli", "Bitwarden CLI"));
        AddSetting(CliDirectoryOverride);
        AddSetting(UsePortableDataDirectory);
        AddSetting(CliDataDirectoryOverride);

        AddSetting(new SectionHeaderSetting("_section_advanced", "Advanced"));
        AddSetting(DebugLogging);
        CaptureDefaults();
        Settings.SettingsChanged += OnSettingsChanged;
        LoadSettings();
        SyncClipboardSettings();
        SyncRepromptSettings();
        DebugLogService.LogDirectory = Path.Combine(Path.GetDirectoryName(FilePath) is { Length: > 0 } dir ? dir : SettingsDir, "logs");
        DebugLogService.Enabled = DebugLogging.Value;
        LogConfig("startup");
    }

    // Stored apart from CliDirectoryOverride so an automated install never overwrites a hand-set path (issue #178).
    private string AutoInstalledCliPathFile => Path.Combine(
        Path.GetDirectoryName(FilePath) is { Length: > 0 } dir ? dir : SettingsDir,
        "cli_install_path");

    public string? AutoInstalledCliPath
    {
        get
        {
            try
            {
                return File.Exists(AutoInstalledCliPathFile)
                    ? File.ReadAllText(AutoInstalledCliPathFile).Trim()
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public void PersistCliPath(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(AutoInstalledCliPathFile);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(AutoInstalledCliPathFile, path);
        }
        catch (Exception ex)
        {
            DebugLogService.Log("Installer", $"Failed to persist auto-installed CLI path: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnSettingsChanged(object sender, Settings e)
    {
        SaveSettings();
        SyncClipboardSettings();
        SyncRepromptSettings();
        DebugLogService.Enabled = DebugLogging.Value;
        LogConfig("settings changed");
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
        yield return (UseApiKeyAuthentication.Key, (object?)UseApiKeyAuthentication.Value);
        yield return (UseDesktopIntegration.Key, (object?)UseDesktopIntegration.Value);
        yield return (AutoBiometricUnlock.Key, (object?)AutoBiometricUnlock.Value);
        yield return (AutoLockTimeout.Key, AutoLockTimeout.Value);
        yield return (RepromptGracePeriod.Key, RepromptGracePeriod.Value);
        yield return (ShowWebsiteIcons.Key, ShowWebsiteIcons.Value);
        yield return (ShowWatchtowerTags.Key, ShowWatchtowerTags.Value);
        yield return (ShowProtectedTag.Key, (object?)ShowProtectedTag.Value);
        yield return (ShowPasskeyTag.Key, ShowPasskeyTag.Value);
        yield return (TotpTagStyle.Key, TotpTagStyle.Value);
        yield return (OrganizationTagStyle.Key, OrganizationTagStyle.Value);
        yield return (BackgroundRefresh.Key, BackgroundRefresh.Value);
        yield return (LiveSync.Key, (object?)LiveSync.Value);
        yield return (ContextAwareness.Key, ContextAwareness.Value);
        yield return (ShowContextTag.Key, ShowContextTag.Value);
        yield return (ContextItemLimit.Key, ContextItemLimit.Value);
        yield return (AutoClearClipboard.Key, AutoClearClipboard.Value);
        yield return (ClipboardClearDelay.Key, ClipboardClearDelay.Value);
        yield return (CliDirectoryOverride.Key, CliDirectoryOverride.Value);
        yield return (UsePortableDataDirectory.Key, (object?)UsePortableDataDirectory.Value);
        yield return (CliDataDirectoryOverride.Key, CliDataDirectoryOverride.Value);
        yield return (DebugLogging.Key, (object?)DebugLogging.Value);
    }
}
