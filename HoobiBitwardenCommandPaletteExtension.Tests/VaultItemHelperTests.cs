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

  // Regression test: PowerToys 0.99 gates right-click context menus on
  // Command.Name being non-empty. The TrackedInvokable wrapper used to drop
  // the inner command's Name, which silently disabled right-click on every
  // vault item. See https://github.com/hoobio/command-palette-bitwarden/issues/140.
  [Fact]
  public void GetDefaultCommand_NoReprompt_ForwardsInnerCommandName()
  {
    var login = new BitwardenItem
    {
      Id = "test-login",
      Type = BitwardenItemType.Login,
      Uris = [new ItemUri("https://example.com", UriMatchType.Default)],
    };
    var note = new BitwardenItem { Id = "test-note", Type = BitwardenItemType.SecureNote };
    var ssh = new BitwardenItem
    {
      Id = "test-ssh",
      Type = BitwardenItemType.SshKey,
      CustomFields = new Dictionary<string, CustomField>
      {
        ["host"] = new CustomField("git@github.com", IsHidden: false),
      },
    };

    foreach (var cmd in new[]
    {
      VaultItemHelper.GetDefaultCommand(login),
      VaultItemHelper.GetDefaultCommand(note),
      VaultItemHelper.GetDefaultCommand(ssh),
    })
    {
      Assert.False(string.IsNullOrEmpty(cmd.Name), $"Default command Name was empty for {cmd.GetType().Name}");
    }
  }

  [Fact]
  public void BuildContextItems_NoReprompt_ForwardInnerCommandName()
  {
    var item = new BitwardenItem
    {
      Id = "test-login",
      Type = BitwardenItemType.Login,
      Reprompt = 0,
      Username = "user@test.com",
      Password = "secret",
      TotpSecret = "JBSWY3DPEHPK3PXP",
      Uris = [new ItemUri("https://example.com", UriMatchType.Default)],
    };
    var contextItems = VaultItemHelper.BuildContextItems(item);
    foreach (var ci in contextItems)
      Assert.False(string.IsNullOrEmpty(ci.Command?.Name), $"Context item '{ci.Title}' had empty Command.Name");
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
    Assert.False(RepromptPage.IsWithinGracePeriod("item-a"));

    RepromptPage.RecordVerification("item-a");
    Assert.True(RepromptPage.IsWithinGracePeriod("item-a"));
  }

  [Fact]
  public void RepromptGracePeriod_IsScopedToVerifiedItem()
  {
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.ClearGracePeriod();

    RepromptPage.RecordVerification("item-a");

    Assert.True(RepromptPage.IsWithinGracePeriod("item-a"));
    Assert.False(RepromptPage.IsWithinGracePeriod("item-b"));
    RepromptPage.ClearGracePeriod();
  }

  [Fact]
  public void RepromptGracePeriod_ClearGracePeriod_ResetsState()
  {
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.RecordVerification("item-a");
    Assert.True(RepromptPage.IsWithinGracePeriod("item-a"));

    RepromptPage.ClearGracePeriod();
    Assert.False(RepromptPage.IsWithinGracePeriod("item-a"));
  }

  [Fact]
  public void RepromptGracePeriod_ZeroSeconds_AlwaysFalse()
  {
    RepromptPage.GracePeriodSeconds = 0;
    RepromptPage.RecordVerification("item-a");
    Assert.False(RepromptPage.IsWithinGracePeriod("item-a"));
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.ClearGracePeriod();
  }

  [Fact]
  public void GetDefaultCommand_WithReprompt_GracePeriod_BypassesReprompt()
  {
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.RecordVerification("test-1");
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
  public void GetDefaultCommand_GracePeriod_ScopedToItem_OtherItemStillProtected()
  {
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.ClearGracePeriod();
    RepromptPage.RecordVerification("verified-item");

    var svc = new BitwardenCliService();
    var otherItem = new BitwardenItem
    {
      Id = "other-item",
      Type = BitwardenItemType.Login,
      Reprompt = 1,
      Uris = [new ItemUri("https://example.com", UriMatchType.Default)],
    };
    var cmd = VaultItemHelper.GetDefaultCommand(otherItem, svc);
    Assert.IsType<RepromptPage>(cmd);
    RepromptPage.ClearGracePeriod();
  }

  [Fact]
  public void BuildContextItems_Login_Reprompt_GracePeriod_BypassesReprompt()
  {
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.RecordVerification("test-login");
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

  [Fact]
  public void RecordVerification_DoesNotPersistAcrossProcesses()
  {
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.ClearGracePeriod();

    RepromptPage.RecordVerification("item-a");

    var graceFile = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "HoobiBitwardenCommandPalette", "grace.json");
    Assert.False(File.Exists(graceFile));
    RepromptPage.ClearGracePeriod();
  }

  [Fact]
  public void BuildBaseTags_ExcludesTotp()
  {
    var item = new BitwardenItem
    {
      Id = "totp-1",
      Type = BitwardenItemType.Login,
      Favorite = true,
      TotpSecret = "JBSWY3DPEHPK3PXP",
    };
    var baseTags = VaultItemHelper.BuildBaseTags(item, showWatchtowerTags: false);
    Assert.Single(baseTags);
    Assert.Contains("\u2605", baseTags[0].Text, StringComparison.Ordinal);
  }

  [Fact]
  public void BuildTags_IncludesTotp_WhenLive()
  {
    var item = new BitwardenItem
    {
      Id = "totp-2",
      Type = BitwardenItemType.Login,
      HasTotp = true,
      TotpSecret = "JBSWY3DPEHPK3PXP",
    };
    var tags = VaultItemHelper.BuildTags(item, showWatchtowerTags: false, totpTagStyle: "live");
    Assert.Single(tags);
    Assert.Contains("s)", tags[0].Text, StringComparison.Ordinal);
  }

  [Fact]
  public void BuildTotpTag_ReturnsLiveTag()
  {
    var tag = VaultItemHelper.BuildTotpTag("JBSWY3DPEHPK3PXP");
    Assert.NotNull(tag);
    Assert.Contains("s)", tag!.Text, StringComparison.Ordinal);
  }

  [Fact]
  public void BuildBaseTags_AndBuildTags_ProduceSameNonTotpTags()
  {
    var item = new BitwardenItem
    {
      Id = "combo-1",
      Type = BitwardenItemType.Login,
      Favorite = true,
      HasTotp = true,
      TotpSecret = "JBSWY3DPEHPK3PXP",
    };
    var baseTags = VaultItemHelper.BuildBaseTags(item, showWatchtowerTags: false);
    var fullTags = VaultItemHelper.BuildTags(item, showWatchtowerTags: false, totpTagStyle: "live");
    Assert.Equal(baseTags.Length + 1, fullTags.Length);
    for (int i = 0; i < baseTags.Length; i++)
      Assert.Equal(baseTags[i].Text, fullTags[i].Text);
  }

  [Fact]
  public void BuildOrganizationTag_OffMode_ReturnsNull()
  {
    var item = new BitwardenItem { Id = "i", Type = BitwardenItemType.Login, OrganizationId = "org-1" };
    Assert.Null(VaultItemHelper.BuildOrganizationTag(item, "Acme Corp", "off"));
  }

  [Fact]
  public void BuildOrganizationTag_NoOrgId_ReturnsNull()
  {
    var item = new BitwardenItem { Id = "i", Type = BitwardenItemType.Login };
    Assert.Null(VaultItemHelper.BuildOrganizationTag(item, "Acme Corp", "icon"));
  }

  [Fact]
  public void BuildOrganizationTag_NoName_ReturnsNull()
  {
    var item = new BitwardenItem { Id = "i", Type = BitwardenItemType.Login, OrganizationId = "org-1" };
    Assert.Null(VaultItemHelper.BuildOrganizationTag(item, null, "icon"));
    Assert.Null(VaultItemHelper.BuildOrganizationTag(item, "", "icon"));
  }

  [Theory]
  [InlineData("Nintex", "N")]
  [InlineData("Acme", "A")]
  [InlineData("test", "T")]
  [InlineData("Test Organization", "TO")]
  [InlineData("Bitwarden Engineering Team", "BET")]
  [InlineData("A B C D E", "ABC")]
  [InlineData("  Spaced  Out  ", "SO")]
  public void GetOrganizationInitials_Cases(string name, string expected)
  {
    Assert.Equal(expected, VaultItemHelper.GetOrganizationInitials(name));
  }

  [Fact]
  public void BuildOrganizationTag_InitialsMode_SingleWord_FirstLetter()
  {
    var item = new BitwardenItem { Id = "i", Type = BitwardenItemType.Login, OrganizationId = "org-1" };
    var tag = VaultItemHelper.BuildOrganizationTag(item, "Nintex", "initials");
    Assert.NotNull(tag);
    Assert.Equal("N", tag!.Text);
    Assert.Equal("Nintex", tag.ToolTip);
  }

  [Fact]
  public void BuildOrganizationTag_InitialsMode_MultiWord_Initials()
  {
    var item = new BitwardenItem { Id = "i", Type = BitwardenItemType.Login, OrganizationId = "org-1" };
    var tag = VaultItemHelper.BuildOrganizationTag(item, "Test Organization", "initials");
    Assert.NotNull(tag);
    Assert.Equal("TO", tag!.Text);
    Assert.Equal("Test Organization", tag.ToolTip);
  }

  [Fact]
  public void BuildOrganizationTag_NameMode_ShortName_NotTruncated()
  {
    var item = new BitwardenItem { Id = "i", Type = BitwardenItemType.Login, OrganizationId = "org-1" };
    var tag = VaultItemHelper.BuildOrganizationTag(item, "Acme Corp", "name");
    Assert.NotNull(tag);
    Assert.Equal("Acme Corp", tag!.Text);
    Assert.Equal("Acme Corp", tag.ToolTip);
  }

  [Fact]
  public void BuildOrganizationTag_NameMode_LongName_TruncatedWithTooltip()
  {
    var item = new BitwardenItem { Id = "i", Type = BitwardenItemType.Login, OrganizationId = "org-1" };
    var full = "Some Very Long Organisation Name Indeed";
    var tag = VaultItemHelper.BuildOrganizationTag(item, full, "name");
    Assert.NotNull(tag);
    Assert.Equal(21, tag!.Text.Length);
    Assert.EndsWith("…", tag.Text, StringComparison.Ordinal);
    Assert.Equal(full, tag.ToolTip);
  }

  [Fact]
  public void GetOrganizationColor_SameId_SameColor()
  {
    var a = VaultItemHelper.GetOrganizationColor("d3aaa1f4-1234-4abc-9876-fedcba987654");
    var b = VaultItemHelper.GetOrganizationColor("d3aaa1f4-1234-4abc-9876-fedcba987654");
    Assert.Equal(a.Color.R, b.Color.R);
    Assert.Equal(a.Color.G, b.Color.G);
    Assert.Equal(a.Color.B, b.Color.B);
  }

  [Fact]
  public void GetOrganizationColor_DifferentIds_SpreadAcrossPalette()
  {
    var ids = new[]
    {
      "00000000-0000-0000-0000-000000000001",
      "00000000-0000-0000-0000-000000000002",
      "00000000-0000-0000-0000-000000000003",
      "00000000-0000-0000-0000-000000000004",
      "00000000-0000-0000-0000-000000000005",
      "00000000-0000-0000-0000-000000000006",
      "00000000-0000-0000-0000-000000000007",
      "00000000-0000-0000-0000-000000000008",
    };
    var distinctColors = ids
      .Select(VaultItemHelper.GetOrganizationColor)
      .Select(c => (c.Color.R, c.Color.G, c.Color.B))
      .Distinct()
      .Count();
    // 8 distinct GUIDs should hit at least 3 distinct palette entries (loose lower bound).
    Assert.True(distinctColors >= 3, $"Expected colour spread, got {distinctColors} distinct colours");
  }
}
