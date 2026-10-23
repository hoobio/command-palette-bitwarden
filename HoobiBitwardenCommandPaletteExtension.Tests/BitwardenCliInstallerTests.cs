using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class BitwardenCliInstallerTests
{
  private sealed class ThrowingHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("network unavailable");
  }

  [Fact]
  public async Task IsWingetAvailable_VersionPrinted_ReturnsTrue()
  {
    var factory = new FakeProcessFactory();
    var process = new FakeCliProcess(stdout: "v1.7.10561\n", exitCode: 0);
    factory.Enqueue(process);
    var installer = new BitwardenCliInstaller(processFactory: factory.Create);
    Assert.True(await installer.IsWingetAvailableAsync(TestContext.Current.CancellationToken));
    Assert.True(process.Disposed);
  }

  [Fact]
  public async Task IsWingetAvailable_NoOutput_ReturnsFalse()
  {
    var factory = new FakeProcessFactory();
    factory.Enqueue(new FakeCliProcess(stdout: "", exitCode: 1));
    var installer = new BitwardenCliInstaller(processFactory: factory.Create);
    Assert.False(await installer.IsWingetAvailableAsync(TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task IsWingetAvailable_FactoryThrows_ReturnsFalse()
  {
    var factory = new FakeProcessFactory();
    var installer = new BitwardenCliInstaller(processFactory: factory.Create);
    Assert.False(await installer.IsWingetAvailableAsync(TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task IsWingetAvailable_NoLineBeforeCancel_KillsAndReturnsFalse()
  {
    var factory = new FakeProcessFactory();
    var hung = new FakeCliProcess(0);
    factory.Enqueue(hung);
    var installer = new BitwardenCliInstaller(processFactory: factory.Create);
    using var cts = new CancellationTokenSource(50);

    Assert.False(await installer.IsWingetAvailableAsync(cts.Token));
    Assert.True(hung.Killed);
  }

  [Fact]
  public async Task InstallViaWinget_Cancelled_KillsProcessTree()
  {
    var factory = new FakeProcessFactory();
    var hung = new FakeCliProcess(0);
    factory.Enqueue(hung);
    var installer = new BitwardenCliInstaller(processFactory: factory.Create);
    using var cts = new CancellationTokenSource(50);

    var result = await installer.InstallViaWingetAsync(ct: cts.Token);

    Assert.False(result.Success);
    Assert.Equal("winget install cancelled", result.Error);
    Assert.True(hung.Killed);
  }

  [Fact]
  public async Task InstallViaWinget_PassesExpectedArgs()
  {
    var factory = new FakeProcessFactory();
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "", exitCode: 1));
    var installer = new BitwardenCliInstaller(processFactory: factory.Create);

    await installer.InstallViaWingetAsync(ct: TestContext.Current.CancellationToken);

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

    var result = await installer.InstallViaWingetAsync(ct: TestContext.Current.CancellationToken);

    Assert.False(result.Success);
    Assert.Equal(CliInstallMethod.Winget, result.Method);
    Assert.Null(result.CliPath);
  }

  [Fact]
  public async Task InstallViaDownload_HttpFails_ReturnsDownloadFailure()
  {
    var installer = new BitwardenCliInstaller(httpClientFactory: () => new HttpClient(new ThrowingHandler()));

    var result = await installer.InstallViaDownloadAsync(ct: TestContext.Current.CancellationToken);

    Assert.False(result.Success);
    Assert.Equal(CliInstallMethod.Download, result.Method);
    Assert.NotNull(result.Error);
  }

  [Fact]
  public void GetCandidateCliPaths_IncludesAppDataInstallDir_NotLinksShim()
  {
    var paths = BitwardenCliInstaller.GetCandidateCliPaths()
        .Select(p => p.Replace('\\', '/'))
        .ToList();

    Assert.Contains(paths, p => p.Contains("HoobiBitwardenCommandPalette/cli/bw.exe", StringComparison.OrdinalIgnoreCase));
    // The WinGet\Links\bw.exe symlink shim is deliberately excluded (bw can't run through it).
    Assert.DoesNotContain(paths, p => p.Contains("Microsoft/WinGet/Links/bw.exe", StringComparison.OrdinalIgnoreCase));
    Assert.All(paths, p => Assert.EndsWith("bw.exe", p, StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void GetCandidateCliPaths_IncludesDirsFromProcessPath()
  {
    var original = Environment.GetEnvironmentVariable("PATH");
    try
    {
      Environment.SetEnvironmentVariable("PATH", @"C:\hoobi-test-cli-dir");
      var paths = BitwardenCliInstaller.GetCandidateCliPaths()
          .Select(p => p.Replace('\\', '/'))
          .ToList();
      Assert.Contains(paths, p => p.Equals("C:/hoobi-test-cli-dir/bw.exe", StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
      Environment.SetEnvironmentVariable("PATH", original);
    }
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
