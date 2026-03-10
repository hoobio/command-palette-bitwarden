namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class MatchesCommandTests
{
  [Theory]
  [InlineData("sy", "sync", true)]
  [InlineData("syn", "sync", true)]
  [InlineData("sync", "sync", true)]
  [InlineData("SYNC", "sync", true)]
  [InlineData("lo", "lock", true)]
  [InlineData("loc", "lock", true)]
  [InlineData("lock", "lock", true)]
  [InlineData("lo", "logout", true)]
  [InlineData("logout", "logout", true)]
  [InlineData("s", "sync", false)]
  [InlineData("", "sync", false)]
  [InlineData("xyz", "sync", false)]
  public void MatchesCommand_BehavesCorrectly(string search, string command, bool expected)
  {
    Assert.Equal(expected, HoobiBitwardenCommandPaletteExtensionPage.MatchesCommand(search, command));
  }
}
