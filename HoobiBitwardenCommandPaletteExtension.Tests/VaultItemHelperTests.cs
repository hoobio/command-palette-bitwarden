using HoobiBitwardenCommandPaletteExtension.Helpers;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class VaultItemHelperTests
{
  [Theory]
  [InlineData("Mastercard", true, "https://vault.bitwarden.com/images/mastercard-dark.png")]
  [InlineData("Mastercard", false, "https://vault.bitwarden.com/images/mastercard-light.png")]
  [InlineData("Visa", false, "https://vault.bitwarden.com/images/visa-light.png")]
  [InlineData("American Express", true, "https://vault.bitwarden.com/images/american_express-dark.png")]
  [InlineData("Diners Club", false, "https://vault.bitwarden.com/images/diners_club-light.png")]
  public void GetCardBrandImageUrl_BuildsExpectedUrl(string brand, bool isDark, string expected)
  {
    BitwardenCliService.ResetStaticState();
    Assert.Equal(expected, VaultItemHelper.GetCardBrandImageUrl(brand, isDark));
  }

  [Fact]
  public void GetIcon_Card_NoBrand_ReturnsCardGlyph()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.Card };
    var icon = VaultItemHelper.GetIcon(item, showWebsiteIcons: true);
    Assert.Equal("\uE8C7", icon.Dark.Icon);
  }

  [Fact]
  public void GetIcon_Card_BrandSet_WebIconsDisabled_ReturnsCardGlyph()
  {
    var item = new BitwardenItem { Type = BitwardenItemType.Card, CardBrand = "Visa" };
    var icon = VaultItemHelper.GetIcon(item, showWebsiteIcons: false);
    Assert.Equal("\uE8C7", icon.Dark.Icon);
  }

  [Theory]
  [InlineData("user@host.com", true)]
  [InlineData("git@github.com", true)]
  [InlineData("deploy+bot@server.example.org", true)]
  [InlineData("root@192.168.1.1", true)]
  [InlineData("user@host-name.com", true)]
  [InlineData("user@host_name.com", true)]
  [InlineData("user.name@host.com", true)]
  [InlineData(null, false)]
  [InlineData("", false)]
  [InlineData("nope", false)]
  [InlineData("@host.com", false)]
  [InlineData("user@", false)]
  [InlineData("user name@host.com", false)]
  [InlineData("user@host .com", false)]
  public void IsValidSshHost_ValidatesCorrectly(string? host, bool expected)
  {
    Assert.Equal(expected, VaultItemHelper.IsValidSshHost(host));
  }
}
