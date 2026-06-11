using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class BitwardenCliInstallerTests
{
  private sealed class ThrowingHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("network unavailable");
  }

  // --- winget availability ---

  [Fact]
  public void IsWingetAvailable_VersionPrinted_ReturnsTrue()
  {
    var factory = new FakeProcessFactory();
    factory.Enqueue(new FakeCliProcess(stdout: "v1.7.10561\n", exitCode: 0));
    var installer = new BitwardenCliInstaller(processFactory: factory.Create);
    Assert.True(installer.IsWingetAvailable());
  }

  [Fact]
  public void IsWingetAvailable_NoOutput_ReturnsFalse()
  {
    var factory = new FakeProcessFactory();
    factory.Enqueue(new FakeCliProcess(stdout: "", exitCode: 1));
    var installer = new BitwardenCliInstaller(processFactory: factory.Create);
    Assert.False(installer.IsWingetAvailable());
  }

  [Fact]
  public void IsWingetAvailable_FactoryThrows_ReturnsFalse()
  {
    // Empty queue makes the factory throw (winget.exe not launchable).
    var factory = new FakeProcessFactory();
    var installer = new BitwardenCliInstaller(processFactory: factory.Create);
    Assert.False(installer.IsWingetAvailable());
  }

  // --- winget install ---

  [Fact]
  public async Task InstallViaWinget_PassesExpectedArgs()
  {
    var factory = new FakeProcessFactory();
    // Exit 1 short-circuits before the (filesystem) path resolution.
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "", exitCode: 1));
    var installer = new BitwardenCliInstaller(processFactory: factory.Create);

    await installer.InstallViaWingetAsync();

    Assert.Contains("install", factory.LastPsi!.Arguments, StringComparison.Ordinal);
    Assert.Contains("--id Bitwarden.CLI", factory.LastPsi!.Arguments, StringComparison.Ordinal);
    Assert.Contains("--silent", factory.LastPsi!.Arguments, StringComparison.Ordinal);
    Assert.Contains("--scope user", factory.LastPsi!.Arguments, StringComparison.Ordinal);
    Assert.Contains("--accept-package-agreements", factory.LastPsi!.Arguments, StringComparison.Ordinal);
  }

  [Fact]
  public async Task InstallViaWinget_NonZeroExit_ReturnsFailure()
  {
    var factory = new FakeProcessFactory();
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "0x8a15000f", exitCode: 1));
    var installer = new BitwardenCliInstaller(processFactory: factory.Create);

    var result = await installer.InstallViaWingetAsync();

    Assert.False(result.Success);
    Assert.Equal(CliInstallMethod.Winget, result.Method);
    Assert.Null(result.CliPath);
  }

  // --- download fallback ---

  [Fact]
  public async Task InstallViaDownload_HttpFails_ReturnsDownloadFailure()
  {
    var installer = new BitwardenCliInstaller(httpClientFactory: () => new HttpClient(new ThrowingHandler()));

    var result = await installer.InstallViaDownloadAsync();

    Assert.False(result.Success);
    Assert.Equal(CliInstallMethod.Download, result.Method);
    Assert.NotNull(result.Error);
  }

  // --- static helpers ---

  [Fact]
  public void GetCandidateCliPaths_IncludesWingetLinksAndAppDataInstallDir()
  {
    var paths = BitwardenCliInstaller.GetCandidateCliPaths()
        .Select(p => p.Replace('\\', '/'))
        .ToList();

    Assert.Contains(paths, p => p.Contains("Microsoft/WinGet/Links/bw.exe", StringComparison.OrdinalIgnoreCase));
    Assert.Contains(paths, p => p.Contains("HoobiBitwardenCommandPalette/cli/bw.exe", StringComparison.OrdinalIgnoreCase));
    Assert.All(paths, p => Assert.EndsWith("bw.exe", p, StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void ManualDownloadUrl_PointsAtBitwardenCliDownload()
  {
    Assert.Contains("bitwarden.com/download", BitwardenCliInstaller.ManualDownloadUrl, StringComparison.Ordinal);
    Assert.Contains("app=cli", BitwardenCliInstaller.ManualDownloadUrl, StringComparison.Ordinal);
    Assert.Contains("platform=windows", BitwardenCliInstaller.ManualDownloadUrl, StringComparison.Ordinal);
  }

  [Fact]
  public void ResolveWingetExecutable_ResolvesToWinget()
  {
    var winget = BitwardenCliInstaller.ResolveWingetExecutable();
    Assert.Contains("winget", winget, StringComparison.OrdinalIgnoreCase);
  }
}
