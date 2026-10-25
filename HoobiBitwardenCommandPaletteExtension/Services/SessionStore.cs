namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static class SessionStore
{
  // MSIX scopes CredWrite/CredRead per package family, so Dev and Release each need their own target name or one sees the other's credential as a stale orphan.
#if DEBUG
  private const string TargetName = "BitwardenCommandPaletteExtension.Debug/default_sessionToken";
#else
  private const string TargetName = "BitwardenCommandPaletteExtension/default_sessionToken";
#endif

  public static void Save(string sessionKey) => CredentialStore.Write(TargetName, "session", sessionKey);

  public static string? Load() => CredentialStore.Read(TargetName)?.Secret;

  public static void Clear() => CredentialStore.Delete(TargetName);
}

internal static class ApiKeyStore
{
#if DEBUG
  private const string TargetName = "BitwardenCommandPaletteExtension.Debug/default_apiKey";
#else
  private const string TargetName = "BitwardenCommandPaletteExtension/default_apiKey";
#endif

  public static void Save(string clientId, string clientSecret) => CredentialStore.Write(TargetName, clientId, clientSecret);

  public static (string ClientId, string ClientSecret)? Load() =>
    CredentialStore.Read(TargetName) is { UserName: { Length: > 0 } id, Secret: var secret } ? (id, secret) : null;

  public static void Clear() => CredentialStore.Delete(TargetName);
}
