using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HoobiBitwardenCompanionIpc;

namespace HoobiBitwardenCommandPaletteExtension.Services;

// Starts the companion WinUI process and the IPC server it talks back to. The extension owns the
// session/CLI, so it hosts the server (one per extension process) and hands the companion the pipe
// name on the command line. The companion runs as a second <Application> in the same package, so its
// exe lives under Companion\ in the install location.
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

    var psi = new ProcessStartInfo(exePath) { UseShellExecute = false };
    psi.ArgumentList.Add(IpcLaunchArgs.Mode);
    psi.ArgumentList.Add(mode);
    psi.ArgumentList.Add(IpcLaunchArgs.Pipe);
    psi.ArgumentList.Add(pipeName);
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
    try
    {
      var installDir = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
      return Path.Combine(installDir, "Companion", "HoobiBitwardenCompanion.exe");
    }
    catch (Exception ex)
    {
      DebugLogService.Log("CompanionIpc", $"Could not resolve package install location: {ex.Message}");
      // Fallback for non-packaged contexts: alongside the extension binary.
      return Path.Combine(AppContext.BaseDirectory, "Companion", "HoobiBitwardenCompanion.exe");
    }
  }

  private static string BuildPipeName()
  {
    var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)))[..16];
    return $"Hoobi.BwCompanion.{Channel}.{hash}";
  }
}
