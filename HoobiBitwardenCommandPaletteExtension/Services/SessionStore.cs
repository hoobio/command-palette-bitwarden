using Windows.Security.Credentials;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static class SessionStore
{
  private const string Resource = "HoobiBitwarden";
  private const string UserName = "session";

  public static void Save(string sessionKey)
  {
    var vault = new PasswordVault();
    try { vault.Remove(vault.Retrieve(Resource, UserName)); } catch { }
    vault.Add(new PasswordCredential(Resource, UserName, sessionKey));
  }

  public static string? Load()
  {
    try
    {
      var vault = new PasswordVault();
      var cred = vault.Retrieve(Resource, UserName);
      cred.RetrievePassword();
      return cred.Password;
    }
    catch
    {
      return null;
    }
  }

  public static void Clear()
  {
    try
    {
      var vault = new PasswordVault();
      vault.Remove(vault.Retrieve(Resource, UserName));
    }
    catch { }
  }
}
