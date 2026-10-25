using System.IO;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public sealed class BitwardenCliServiceDataFileTests : IDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bw_datafile_{Guid.NewGuid():N}");

  public BitwardenCliServiceDataFileTests() => Directory.CreateDirectory(_dir);

  public void Dispose() => Directory.Delete(_dir, true);

  private string WriteDataFile(string json = """{"stateVersion":78}""")
  {
    var path = Path.Combine(_dir, "data.json");
    File.WriteAllText(path, json);
    return path;
  }

  private sealed class Counter
  {
    public int Lists;
  }

  private static BitwardenCliService UnlockedService(string dataFile, Counter counter)
  {
    var svc = new BitwardenCliService(processFactory: psi =>
    {
      if (psi.Arguments.StartsWith("list ", StringComparison.Ordinal))
        Interlocked.Increment(ref counter.Lists);
      return new FakeCliProcess("[]\n");
    }) { DataFileOverride = dataFile };
    svc.SetSession("test-session");
    return svc;
  }

  private static async Task WaitFor(Func<bool> condition)
  {
    for (var i = 0; i < 100 && !condition(); i++)
      await Task.Delay(20, TestContext.Current.CancellationToken);
  }

  private sealed class Recorder
  {
    public readonly System.Collections.Concurrent.ConcurrentQueue<string> Args = new();
    public string Version = "2025.1.0\n";

    public FakeCliProcess Create(System.Diagnostics.ProcessStartInfo psi)
    {
      Args.Enqueue(psi.Arguments);
      return new FakeCliProcess(psi.Arguments switch
      {
        "--version" => Version,
        "status" => "{\"status\":\"locked\",\"serverUrl\":\"https://status.example.com\"}\n",
        _ => "",
      });
    }

    public int Count(string args) => Args.Count(a => a == args);
  }

  private static BitwardenCliService StatusService(Recorder recorder, string dataFile, BitwardenSettingsManager? settings = null)
  {
    BitwardenCliService.ResetStaticState();
    return new BitwardenCliService(settings, recorder.Create) { DataFileOverride = dataFile };
  }

  [Theory]
  [InlineData("""{"stateVersion":78,"global_account_activeAccountId":"u1","user_u1_token_accessToken":"t","user_u1_environment_environment":{"region":"EU","urls":{}}}""", "Locked", null)]
  [InlineData("""{"stateVersion":78}""", "Unauthenticated", null)]
  [InlineData("""{"stateVersion":60,"global_account_activeAccountId":"u1"}""", "Unauthenticated", null)]
  [InlineData("""{"stateVersion":78,"global_environment_environment":{"region":"Self-hosted","urls":{"base":"https://vault.example.com/"}}}""", "Unauthenticated", "https://vault.example.com")]
  [InlineData("""{"stateVersion":78,"global_environment_environment":{"region":"Self-hosted","urls":{}}}""", "Unauthenticated", null)]
  [InlineData("""{"stateVersion":78,"global_account_activeAccountId":"u1","user_u1_token_accessToken":"t","user_u1_environment_environment":{"region":"US","urls":{"base":null}}}""", "Locked", null)]
  public async Task GetVaultStatus_NoSession_ReadsDataFileWithoutStatusProcess(string json, string expected, string? serverUrl)
  {
    var recorder = new Recorder();
    var svc = StatusService(recorder, WriteDataFile(json));

    Assert.Equal(expected, (await svc.GetVaultStatusAsync()).ToString());
    Assert.Equal(0, recorder.Count("status"));
    Assert.Equal(serverUrl, BitwardenCliService.ServerUrl);
    Assert.True(BitwardenCliService.ServerUrlResolved);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("not json")]
  [InlineData("[]")]
  [InlineData("""{"global_account_activeAccountId":"u1","user_u1_token_accessToken":"t"}""")]
  [InlineData("""{"stateVersion":"78","global_account_activeAccountId":"u1","user_u1_token_accessToken":"t"}""")]
  [InlineData("""{"stateVersion":59,"global_account_activeAccountId":"u1","user_u1_token_accessToken":"t"}""")]
  public async Task GetVaultStatus_DataFileUnusable_FallsBackToStatusProcess(string? json)
  {
    var recorder = new Recorder();
    var svc = StatusService(recorder, json == null ? Path.Combine(_dir, "missing.json") : WriteDataFile(json));

    Assert.Equal(VaultStatus.Locked, await svc.GetVaultStatusAsync());
    Assert.Equal(1, recorder.Count("status"));
    Assert.Equal("https://status.example.com", BitwardenCliService.ServerUrl);
  }

  [Fact]
  public async Task GetVaultStatus_DataFilePathInvalid_FallsBackToStatusProcess()
  {
    var recorder = new Recorder();
    var svc = StatusService(recorder, "bad\0path");

    Assert.Equal(VaultStatus.Locked, await svc.GetVaultStatusAsync());
    Assert.Equal(1, recorder.Count("status"));
  }

  private BitwardenSettingsManager SettingsWithCli(string exe)
  {
    File.WriteAllText(exe, "");
    var settings = new BitwardenSettingsManager(Path.Combine(_dir, "settings.json"));
    settings.CliDirectoryOverride.Value = exe;
    return settings;
  }

  [Fact]
  public async Task GetVaultStatus_SameCliExecutable_ProbesVersionOnce()
  {
    var recorder = new Recorder();
    var svc = StatusService(recorder, WriteDataFile(), SettingsWithCli(Path.Combine(_dir, "bw.exe")));

    await svc.GetVaultStatusAsync();
    await svc.GetVaultStatusAsync();

    Assert.Equal(1, recorder.Count("--version"));
  }

  [Fact]
  public async Task GetVaultStatus_CliExecutableRewritten_ProbesVersionAgain()
  {
    var recorder = new Recorder();
    var exe = Path.Combine(_dir, "bw.exe");
    var svc = StatusService(recorder, WriteDataFile(), SettingsWithCli(exe));

    await svc.GetVaultStatusAsync();
    File.SetLastWriteTimeUtc(exe, File.GetLastWriteTimeUtc(exe).AddMinutes(1));
    await svc.GetVaultStatusAsync();

    Assert.Equal(2, recorder.Count("--version"));
  }

  [Fact]
  public async Task GetVaultStatus_LastProbeFailed_ProbesVersionAgain()
  {
    var recorder = new Recorder { Version = "" };
    var svc = StatusService(recorder, WriteDataFile(), SettingsWithCli(Path.Combine(_dir, "bw.exe")));

    Assert.Equal(VaultStatus.CliNotFound, await svc.GetVaultStatusAsync());
    recorder.Version = "2025.1.0\n";
    Assert.Equal(VaultStatus.Unauthenticated, await svc.GetVaultStatusAsync());
    await svc.GetVaultStatusAsync();

    Assert.Equal(2, recorder.Count("--version"));
  }

  [Fact]
  public async Task GetVaultStatus_CliConfigChanged_ProbesVersionAgain()
  {
    var recorder = new Recorder();
    var settings = SettingsWithCli(Path.Combine(_dir, "bw.exe"));
    var svc = StatusService(recorder, WriteDataFile(), settings);
    await svc.GetVaultStatusAsync();

    var other = Path.Combine(_dir, "other", "bw.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(other)!);
    await File.WriteAllTextAsync(other, "", TestContext.Current.CancellationToken);
    settings.CliDirectoryOverride.Value = other;
    settings.Settings.GetType().GetMethod("RaiseSettingsChanged",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
      .Invoke(settings.Settings, null);
    await svc.GetVaultStatusAsync();

    Assert.Equal(2, recorder.Count("--version"));
  }

  [Fact]
  public void CliProbeKey_BareNameNotOnPath_ReturnsNull() =>
    Assert.Null(BitwardenCliService.CliProbeKey.For($"bw_missing_{Guid.NewGuid():N}"));

  [Fact]
  public void CliProbeKey_RootedPathWithoutExtension_FindsExe()
  {
    var exe = Path.Combine(_dir, "bw.exe");
    File.WriteAllText(exe, "");
    Assert.Equal(exe, BitwardenCliService.CliProbeKey.For(Path.Combine(_dir, "bw"))!.Path);
  }

  [Fact]
  public async Task TriggerBackgroundRefresh_DataFileUnchanged_SkipsRefresh()
  {
    var counter = new Counter();
    var svc = UnlockedService(WriteDataFile(), counter);
    await svc.RefreshCacheAsync();
    svc.LastRefresh = DateTime.UtcNow.AddHours(-1);

    svc.TriggerBackgroundRefreshIfStale();
    await Task.Delay(200, TestContext.Current.CancellationToken);

    Assert.Equal(3, Volatile.Read(ref counter.Lists));
    Assert.True(DateTime.UtcNow - svc.LastRefresh < TimeSpan.FromMinutes(1));
  }

  [Fact]
  public async Task TriggerBackgroundRefresh_DataFileChanged_Refreshes()
  {
    var counter = new Counter();
    var path = WriteDataFile();
    var svc = UnlockedService(path, counter);
    await svc.RefreshCacheAsync();
    svc.LastRefresh = DateTime.UtcNow.AddHours(-1);
    File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));

    svc.TriggerBackgroundRefreshIfStale();
    await WaitFor(() => Volatile.Read(ref counter.Lists) >= 6);

    Assert.Equal(6, Volatile.Read(ref counter.Lists));
  }

  [Fact]
  public async Task TriggerBackgroundRefresh_DataFileLengthChangedWithSameWriteTime_Refreshes()
  {
    var counter = new Counter();
    var path = WriteDataFile();
    var svc = UnlockedService(path, counter);
    await svc.RefreshCacheAsync();
    svc.LastRefresh = DateTime.UtcNow.AddHours(-1);
    var writeTime = File.GetLastWriteTimeUtc(path);
    await File.WriteAllTextAsync(path, """{"stateVersion":78,"changed":true}""", TestContext.Current.CancellationToken);
    File.SetLastWriteTimeUtc(path, writeTime);

    svc.TriggerBackgroundRefreshIfStale();
    await WaitFor(() => Volatile.Read(ref counter.Lists) >= 6);

    Assert.Equal(6, Volatile.Read(ref counter.Lists));
  }

  [Fact]
  public async Task TriggerBackgroundRefresh_UnchangedPastMaxAge_Refreshes()
  {
    var counter = new Counter();
    var svc = UnlockedService(WriteDataFile(), counter);
    svc.UnchangedDataMaxAge = TimeSpan.Zero;
    await svc.RefreshCacheAsync();
    svc.LastRefresh = DateTime.UtcNow.AddHours(-1);

    svc.TriggerBackgroundRefreshIfStale();
    await WaitFor(() => Volatile.Read(ref counter.Lists) >= 6);

    Assert.Equal(6, Volatile.Read(ref counter.Lists));
  }

  [Fact]
  public async Task TriggerBackgroundRefresh_DataFileMissing_Refreshes()
  {
    var counter = new Counter();
    var path = WriteDataFile();
    var svc = UnlockedService(path, counter);
    await svc.RefreshCacheAsync();
    svc.LastRefresh = DateTime.UtcNow.AddHours(-1);
    File.Delete(path);

    svc.TriggerBackgroundRefreshIfStale();
    await WaitFor(() => Volatile.Read(ref counter.Lists) >= 6);

    Assert.Equal(6, Volatile.Read(ref counter.Lists));
  }
}
