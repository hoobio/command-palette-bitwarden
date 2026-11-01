using System;
using System.Linq;
using System.Reflection;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed record AppInfo(string Version, string? Commit, string Channel, bool IsStore)
{
  private const int ShortCommitLength = 7;

  internal static AppInfo Current { get; } = ReadCurrent();

  internal static string ChannelOf(string packageName) =>
    packageName.EndsWith(".Dev", StringComparison.OrdinalIgnoreCase) ? AppChannels.Dev
    : packageName.EndsWith(".Prerelease", StringComparison.OrdinalIgnoreCase) ? AppChannels.Prerelease
    : AppChannels.Release;

  internal static string? ShortCommit(string? informationalVersion)
  {
    var plus = informationalVersion?.IndexOf('+') ?? -1;
    if (plus < 0 || plus == informationalVersion!.Length - 1)
      return null;
    var commit = informationalVersion[(plus + 1)..];
    return commit[..Math.Min(ShortCommitLength, commit.Length)];
  }

  internal static AppInfo From(string version, string packageName, bool isStore, string? informationalVersion) =>
    new(version, ShortCommit(informationalVersion), ChannelOf(packageName), isStore);

  private static AppInfo ReadCurrent()
  {
    var informational = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    try
    {
      var (version, name, isStore) = PackageFacts.Read();
      return From(version, name, isStore, informational);
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Updates", $"Package identity unavailable: {ex.GetType().Name}");
      var plus = informational?.IndexOf('+') ?? -1;
      var version = plus >= 0 ? informational![..plus] : informational;
      return From(string.IsNullOrWhiteSpace(version) ? "0.0.0" : version, ".Dev", false, informational);
    }
  }
}
