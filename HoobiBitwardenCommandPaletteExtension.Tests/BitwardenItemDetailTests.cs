using HoobiBitwardenCommandPaletteExtension.Models;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class BitwardenItemDetailTests
{
  // --- TruncateLine ---

  [Fact]
  public void TruncateLine_ReturnsNull_ForNullInput()
  {
    Assert.Null(BitwardenItem.TruncateLine(null, 60));
  }

  [Fact]
  public void TruncateLine_ReturnsNull_ForEmptyInput()
  {
    Assert.Null(BitwardenItem.TruncateLine("", 60));
  }

  [Fact]
  public void TruncateLine_ReturnsFirstLine_WhenMultipleLines()
  {
    Assert.Equal("First line", BitwardenItem.TruncateLine("First line\nSecond line", 60));
  }

  [Fact]
  public void TruncateLine_TruncatesWithEllipsis_WhenTooLong()
  {
    var longLine = new string('x', 100);
    var result = BitwardenItem.TruncateLine(longLine, 60);
    Assert.Equal(61, result!.Length); // 60 chars + ellipsis
    Assert.EndsWith("\u2026", result, StringComparison.Ordinal);
  }

  [Fact]
  public void TruncateLine_DoesNotTruncate_WhenWithinLimit()
  {
    Assert.Equal("Short", BitwardenItem.TruncateLine("Short", 60));
  }

  [Fact]
  public void TruncateLine_Trims_Whitespace()
  {
    Assert.Equal("Trimmed", BitwardenItem.TruncateLine("  Trimmed  ", 60));
  }

  // --- Subtitle edge cases ---

  [Fact]
  public void Subtitle_SecureNote_NoNotes_ReturnsDefault()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.SecureNote };
    Assert.Equal("Secure Note", item.Subtitle);
  }

  [Fact]
  public void Subtitle_SecureNote_FirstLineOnly()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.SecureNote, Notes = "Line one\nLine two" };
    Assert.Equal("Line one", item.Subtitle);
  }

  [Fact]
  public void Subtitle_Identity_FallsBackToFullName()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.Identity, IdentityFullName = "John Doe" };
    Assert.Equal("John Doe", item.Subtitle);
  }

  [Fact]
  public void Subtitle_Identity_Default_WhenNoFields()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.Identity };
    Assert.Equal("Identity", item.Subtitle);
  }

  [Fact]
  public void Subtitle_SshKey_ReturnsFingerprint()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.SshKey, SshFingerprint = "SHA256:abc123" };
    Assert.Equal("SHA256:abc123", item.Subtitle);
  }

  [Fact]
  public void Subtitle_SshKey_Default_WhenNoFingerprint()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.SshKey };
    Assert.Equal("SSH Key", item.Subtitle);
  }

  [Fact]
  public void Subtitle_Card_NoBrandNoNumber_ReturnsCard()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.Card };
    Assert.Equal("Card", item.Subtitle);
  }

  // --- SshHost from CustomFields ---

  [Fact]
  public void SshHost_CaseInsensitive()
  {
    var item = new BitwardenItem
    {
      CustomFields = new Dictionary<string, CustomField>(StringComparer.OrdinalIgnoreCase)
      {
        ["Host"] = new("user@server.com", false)
      }
    };
    Assert.Equal("user@server.com", item.SshHost);
  }

  // --- CardExpiration edge cases ---

  [Fact]
  public void CardExpiration_BothPresent()
  {
    var item = new BitwardenItem { CardExpMonth = "01", CardExpYear = "2030" };
    Assert.Equal("01 / 2030", item.CardExpiration);
  }

  [Fact]
  public void CardExpiration_OnlyYear_ReturnsNull()
  {
    var item = new BitwardenItem { CardExpYear = "2030" };
    Assert.Null(item.CardExpiration);
  }

  // --- FirstUri ---

  [Fact]
  public void FirstUri_WithMultipleUris_ReturnsFirst()
  {
    var item = new BitwardenItem
    {
      Uris =
      [
        new ItemUri("https://first.com", UriMatchType.Default),
        new ItemUri("https://second.com", UriMatchType.Default),
      ]
    };
    Assert.Equal("https://first.com", item.FirstUri);
  }
}
