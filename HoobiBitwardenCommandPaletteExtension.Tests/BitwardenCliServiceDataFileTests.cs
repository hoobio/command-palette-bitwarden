using System.IO;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public sealed class BitwardenCliServiceDataFileTests : IDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bw_datafile_{Guid.NewGuid():N}");

  public BitwardenCliServiceDataFileTests() => Directory.CreateDirectory(_dir);

  public void Dispose() => Directory.Delete(_dir, true);

  private string WriteDataFile(string json = "{}")
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
