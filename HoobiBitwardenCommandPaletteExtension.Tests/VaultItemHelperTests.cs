using HoobiBitwardenCommandPaletteExtension.Helpers;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Pages;
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

  [Theory]
  [InlineData("../../admin", "admin")]
  [InlineData("Visa<script>", "visascript")]
  [InlineData("Normal Brand", "normal_brand")]
  public void SanitizeBrandSlug_StripsUnsafeChars(string brand, string expected)
  {
    Assert.Equal(expected, VaultItemHelper.SanitizeBrandSlug(brand));
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

  [Fact]
  public void GetDefaultCommand_NoReprompt_ReturnsInvokable()
  {
    var item = new BitwardenItem
    {
      Id = "test-1",
      Type = BitwardenItemType.Login,
      Uris = [new ItemUri("https://example.com", UriMatchType.Default)],
    };
    var cmd = VaultItemHelper.GetDefaultCommand(item);
    Assert.IsNotType<RepromptPage>(cmd);
  }

  [Fact]
  public void GetDefaultCommand_WithReprompt_ReturnsRepromptPage()
  {
    RepromptPage.ClearGracePeriod();
    var svc = new BitwardenCliService();
    var item = new BitwardenItem
    {
      Id = "test-1",
      Type = BitwardenItemType.Login,
      Reprompt = 1,
      Uris = [new ItemUri("https://example.com", UriMatchType.Default)],
    };
    var cmd = VaultItemHelper.GetDefaultCommand(item, svc);
    Assert.IsType<RepromptPage>(cmd);
  }

  [Fact]
  public void GetDefaultCommand_RepromptNoService_ReturnsInvokable()
  {
    var item = new BitwardenItem
    {
      Id = "test-1",
      Type = BitwardenItemType.Login,
      Reprompt = 1,
      Uris = [new ItemUri("https://example.com", UriMatchType.Default)],
    };
    var cmd = VaultItemHelper.GetDefaultCommand(item);
    Assert.IsNotType<RepromptPage>(cmd);
  }

  [Fact]
  public void BuildContextItems_Login_Reprompt_AllFieldsProtected()
  {
    RepromptPage.ClearGracePeriod();
    var svc = new BitwardenCliService();
    var item = new BitwardenItem
    {
      Id = "test-login",
      Type = BitwardenItemType.Login,
      Reprompt = 1,
      Username = "user@test.com",
      Password = "secret",
      Uris = [new ItemUri("https://example.com", UriMatchType.Default)],
    };
    var contextItems = VaultItemHelper.BuildContextItems(item, svc);
    var copyItems = contextItems.Where(c => c.Title.StartsWith("Copy", StringComparison.Ordinal)).ToArray();
    Assert.True(copyItems.Length >= 2);
    foreach (var ci in copyItems)
      Assert.IsType<RepromptPage>(ci.Command);
  }

  [Fact]
  public void BuildContextItems_Login_NoReprompt_UsernameNotProtected()
  {
    var item = new BitwardenItem
    {
      Id = "test-login",
      Type = BitwardenItemType.Login,
      Reprompt = 0,
      Username = "user@test.com",
      Password = "secret",
    };
    var contextItems = VaultItemHelper.BuildContextItems(item);
    var usernameItem = contextItems.First(c => c.Title == "Copy Username");
    Assert.IsNotType<RepromptPage>(usernameItem.Command);
  }

  [Fact]
  public void BuildContextItems_Card_Reprompt_CardholderNameProtected()
  {
    RepromptPage.ClearGracePeriod();
    var svc = new BitwardenCliService();
    var item = new BitwardenItem
    {
      Id = "test-card",
      Type = BitwardenItemType.Card,
      Reprompt = 1,
      CardholderName = "John Doe",
      CardNumber = "4111111111111111",
      CardCode = "123",
      CardExpMonth = "12",
      CardExpYear = "2025",
    };
    var contextItems = VaultItemHelper.BuildContextItems(item, svc);
    var copyItems = contextItems.Where(c => c.Title.StartsWith("Copy", StringComparison.Ordinal)).ToArray();
    Assert.True(copyItems.Length >= 4);
    foreach (var ci in copyItems)
      Assert.IsType<RepromptPage>(ci.Command);
  }

  [Fact]
  public void BuildContextItems_Card_NoReprompt_CardholderNameNotProtected()
  {
    var item = new BitwardenItem
    {
      Id = "test-card",
      Type = BitwardenItemType.Card,
      Reprompt = 0,
      CardholderName = "John Doe",
      CardNumber = "4111111111111111",
    };
    var contextItems = VaultItemHelper.BuildContextItems(item);
    var holderItem = contextItems.First(c => c.Title == "Copy Cardholder Name");
    Assert.IsNotType<RepromptPage>(holderItem.Command);
  }

  [Fact]
  public void BuildContextItems_Identity_Reprompt_AllFieldsProtected()
  {
    RepromptPage.ClearGracePeriod();
    var svc = new BitwardenCliService();
    var item = new BitwardenItem
    {
      Id = "test-id",
      Type = BitwardenItemType.Identity,
      Reprompt = 1,
      IdentityEmail = "test@test.com",
      IdentityFullName = "Test User",
      IdentityPhone = "555-0100",
    };
    var contextItems = VaultItemHelper.BuildContextItems(item, svc);
    var copyItems = contextItems.Where(c => c.Title.StartsWith("Copy", StringComparison.Ordinal)).ToArray();
    Assert.True(copyItems.Length >= 3);
    foreach (var ci in copyItems)
      Assert.IsType<RepromptPage>(ci.Command);
  }

  [Fact]
  public void BuildContextItems_SshKey_Reprompt_AllFieldsProtected()
  {
    RepromptPage.ClearGracePeriod();
    var svc = new BitwardenCliService();
    var item = new BitwardenItem
    {
      Id = "test-ssh",
      Type = BitwardenItemType.SshKey,
      Reprompt = 1,
      SshPublicKey = "ssh-ed25519 AAAA...",
      SshFingerprint = "SHA256:abc123",
    };
    var contextItems = VaultItemHelper.BuildContextItems(item, svc);
    var copyItems = contextItems.Where(c => c.Title.StartsWith("Copy", StringComparison.Ordinal)).ToArray();
    Assert.True(copyItems.Length >= 2);
    foreach (var ci in copyItems)
      Assert.IsType<RepromptPage>(ci.Command);
  }

  [Fact]
  public void BuildContextItems_CustomField_Reprompt_AllProtected()
  {
    RepromptPage.ClearGracePeriod();
    var svc = new BitwardenCliService();
    var item = new BitwardenItem
    {
      Id = "test-custom",
      Type = BitwardenItemType.Login,
      Reprompt = 1,
      CustomFields = new Dictionary<string, CustomField>
      {
        ["apiKey"] = new("abc123", false),
        ["secret"] = new("hidden", true),
      },
    };
    var contextItems = VaultItemHelper.BuildContextItems(item, svc);
    var copyItems = contextItems.Where(c => c.Title.StartsWith("Copy", StringComparison.Ordinal)).ToArray();
    foreach (var ci in copyItems)
      Assert.IsType<RepromptPage>(ci.Command);
  }

  [Fact]
  public void RepromptGracePeriod_IsWithinGracePeriod_AfterVerification()
  {
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.ClearGracePeriod();
    Assert.False(RepromptPage.IsWithinGracePeriod());

    RepromptPage.RecordVerification();
    Assert.True(RepromptPage.IsWithinGracePeriod());
  }

  [Fact]
  public void RepromptGracePeriod_ClearGracePeriod_ResetsState()
  {
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.RecordVerification();
    Assert.True(RepromptPage.IsWithinGracePeriod());

    RepromptPage.ClearGracePeriod();
    Assert.False(RepromptPage.IsWithinGracePeriod());
  }

  [Fact]
  public void RepromptGracePeriod_ZeroSeconds_AlwaysFalse()
  {
    RepromptPage.GracePeriodSeconds = 0;
    RepromptPage.RecordVerification();
    Assert.False(RepromptPage.IsWithinGracePeriod());
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.ClearGracePeriod();
  }

  [Fact]
  public void GetDefaultCommand_WithReprompt_GracePeriod_BypassesReprompt()
  {
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.RecordVerification();
    var svc = new BitwardenCliService();
    var item = new BitwardenItem
    {
      Id = "test-1",
      Type = BitwardenItemType.Login,
      Reprompt = 1,
      Uris = [new ItemUri("https://example.com", UriMatchType.Default)],
    };
    var cmd = VaultItemHelper.GetDefaultCommand(item, svc);
    Assert.IsNotType<RepromptPage>(cmd);
    RepromptPage.ClearGracePeriod();
  }

  [Fact]
  public void BuildContextItems_Login_Reprompt_GracePeriod_BypassesReprompt()
  {
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.RecordVerification();
    var svc = new BitwardenCliService();
    var item = new BitwardenItem
    {
      Id = "test-login",
      Type = BitwardenItemType.Login,
      Reprompt = 1,
      Username = "user@test.com",
      Password = "secret",
    };
    var contextItems = VaultItemHelper.BuildContextItems(item, svc);
    var copyItems = contextItems.Where(c => c.Title.StartsWith("Copy", StringComparison.Ordinal)).ToArray();
    Assert.True(copyItems.Length >= 2);
    foreach (var ci in copyItems)
      Assert.IsNotType<RepromptPage>(ci.Command);
    RepromptPage.ClearGracePeriod();
  }
}
