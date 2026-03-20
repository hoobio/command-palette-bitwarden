using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class DebugLogServiceTests : IDisposable
{
  public DebugLogServiceTests()
  {
    DebugLogService.Clear();
    DebugLogService.Enabled = true;
  }

  public void Dispose()
  {
    DebugLogService.Clear();
    DebugLogService.Enabled = false;
    GC.SuppressFinalize(this);
  }

  [Fact]
  public void Log_WhenDisabled_DoesNotRecord()
  {
    DebugLogService.Enabled = false;
    DebugLogService.Log("Test", "should not appear");
    Assert.Equal(0, DebugLogService.Count);
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
