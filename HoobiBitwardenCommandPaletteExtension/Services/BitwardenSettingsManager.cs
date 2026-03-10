using System;
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

    public BitwardenSettingsManager()
    {
        Directory.CreateDirectory(SettingsDir);
        FilePath = Path.Combine(SettingsDir, "settings.json");
        ContextItemLimit.Value = "3";
        TotpTagStyle.Value = "static";
        BackgroundRefresh.Value = "5";
        Settings.Add(RememberSession);
        Settings.Add(AutoLockTimeout);
        Settings.Add(ShowWatchtowerTags);
        Settings.Add(ContextAwareness);
        Settings.Add(ShowContextTag);
        Settings.Add(ShowPasskeyTag);
        Settings.Add(TotpTagStyle);
        Settings.Add(ContextItemLimit);
        Settings.Add(BackgroundRefresh);
        Settings.Add(AutoClearClipboard);
        Settings.Add(ClipboardClearDelay);
        Settings.SettingsChanged += OnSettingsChanged;
        LoadSettings();
        SyncClipboardSettings();
    }

    private void OnSettingsChanged(object sender, Settings e)
    {
        SaveSettings();
        SyncClipboardSettings();

        if (!RememberSession.Value)
            SessionStore.Clear();
    }

    private void SyncClipboardSettings()
    {
        SecureClipboardService.AutoClearEnabled = AutoClearClipboard.Value;
        if (int.TryParse(ClipboardClearDelay.Value, out var delay))
            SecureClipboardService.ClearDelaySeconds = delay;
    }
}
