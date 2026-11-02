using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HoobiBitwarden.Ipc;
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
        "Keeps session key in Windows Credential Manager",
        false);

    public ToggleSetting UseApiKeyAuthentication { get; } = new(
        "useApiKeyAuthentication",
        "Use API Key Authentication",
        "Logs in with a personal API key, for FIDO2 or Duo. Changing this logs you out",
        false);

    public ToggleSetting UseDesktopIntegration { get; } = new(
        "useDesktopIntegration",
        "Unlock with Windows Hello",
        "Needs the Bitwarden Desktop app running with browser integration on",
        true);

    public ToggleSetting AutoBiometricUnlock { get; } = new(
        "autoBiometricUnlock",
        "Automatically Prompt for Biometrics",
        "Prompts for Windows Hello when you open a locked vault",
        true);

    public ToggleSetting ShowWebsiteIcons { get; } = new(
        "showWebsiteIcons",
        "Show Website Icons",
        "Downloads website icons for login items",
        true);

    public ToggleSetting ShowTags { get; } = new(
        "showTags",
        "Show Tags",
        "Shows tags on vault items",
        true);

    public ToggleSetting ShowWatchtowerTags { get; } = new(
        "showWatchtowerTags",
        "Watchtower",
        "Tags weak, old or insecure passwords",
        true);

    public ToggleSetting ContextAwareness { get; } = new(
        "contextAwareness",
        "Context Awareness",
        "Moves items that match the foreground app to the top",
        true);

    public ToggleSetting ShowContextTag { get; } = new(
        "showContextTag",
        "Show Context Tag",
        "Tags items that match the foreground app",
        false);

    public ToggleSetting ShowProtectedTag { get; } = new(
        "showProtectedTag",
        "Protected",
        "Tags items that need a master password re-prompt",
        true);

    public ToggleSetting ShowPasskeyTag { get; } = new(
        "showPasskeyTag",
        "Passkey",
        "Tags items that have a passkey",
        true);

    public ChoiceSetSetting TotpTagStyle { get; } = new(
        "totpTagStyle",
        "TOTP",
        "Tags items that have an authenticator key",
        [
            new("Off", "off"),
            new("Static", "static"),
            new("Live", "live"),
        ]);

    public ChoiceSetSetting OrganizationTagStyle { get; } = new(
        "organizationTagStyle",
        "Organisation",
        "Tags items owned by an organisation",
        [
            new("Initials", "initials"),
            new("Name", "name"),
            new("Off", "off"),
        ]);

    private static readonly Dictionary<(string Setting, string Value), string> ChoiceDescriptions = new()
    {
        [("totpTagStyle", "static")] = "Shows a 2FA badge",
        [("totpTagStyle", "live")] = "Shows the live code and countdown",
        [("organizationTagStyle", "initials")] = "Shows 1-3 letter initials",
        [("organizationTagStyle", "name")] = "Shows the full organisation name",
    };

    internal static string? ChoiceDescription(string settingKey, string value) =>
        ChoiceDescriptions.GetValueOrDefault((settingKey, value));

    public ChoiceSetSetting AutoLockTimeout { get; } = new(
        "autoLockTimeout",
        "Auto-Lock Timeout",
        "Locks the vault after inactivity",
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
        "Clears copied secrets from the clipboard",
        true);

    public ChoiceSetSetting ContextItemLimit { get; } = new(
        "contextItemLimit",
        "Context Items Limit",
        "Matched items shown before you search",
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
        "Syncs the vault on this interval while unlocked",
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
        "Syncs the vault when it changes on another device",
        true);
    public ChoiceSetSetting ClipboardClearDelay { get; } = new(
        "clipboardClearDelay",
        "Clipboard Clear Delay",
        "Time before the clipboard is cleared",
        [
            new("10 seconds", "10"),
            new("15 seconds", "15"),
            new("30 seconds", "30"),
            new("60 seconds", "60"),
            new("2 minutes", "120"),
        ]);

    public ToggleSetting UseCliDirectoryOverride { get; } = new(
        "useCliDirectoryOverride",
        "CLI Path Override",
        "Uses your own bw instead of the one the extension finds",
        false);

    public TextSetting CliDirectoryOverride { get; } = new(
        "cliDirectoryOverride",
        "CLI path",
        "Folder that contains bw, or the path to bw.exe. Empty uses PATH",
        "");

    public ToggleSetting UseCliDataDirectoryOverride { get; } = new(
        "useCliDataDirectoryOverride",
        "CLI Data Directory Override",
        "Uses your own folder for CLI data",
        false);

    internal string EffectiveCliDirectoryOverride => UseCliDirectoryOverride.Value ? CliDirectoryOverride.Value ?? string.Empty : string.Empty;

    internal string EffectiveCliDataDirectoryOverride => UseCliDataDirectoryOverride.Value ? CliDataDirectoryOverride.Value ?? string.Empty : string.Empty;

    internal bool RevealsChildren(string key) => key == UseCliDirectoryOverride.Key || key == UseCliDataDirectoryOverride.Key;

    public ToggleSetting UsePortableDataDirectory { get; } = new(
        "usePortableDataDirectory",
        "Use CLI Path as Data Directory",
        "Keeps CLI data next to the CLI",
        false);

    public TextSetting CliDataDirectoryOverride { get; } = new(
        "cliDataDirectoryOverride",
        "CLI data folder",
        "Folder for CLI data (data.json). Overrides the other locations",
        "");

    public ChoiceSetSetting RepromptGracePeriod { get; } = new(
        "repromptGracePeriod",
        "Re-prompt Grace Period",
        "Time before a protected item asks for the master password again",
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
        "Writes debug logs to disk and adds a Copy Debug Log item",
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

    private void AddSetting<T>(Setting<T> setting, Setting<string> nonEmptyParent)
    {
        AddSetting(setting);
        _parents[setting.Key] = (nonEmptyParent.Key, ParentValues.NonEmpty);
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
        AddSetting(ShowTags);
        AddSetting(ShowWatchtowerTags, ShowTags);
        AddSetting(ShowProtectedTag, ShowTags);
        AddSetting(ShowPasskeyTag, ShowTags);
        AddSetting(TotpTagStyle, ShowTags);
        AddSetting(OrganizationTagStyle, ShowTags);
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
        AddSetting(UseCliDirectoryOverride);
        AddSetting(CliDirectoryOverride, UseCliDirectoryOverride);
        AddSetting(UsePortableDataDirectory, CliDirectoryOverride);
        AddSetting(UseCliDataDirectoryOverride);
        AddSetting(CliDataDirectoryOverride, UseCliDataDirectoryOverride);

        AddSetting(new SectionHeaderSetting("_section_advanced", "Diagnostics"));
        AddSetting(DebugLogging);
        CaptureDefaults();
        Settings.SettingsChanged += OnSettingsChanged;
        LoadSettings();
        MigrateOverrideSwitches();
        SyncClipboardSettings();
        SyncRepromptSettings();
        DebugLogService.LogDirectory = Path.Combine(Path.GetDirectoryName(FilePath) is { Length: > 0 } dir ? dir : SettingsDir, "logs");
        DebugLogService.Enabled = DebugLogging.Value;
        LogConfig("startup");
    }

    private void MigrateOverrideSwitches()
    {
        JsonObject? stored = null;
        try
        {
            if (File.Exists(FilePath))
                stored = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLogService.Log("Config", $"Could not read settings for the override migration: {ex.Message}");
        }

        if (stored?.ContainsKey(UseCliDirectoryOverride.Key) != true)
            UseCliDirectoryOverride.Value = !string.IsNullOrEmpty(CliDirectoryOverride.Value);
        if (stored?.ContainsKey(UseCliDataDirectoryOverride.Key) != true)
            UseCliDataDirectoryOverride.Value = !string.IsNullOrEmpty(CliDataDirectoryOverride.Value);
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
        RepromptGrace.GracePeriodSeconds = Math.Clamp(seconds, 0, 300);
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
        yield return (ShowTags.Key, (object?)ShowTags.Value);
        yield return (ShowWatchtowerTags.Key,ShowWatchtowerTags.Value);
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
        yield return (UseCliDirectoryOverride.Key, (object?)UseCliDirectoryOverride.Value);
        yield return (CliDirectoryOverride.Key, CliDirectoryOverride.Value);
        yield return (UsePortableDataDirectory.Key, (object?)UsePortableDataDirectory.Value);
        yield return (UseCliDataDirectoryOverride.Key, (object?)UseCliDataDirectoryOverride.Value);
        yield return (CliDataDirectoryOverride.Key, CliDataDirectoryOverride.Value);
        yield return (DebugLogging.Key, (object?)DebugLogging.Value);
    }
}
