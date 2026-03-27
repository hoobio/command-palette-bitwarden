using HoobiBitwardenCommandPaletteExtension.Models;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class BitwardenItemTests
{
  [Fact]
  public void Subtitle_Login_ReturnsUsername()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.Login, Username = "user@test.com" };
    Assert.Equal("user@test.com", item.Subtitle);
  }

  [Fact]
  public void Subtitle_Login_NoUsername_ReturnsEmpty()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.Login };
    Assert.Equal(string.Empty, item.Subtitle);
  }

  [Fact]
  public void Subtitle_Card_WithBrandAndNumber()
  {
    var item = new BitwardenItem
    {
      Type = BitwardenItemType.Card,
      CardBrand = "Visa",
      CardNumber = "4111111111111234"
    };
    Assert.Equal("Visa ····1234", item.Subtitle);
  }

  [Fact]
  public void Subtitle_Card_BrandOnly()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.Card, CardBrand = "Mastercard" };
    Assert.Equal("Mastercard", item.Subtitle);
  }

  [Fact]
  public void Subtitle_SecureNote_TruncatesLongNotes()
  {
    var longNote = new string('x', 100);
    var item = new BitwardenItem { Type = BitwardenItemType.SecureNote, Notes = longNote };
    Assert.True(item.Subtitle.Length <= 61);
    Assert.EndsWith("\u2026", item.Subtitle, StringComparison.Ordinal);
  }

  [Fact]
  public void Subtitle_Identity_PrefersEmail()
  {
    var item = new BitwardenItem
    {
      Type = BitwardenItemType.Identity,
      IdentityEmail = "test@test.com",
      IdentityFullName = "Test User"
    };
    Assert.Equal("test@test.com", item.Subtitle);
  }

  [Fact]
  public void CardExpiration_FormatsMonthAndYear()
  {
    var item = new BitwardenItem { CardExpMonth = "12", CardExpYear = "2025" };
    Assert.Equal("12 / 2025", item.CardExpiration);
  }

  [Fact]
  public void CardExpiration_Null_WhenMissing()
  {
    var item = new BitwardenItem { CardExpMonth = "12" };
    Assert.Null(item.CardExpiration);
  }

  [Fact]
  public void CardLast4_ExtractsLastFourDigits()
  {
    var item = new BitwardenItem { CardNumber = "4111111111111234" };
    Assert.Equal("1234", item.CardLast4);
  }

  [Fact]
  public void CardLast4_Null_WhenShortNumber()
  {
    var item = new BitwardenItem { CardNumber = "12" };
    Assert.Null(item.CardLast4);
  }

  [Fact]
  public void FirstUri_ReturnsFirstEntry()
  {
    var item = new BitwardenItem
    {
      Uris = [new ItemUri("https://example.com", UriMatchType.Default)]
    };
    Assert.Equal("https://example.com", item.FirstUri);
  }

  [Fact]
  public void FirstUri_Null_WhenEmpty()
  {
    var item = new BitwardenItem();
    Assert.Null(item.FirstUri);
  }

  [Fact]
  public void SshHost_ReturnsCustomFieldValue()
  {
    var item = new BitwardenItem
    {
      Type = BitwardenItemType.SshKey,
      CustomFields = new Dictionary<string, CustomField>
      {
        ["host"] = new CustomField("user@server.com", false)
      }
    };
    Assert.Equal("user@server.com", item.SshHost);
  }

  [Fact]
  public void SshHost_Null_WhenNoHostField()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.SshKey };
    Assert.Null(item.SshHost);
  }

  // --- Reprompt subtitle masking ---

  [Fact]
  public void Subtitle_SecureNote_Reprompt_MasksNotes()
  {
    var item = new BitwardenItem
    {
      Type = BitwardenItemType.SecureNote,
      Notes = "Secret content that should be hidden",
      Reprompt = 1,
    };
    Assert.Equal("Protected", item.Subtitle);
  }

  [Fact]
  public void Subtitle_SecureNote_NoReprompt_ShowsNotes()
  {
    var item = new BitwardenItem
    {
      Type = BitwardenItemType.SecureNote,
      Notes = "Visible content",
      Reprompt = 0,
    };
    Assert.Equal("Visible content", item.Subtitle);
  }

  [Fact]
  public void Subtitle_SecureNote_Reprompt_NoNotes_ShowsMasked()
  {
    var item = new BitwardenItem
    {
      Type = BitwardenItemType.SecureNote,
      Reprompt = 1,
    };
    Assert.Equal("Protected", item.Subtitle);
  }

  [Fact]
  public void Subtitle_Login_Reprompt_StillShowsUsername()
  {
    var item = new BitwardenItem
    {
      Type = BitwardenItemType.Login,
      Username = "user@test.com",
      Reprompt = 1,
    };
    Assert.Equal("user@test.com", item.Subtitle);
  }

  [Fact]
  public void Subtitle_Card_Reprompt_StillShowsBrandAndLast4()
  {
    var item = new BitwardenItem
    {
      Type = BitwardenItemType.Card,
      CardBrand = "Visa",
      CardNumber = "4111111111111234",
      Reprompt = 1,
    };
    Assert.Equal("Visa \u00B7\u00B7\u00B7\u00B71234", item.Subtitle);
  }
}
