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

  public bool IsUnlocked => _sessionKey != null;

  public bool IsCacheLoaded => _cacheLoaded;

  internal static string? ServerUrl { get; private set; }

  public event Action? CacheUpdated;

  public BitwardenCliService(BitwardenSettingsManager? settings = null)
  {
    _settings = settings;
  }

  public void SetSession(string sessionKey) => _sessionKey = sessionKey;

  public void ClearSession()
  {
    _sessionKey = null;
    lock (_cacheLock)
    {
      _cache = [];
      _cacheLoaded = false;
    }
  }

  public async Task<bool> CheckStatusAsync()
  {
    if (_sessionKey != null)
    {
      _ = Task.Run(FetchServerUrlAsync);
      return true;
    }

    var envSession = Environment.GetEnvironmentVariable("BW_SESSION");
    if (!string.IsNullOrWhiteSpace(envSession))
    {
      _sessionKey = envSession;
      _ = Task.Run(FetchServerUrlAsync);
      return true;
    }

    if (_settings?.RememberSession.Value != true)
      return false;

    var stored = SessionStore.Load();
    if (string.IsNullOrEmpty(stored))
      return false;

    _sessionKey = stored;
    try
    {
      var output = await RunCliAsync("status");
      var json = JsonNode.Parse(output);
      var status = json?["status"]?.GetValue<string>();
      ServerUrl ??= json?["serverUrl"]?.GetValue<string>()?.TrimEnd('/');
      if (status == "unlocked")
        return true;
    }
    catch { }

    _sessionKey = null;
    SessionStore.Clear();
    return false;
  }

  public async Task<string?> UnlockAsync(string masterPassword)
  {
    try
    {
      var output = await RunCliWithStdinAsync("unlock --raw", masterPassword);
      var key = output.Trim();
      if (!string.IsNullOrEmpty(key))
      {
        _sessionKey = key;

        if (_settings?.RememberSession.Value == true)
          SessionStore.Save(key);

        _ = Task.Run(async () =>
        {
          await FetchServerUrlAsync();
          await RefreshCacheAsync();
        });
        return key;
      }

      return null;
    }
    catch
    {
      return null;
    }
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
      // Refresh failed — keep existing cache
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

  private async Task<string> RunCliAsync(string args)
  {
    using var process = StartProcess(args);
    var output = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    return output;
  }

  private async Task<string> RunCliWithStdinAsync(string args, string stdinInput)
  {
    var psi = new ProcessStartInfo("bw", args)
    {
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      RedirectStandardInput = true,
      CreateNoWindow = true,
    };

    if (_sessionKey != null)
      psi.Environment["BW_SESSION"] = _sessionKey;

    using var process = Process.Start(psi)!;
    await process.StandardInput.WriteLineAsync(stdinInput);
    process.StandardInput.Close();

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
