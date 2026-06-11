using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal enum CliInstallMethod { None, AlreadyPresent, Winget, Download }

internal sealed record CliInstallResult(bool Success, CliInstallMethod Method, string? CliPath, string? Error);

// User-initiated acquisition of the Bitwarden CLI. Order: detect an existing
// install, then winget (preferred - it verifies its own packages and auto-updates),
// then a direct download we verify ourselves, and finally the caller falls back to
// opening the manual download page. Never runs unprompted: the Store frowns on a
// packaged app installing other software on its own, and a surprise install/UAC is
// poor UX. After success the caller writes the resolved path into the CLI override
// rather than relying on PATH, which is a launch-time snapshot for this process.
internal sealed class BitwardenCliInstaller
{
  public const string ManualDownloadUrl = "https://bitwarden.com/download/?app=cli&platform=windows";
  internal const string WingetPackageId = "Bitwarden.CLI";
  internal const string WingetInstallArgs =
      "install --id Bitwarden.CLI --exact --silent --scope user --accept-package-agreements --accept-source-agreements";

  private const int WingetVersionTimeoutMs = 10_000;
  private const int WingetInstallTimeoutMs = 180_000;

  private readonly CliProcessFactory _processFactory;
  private readonly Func<HttpClient> _httpClientFactory;
  private readonly string _installDir;

