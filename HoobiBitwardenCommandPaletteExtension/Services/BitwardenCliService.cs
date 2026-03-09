using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HoobiBitwardenCommandPaletteExtension.Models;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal enum VaultStatus { Unlocked, Locked, Unauthenticated, CliNotFound }

internal sealed class BitwardenCliService
{
  private readonly BitwardenSettingsManager? _settings;
  private string? _sessionKey;

  private List<BitwardenItem> _cache = [];
  private readonly Lock _cacheLock = new();
  private bool _cacheLoaded;
  private DateTime _lastRefresh;
  private int _refreshing;
  private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

  private VaultStatus? _lastStatus;

  public bool IsUnlocked => _sessionKey != null;

  public bool IsCacheLoaded => _cacheLoaded;

  public VaultStatus? LastStatus => _lastStatus;

  internal static string? ServerUrl { get; private set; }

  public event Action? CacheUpdated;
  public event Action? StatusChanged;
  public event Action? WarmupCompleted;

  public BitwardenCliService(BitwardenSettingsManager? settings = null)
  {
    _settings = settings;
  }

  public void SetSession(string sessionKey) => _sessionKey = sessionKey;

  public void ClearSession()
  {
    _sessionKey = null;
    _lastStatus = null;
    lock (_cacheLock)
    {
      _cache = [];
      _cacheLoaded = false;
    }
  }

  public async Task<VaultStatus> GetVaultStatusAsync()
  {
    if (!IsCliAvailable())
      return SetStatus(VaultStatus.CliNotFound);

    if (_sessionKey != null && await VerifySessionAsync())
      return SetStatus(VaultStatus.Unlocked);

    var envSession = Environment.GetEnvironmentVariable("BW_SESSION");
    if (!string.IsNullOrWhiteSpace(envSession))
    {
      _sessionKey = envSession;
      if (await VerifySessionAsync())
        return SetStatus(VaultStatus.Unlocked);
    }

    if (_settings?.RememberSession.Value == true)
    {
      var stored = SessionStore.Load();
      if (!string.IsNullOrEmpty(stored))
      {
        _sessionKey = stored;
        if (await VerifySessionAsync())
          return SetStatus(VaultStatus.Unlocked);
        SessionStore.Clear();
      }
    }

    return SetStatus(await FetchStatusAsync());
  }

  private VaultStatus SetStatus(VaultStatus status)
  {
    _lastStatus = status;
    return status;
  }

  private static bool IsCliAvailable()
  {
    try
    {
      using var process = Process.Start(new ProcessStartInfo("bw", "--version")
      {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
      })!;
      process.WaitForExit(5000);
      return process.ExitCode == 0;
    }
    catch
    {
      return false;
    }
  }

  private async Task<bool> VerifySessionAsync()
  {
    try
    {
      using var process = StartProcess("sync");
      await process.StandardOutput.ReadToEndAsync();
      await process.WaitForExitAsync();
      if (process.ExitCode == 0)
      {
        await FetchServerUrlAsync();
        return true;
      }
    }
    catch { }
    _sessionKey = null;
    return false;
  }

  private async Task<VaultStatus> FetchStatusAsync()
  {
    try
    {
      var output = await RunCliAsync("status");
      var json = JsonNode.Parse(output);
      ServerUrl ??= json?["serverUrl"]?.GetValue<string>()?.TrimEnd('/');
      return json?["status"]?.GetValue<string>() == "unauthenticated"
          ? VaultStatus.Unauthenticated
          : VaultStatus.Locked;
    }
    catch
    {
      return VaultStatus.Locked;
    }
  }

  public async Task<(bool Success, string? Error)> UnlockAsync(string masterPassword)
  {
    try
    {
      var psi = new ProcessStartInfo("bw", "unlock --passwordenv BW_MP --raw")
      {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
      };

      psi.Environment["BW_MP"] = masterPassword;
      if (_sessionKey != null)
        psi.Environment["BW_SESSION"] = _sessionKey;

      using var process = Process.Start(psi)!;
      var stdout = await process.StandardOutput.ReadToEndAsync();
      var stderr = await process.StandardError.ReadToEndAsync();
      await process.WaitForExitAsync();

      var key = stdout.Trim();
      if (process.ExitCode == 0 && !string.IsNullOrEmpty(key))
      {
        _sessionKey = key;
        SetStatus(VaultStatus.Unlocked);
        if (_settings?.RememberSession.Value == true)
          SessionStore.Save(key);
        return (true, null);
      }

      var error = stderr.Trim();

      if (error.Contains("not logged in", StringComparison.OrdinalIgnoreCase))
        ResetToLoggedOut();

      return (false, string.IsNullOrEmpty(error) ? "Unlock failed" : error);
    }
    catch (Exception ex)
    {
      return (false, ex.Message);
    }
  }

