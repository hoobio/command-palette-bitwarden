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

  public BitwardenSettingsManager()
  {
    Directory.CreateDirectory(SettingsDir);
    FilePath = Path.Combine(SettingsDir, "settings.json");
    Settings.Add(RememberSession);
    Settings.SettingsChanged += OnSettingsChanged;
    LoadSettings();
  }

  private void OnSettingsChanged(object sender, Settings e)
  {
    SaveSettings();

    if (!RememberSession.Value)
      SessionStore.Clear();
  }
}
