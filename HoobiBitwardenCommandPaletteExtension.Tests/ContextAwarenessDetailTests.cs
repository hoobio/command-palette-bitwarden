using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class ContextAwarenessDetailTests
{
  // --- ExtractHost ---

  [Theory]
  [InlineData("https://www.example.com/path", "example.com")]
  [InlineData("https://sub.example.com", "sub.example.com")]
  [InlineData("http://example.com", "example.com")]
  [InlineData("example.com", "example.com")]
  [InlineData("https://www.github.com/user/repo", "github.com")]
  public void ExtractHost_ParsesCorrectly(string url, string expected)
  {
    Assert.Equal(expected, ContextAwarenessService.ExtractHost(url));
  }

  [Theory]
  [InlineData("")]
  [InlineData("not-a-url-at-all:::")]
  public void ExtractHost_ReturnsNull_ForInvalidUrls(string url)
  {
    Assert.Null(ContextAwarenessService.ExtractHost(url));
  }

  // --- HostsMatch ---

  [Theory]
  [InlineData("example.com", "example.com", true)]
  [InlineData("sub.example.com", "example.com", true)]
  [InlineData("example.com", "sub.example.com", true)]
  [InlineData("mail.google.com", "google.com", true)]
  [InlineData("example.com", "other.com", false)]
  [InlineData("notexample.com", "example.com", false)]
  public void HostsMatch_SubdomainLogic(string a, string b, bool expected)
  {
    Assert.Equal(expected, ContextAwarenessService.HostsMatch(a, b));
  }

  // --- NormalizeUrl ---

  [Theory]
  [InlineData("https://example.com/", "https://example.com")]
  [InlineData("example.com", "https://example.com")]
  [InlineData("https://example.com", "https://example.com")]
  [InlineData("http://test.com/path/", "http://test.com/path")]
  public void NormalizeUrl_AddsSchemeAndTrimsSlash(string input, string expected)
  {
    Assert.Equal(expected, ContextAwarenessService.NormalizeUrl(input));
  }

  // --- StripBrowserSuffix ---

  [Theory]
  [InlineData("GitHub - Google Chrome", "chrome", "GitHub")]
  [InlineData("Reddit — Mozilla Firefox", "firefox", "Reddit")]
  [InlineData("YouTube - Microsoft Edge", "msedge", "YouTube")]
  [InlineData("My App", "notepad", "My App")]
  [InlineData(null, "chrome", null)]
  public void StripBrowserSuffix_RemovesBrowserName(string? title, string? process, string? expected)
  {
    Assert.Equal(expected, ContextAwarenessService.StripBrowserSuffix(title, process));
  }

  // --- NamesSimilar ---

  [Theory]
  [InlineData("discord", "Discord", true)]
  [InlineData("steamwebhelper", "Steam", true)]
  [InlineData("slack", "Slack", true)]
  [InlineData("chrome", "Firefox", false)]
  [InlineData("notepad", "Excel", false)]
  public void NamesSimilar_PrefixOrExactMatch(string a, string b, bool expected)
  {
    Assert.Equal(expected, ContextAwarenessService.NamesSimilar(a, b));
  }

  // --- ContainsWholeWord ---

  [Theory]
  [InlineData("Welcome to Discord", "Discord", true)]
  [InlineData("Discord - Chat", "Discord", true)]
  [InlineData("MyDiscordApp", "Discord", false)]
  [InlineData("Test Discord", "Discord", true)]
  [InlineData("discord", "Discord", true)]
  [InlineData("nothing here", "Discord", false)]
  public void ContainsWholeWord_FindsWordBoundaries(string text, string word, bool expected)
  {
    Assert.Equal(expected, ContextAwarenessService.ContainsWholeWord(text, word));
  }

  // --- ExtractUrlFromTitle ---

  [Theory]
  [InlineData("GitHub - github.com", "github.com")]
  [InlineData("example.com — Google Chrome", "example.com")]
  [InlineData("My Document | Word", null)]
  [InlineData(null, null)]
  [InlineData("", null)]
  public void ExtractUrlFromTitle_ExtractsUrlLikeParts(string? title, string? expected)
  {
    Assert.Equal(expected, ContextAwarenessService.ExtractUrlFromTitle(title));
  }

  // --- LooksLikeUrl ---

  [Theory]
  [InlineData("https://example.com", true)]
  [InlineData("example.com", true)]
  [InlineData("not a url", false)]
  [InlineData("", false)]
  [InlineData(null, false)]
  public void LooksLikeUrl_DetectsUrls(string? value, bool expected)
  {
    Assert.Equal(expected, ContextAwarenessService.LooksLikeUrl(value!));
  }

  // --- UriMatchesBrowserContext ---

  [Fact]
  public void UriMatch_Exact_MatchesNormalizedUrl()
  {
    var entry = new ItemUri("https://example.com", UriMatchType.Exact);
    Assert.True(ContextAwarenessService.UriMatchesBrowserContext(entry, "https://example.com/", "example.com"));
    Assert.False(ContextAwarenessService.UriMatchesBrowserContext(entry, "https://other.com", "other.com"));
  }

  [Fact]
  public void UriMatch_StartsWith_MatchesPrefix()
  {
    var entry = new ItemUri("https://example.com/path", UriMatchType.StartsWith);
    Assert.True(ContextAwarenessService.UriMatchesBrowserContext(entry, "https://example.com/path/sub", "example.com"));
    Assert.False(ContextAwarenessService.UriMatchesBrowserContext(entry, "https://other.com/path", "other.com"));
  }

  [Fact]
  public void UriMatch_Host_MatchesExactHost()
  {
    var entry = new ItemUri("https://example.com/login", UriMatchType.Host);
    Assert.True(ContextAwarenessService.UriMatchesBrowserContext(entry, "https://example.com/other", "example.com"));
    Assert.False(ContextAwarenessService.UriMatchesBrowserContext(entry, "https://sub.example.com", "sub.example.com"));
  }

  [Fact]
  public void UriMatch_Domain_MatchesSubdomains()
  {
    var entry = new ItemUri("https://example.com", UriMatchType.Domain);
    Assert.True(ContextAwarenessService.UriMatchesBrowserContext(entry, "https://sub.example.com", "sub.example.com"));
    Assert.False(ContextAwarenessService.UriMatchesBrowserContext(entry, "https://other.com", "other.com"));
  }

  [Fact]
  public void UriMatch_Default_BehavesLikeDomain()
  {
    var entry = new ItemUri("https://example.com", UriMatchType.Default);
    Assert.True(ContextAwarenessService.UriMatchesBrowserContext(entry, "https://sub.example.com", "sub.example.com"));
  }

  [Fact]
  public void UriMatch_RegularExpression_MatchesPattern()
  {
    var entry = new ItemUri(@"^https://.*\.example\.com", UriMatchType.RegularExpression);
    Assert.True(ContextAwarenessService.UriMatchesBrowserContext(entry, "https://sub.example.com", "sub.example.com"));
    Assert.False(ContextAwarenessService.UriMatchesBrowserContext(entry, "http://sub.example.com", "sub.example.com"));
  }

  [Fact]
  public void UriMatch_Never_NeverMatches()
  {
    var item = new BitwardenItem
    {
      Id = "1",
      Name = "XY",
      Type = BitwardenItemType.Login,
      Uris = [new ItemUri("https://example.com", UriMatchType.Never)]
    };

    var context = new ForegroundContext
    {
      Windows = [new WindowContext { ProcessName = "chrome", WindowTitle = "Something - Google Chrome", BrowserUrl = "https://example.com", IsBrowser = true }]
    };

    Assert.Equal(0, ContextAwarenessService.ContextScore(context, item));
  }

  // --- ProcessNameMatchesItem ---

  [Fact]
  public void ProcessNameMatchesItem_MatchesByProcessName()
  {
    var item = new BitwardenItem { Id = "1", Name = "Discord", Type = BitwardenItemType.Login };
    Assert.True(ContextAwarenessService.ProcessNameMatchesItem("discord", "Discord App", item));
  }

  [Fact]
  public void ProcessNameMatchesItem_MatchesCompoundProcessName()
  {
    var item = new BitwardenItem { Id = "1", Name = "Steam", Type = BitwardenItemType.Login };
    Assert.True(ContextAwarenessService.ProcessNameMatchesItem("steamwebhelper", "Steam", item));
  }

  [Fact]
  public void ProcessNameMatchesItem_MatchesByWindowTitle()
  {
    var item = new BitwardenItem { Id = "1", Name = "Slack", Type = BitwardenItemType.Login };
    Assert.True(ContextAwarenessService.ProcessNameMatchesItem("unknown", "Welcome to Slack", item));
  }

  [Fact]
  public void ProcessNameMatchesItem_MatchesByDomainBase()
  {
    var item = new BitwardenItem
    {
      Id = "1",
      Name = "My Discord",
      Type = BitwardenItemType.Login,
      Uris = [new ItemUri("https://discord.com/channels", UriMatchType.Default)]
    };
    Assert.True(ContextAwarenessService.ProcessNameMatchesItem("discord", "Something else entirely", item));
  }

  [Fact]
  public void ProcessNameMatchesItem_ReturnsFlase_WhenNoMatch()
  {
    var item = new BitwardenItem { Id = "1", Name = "GitHub", Type = BitwardenItemType.Login };
    Assert.False(ContextAwarenessService.ProcessNameMatchesItem("notepad", "Untitled", item));
  }

  [Fact]
  public void ProcessNameMatchesItem_ReturnsFalse_WhenBothNull()
  {
    var item = new BitwardenItem { Id = "1", Name = "Test", Type = BitwardenItemType.Login };
    Assert.False(ContextAwarenessService.ProcessNameMatchesItem(null, null, item));
  }

  [Fact]
  public void ProcessNameMatchesItem_SkipsNeverUri()
  {
    var item = new BitwardenItem
    {
      Id = "1",
      Name = "Test",
      Type = BitwardenItemType.Login,
      Uris = [new ItemUri("https://discord.com", UriMatchType.Never)]
    };
    Assert.False(ContextAwarenessService.ProcessNameMatchesItem("discord", "Something", item));
  }

  // --- BrowserContext matching via ContextScore ---

  [Fact]
  public void ContextScore_BrowserUrlMatch_Domain()
  {
    var context = new ForegroundContext
    {
      Windows = [new WindowContext
      {
        ProcessName = "chrome",
        WindowTitle = "GitHub - Google Chrome",
        BrowserUrl = "https://github.com/user/repo",
        IsBrowser = true,
      }]
    };
    var item = new BitwardenItem
    {
      Id = "1",
      Name = "GitHub",
      Type = BitwardenItemType.Login,
      Uris = [new ItemUri("https://github.com", UriMatchType.Domain)]
    };

    Assert.True(ContextAwarenessService.ContextScore(context, item) > 0);
  }

  [Fact]
  public void ContextScore_BrowserTitleMatch_FallbackWhenNoUriMatch()
  {
    var context = new ForegroundContext
    {
      Windows = [new WindowContext
      {
        ProcessName = "chrome",
        WindowTitle = "Bitwarden Web Vault - Google Chrome",
        BrowserUrl = "https://vault.bitwarden.com",
        IsBrowser = true,
      }]
    };
    var item = new BitwardenItem
    {
      Id = "1",
      Name = "Bitwarden",
      Type = BitwardenItemType.Login,
      Uris = [new ItemUri("https://other.bitwarden.com", UriMatchType.Host)]
    };

    Assert.True(ContextAwarenessService.ContextScore(context, item) > 0);
  }

  [Fact]
  public void ContextScore_NonLogin_NoBrowserMatch()
  {
    var context = new ForegroundContext
    {
      Windows = [new WindowContext
      {
        ProcessName = "chrome",
        WindowTitle = "Test - Google Chrome",
        BrowserUrl = "https://example.com",
        IsBrowser = true,
      }]
    };
    var item = new BitwardenItem
    {
      Id = "1",
      Name = "example.com",
      Type = BitwardenItemType.SecureNote,
    };

    Assert.Equal(0, ContextAwarenessService.ContextScore(context, item));
  }
}
