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

  public ToggleSetting AutoClearClipboard { get; } = new(
      "autoClearClipboard",
      "Auto-Clear Clipboard",
      "Automatically clear sensitive data from clipboard after a delay",
      true);

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
    Settings.Add(RememberSession);
    Settings.Add(ShowWatchtowerTags);
    Settings.Add(ContextAwareness);
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