  public BitwardenCliInstaller(
      CliProcessFactory? processFactory = null,
      Func<HttpClient>? httpClientFactory = null,
      string? installDir = null)
  {
    _processFactory = processFactory ?? (psi => new RealCliProcess(Process.Start(psi)!));
    _httpClientFactory = httpClientFactory ?? (() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });
    _installDir = installDir ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HoobiBitwardenCommandPalette", "cli");
  }

  public async Task<CliInstallResult> EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken ct = default)
  {
    var existing = ResolveInstalledCliPath();
    if (existing != null)
    {
      DebugLogService.Log("Installer", $"CLI already present at {existing}");
      return new CliInstallResult(true, CliInstallMethod.AlreadyPresent, existing, null);
    }

    if (IsWingetAvailable())
    {
      DebugLogService.Log("Installer", "winget available; attempting install");
      var winget = await InstallViaWingetAsync(progress, ct);
      if (winget.Success)
        return winget;
      DebugLogService.Log("Installer", $"winget install failed ({winget.Error}); falling back to download");
    }
    else
    {
      DebugLogService.Log("Installer", "winget not available; using download fallback");
    }

    var download = await InstallViaDownloadAsync(progress, ct);
    return download.Success
        ? download
        : new CliInstallResult(false, CliInstallMethod.None, null, download.Error ?? "Automatic installation failed");
  }

  public bool IsWingetAvailable()
  {
    try
    {
      using var process = _processFactory(WingetStartInfo("--version"));
      var line = process.StandardOutput.ReadLine();
      try { process.Kill(true); } catch { }
      return !string.IsNullOrWhiteSpace(line);
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Installer", $"winget availability check failed: {ex.GetType().Name}: {ex.Message}");
      return false;
    }
  }

  public async Task<CliInstallResult> InstallViaWingetAsync(IProgress<string>? progress = null, CancellationToken ct = default)
  {
    progress?.Report("Installing Bitwarden CLI via winget...");
    try
    {
      using var process = _processFactory(WingetStartInfo(WingetInstallArgs));
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      cts.CancelAfter(WingetInstallTimeoutMs);

      var drainOut = process.StandardOutput.ReadToEndAsync(cts.Token);
      var drainErr = process.StandardError.ReadToEndAsync(cts.Token);
      await process.WaitForExitAsync(cts.Token);
      await Task.WhenAll(drainOut, drainErr);

      if (process.ExitCode != 0)
        return new CliInstallResult(false, CliInstallMethod.Winget, null, $"winget exited with code {process.ExitCode}");

      var path = ResolveInstalledCliPath();
      return path != null
          ? new CliInstallResult(true, CliInstallMethod.Winget, path, null)
          : new CliInstallResult(false, CliInstallMethod.Winget, null, "winget reported success but bw.exe was not found");
    }
    catch (OperationCanceledException)
    {
      return new CliInstallResult(false, CliInstallMethod.Winget, null, "winget install timed out");
    }
    catch (Exception ex)
    {
      return new CliInstallResult(false, CliInstallMethod.Winget, null, ex.Message);
    }
  }

  public async Task<CliInstallResult> InstallViaDownloadAsync(IProgress<string>? progress = null, CancellationToken ct = default)
  {
    var tempZip = Path.Combine(Path.GetTempPath(), $"bw-cli-{Guid.NewGuid():N}.zip");
    var tempExtract = Path.Combine(Path.GetTempPath(), $"bw-cli-{Guid.NewGuid():N}");
    try
    {
      progress?.Report("Downloading Bitwarden CLI...");
      using (var http = _httpClientFactory())
      using (var response = await http.GetAsync(ManualDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
      {
        response.EnsureSuccessStatusCode();
        using var fs = File.Create(tempZip);
        await response.Content.CopyToAsync(fs, ct);
      }

      progress?.Report("Extracting...");
      Directory.CreateDirectory(tempExtract);
      ZipFile.ExtractToDirectory(tempZip, tempExtract, overwriteFiles: true);

      var extractedBw = Directory.EnumerateFiles(tempExtract, "bw.exe", SearchOption.AllDirectories).FirstOrDefault();
      if (extractedBw == null)
        return new CliInstallResult(false, CliInstallMethod.Download, null, "Downloaded archive did not contain bw.exe");

      progress?.Report("Verifying signature...");
      if (!SignatureVerifier.IsTrustedBitwardenBinary(extractedBw))
        return new CliInstallResult(false, CliInstallMethod.Download, null, "Downloaded bw.exe failed Bitwarden signature verification");

      Directory.CreateDirectory(_installDir);
      var dest = Path.Combine(_installDir, "bw.exe");
      File.Copy(extractedBw, dest, overwrite: true);
      DebugLogService.Log("Installer", $"Installed verified bw.exe to {dest}");
      return new CliInstallResult(true, CliInstallMethod.Download, dest, null);
    }
    catch (OperationCanceledException)
    {
      return new CliInstallResult(false, CliInstallMethod.Download, null, "Download cancelled or timed out");
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Installer", $"Download install failed: {ex.GetType().Name}: {ex.Message}");
      return new CliInstallResult(false, CliInstallMethod.Download, null, ex.Message);
    }
    finally
    {
      try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
      try { if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, recursive: true); } catch { }
    }
  }

  // First existing bw.exe across the package managers' deterministic install
  // locations and PATH. Probing fixed locations sidesteps the stale-PATH problem:
  // winget updates the registry PATH, but this process's copy was snapshotted at
  // launch, so a freshly winget-installed shim wouldn't be visible via PATH alone.
  public static string? ResolveInstalledCliPath() =>
      GetCandidateCliPaths().FirstOrDefault(File.Exists);

  internal static IEnumerable<string> GetCandidateCliPaths()
  {
    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "bw.exe");
    yield return Path.Combine(localAppData, "HoobiBitwardenCommandPalette", "cli", "bw.exe");
    yield return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims", "bw.exe");

    var programData = Environment.GetEnvironmentVariable("ProgramData");
    if (!string.IsNullOrEmpty(programData))
      yield return Path.Combine(programData, "chocolatey", "bin", "bw.exe");

    var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      var candidate = TryCombine(dir, "bw.exe");
      if (candidate != null)
        yield return candidate;
    }
  }

  private static string? TryCombine(string dir, string file)
  {
    try { return Path.Combine(dir, file); }
    catch (ArgumentException) { return null; }
  }

  internal static string ResolveWingetExecutable()
  {
    var alias = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WindowsApps", "winget.exe");
    return File.Exists(alias) ? alias : "winget";
  }

  private static ProcessStartInfo WingetStartInfo(string args)
  {
    var psi = new ProcessStartInfo(ResolveWingetExecutable(), args)
    {
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true,
      StandardOutputEncoding = System.Text.Encoding.UTF8,
      StandardErrorEncoding = System.Text.Encoding.UTF8,
    };
    psi.Environment["NO_COLOR"] = "1";
    return psi;
  }
}
