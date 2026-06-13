using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HoobiBitwardenCompanionIpc;

namespace HoobiBitwardenCommandPaletteExtension.Services;

// Starts the companion WinUI process and the IPC server it talks back to. The extension owns the
// session/CLI, so it hosts the server (one per extension process) and hands the companion the pipe
// name on the command line. The companion ships under Companion\ in the package and runs with the
// shared package identity (it's built as a packaged self-contained Windows App SDK app), so a plain
// child Process.Start is all that's needed.
internal static class CompanionLauncher
{
#if CHANNEL_DEV
  private const string Channel = "Dev";
#elif CHANNEL_PRERELEASE
  private const string Channel = "Prerelease";
#else
  private const string Channel = "Release";
#endif

  public const string ModeLogin = "login";
  public const string ModeItem = "item";
  public const string ModeGenerate = "generate";
  public const string ModeRotate = "rotate";

  // Backdrop material for companion windows, kept in sync from settings (single source of truth on
  // the extension). Passed on the command line so the window can pick its material at construction,
  // before the IPC channel is up.
  public static string Backdrop { get; set; } = "Mica";

  // Whether website icons are enabled (privacy setting). When on, the companion is handed the
  // resolved icon-server base at launch so item windows show favicons.
  public static bool ShowWebsiteIcons { get; set; } = true;

  private static readonly System.Threading.Lock Gate = new();
  private static CompanionIpcServer? _server;
  private static string? _pipeName;

  // Ensures the IPC server is running for this extension/service and returns the pipe name.
  public static string EnsureServer(BitwardenCliService service)
  {
    lock (Gate)
    {
      if (_server == null)
      {
        _pipeName = BuildPipeName();
        _server = new CompanionIpcServer(_pipeName, service);
        _server.Start();
      }
      return _pipeName!;
    }
  }

  public static void Launch(BitwardenCliService service, string mode, string? itemId = null)
  {
    var pipeName = EnsureServer(service);
    var exePath = ResolveCompanionExePath();
    if (exePath == null || !File.Exists(exePath))
    {
      DebugLogService.Log("CompanionIpc", $"Companion exe not found at {exePath ?? "(null)"}");
      return;
    }

    var psi = new ProcessStartInfo(exePath)
    {
      UseShellExecute = false,
      WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
    };
    psi.ArgumentList.Add(IpcLaunchArgs.Mode);
    psi.ArgumentList.Add(mode);
    psi.ArgumentList.Add(IpcLaunchArgs.Pipe);
    psi.ArgumentList.Add(pipeName);
    psi.ArgumentList.Add(IpcLaunchArgs.Backdrop);
    psi.ArgumentList.Add(Backdrop);
    psi.ArgumentList.Add(IpcLaunchArgs.IconBase);
    psi.ArgumentList.Add(ShowWebsiteIcons ? Helpers.VaultItemHelper.GetIconBaseUrl() : string.Empty);
    if (!string.IsNullOrEmpty(itemId))
    {
      psi.ArgumentList.Add(IpcLaunchArgs.ItemId);
      psi.ArgumentList.Add(itemId);
    }

    try
    {
      Process.Start(psi);
      DebugLogService.Log("CompanionIpc", $"Launched companion: mode={mode} id={itemId ?? "(none)"}");
    }
    catch (Exception ex)
    {
      DebugLogService.Log("CompanionIpc", $"Failed to launch companion: {ex.GetType().Name}: {ex.Message}");
    }
  }

  private static string? ResolveCompanionExePath()
  {
    // The companion is the package owner, so its exe sits at the package root. The extension runs
    // from the Extension\ subfolder, so step up one level to reach it.
    try
    {
      var installDir = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
      return Path.Combine(installDir, "HoobiBitwardenCompanion.exe");
    }
    catch (Exception ex)
    {
      DebugLogService.Log("CompanionIpc", $"Could not resolve package install location: {ex.Message}");
      var parent = Directory.GetParent(AppContext.BaseDirectory)?.FullName ?? AppContext.BaseDirectory;
      return Path.Combine(parent, "HoobiBitwardenCompanion.exe");
    }
  }

  private static string BuildPipeName()
  {
    var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)))[..16];
    return $"Hoobi.BwCompanion.{Channel}.{hash}";
  }
}
