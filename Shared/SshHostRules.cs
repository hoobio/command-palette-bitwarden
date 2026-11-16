using System.Text.RegularExpressions;

namespace HoobiBitwarden.Ipc;

internal static partial class SshHostRules
{
  public static bool IsValid(string? host) =>
      !string.IsNullOrEmpty(host) && SshHostPattern().IsMatch(host);

  [GeneratedRegex(@"^(?!-)[\w.+\\-]+@[\w.-]+$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
  private static partial Regex SshHostPattern();
}