  private void ResetToLoggedOut()
  {
    _sessionKey = null;
    SetStatus(VaultStatus.Unauthenticated);
    lock (_cacheLock)
    {
      _cache = [];
      _cacheLoaded = false;
    }
    SessionStore.Clear();
    StatusChanged?.Invoke();
  }

  private async Task FetchServerUrlAsync()
  {
    if (ServerUrl != null)
      return;

    try
    {
      var output = await RunCliAsync("status");
      var url = JsonNode.Parse(output)?["serverUrl"]?.GetValue<string>()?.TrimEnd('/');
      if (!string.IsNullOrWhiteSpace(url))
        ServerUrl = url;
    }
    catch { }
  }

  public async Task<(bool Success, string? Error, bool TwoFactorRequired)> LoginAsync(string email, string password, string? twoFactorCode = null, int? twoFactorMethod = null)
  {
    try
    {
      var sanitizedEmail = email.Replace("\"", "");
      var args = $"login \"{sanitizedEmail}\" --passwordenv BW_MP";
      if (!string.IsNullOrEmpty(twoFactorCode))
      {
        var sanitizedCode = twoFactorCode.Replace("\"", "");
        args += $" --method {twoFactorMethod ?? 0} --code \"{sanitizedCode}\"";
      }
      args += " --raw";

      var psi = new ProcessStartInfo("bw", args)
      {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        CreateNoWindow = true,
      };

      psi.Environment["BW_MP"] = password;

      using var process = Process.Start(psi)!;
      process.StandardInput.Close();

      var stdoutTask = process.StandardOutput.ReadToEndAsync();
      var (stderr, twoFactorDetected) = await ReadStderrWithTwoFactorDetectionAsync(process);

      if (twoFactorDetected)
      {
        try { process.Kill(); } catch { }
        return (false, "Two-factor authentication required - enter your 2FA code", true);
      }

      await process.WaitForExitAsync();

      var key = (await stdoutTask).Trim();
      if (process.ExitCode == 0 && !string.IsNullOrEmpty(key))
      {
        _sessionKey = key;
        SetStatus(VaultStatus.Unlocked);
        if (_settings?.RememberSession.Value == true)
          SessionStore.Save(key);
        return (true, null, false);
      }

      var error = stderr.Trim();
      var needs2fa = error.Contains("Two-step", StringComparison.OrdinalIgnoreCase)
          || error.Contains("two-factor", StringComparison.OrdinalIgnoreCase);

      return (false, string.IsNullOrEmpty(error) ? "Login failed" : error, needs2fa);
    }
    catch (Exception ex)
    {
      return (false, ex.Message, false);
    }
  }

  public async Task LogoutAsync()
  {
    _sessionKey = null;
    SetStatus(VaultStatus.Unauthenticated);
    lock (_cacheLock)
    {
      _cache = [];
      _cacheLoaded = false;
    }
    SessionStore.Clear();
    ServerUrl = null;
    StatusChanged?.Invoke();

    try { await RunCliAsync("logout"); } catch { }
  }

  public async Task LockAsync()
  {
    _sessionKey = null;
    SetStatus(VaultStatus.Locked);
    lock (_cacheLock)
    {
      _cache = [];
      _cacheLoaded = false;
    }
    SessionStore.Clear();
    StatusChanged?.Invoke();

    try { await RunCliAsync("lock"); } catch { }
  }

  public async Task<string?> SetServerUrlAsync(string url)
  {
    using var process = StartProcess($"config server \"{url}\"");
    var stdout = await process.StandardOutput.ReadToEndAsync();
    var stderr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    if (process.ExitCode == 0)
    {
      ServerUrl = url.TrimEnd('/');
      StatusChanged?.Invoke();
      return null;
    }

    var error = stderr.Trim();
    return string.IsNullOrEmpty(error) ? "Failed to set server URL" : error;
  }

  public List<BitwardenItem> SearchCached(string? query = null)
  {
    lock (_cacheLock)
    {
      if (string.IsNullOrWhiteSpace(query))
        return [.. _cache.OrderByDescending(i => AccessTracker.GetLastAccess(i.Id)).ThenByDescending(i => i.RevisionDate)];

      var q = query.Trim();
      return _cache
          .Where(i => Matches(i, q))
          .OrderBy(i => Relevance(i, q))
          .ThenByDescending(i => AccessTracker.GetLastAccess(i.Id))
          .ThenByDescending(i => i.RevisionDate)
          .ToList();
    }
  }

