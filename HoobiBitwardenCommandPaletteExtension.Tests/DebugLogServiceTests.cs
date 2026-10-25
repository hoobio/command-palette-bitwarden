using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[CollectionDefinition("SessionStore", DisableParallelization = true)]
public class SessionStoreTestGroup;

[Collection("SessionStore")]
public class DebugLogServiceTests : IDisposable
{
  public DebugLogServiceTests()
  {
    DebugLogService.Clear();
    DebugLogService.LogDirectory = null;
    DebugLogService.Enabled = true;
  }

  public void Dispose()
  {
    DebugLogService.Clear();
    DebugLogService.Enabled = false;
    DebugLogService.LogDirectory = null;
    GC.SuppressFinalize(this);
  }

  private static string NewLogDirectory() => Path.Combine(Path.GetTempPath(), $"bw_logs_{Guid.NewGuid():N}");

  [Fact]
  public void Log_WhenEnabledWithADirectory_AppendsToTheDailyFile()
  {
    var directory = NewLogDirectory();
    try
    {
      DebugLogService.LogDirectory = directory;
      DebugLogService.Log("Disk", "first");
      DebugLogService.Log("Disk", "second");

      var file = Assert.Single(Directory.GetFiles(directory, DebugLogService.FilePrefix + "*.log"));
      var text = File.ReadAllText(file);
      Assert.Contains("[Disk] first", text, StringComparison.Ordinal);
      Assert.Contains("[Disk] second", text, StringComparison.Ordinal);
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Fact]
  public void Log_WhenDisabled_DoesNotWriteToDisk()
  {
    var directory = NewLogDirectory();
    DebugLogService.Enabled = false;
    DebugLogService.LogDirectory = directory;
    DebugLogService.Log("Disk", "not written");
    Assert.False(Directory.Exists(directory));
  }

  [Fact]
  public void Purge_DeletesFilesOlderThanTheRetention_AndKeepsNewerOnes()
  {
    var directory = NewLogDirectory();
    Directory.CreateDirectory(directory);
    try
    {
      var now = DateTime.UtcNow;
      var old = Path.Combine(directory, DebugLogService.FilePrefix + "old.log");
      var recent = Path.Combine(directory, DebugLogService.FilePrefix + "recent.log");
      var other = Path.Combine(directory, "other.log");
      foreach (var path in new[] { old, recent, other })
        File.WriteAllText(path, "x");
      File.SetLastWriteTimeUtc(old, now.AddDays(-(DebugLogService.RetentionDays + 1)));
      File.SetLastWriteTimeUtc(other, now.AddDays(-30));

      DebugLogService.Purge(directory, now);

      Assert.False(File.Exists(old));
      Assert.True(File.Exists(recent));
      Assert.True(File.Exists(other));
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Fact]
  public void ParseItems_UnexpectedOutput_DoesNotLogContent()
  {
    BitwardenCliService.ParseItems("{\"login\":{\"password\":\"hunter2\"}}");
    Assert.DoesNotContain("hunter2", DebugLogService.Export(), StringComparison.Ordinal);
  }

  [Fact]
  public void ApplyOrgFilter_DoesNotLogFilterValue()
  {
    var svc = new BitwardenCliService();
    _ = svc.ApplyOrgFilter([], "hunter2").ToList();
    Assert.DoesNotContain("hunter2", DebugLogService.Export(), StringComparison.Ordinal);
  }

  [Fact]
  public void Log_WhenDisabled_StillRecordsEntry()
  {
    DebugLogService.Enabled = false;
    DebugLogService.Log("Test", "should still appear");
    Assert.Equal(1, DebugLogService.Count);
  }

  [Fact]
  public void Log_WhenEnabled_RecordsEntry()
  {
    DebugLogService.Log("Cat", "hello");
    Assert.Equal(1, DebugLogService.Count);
  }

  [Fact]
  public void Export_ContainsCategoryAndMessage()
  {
    DebugLogService.Log("MyCategory", "some message");
    var output = DebugLogService.Export();
    Assert.Contains("[MyCategory]", output, StringComparison.Ordinal);
    Assert.Contains("some message", output, StringComparison.Ordinal);
  }

  [Fact]
  public void Export_WhenEmpty_ReturnsPlaceholder()
  {
    var output = DebugLogService.Export();
    Assert.Contains("no log entries", output, StringComparison.Ordinal);
  }

  [Fact]
  public void Clear_RemovesAllEntries()
  {
    DebugLogService.Log("A", "1");
    DebugLogService.Log("B", "2");
    Assert.Equal(2, DebugLogService.Count);
    DebugLogService.Clear();
    Assert.Equal(0, DebugLogService.Count);
  }

  [Fact]
  public void Log_EvictsOldEntries_WhenMaxExceeded()
  {
    for (var i = 0; i < 510; i++)
      DebugLogService.Log("Bulk", $"entry-{i:D4}");

    Assert.Equal(500, DebugLogService.Count);

    var output = DebugLogService.Export();
    Assert.DoesNotContain("entry-0000", output, StringComparison.Ordinal);
    Assert.DoesNotContain("entry-0009", output, StringComparison.Ordinal);
    Assert.Contains("entry-0509", output, StringComparison.Ordinal);
  }

  [Fact]
  public void Export_IncludesTimestamp()
  {
    DebugLogService.Log("Time", "check");
    var output = DebugLogService.Export();
    Assert.Matches(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\]", output);
  }

  [Fact]
  public void Export_IncludesHeader()
  {
    DebugLogService.Log("A", "1");
    var output = DebugLogService.Export();
    Assert.StartsWith("Debug log (1 entries)", output, StringComparison.Ordinal);
  }
}
