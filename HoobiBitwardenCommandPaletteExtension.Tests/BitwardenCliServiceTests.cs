using System.Text.RegularExpressions;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class BitwardenCliServiceTests
{
  // --- IsKnownFilter ---

  [Theory]
  [InlineData("folder", true)]
  [InlineData("url", true)]
  [InlineData("host", true)]
  [InlineData("type", true)]
  [InlineData("org", true)]
  [InlineData("is", true)]
  [InlineData("unknown", false)]
  [InlineData("has", false)]
  [InlineData("", false)]
  public void IsKnownFilter_RecognizesValidFilters(string key, bool expected)
  {
    Assert.Equal(expected, BitwardenCliService.IsKnownFilter(key));
  }

  // --- IsSessionInvalidError ---

  [Theory]
  [InlineData("You are not logged in.", true)]
  [InlineData("vault is locked", true)]
  [InlineData("invalid session", true)]
  [InlineData("session key is invalid", true)]
  [InlineData("Some other error", false)]
  [InlineData("", false)]
  public void IsSessionInvalidError_DetectsSessionErrors(string error, bool expected)
  {
    Assert.Equal(expected, BitwardenCliService.IsSessionInvalidError(error));
  }

  // --- ParseSearchFilters ---

  [Fact]
  public void ParseSearchFilters_Null_ReturnsEmptyFiltersAndNullText()
  {
    var (filters, text) = BitwardenCliService.ParseSearchFilters(null);
    Assert.Empty(filters);
    Assert.Null(text);
  }

  [Fact]
  public void ParseSearchFilters_PlainText_ReturnsTextOnly()
  {
    var (filters, text) = BitwardenCliService.ParseSearchFilters("my search");
    Assert.Empty(filters);
    Assert.Equal("my search", text);
  }

  [Fact]
  public void ParseSearchFilters_SingleFilter_ExtractedCorrectly()
  {
    var (filters, text) = BitwardenCliService.ParseSearchFilters("folder:Work");
    Assert.Single(filters);
    Assert.Equal("folder", filters[0].Key);
    Assert.Equal("Work", filters[0].Value);
    Assert.Null(text);
  }

  [Fact]
  public void ParseSearchFilters_FilterWithText_BothExtracted()
  {
    var (filters, text) = BitwardenCliService.ParseSearchFilters("folder:Work github");
    Assert.Single(filters);
    Assert.Equal("folder", filters[0].Key);
    Assert.Equal("github", text);
  }

  [Fact]
  public void ParseSearchFilters_HasFilter_ExtractedCorrectly()
  {
    var (filters, text) = BitwardenCliService.ParseSearchFilters("has:totp");
    Assert.Single(filters);
    Assert.Equal("has", filters[0].Key);
    Assert.Equal("totp", filters[0].Value);
    Assert.Null(text);
  }

  [Fact]
  public void ParseSearchFilters_MultipleFilters()
  {
    var (filters, text) = BitwardenCliService.ParseSearchFilters("type:login is:favorite searchterm");
    Assert.Equal(2, filters.Count);
    Assert.Equal("type", filters[0].Key);
    Assert.Equal("login", filters[0].Value);
    Assert.Equal("is", filters[1].Key);
    Assert.Equal("favorite", filters[1].Value);
    Assert.Equal("searchterm", text);
  }

  [Fact]
  public void ParseSearchFilters_UnknownFilter_TreatedAsText()
  {
    var (filters, text) = BitwardenCliService.ParseSearchFilters("unknown:value");
    Assert.Empty(filters);
    Assert.Equal("unknown:value", text);
  }

  // --- Matches ---

  [Fact]
  public void Matches_ByName()
  {
    var item = new BitwardenItem { Name = "GitHub", Type = BitwardenItemType.Login };
    Assert.True(BitwardenCliService.Matches(item, "git"));
    Assert.False(BitwardenCliService.Matches(item, "bitbucket"));
  }

  [Fact]
  public void Matches_ByNotes()
  {
    var item = new BitwardenItem { Name = "Test", Type = BitwardenItemType.SecureNote, Notes = "my secret note" };
    Assert.True(BitwardenCliService.Matches(item, "secret"));
  }

  [Fact]
  public void Matches_Login_ByUsername()
  {
    var item = new BitwardenItem { Name = "Test", Type = BitwardenItemType.Login, Username = "user@test.com" };
    Assert.True(BitwardenCliService.Matches(item, "user@test"));
  }

  [Fact]
  public void Matches_Login_ByUri()
  {
    var item = new BitwardenItem
    {
      Name = "Test",
      Type = BitwardenItemType.Login,
      Uris = [new ItemUri("https://github.com", UriMatchType.Default)]
    };
    Assert.True(BitwardenCliService.Matches(item, "github"));
  }

  [Fact]
  public void Matches_Card_ByBrand()
  {
    var item = new BitwardenItem { Name = "My Card", Type = BitwardenItemType.Card, CardBrand = "Visa" };
    Assert.True(BitwardenCliService.Matches(item, "Visa"));
  }

  [Fact]
  public void Matches_Card_ByCardholderName()
  {
    var item = new BitwardenItem { Name = "Card", Type = BitwardenItemType.Card, CardholderName = "John Doe" };
    Assert.True(BitwardenCliService.Matches(item, "John"));
  }

  [Fact]
  public void Matches_Identity_ByEmail()
  {
    var item = new BitwardenItem { Name = "Id", Type = BitwardenItemType.Identity, IdentityEmail = "test@example.com" };
    Assert.True(BitwardenCliService.Matches(item, "example"));
  }

  [Fact]
  public void Matches_Identity_ByFullName()
  {
    var item = new BitwardenItem { Name = "Id", Type = BitwardenItemType.Identity, IdentityFullName = "Jane Smith" };
    Assert.True(BitwardenCliService.Matches(item, "Jane"));
  }

  [Fact]
  public void Matches_Identity_ByUsername()
  {
    var item = new BitwardenItem { Name = "Id", Type = BitwardenItemType.Identity, IdentityUsername = "jsmith" };
    Assert.True(BitwardenCliService.Matches(item, "jsmith"));
  }

  [Fact]
  public void Matches_Identity_ByCompany()
  {
    var item = new BitwardenItem { Name = "Id", Type = BitwardenItemType.Identity, IdentityCompany = "Acme Corp" };
    Assert.True(BitwardenCliService.Matches(item, "Acme"));
  }

  [Fact]
  public void Matches_SshKey_ByFingerprint()
  {
    var item = new BitwardenItem { Name = "Key", Type = BitwardenItemType.SshKey, SshFingerprint = "SHA256:abc123" };
    Assert.True(BitwardenCliService.Matches(item, "abc123"));
  }

  [Fact]
  public void Matches_SshKey_ByHost()
  {
    var item = new BitwardenItem
    {
      Name = "Key",
      Type = BitwardenItemType.SshKey,
      CustomFields = new Dictionary<string, CustomField>(StringComparer.OrdinalIgnoreCase)
      {
        ["host"] = new("user@server.com", false)
      }
    };
    Assert.True(BitwardenCliService.Matches(item, "server"));
  }

  // --- Relevance ---

  [Fact]
  public void Relevance_ExactMatch_ReturnsZero()
  {
    var item = new BitwardenItem { Name = "GitHub" };
    var regex = new Regex(@"\bGitHub\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    Assert.Equal(0, BitwardenCliService.Relevance(item, "GitHub", regex));
  }

  [Fact]
  public void Relevance_StartsWith_ReturnsOne()
  {
    var item = new BitwardenItem { Name = "GitHub Enterprise" };
    var regex = new Regex(@"\bGit\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    Assert.Equal(1, BitwardenCliService.Relevance(item, "Git", regex));
  }

  [Fact]
  public void Relevance_WordBoundary_ReturnsTwo()
  {
    var item = new BitwardenItem { Name = "My GitHub Account" };
    var regex = new Regex(@"\bGitHub\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    Assert.Equal(2, BitwardenCliService.Relevance(item, "GitHub", regex));
  }

  [Fact]
  public void Relevance_Contains_ReturnsThree()
  {
    var item = new BitwardenItem { Name = "MyGitHubAccount" };
    var regex = new Regex(@"\bGitHub\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    Assert.Equal(3, BitwardenCliService.Relevance(item, "GitHub", regex));
  }

  [Fact]
  public void Relevance_NoMatch_ReturnsFour()
  {
    var item = new BitwardenItem { Name = "BitBucket" };
    var regex = new Regex(@"\bGitHub\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    Assert.Equal(4, BitwardenCliService.Relevance(item, "GitHub", regex));
  }

  // --- ParseItems ---

  [Fact]
  public void ParseItems_ValidLoginJson()
  {
    var json = """
    [
      {
        "id": "abc-123",
        "type": 1,
        "name": "GitHub",
        "notes": null,
        "favorite": true,
        "folderId": "folder-1",
        "organizationId": null,
        "reprompt": 0,
        "revisionDate": "2024-01-01T00:00:00Z",
        "login": {
          "username": "octocat",
          "password": "pass123",
          "totp": null,
          "uris": [
            { "uri": "https://github.com", "match": null }
          ]
        }
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.Single(items);
    var item = items[0];
    Assert.Equal("abc-123", item.Id);
    Assert.Equal("GitHub", item.Name);
    Assert.Equal(BitwardenItemType.Login, item.Type);
    Assert.Equal("octocat", item.Username);
    Assert.Equal("pass123", item.Password);
    Assert.True(item.Favorite);
    Assert.Equal("folder-1", item.FolderId);
    Assert.Single(item.Uris);
    Assert.Equal("https://github.com", item.Uris[0].Uri);
    Assert.Equal(UriMatchType.Default, item.Uris[0].Match);
  }

  [Fact]
  public void ParseItems_CardJson()
  {
    var json = """
    [
      {
        "id": "card-1",
        "type": 3,
        "name": "My Visa",
        "revisionDate": "2024-01-01T00:00:00Z",
        "card": {
          "cardholderName": "John Doe",
          "brand": "Visa",
          "number": "4111111111111111",
          "expMonth": "12",
          "expYear": "2025",
          "code": "123"
        }
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.Single(items);
    var item = items[0];
    Assert.Equal(BitwardenItemType.Card, item.Type);
    Assert.Equal("John Doe", item.CardholderName);
    Assert.Equal("Visa", item.CardBrand);
    Assert.Equal("4111111111111111", item.CardNumber);
    Assert.Equal("12", item.CardExpMonth);
    Assert.Equal("2025", item.CardExpYear);
    Assert.Equal("123", item.CardCode);
  }

  [Fact]
  public void ParseItems_IdentityJson()
  {
    var json = """
    [
      {
        "id": "id-1",
        "type": 4,
        "name": "My Identity",
        "revisionDate": "2024-01-01T00:00:00Z",
        "identity": {
          "firstName": "John",
          "middleName": null,
          "lastName": "Doe",
          "email": "john@example.com",
          "phone": "555-1234",
          "username": "jdoe",
          "company": "Acme",
          "address1": "123 Main St",
          "city": "Springfield",
          "state": "IL",
          "postalCode": "62701",
          "country": "US"
        }
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.Single(items);
    var item = items[0];
    Assert.Equal(BitwardenItemType.Identity, item.Type);
    Assert.Equal("John Doe", item.IdentityFullName);
    Assert.Equal("john@example.com", item.IdentityEmail);
    Assert.Equal("555-1234", item.IdentityPhone);
    Assert.Equal("jdoe", item.IdentityUsername);
    Assert.Equal("Acme", item.IdentityCompany);
    Assert.Contains("123 Main St", item.IdentityAddress, StringComparison.Ordinal);
    Assert.Contains("Springfield", item.IdentityAddress, StringComparison.Ordinal);
  }

  [Fact]
  public void ParseItems_SshKeyJson()
  {
    var json = """
    [
      {
        "id": "ssh-1",
        "type": 5,
        "name": "My SSH Key",
        "revisionDate": "2024-01-01T00:00:00Z",
        "sshKey": {
          "publicKey": "ssh-ed25519 AAAAC3...",
          "keyFingerprint": "SHA256:abc123",
          "privateKey": "-----BEGIN OPENSSH PRIVATE KEY-----"
        },
        "fields": [
          { "name": "host", "value": "git@github.com", "type": 0 }
        ]
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.Single(items);
    var item = items[0];
    Assert.Equal(BitwardenItemType.SshKey, item.Type);
    Assert.Equal("ssh-ed25519 AAAAC3...", item.SshPublicKey);
    Assert.Equal("SHA256:abc123", item.SshFingerprint);
    Assert.Equal("git@github.com", item.SshHost);
  }

  [Fact]
  public void ParseItems_SecureNoteJson()
  {
    var json = """
    [
      {
        "id": "note-1",
        "type": 2,
        "name": "My Note",
        "notes": "Secret content here",
        "revisionDate": "2024-01-01T00:00:00Z"
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.Single(items);
    Assert.Equal(BitwardenItemType.SecureNote, items[0].Type);
    Assert.Equal("Secret content here", items[0].Notes);
  }

  [Fact]
  public void ParseItems_EmptyArray_ReturnsEmpty()
  {
    Assert.Empty(BitwardenCliService.ParseItems("[]"));
  }

  [Fact]
  public void ParseItems_InvalidJson_ReturnsEmpty()
  {
    Assert.Empty(BitwardenCliService.ParseItems("not json"));
  }

  [Fact]
  public void ParseItems_SkipsInvalidType()
  {
    var json = """[{"id":"x","type":99,"name":"Bad","revisionDate":"2024-01-01T00:00:00Z"}]""";
    Assert.Empty(BitwardenCliService.ParseItems(json));
  }

  [Fact]
  public void ParseItems_LoginWithTotp()
  {
    var json = """
    [
      {
        "id": "t-1",
        "type": 1,
        "name": "With TOTP",
        "revisionDate": "2024-01-01T00:00:00Z",
        "login": {
          "username": "user",
          "totp": "JBSWY3DPEHPK3PXP",
          "uris": []
        }
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.True(items[0].HasTotp);
    Assert.Equal("JBSWY3DPEHPK3PXP", items[0].TotpSecret);
  }

  [Fact]
  public void ParseItems_LoginWithPasskey()
  {
    var json = """
    [
      {
        "id": "p-1",
        "type": 1,
        "name": "With Passkey",
        "revisionDate": "2024-01-01T00:00:00Z",
        "login": {
          "username": "user",
          "fido2Credentials": [{"credentialId": "abc"}],
          "uris": []
        }
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.True(items[0].HasPasskey);
  }

  [Fact]
  public void ParseItems_LoginWithUriMatchTypes()
  {
    var json = """
    [
      {
        "id": "u-1",
        "type": 1,
        "name": "URI Types",
        "revisionDate": "2024-01-01T00:00:00Z",
        "login": {
          "uris": [
            { "uri": "https://exact.com", "match": 3 },
            { "uri": "https://host.com", "match": 1 },
            { "uri": "https://default.com", "match": null }
          ]
        }
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.Equal(3, items[0].Uris.Count);
    Assert.Equal(UriMatchType.Exact, items[0].Uris[0].Match);
    Assert.Equal(UriMatchType.Host, items[0].Uris[1].Match);
    Assert.Equal(UriMatchType.Default, items[0].Uris[2].Match);
  }

  [Fact]
  public void ParseItems_CustomFields_HiddenField()
  {
    var json = """
    [
      {
        "id": "cf-1",
        "type": 1,
        "name": "With Fields",
        "revisionDate": "2024-01-01T00:00:00Z",
        "login": { "uris": [] },
        "fields": [
          { "name": "API Key", "value": "secret123", "type": 1 },
          { "name": "Region", "value": "US-East", "type": 0 }
        ]
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.Equal(2, items[0].CustomFields.Count);
    Assert.True(items[0].CustomFields["API Key"].IsHidden);
    Assert.False(items[0].CustomFields["Region"].IsHidden);
  }

  // --- ParseFolders ---

  [Fact]
  public void ParseFolders_ValidJson()
  {
    var json = """
    [
      { "id": "f1", "name": "Work" },
      { "id": "f2", "name": "Personal" }
    ]
    """;

    var folders = BitwardenCliService.ParseFolders(json);
    Assert.Equal(2, folders.Count);
    Assert.Equal("Work", folders["f1"]);
    Assert.Equal("Personal", folders["f2"]);
  }

  [Fact]
  public void ParseFolders_EmptyArray_ReturnsEmpty()
  {
    Assert.Empty(BitwardenCliService.ParseFolders("[]"));
  }

  [Fact]
  public void ParseFolders_InvalidJson_ReturnsEmpty()
  {
    Assert.Empty(BitwardenCliService.ParseFolders("broken"));
  }

  [Fact]
  public void ParseItems_PasswordRevisionDate_Parsed()
  {
    var json = """
    [
      {
        "id": "pr-1",
        "type": 1,
        "name": "Old Password",
        "revisionDate": "2024-06-01T00:00:00Z",
        "login": {
          "username": "user",
          "password": "weakpw",
          "passwordRevisionDate": "2023-01-01T00:00:00Z",
          "uris": []
        }
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.NotNull(items[0].PasswordRevisionDate);
    Assert.Equal(2023, items[0].PasswordRevisionDate!.Value.Year);
  }
}