  private static int Relevance(BitwardenItem item, string query)
  {
    if (item.Name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
    if (item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
    if (Regex.IsMatch(item.Name, @"\b" + Regex.Escape(query) + @"\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)) return 2;
    if (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 3;
    return 4;
  }

  public async Task RefreshCacheAsync()
  {
    if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
      return;

    try
    {
      var output = await RunCliAsync("list items");
      var items = ParseItems(output);
      lock (_cacheLock)
      {
        _cache = items;
        _cacheLoaded = true;
        _lastRefresh = DateTime.UtcNow;
      }

      CacheUpdated?.Invoke();
    }
    catch
    {
      // Refresh failed: keep existing cache
    }
    finally
    {
      Interlocked.Exchange(ref _refreshing, 0);
    }
  }

  public void TriggerBackgroundRefreshIfStale()
  {
    if (_refreshing == 0 && DateTime.UtcNow - _lastRefresh > RefreshInterval)
      _ = Task.Run(RefreshCacheAsync);
  }

  public async Task WarmCacheAsync()
  {
    var status = await GetVaultStatusAsync();
    if (status == VaultStatus.Unlocked)
      await RefreshCacheAsync();
    WarmupCompleted?.Invoke();
  }



  private static bool Matches(BitwardenItem item, string query)
  {
    if (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
    if (item.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) return true;

    return item.Type switch
    {
      BitwardenItemType.Login =>
          (item.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
          || item.Uris.Any(u => u.Contains(query, StringComparison.OrdinalIgnoreCase)),
      BitwardenItemType.Card =>
          (item.CardholderName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
          || (item.CardBrand?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false),
      BitwardenItemType.Identity =>
          (item.IdentityFullName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
          || (item.IdentityEmail?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
          || (item.IdentityUsername?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
          || (item.IdentityCompany?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false),
      BitwardenItemType.SshKey =>
          (item.SshFingerprint?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
          || (item.SshHost?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false),
      _ => false,
    };
  }

  private Process StartProcess(string args)
  {
    var psi = new ProcessStartInfo("bw", args)
    {
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      RedirectStandardInput = false,
      CreateNoWindow = true,
    };

    if (_sessionKey != null)
      psi.Environment["BW_SESSION"] = _sessionKey;

    return Process.Start(psi)!;
  }

  private static async Task<(string Content, bool TwoFactorDetected)> ReadStderrWithTwoFactorDetectionAsync(Process process)
  {
    var sb = new System.Text.StringBuilder();
    var buffer = new char[256];
    while (true)
    {
      var count = await process.StandardError.ReadAsync(buffer.AsMemory());
      if (count == 0) break;
      sb.Append(buffer, 0, count);
      if (sb.ToString().Contains("Two-step", StringComparison.OrdinalIgnoreCase))
        return (sb.ToString(), true);
    }
    return (sb.ToString(), false);
  }

  private async Task<string> RunCliAsync(string args)
  {
    using var process = StartProcess(args);
    var output = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    return output;
  }

  private static List<BitwardenItem> ParseItems(string json)
  {
    var items = new List<BitwardenItem>();

    try
    {
      var array = JsonNode.Parse(json)?.AsArray();
      if (array == null)
        return items;

      foreach (var node in array)
      {
        if (node == null)
          continue;

        var typeInt = node["type"]?.GetValue<int>() ?? 0;
        if (typeInt < 1 || typeInt > 5)
          continue;

        var type = (BitwardenItemType)typeInt;
        var id = node["id"]?.GetValue<string>() ?? string.Empty;
        var name = node["name"]?.GetValue<string>() ?? string.Empty;
        var notes = node["notes"]?.GetValue<string>();
        var revisionDate = DateTime.TryParse(node["revisionDate"]?.GetValue<string>(), out var rd) ? rd.ToUniversalTime() : DateTime.MinValue;
        var customFields = ParseCustomFields(node["fields"]);

        var item = type switch
        {
          BitwardenItemType.Login => ParseLogin(node["login"], id, name, notes, revisionDate, customFields),
          BitwardenItemType.SecureNote => new BitwardenItem { Id = id, Name = name, Type = type, Notes = notes, RevisionDate = revisionDate, CustomFields = customFields },
          BitwardenItemType.Card => ParseCard(node["card"], id, name, notes, revisionDate, customFields),
          BitwardenItemType.Identity => ParseIdentity(node["identity"], id, name, notes, revisionDate, customFields),
          BitwardenItemType.SshKey => ParseSshKey(node["sshKey"], id, name, notes, revisionDate, customFields),
          _ => null,
        };

        if (item != null)
          items.Add(item);
      }
    }
    catch
    {
    }

    return items;
  }

  private static BitwardenItem ParseLogin(JsonNode? login, string id, string name, string? notes, DateTime revisionDate, Dictionary<string, string> customFields)
  {
    var uris = login?["uris"]?.AsArray()
        ?.Select(u => u?["uri"]?.GetValue<string>())
        .Where(u => !string.IsNullOrEmpty(u))
        .Cast<string>()
        .ToList() ?? [];

    return new BitwardenItem
    {
      Id = id,
      Name = name,
      Type = BitwardenItemType.Login,
      Notes = notes,
      RevisionDate = revisionDate,
      CustomFields = customFields,
      Username = login?["username"]?.GetValue<string>(),
      Password = login?["password"]?.GetValue<string>(),
      HasTotp = !string.IsNullOrEmpty(login?["totp"]?.GetValue<string>()),
      TotpSecret = login?["totp"]?.GetValue<string>(),
      Uris = uris,
    };
  }

  private static BitwardenItem ParseCard(JsonNode? card, string id, string name, string? notes, DateTime revisionDate, Dictionary<string, string> customFields) => new()
  {
    Id = id,
    Name = name,
    Type = BitwardenItemType.Card,
    Notes = notes,
    RevisionDate = revisionDate,
    CustomFields = customFields,
    CardholderName = card?["cardholderName"]?.GetValue<string>(),
    CardBrand = card?["brand"]?.GetValue<string>(),
    CardNumber = card?["number"]?.GetValue<string>(),
    CardExpMonth = card?["expMonth"]?.GetValue<string>(),
    CardExpYear = card?["expYear"]?.GetValue<string>(),
    CardCode = card?["code"]?.GetValue<string>(),
  };

  private static BitwardenItem ParseIdentity(JsonNode? id_node, string id, string name, string? notes, DateTime revisionDate, Dictionary<string, string> customFields)
  {
    var parts = new[] { id_node?["firstName"]?.GetValue<string>(), id_node?["middleName"]?.GetValue<string>(), id_node?["lastName"]?.GetValue<string>() };
    var fullName = string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));

    var addrParts = new[] { id_node?["address1"]?.GetValue<string>(), id_node?["address2"]?.GetValue<string>(), id_node?["address3"]?.GetValue<string>() };
    var addrLine = string.Join(", ", addrParts.Where(p => !string.IsNullOrEmpty(p)));
    var cityParts = new[] { id_node?["city"]?.GetValue<string>(), id_node?["state"]?.GetValue<string>(), id_node?["postalCode"]?.GetValue<string>() };
    var cityLine = string.Join(", ", cityParts.Where(p => !string.IsNullOrEmpty(p)));
    var country = id_node?["country"]?.GetValue<string>();
    var address = string.Join("\n", new[] { addrLine, cityLine, country }.Where(p => !string.IsNullOrEmpty(p)));

    return new BitwardenItem
    {
      Id = id,
      Name = name,
      Type = BitwardenItemType.Identity,
      Notes = notes,
      RevisionDate = revisionDate,
      CustomFields = customFields,
      IdentityFullName = string.IsNullOrEmpty(fullName) ? null : fullName,
      IdentityEmail = id_node?["email"]?.GetValue<string>(),
      IdentityPhone = id_node?["phone"]?.GetValue<string>(),
      IdentityUsername = id_node?["username"]?.GetValue<string>(),
      IdentityCompany = id_node?["company"]?.GetValue<string>(),
      IdentityAddress = string.IsNullOrEmpty(address) ? null : address,
      IdentitySsn = id_node?["ssn"]?.GetValue<string>(),
      IdentityPassportNumber = id_node?["passportNumber"]?.GetValue<string>(),
      IdentityLicenseNumber = id_node?["licenseNumber"]?.GetValue<string>(),
    };
  }

  private static BitwardenItem ParseSshKey(JsonNode? ssh, string id, string name, string? notes, DateTime revisionDate, Dictionary<string, string> customFields) => new()
  {
    Id = id,
    Name = name,
    Type = BitwardenItemType.SshKey,
    Notes = notes,
    RevisionDate = revisionDate,
    CustomFields = customFields,
    SshPublicKey = ssh?["publicKey"]?.GetValue<string>(),
    SshFingerprint = ssh?["keyFingerprint"]?.GetValue<string>(),
    SshPrivateKey = ssh?["privateKey"]?.GetValue<string>(),
  };

  private static Dictionary<string, string> ParseCustomFields(JsonNode? fields)
  {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (fields is not JsonArray arr) return result;

    foreach (var field in arr)
    {
      var fieldName = field?["name"]?.GetValue<string>();
      var fieldValue = field?["value"]?.GetValue<string>();
      if (!string.IsNullOrEmpty(fieldName) && fieldValue != null)
        result.TryAdd(fieldName, fieldValue);
    }

    return result;
  }
}
