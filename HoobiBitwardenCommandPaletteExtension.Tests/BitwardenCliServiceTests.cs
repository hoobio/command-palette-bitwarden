using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class BitwardenCliServiceTests
{
  // --- ResolveCliExecutable ---

  [Theory]
  [InlineData(null, "bw")]
  [InlineData("", "bw")]
  [InlineData("  ", "bw")]
  public void ResolveCliExecutable_EmptyOrNull_ReturnsBw(string? dir, string expected)
  {
    Assert.Equal(expected, BitwardenCliService.ResolveCliExecutable(dir));
  }

  [Fact]
  public void ResolveCliExecutable_WithDirectory_ReturnsCombinedPath()
  {
    var result = BitwardenCliService.ResolveCliExecutable(@"C:\tools\bitwarden");
    Assert.Equal(@"C:\tools\bitwarden\bw", result);
  }

  [Fact]
  public void ResolveCliExecutable_WithExePath_ReturnsAsIs()
  {
    var result = BitwardenCliService.ResolveCliExecutable(@"C:\tools\bw-windows-2026.2.0\bw.exe");
    Assert.Equal(@"C:\tools\bw-windows-2026.2.0\bw.exe", result);
  }

  [Fact]
  public void ResolveCliExecutable_WithBwFilename_ReturnsAsIs()
  {
    var result = BitwardenCliService.ResolveCliExecutable(@"C:\tools\bw-windows-2026.2.0\bw");
    Assert.Equal(@"C:\tools\bw-windows-2026.2.0\bw", result);
  }

  // --- ResolveDataDirectory ---

  [Fact]
  public void ResolveDataDirectory_AllEmpty_ReturnsNull()
  {
    Assert.Null(BitwardenCliService.ResolveDataDirectory(null, false, null));
  }

  [Fact]
  public void ResolveDataDirectory_ExplicitOverride_TakesPrecedence()
  {
    var result = BitwardenCliService.ResolveDataDirectory(@"C:\cli", true, @"C:\custom-data");
    Assert.Equal(@"C:\custom-data", result);
  }

  [Fact]
  public void ResolveDataDirectory_PortableWithDirectory_ReturnsCliDir()
  {
    var result = BitwardenCliService.ResolveDataDirectory(@"C:\tools\bw-portable", true, null);
    Assert.Equal(@"C:\tools\bw-portable", result);
  }

  [Fact]
  public void ResolveDataDirectory_PortableWithExePath_ReturnsParentDir()
  {
    var result = BitwardenCliService.ResolveDataDirectory(@"C:\tools\bw-portable\bw.exe", true, null);
    Assert.Equal(@"C:\tools\bw-portable", result);
  }

  [Fact]
  public void ResolveDataDirectory_PortableWithBwFilename_ReturnsParentDir()
  {
    var result = BitwardenCliService.ResolveDataDirectory(@"C:\tools\bw-portable\bw", true, "");
    Assert.Equal(@"C:\tools\bw-portable", result);
  }

  [Fact]
  public void ResolveDataDirectory_PortableDisabled_ReturnsNull()
  {
    Assert.Null(BitwardenCliService.ResolveDataDirectory(@"C:\tools\bw-portable", false, null));
  }

  [Fact]
  public void ResolveDataDirectory_PortableEnabledNoCliPath_ReturnsNull()
  {
    Assert.Null(BitwardenCliService.ResolveDataDirectory(null, true, null));
  }

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

  // --- SanitizeServerUrl ---

  [Theory]
  [InlineData(null, "(default)")]
  [InlineData("", "(default)")]
  [InlineData("https://vault.bitwarden.com", "https://vault.bitwarden.com")]
  [InlineData("https://vault.bitwarden.com/", "https://vault.bitwarden.com")]
  [InlineData("https://vault.bitwarden.eu", "https://vault.bitwarden.eu")]
  [InlineData("https://vault.hoobi.io", "[custom server]")]
  [InlineData("https://self-hosted.example.com", "[custom server]")]
  public void SanitizeServerUrl_MasksCustomServers(string? url, string expected)
  {
    Assert.Equal(expected, BitwardenCliService.SanitizeServerUrl(url));
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

  [Fact]
  public void ParseItems_LoginWithNullLoginNode_CreatesLoginWithDefaults()
  {
    var json = """[{"id":"n-1","type":1,"name":"Null Login","revisionDate":"2024-01-01T00:00:00Z","login":null}]""";
    var items = BitwardenCliService.ParseItems(json);
    Assert.Single(items);
    Assert.Equal(BitwardenItemType.Login, items[0].Type);
    Assert.Null(items[0].Username);
    Assert.Empty(items[0].Uris);
  }

  [Fact]
  public void ParseItems_IdentityWithAllAddressParts()
  {
    var json = """
    [
      {
        "id": "addr-1",
        "type": 4,
        "name": "Full Address",
        "revisionDate": "2024-01-01T00:00:00Z",
        "identity": {
          "firstName": "A",
          "middleName": "B",
          "lastName": "C",
          "address1": "Line 1",
          "address2": "Line 2",
          "address3": "Line 3",
          "city": "City",
          "state": "ST",
          "postalCode": "12345",
          "country": "US"
        }
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.Equal("A B C", items[0].IdentityFullName);
    Assert.Contains("Line 1", items[0].IdentityAddress!, StringComparison.Ordinal);
    Assert.Contains("Line 2", items[0].IdentityAddress!, StringComparison.Ordinal);
    Assert.Contains("Line 3", items[0].IdentityAddress!, StringComparison.Ordinal);
    Assert.Contains("City", items[0].IdentityAddress!, StringComparison.Ordinal);
    Assert.Contains("US", items[0].IdentityAddress!, StringComparison.Ordinal);
  }

  [Fact]
  public void ParseItems_IdentityWithOnlyFirstName()
  {
    var json = """
    [
      {
        "id": "id-partial",
        "type": 4,
        "name": "Partial",
        "revisionDate": "2024-01-01T00:00:00Z",
        "identity": { "firstName": "Solo" }
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.Equal("Solo", items[0].IdentityFullName);
    Assert.Null(items[0].IdentityAddress);
  }

  [Fact]
  public void ParseItems_IdentityEmptyFields_NullFullNameAndAddress()
  {
    var json = """[{"id":"e","type":4,"name":"Empty","revisionDate":"2024-01-01T00:00:00Z","identity":{}}]""";
    var items = BitwardenCliService.ParseItems(json);
    Assert.Null(items[0].IdentityFullName);
    Assert.Null(items[0].IdentityAddress);
  }

  [Fact]
  public void ParseItems_LoginWithNullPasswordRevisionDate()
  {
    var json = """
    [
      {
        "id": "np-1",
        "type": 1,
        "name": "No PW Rev",
        "revisionDate": "2024-01-01T00:00:00Z",
        "login": { "username": "u", "password": "p", "uris": [] }
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.Null(items[0].PasswordRevisionDate);
  }

  [Fact]
  public void ParseItems_LoginWithEmptyUriString_Skipped()
  {
    var json = """
    [
      {
        "id": "eu-1",
        "type": 1,
        "name": "Empty URI",
        "revisionDate": "2024-01-01T00:00:00Z",
        "login": { "uris": [{ "uri": "", "match": null }, { "uri": "https://valid.com", "match": null }] }
      }
    ]
    """;

    var items = BitwardenCliService.ParseItems(json);
    Assert.Single(items[0].Uris);
    Assert.Equal("https://valid.com", items[0].Uris[0].Uri);
  }

  [Fact]
  public void ParseItems_NullNodeInArray_Skipped()
  {
    var arr = new JsonArray(null, JsonNode.Parse("""{"id":"ok","type":2,"name":"Note","revisionDate":"2024-01-01T00:00:00Z"}"""));
    var items = BitwardenCliService.ParseItems(arr.ToJsonString());
    Assert.Single(items);
  }

  // --- ParseCustomFields ---

  [Fact]
  public void ParseCustomFields_Null_ReturnsEmptyDictionary()
  {
    var result = BitwardenCliService.ParseCustomFields(null);
    Assert.Empty(result);
  }

  [Fact]
  public void ParseCustomFields_EmptyArray_ReturnsEmpty()
  {
    var result = BitwardenCliService.ParseCustomFields(JsonNode.Parse("[]"));
    Assert.Empty(result);
  }

  [Fact]
  public void ParseCustomFields_MixedFields()
  {
    var json = JsonNode.Parse("""
    [
      {"name":"Visible","value":"v1","type":0},
      {"name":"Hidden","value":"v2","type":1}
    ]
    """);
    var result = BitwardenCliService.ParseCustomFields(json);
    Assert.Equal(2, result.Count);
    Assert.False(result["Visible"].IsHidden);
    Assert.True(result["Hidden"].IsHidden);
    Assert.Equal("v1", result["Visible"].Value);
  }

  [Fact]
  public void ParseCustomFields_DuplicateName_FirstWins()
  {
    var json = JsonNode.Parse("""
    [
      {"name":"Key","value":"first","type":0},
      {"name":"Key","value":"second","type":0}
    ]
    """);
    var result = BitwardenCliService.ParseCustomFields(json);
    Assert.Single(result);
    Assert.Equal("first", result["Key"].Value);
  }

  [Fact]
  public void ParseCustomFields_NullNameOrValue_Skipped()
  {
    var json = JsonNode.Parse("""
    [
      {"name":null,"value":"v","type":0},
      {"name":"","value":"v","type":0},
      {"name":"Good","value":null,"type":0},
      {"name":"Valid","value":"ok","type":0}
    ]
    """);
    var result = BitwardenCliService.ParseCustomFields(json);
    Assert.Single(result);
    Assert.Equal("ok", result["Valid"].Value);
  }

  [Fact]
  public void ParseCustomFields_CaseInsensitiveLookup()
  {
    var json = JsonNode.Parse("""
    [{"name":"MyKey","value":"val","type":0}]
    """);
    var result = BitwardenCliService.ParseCustomFields(json);
    Assert.True(result.ContainsKey("mykey"));
    Assert.True(result.ContainsKey("MYKEY"));
  }

  // --- GetFolderName ---

  [Fact]
  public void GetFolderName_ExistingFolder_ReturnsName()
  {
    var svc = new BitwardenCliService();
    svc.LoadTestData([], new Dictionary<string, string> { ["f1"] = "Work" });
    Assert.Equal("Work", svc.GetFolderName("f1"));
  }

  [Fact]
  public void GetFolderName_MissingFolder_ReturnsNull()
  {
    var svc = new BitwardenCliService();
    svc.LoadTestData([], []);
    Assert.Null(svc.GetFolderName("nonexistent"));
  }

  [Fact]
  public void GetFolderName_NullId_ReturnsNull()
  {
    var svc = new BitwardenCliService();
    Assert.Null(svc.GetFolderName(null));
  }

  // --- ApplyFilter ---

  private static BitwardenCliService CreateServiceWithFolders(Dictionary<string, string>? folders = null)
  {
    var svc = new BitwardenCliService();
    svc.LoadTestData([], folders ?? []);
    return svc;
  }

  private static List<BitwardenItem> TestItems =>
  [
    new()
    {
      Id = "login-1", Name = "GitHub", Type = BitwardenItemType.Login,
      Username = "octocat", Password = "strongpass123!", HasTotp = true, TotpSecret = "JBSWY3DPEHPK3PXP",
      HasPasskey = true, Favorite = true, FolderId = "f-work", Notes = "dev account",
      OrganizationId = "org-1", RevisionDate = DateTime.UtcNow,
      Uris = [new ItemUri("https://github.com", UriMatchType.Default)],
    },
    new()
    {
      Id = "login-2", Name = "Example HTTP", Type = BitwardenItemType.Login,
      Password = "short", FolderId = "f-personal",
      RevisionDate = DateTime.UtcNow - TimeSpan.FromDays(400),
      PasswordRevisionDate = DateTime.UtcNow - TimeSpan.FromDays(400),
      Uris = [new ItemUri("http://example.com", UriMatchType.Default)],
    },
    new()
    {
      Id = "card-1", Name = "My Visa", Type = BitwardenItemType.Card,
      CardBrand = "Visa", CardNumber = "4111", RevisionDate = DateTime.UtcNow,
    },
    new()
    {
      Id = "note-1", Name = "Secret", Type = BitwardenItemType.SecureNote,
      Notes = "confidential", RevisionDate = DateTime.UtcNow,
    },
    new()
    {
      Id = "identity-1", Name = "Me", Type = BitwardenItemType.Identity,
      IdentityEmail = "me@test.com", RevisionDate = DateTime.UtcNow,
    },
    new()
    {
      Id = "ssh-1", Name = "My Key", Type = BitwardenItemType.SshKey,
      RevisionDate = DateTime.UtcNow,
    },
    new()
    {
      Id = "login-nopw", Name = "No Password", Type = BitwardenItemType.Login,
      Uris = [], RevisionDate = DateTime.UtcNow,
    },
    new()
    {
      Id = "note-reprompt", Name = "Protected Note", Type = BitwardenItemType.SecureNote,
      Reprompt = 1, RevisionDate = DateTime.UtcNow,
    },
    new()
    {
      Id = "login-reprompt", Name = "Protected Login", Type = BitwardenItemType.Login,
      Password = "Str0ngPassw0rd!", Reprompt = 1, Uris = [], RevisionDate = DateTime.UtcNow,
    },
  ];

  [Fact]
  public void ApplyFilter_Folder_MatchesByName()
  {
    var svc = CreateServiceWithFolders(new() { ["f-work"] = "Work", ["f-personal"] = "Personal" });
    var result = svc.ApplyFilter(TestItems, ("folder", "Work")).ToList();
    Assert.Single(result);
    Assert.Equal("login-1", result[0].Id);
  }

  [Fact]
  public void ApplyFilter_Folder_CaseInsensitive()
  {
    var svc = CreateServiceWithFolders(new() { ["f-work"] = "Work" });
    var result = svc.ApplyFilter(TestItems, ("folder", "work")).ToList();
    Assert.Single(result);
  }

  [Fact]
  public void ApplyFilter_Folder_NoMatch_ReturnsEmpty()
  {
    var svc = CreateServiceWithFolders(new() { ["f-work"] = "Work" });
    var result = svc.ApplyFilter(TestItems, ("folder", "Archive")).ToList();
    Assert.Empty(result);
  }

  [Fact]
  public void ApplyFilter_Url_MatchesLoginUris()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("url", "github")).ToList();
    Assert.Single(result);
    Assert.Equal("login-1", result[0].Id);
  }

  [Fact]
  public void ApplyFilter_Host_MatchesLoginUris()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("host", "example.com")).ToList();
    Assert.Single(result);
    Assert.Equal("login-2", result[0].Id);
  }

  [Fact]
  public void ApplyFilter_Url_NonLoginItems_Excluded()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("url", "visa")).ToList();
    Assert.Empty(result);
  }

  [Fact]
  public void ApplyFilter_Type_ByName()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("type", "Card")).ToList();
    Assert.Single(result);
    Assert.Equal("card-1", result[0].Id);
  }

  [Fact]
  public void ApplyFilter_Type_ByNumber()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("type", "3")).ToList();
    Assert.Single(result);
    Assert.Equal(BitwardenItemType.Card, result[0].Type);
  }

  [Fact]
  public void ApplyFilter_Type_CaseInsensitive()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("type", "login")).ToList();
    Assert.Equal(4, result.Count);
  }

  [Fact]
  public void ApplyFilter_Org_MatchesOrganizationId()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("org", "org-1")).ToList();
    Assert.Single(result);
    Assert.Equal("login-1", result[0].Id);
  }

  [Fact]
  public void ApplyFilter_Org_NullOrg_Excluded()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("org", "nonexistent")).ToList();
    Assert.Empty(result);
  }

  [Fact]
  public void ApplyFilter_Has_Totp()
  {
    var svc = CreateServiceWithFolders();
    foreach (var alias in new[] { "totp", "otp", "2fa", "mfa" })
    {
      var result = svc.ApplyFilter(TestItems, ("has", alias)).ToList();
      Assert.Single(result);
      Assert.Equal("login-1", result[0].Id);
    }
  }

  [Fact]
  public void ApplyFilter_Has_Passkey()
  {
    var svc = CreateServiceWithFolders();
    foreach (var alias in new[] { "passkey", "fido2", "webauthn", "passwordless" })
    {
      var result = svc.ApplyFilter(TestItems, ("has", alias)).ToList();
      Assert.Single(result);
      Assert.Equal("login-1", result[0].Id);
    }
  }

  [Fact]
  public void ApplyFilter_Has_Password()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("has", "password")).ToList();
    Assert.Equal(3, result.Count);
  }

  [Fact]
  public void ApplyFilter_Has_Url()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("has", "url")).ToList();
    Assert.Equal(2, result.Count);
  }

  [Fact]
  public void ApplyFilter_Has_Notes()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("has", "notes")).ToList();
    Assert.Equal(2, result.Count);
  }

  [Fact]
  public void ApplyFilter_Has_Folder()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("has", "folder")).ToList();
    Assert.Equal(2, result.Count);
  }

  [Fact]
  public void ApplyFilter_Has_Attachment_Passthrough()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("has", "attachment")).ToList();
    Assert.Equal(TestItems.Count, result.Count);
  }

  [Fact]
  public void ApplyFilter_Has_Unknown_Passthrough()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("has", "nonexistent")).ToList();
    Assert.Equal(TestItems.Count, result.Count);
  }

  [Fact]
  public void ApplyFilter_Is_Favorite()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "favorite")).ToList();
    Assert.Single(result);
    Assert.Equal("login-1", result[0].Id);
  }

  [Fact]
  public void ApplyFilter_Is_Fav_Alias()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "fav")).ToList();
    Assert.Single(result);
  }

  [Fact]
  public void ApplyFilter_Is_Weak()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "weak")).ToList();
    Assert.Single(result);
    Assert.Equal("login-2", result[0].Id);
  }

  [Fact]
  public void ApplyFilter_Is_Old()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "old")).ToList();
    Assert.Single(result);
    Assert.Equal("login-2", result[0].Id);
  }

  [Fact]
  public void ApplyFilter_Is_Stale_Alias()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "stale")).ToList();
    Assert.Single(result);
  }

  [Fact]
  public void ApplyFilter_Is_Insecure()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "insecure")).ToList();
    Assert.Single(result);
    Assert.Equal("login-2", result[0].Id);
  }

  [Fact]
  public void ApplyFilter_Is_Http_Alias()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "http")).ToList();
    Assert.Single(result);
  }

  [Fact]
  public void ApplyFilter_Is_Watchtower_CombinesAllFlags()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "watchtower")).ToList();
    Assert.Single(result);
    Assert.Equal("login-2", result[0].Id);
  }

  [Fact]
  public void ApplyFilter_Is_Flagged_Alias()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "flagged")).ToList();
    Assert.Single(result);
  }

  [Fact]
  public void ApplyFilter_Is_Unknown_Passthrough()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "nonexistent")).ToList();
    Assert.Equal(TestItems.Count, result.Count);
  }

  [Fact]
  public void ApplyFilter_UnknownKey_Passthrough()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("unknown", "value")).ToList();
    Assert.Equal(TestItems.Count, result.Count);
  }

  [Fact]
  public void ApplyFilter_Is_Weak_OnlyLoginsWithPasswords()
  {
    var svc = CreateServiceWithFolders();
    var items = new List<BitwardenItem>
    {
      new() { Id = "card", Name = "Card", Type = BitwardenItemType.Card, Password = "abc" },
      new() { Id = "login", Name = "Login", Type = BitwardenItemType.Login, Password = "abc", Uris = [] },
    };
    var result = svc.ApplyFilter(items, ("is", "weak")).ToList();
    Assert.Single(result);
    Assert.Equal("login", result[0].Id);
  }

  [Fact]
  public void ApplyFilter_Has_Pw_Alias()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("has", "pw")).ToList();
    Assert.Equal(3, result.Count);
  }

  [Fact]
  public void ApplyFilter_Has_Note_Alias()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("has", "note")).ToList();
    Assert.Equal(2, result.Count);
  }

  [Fact]
  public void ApplyFilter_Has_Attachments_Alias()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("has", "attachments")).ToList();
    Assert.Equal(TestItems.Count, result.Count);
  }

  [Fact]
  public void ApplyFilter_Has_Uri_Alias()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("has", "uri")).ToList();
    Assert.Equal(2, result.Count);
  }

  // --- SetSession / ClearSession / IsUnlocked / IsCacheLoaded ---

  [Fact]
  public void IsUnlocked_Default_IsFalse()
  {
    var svc = new BitwardenCliService();
    Assert.False(svc.IsUnlocked);
  }

  [Fact]
  public void SetSession_SetsIsUnlocked()
  {
    var svc = new BitwardenCliService();
    svc.SetSession("test-key");
    Assert.True(svc.IsUnlocked);
  }

  // --- is:protected / is:locked / is:reprompt ---

  [Fact]
  public void ApplyFilter_Is_Protected_ReturnsRepromptItems()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "protected")).ToList();
    Assert.Equal(2, result.Count);
    Assert.Contains(result, i => i.Id == "note-reprompt");
    Assert.Contains(result, i => i.Id == "login-reprompt");
  }

  [Fact]
  public void ApplyFilter_Is_Locked_Alias()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "locked")).ToList();
    Assert.Equal(2, result.Count);
  }

  [Fact]
  public void ApplyFilter_Is_Reprompt_Alias()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "reprompt")).ToList();
    Assert.Equal(2, result.Count);
  }

  [Fact]
  public void ApplyFilter_Is_Protected_ExcludesNonRepromptItems()
  {
    var svc = CreateServiceWithFolders();
    var result = svc.ApplyFilter(TestItems, ("is", "protected")).ToList();
    Assert.DoesNotContain(result, i => i.Id == "note-1");
    Assert.DoesNotContain(result, i => i.Id == "login-1");
  }

  [Fact]
  public void ClearSession_ResetsIsUnlockedAndCache()
  {
    var svc = new BitwardenCliService();
    svc.SetSession("key");
    svc.LoadTestData([new() { Id = "x", Name = "X", Type = BitwardenItemType.Login, Uris = [], RevisionDate = DateTime.UtcNow }], []);
    Assert.True(svc.IsUnlocked);
    Assert.True(svc.IsCacheLoaded);
    svc.ClearSession();
    Assert.False(svc.IsUnlocked);
    Assert.False(svc.IsCacheLoaded);
  }

  [Fact]
  public void LastStatus_Initial_IsNull()
  {
    var svc = new BitwardenCliService();
    Assert.Null(svc.LastStatus);
  }

  // --- WarmCacheAsync / WarmupTask ---

  [Fact]
  public void WarmupTask_Initial_IsCompleted()
  {
    var svc = new BitwardenCliService();
    Assert.True(svc.WarmupTask.IsCompleted);
  }

  [Fact]
  public void WarmCacheAsync_ReturnsSameTaskAsWarmupTask()
  {
    var svc = new BitwardenCliService();
    var returned = svc.WarmCacheAsync();
    Assert.Same(returned, svc.WarmupTask);
  }

  [Fact]
  public void WarmCacheAsync_CalledTwice_WarmupTaskReflectsLatestCall()
  {
    var svc = new BitwardenCliService();
    var first = svc.WarmCacheAsync();
    var second = svc.WarmCacheAsync();
    Assert.Same(second, svc.WarmupTask);
    Assert.NotSame(first, second);
  }

  // --- SearchCached ---

  [Fact]
  public void SearchCached_EmptyCache_ReturnsEmpty()
  {
    var svc = new BitwardenCliService();
    svc.LoadTestData([], []);
    Assert.Empty(svc.SearchCached());
  }

  [Fact]
  public void SearchCached_NullQuery_ReturnsAll()
  {
    var svc = new BitwardenCliService();
    svc.LoadTestData(
      [new() { Id = "a", Name = "Alpha", Type = BitwardenItemType.Login, Uris = [], RevisionDate = DateTime.UtcNow }],
      []);
    Assert.Single(svc.SearchCached(null));
  }

  [Fact]
  public void SearchCached_EmptyQuery_FavoritesFirst()
  {
    var svc = new BitwardenCliService();
    var now = DateTime.UtcNow;
    svc.LoadTestData([
      new() { Id = "normal", Name = "Normal", Type = BitwardenItemType.Login, Uris = [], Favorite = false, RevisionDate = now },
      new() { Id = "fav", Name = "Favorite", Type = BitwardenItemType.Login, Uris = [], Favorite = true, RevisionDate = now },
    ], []);
    var result = svc.SearchCached();
    Assert.Equal("fav", result[0].Id);
    Assert.Equal("normal", result[1].Id);
  }

  [Fact]
  public void SearchCached_TextQuery_ReturnsMatchingItems()
  {
    var svc = new BitwardenCliService();
    svc.LoadTestData([
      new() { Id = "gh", Name = "GitHub", Type = BitwardenItemType.Login, Uris = [], RevisionDate = DateTime.UtcNow },
      new() { Id = "bb", Name = "BitBucket", Type = BitwardenItemType.Login, Uris = [], RevisionDate = DateTime.UtcNow },
    ], []);
    var result = svc.SearchCached("github");
    Assert.Single(result);
    Assert.Equal("gh", result[0].Id);
  }

  [Fact]
  public void SearchCached_TextQuery_NoMatch_ReturnsEmpty()
  {
    var svc = new BitwardenCliService();
    svc.LoadTestData([
      new() { Id = "gh", Name = "GitHub", Type = BitwardenItemType.Login, Uris = [], RevisionDate = DateTime.UtcNow },
    ], []);
    Assert.Empty(svc.SearchCached("xyz123doesnotexist"));
  }

  [Fact]
  public void SearchCached_FilterAndText_AppliesBoth()
  {
    var svc = new BitwardenCliService();
    svc.LoadTestData([
      new() { Id = "login-1", Name = "GitHub", Type = BitwardenItemType.Login, Uris = [new ItemUri("https://github.com", UriMatchType.Default)], RevisionDate = DateTime.UtcNow },
      new() { Id = "login-2", Name = "GitLab", Type = BitwardenItemType.Login, Uris = [new ItemUri("https://gitlab.com", UriMatchType.Default)], RevisionDate = DateTime.UtcNow },
      new() { Id = "card-1", Name = "GitHub Card", Type = BitwardenItemType.Card, RevisionDate = DateTime.UtcNow },
    ], []);
    // url:github narrows to login-1 and login-2; text "GitHub" then filters to login-1
    var result = svc.SearchCached("url:github GitHub");
    Assert.Single(result);
    Assert.Equal("login-1", result[0].Id);
  }

  [Fact]
  public void SearchCached_TextQuery_SortedByRelevance()
  {
    var svc = new BitwardenCliService();
    var now = DateTime.UtcNow;
    svc.LoadTestData([
      new() { Id = "partial", Name = "MyGitHubAccount", Type = BitwardenItemType.Login, Uris = [], RevisionDate = now },
      new() { Id = "exact", Name = "GitHub", Type = BitwardenItemType.Login, Uris = [], RevisionDate = now },
    ], []);
    var result = svc.SearchCached("GitHub");
    Assert.Equal("exact", result[0].Id);
    Assert.Equal("partial", result[1].Id);
  }

  [Fact]
  public void SearchCached_RecentItemPinnedAboveContextMatches()
  {
    var svc = new BitwardenCliService();
    var now = DateTime.UtcNow;
    svc.LoadTestData([
      new() { Id = "ctx1", Name = "GitHub", Type = BitwardenItemType.Login, Uris = [new ItemUri("https://github.com", UriMatchType.Default)], RevisionDate = now },
      new() { Id = "ctx2", Name = "GitLab", Type = BitwardenItemType.Login, Uris = [new ItemUri("https://gitlab.com", UriMatchType.Default)], RevisionDate = now },
      new() { Id = "recent", Name = "Amazon", Type = BitwardenItemType.Login, Uris = [], RevisionDate = now },
    ], []);

    AccessTracker.Record("recent");
    try
    {
      var context = new ForegroundContext
      {
        Windows =
        [
          new WindowContext { ProcessName = "chrome", WindowTitle = "GitHub", IsBrowser = true, BrowserUrl = "https://github.com" },
          new WindowContext { ProcessName = "chrome", WindowTitle = "GitLab", IsBrowser = true, BrowserUrl = "https://gitlab.com" },
        ]
      };

      var result = svc.SearchCached(null, context, 3);

      Assert.Equal("recent", result[0].Id);
      var contextIds = result.Skip(1).Take(2).Select(i => i.Id).ToHashSet();
      Assert.Contains("ctx1", contextIds);
      Assert.Contains("ctx2", contextIds);
    }
    finally
    {
      // Clear static state so other tests are not affected
      AccessTracker.Record("__clear__");
    }
  }
}
