using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed class GitHubRelease
{
  [JsonPropertyName("tag_name")]
  public string? TagName { get; set; }

  [JsonPropertyName("name")]
  public string? Name { get; set; }

  [JsonPropertyName("html_url")]
  public string? HtmlUrl { get; set; }

  [JsonPropertyName("prerelease")]
  public bool Prerelease { get; set; }

  [JsonPropertyName("draft")]
  public bool Draft { get; set; }
}

[JsonSerializable(typeof(GitHubRelease[]))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct SemVer(int Major, int Minor, int Patch, int Pre)
{
  private const int StablePre = int.MaxValue;

  public static bool TryParse(string? tag, out SemVer value)
  {
    value = default;
    if (string.IsNullOrWhiteSpace(tag))
      return false;

    var text = tag.Trim();
    if (text[0] is 'v' or 'V')
      text = text[1..];

    var dash = text.IndexOf('-');
    var core = dash >= 0 ? text[..dash] : text;
    var pre = dash >= 0 ? text[(dash + 1)..] : null;

    var parts = core.Split('.');
    if (parts.Length < 3
        || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
        || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
        || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
      return false;

    var preNumber = StablePre;
    if (pre is not null)
    {
      var trailing = pre[(pre.LastIndexOf('.') + 1)..];
      preNumber = int.TryParse(trailing, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    value = new SemVer(major, minor, patch, preNumber);
    return true;
  }

  public int Compare(SemVer other)
  {
    var result = Major.CompareTo(other.Major);
    if (result != 0)
      return result;
    result = Minor.CompareTo(other.Minor);
    if (result != 0)
      return result;
    result = Patch.CompareTo(other.Patch);
    return result != 0 ? result : Pre.CompareTo(other.Pre);
  }
}

internal sealed class GitHubReleaseClient(HttpClient http)
{
  internal const string RepoUrl = "https://github.com/hoobio/command-palette-bitwarden";
  internal const string ReleasesApiUrl = "https://api.github.com/repos/hoobio/command-palette-bitwarden/releases?per_page=30";

  internal static HttpClient CreateHttpClient(string version)
  {
    var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd($"HoobiBitwardenCommandPalette/{version}");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    return client;
  }

  internal static (GitHubRelease Release, SemVer Version)? SelectLatest(IEnumerable<GitHubRelease> releases, bool includePrereleases)
  {
    (GitHubRelease Release, SemVer Version)? best = null;
    foreach (var release in releases)
    {
      if (release.Draft || (release.Prerelease && !includePrereleases))
        continue;
      if (SemVer.TryParse(release.TagName, out var version) && (best is null || version.Compare(best.Value.Version) > 0))
        best = (release, version);
    }

    return best;
  }

  internal async Task<UpdateResponse> CheckAsync(AppInfo app, CancellationToken cancellationToken)
  {
    using var response = await http.GetAsync(ReleasesApiUrl, cancellationToken);
    response.EnsureSuccessStatusCode();
    var releases = await JsonSerializer.DeserializeAsync(await response.Content.ReadAsStreamAsync(cancellationToken), GitHubJsonContext.Default.GitHubReleaseArray, cancellationToken);

    if (!SemVer.TryParse(app.Version, out var current))
    {
      DebugLogService.Log("Updates", $"Could not parse the current version '{app.Version}'");
      return new UpdateResponse(UpdateStates.Failed, null, null, false);
    }

    if (SelectLatest(releases ?? [], app.Channel == AppChannels.Prerelease) is not { } latest)
    {
      DebugLogService.Log("Updates", "No comparable releases found");
      return new UpdateResponse(UpdateStates.Failed, null, null, false);
    }

    var newer = latest.Version.Compare(current) > 0;
    DebugLogService.Log("Updates", $"Check ({app.Channel}): current {app.Version}, latest {latest.Release.TagName} -> {(newer ? "update available" : "up to date")}");
    var name = latest.Release.Name is { Length: > 0 } title ? title : latest.Release.TagName;
    var url = latest.Release.HtmlUrl is { } html && html.StartsWith($"{RepoUrl}/", StringComparison.Ordinal) ? html : $"{RepoUrl}/releases/latest";
    return new UpdateResponse(newer ? UpdateStates.Available : UpdateStates.UpToDate, name, url, false);
  }
}
