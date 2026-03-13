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

#pragma warning disable CA1001 // singleton-lifetime; timer disposed when lock fires
internal sealed class BitwardenCliService
{
  private readonly BitwardenSettingsManager? _settings;
  private string? _sessionKey;

  private List<BitwardenItem> _cache = [];
  private readonly Lock _cacheLock = new();
  private bool _cacheLoaded;
  private DateTime _lastRefresh;
  private int _refreshing;
  private TimeSpan RefreshInterval
  {
    get
    {
      var minutes = int.TryParse(_settings?.BackgroundRefresh.Value, out var m) ? m : 5;
      return minutes > 0 ? TimeSpan.FromMinutes(minutes) : Timeout.InfiniteTimeSpan;
    }
  }

  public DateTime LastRefresh => _lastRefresh;

  private VaultStatus? _lastStatus;
  private Dictionary<string, string> _folders = [];

  private Timer? _autoLockTimer;
  private TimeSpan _autoLockTimeout;

  public bool IsUnlocked => _sessionKey != null;

  public bool IsCacheLoaded => _cacheLoaded;

  public VaultStatus? LastStatus => _lastStatus;

  internal static string? ServerUrl { get; private set; }

  internal IReadOnlyDictionary<string, string> Folders
  {
    get { lock (_cacheLock) return _folders; }
  }

  public event Action? CacheUpdated;
  public event Action? StatusChanged;
  public event Action? WarmupCompleted;
  public event Action? AutoLocking;
  public event Action? AutoLocked;

  public BitwardenCliService(BitwardenSettingsManager? settings = null)
  {
    _settings = settings;
    ApplyAutoLockSetting();
    AccessTracker.ItemAccessed += ResetAutoLockTimer;
    if (_settings != null)
      _settings.Settings.SettingsChanged += (_, _) => ApplyAutoLockSetting();
  }

  internal void LoadTestData(List<BitwardenItem> items, Dictionary<string, string> folders)
  {
    lock (_cacheLock) { _cache = items; _folders = folders; _cacheLoaded = true; }
  }

  private void ApplyAutoLockSetting()
  {
    var minutes = int.TryParse(_settings?.AutoLockTimeout.Value, out var m) ? m : 0;
    _autoLockTimeout = minutes > 0 ? TimeSpan.FromMinutes(minutes) : Timeout.InfiniteTimeSpan;
    ResetAutoLockTimer();
  }

  public void ResetAutoLockTimer()
  {
    if (_autoLockTimeout == Timeout.InfiniteTimeSpan || !IsUnlocked)
    {
      _autoLockTimer?.Dispose();
      _autoLockTimer = null;
      return;
    }
    if (_autoLockTimer == null)
      _autoLockTimer = new Timer(OnAutoLockTick, null, _autoLockTimeout, Timeout.InfiniteTimeSpan);
    else
      _autoLockTimer.Change(_autoLockTimeout, Timeout.InfiniteTimeSpan);
  }

  private void OnAutoLockTick(object? _)
  {
    _ = Task.Run(async () =>
    {
      try
      {
        AutoLocking?.Invoke();
        await LockAsync();
        AutoLocked?.Invoke();
      }
      catch { }
    });
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
    if (status == VaultStatus.Unlocked)
      ResetAutoLockTimer();
    else
    {
      _autoLockTimer?.Dispose();
      _autoLockTimer = null;
    }
    return status;
  }

  // Intentionally synchronous: runs once at startup on a fast-exit 'bw --version' process.
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
      using var cts = new CancellationTokenSource(CliTimeoutMs);
      await process.StandardOutput.ReadToEndAsync(cts.Token);
      await process.WaitForExitAsync(cts.Token);
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
      using var cts = new CancellationTokenSource(CliTimeoutMs);
      var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
      var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
      await process.WaitForExitAsync(cts.Token);

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
      using var cts = new CancellationTokenSource(CliTimeoutMs);
      process.StandardInput.Close();

      var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
      var (stderr, twoFactorDetected) = await ReadStderrWithTwoFactorDetectionAsync(process, cts.Token);

      if (twoFactorDetected)
      {
        try { process.Kill(); } catch { }
        return (false, "Two-factor authentication required - enter your 2FA code", true);
      }

      await process.WaitForExitAsync(cts.Token);

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
    var sanitizedUrl = url.Replace("\"", "");
    using var process = StartProcess($"config server \"{sanitizedUrl}\"");
    using var cts = new CancellationTokenSource(CliTimeoutMs);
    var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
    var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
    await process.WaitForExitAsync(cts.Token);

    if (process.ExitCode == 0)
    {
      ServerUrl = sanitizedUrl.TrimEnd('/');
      StatusChanged?.Invoke();
      return null;
    }

    var error = stderr.Trim();
    return string.IsNullOrEmpty(error) ? "Failed to set server URL" : error;
  }

  public List<BitwardenItem> SearchCached(string? query = null, ForegroundContext? context = null, int maxContextItems = 0)
  {
    lock (_cacheLock)
    {
      var (filters, textQuery) = ParseSearchFilters(query);

      IEnumerable<BitwardenItem> results = _cache;

      foreach (var filter in filters)
        results = ApplyFilter(results, filter);

      if (string.IsNullOrWhiteSpace(textQuery))
      {
        var sorted = results
            .OrderByDescending(i => AccessTracker.IsLastCopied(i.Id) ? 1 : 0)
            .ThenByDescending(i => i.Favorite ? 1 : 0)
            .ThenByDescending(i => ContextBoost(i, context))
            .ThenByDescending(i => AccessTracker.GetLastAccess(i.Id))
            .ThenByDescending(i => i.RevisionDate)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (maxContextItems > 0 && context != null)
        {
          var contextMatches = sorted.Where(i => ContextBoost(i, context) > 0).Take(maxContextItems).ToList();
          if (contextMatches.Count > 0)
          {
            var contextMatchIds = contextMatches.Select(i => i.Id).ToHashSet();
            var remainder = sorted
                .Where(i => !contextMatchIds.Contains(i.Id))
                .OrderByDescending(i => AccessTracker.IsLastCopied(i.Id) ? 1 : 0)
                .ThenByDescending(i => i.Favorite ? 1 : 0)
                .ThenByDescending(i => AccessTracker.GetLastAccess(i.Id))
                .ThenByDescending(i => i.RevisionDate)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
            return [.. contextMatches, .. remainder];
          }
        }

        return sorted;
      }

      var wordBoundaryRegex = new Regex(@"\b" + Regex.Escape(textQuery) + @"\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
      return results
          .Where(i => Matches(i, textQuery))
          .OrderBy(i => Relevance(i, textQuery, wordBoundaryRegex))
          .ThenByDescending(i => AccessTracker.IsLastCopied(i.Id) ? 1 : 0)
          .ThenByDescending(i => i.Favorite ? 1 : 0)
          .ThenByDescending(i => ContextBoost(i, context))
          .ThenByDescending(i => AccessTracker.GetLastAccess(i.Id))
          .ThenByDescending(i => i.RevisionDate)
          .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
          .ToList();
    }
  }

  internal string? GetFolderName(string? folderId)
  {
    if (folderId == null) return null;
    lock (_cacheLock)
      return _folders.GetValueOrDefault(folderId);
  }

  private static int ContextBoost(BitwardenItem item, ForegroundContext? context)
  {
    if (context == null)
      return 0;

    return ContextAwarenessService.ContextScore(context, item);
  }

  internal static (List<(string Key, string Value)> Filters, string? TextQuery) ParseSearchFilters(string? query)
  {
    var filters = new List<(string Key, string Value)>();
    if (string.IsNullOrWhiteSpace(query))
      return (filters, null);

    var remaining = new System.Text.StringBuilder();
    foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
      var colonIdx = token.IndexOf(':');
      if (colonIdx > 0 && colonIdx < token.Length - 1)
      {
        var key = token[..colonIdx].ToLowerInvariant();
        var value = token[(colonIdx + 1)..];
        if (IsKnownFilter(key))
        {
          filters.Add((key, value));
          continue;
        }
      }

      if (token.StartsWith("has:", StringComparison.OrdinalIgnoreCase) && token.Length > 4)
      {
        filters.Add(("has", token[4..].ToLowerInvariant()));
        continue;
      }

      if (remaining.Length > 0) remaining.Append(' ');
      remaining.Append(token);
    }

    var text = remaining.Length > 0 ? remaining.ToString() : null;
    return (filters, text);
  }

  internal static bool IsKnownFilter(string key) => key is "folder" or "url" or "host" or "type" or "org" or "is";

  internal IEnumerable<BitwardenItem> ApplyFilter(IEnumerable<BitwardenItem> items, (string Key, string Value) filter) => filter.Key switch
  {
    "folder" => items.Where(i =>
    {
      var name = GetFolderName(i.FolderId);
      return name != null && name.Contains(filter.Value, StringComparison.OrdinalIgnoreCase);
    }),
    "url" or "host" => items.Where(i =>
        i.Type == BitwardenItemType.Login && i.Uris.Any(u => u.Uri.Contains(filter.Value, StringComparison.OrdinalIgnoreCase))),
    "type" => items.Where(i => i.Type.ToString().Equals(filter.Value, StringComparison.OrdinalIgnoreCase)
        || ((int)i.Type).ToString(System.Globalization.CultureInfo.InvariantCulture) == filter.Value),
    "org" => items.Where(i => i.OrganizationId != null && i.OrganizationId.Contains(filter.Value, StringComparison.OrdinalIgnoreCase)),
    "has" => filter.Value switch
    {
      "totp" or "otp" or "2fa" or "mfa" => items.Where(i => i.HasTotp),
      "passkey" or "fido2" or "webauthn" or "passwordless" => items.Where(i => i.HasPasskey),
      "password" or "pw" => items.Where(i => !string.IsNullOrEmpty(i.Password)),
      "url" or "uri" => items.Where(i => i.Uris.Count > 0),
      "notes" or "note" => items.Where(i => !string.IsNullOrEmpty(i.Notes)),
      "folder" => items.Where(i => i.FolderId != null),
      "attachment" or "attachments" => items, // not tracked yet, pass-through
      _ => items,
    },
    "is" => filter.Value switch
    {
      "favorite" or "fav" => items.Where(i => i.Favorite),
      "weak" => items.Where(i => i.Type == BitwardenItemType.Login
          && !string.IsNullOrEmpty(i.Password) && i.Password!.Length < 8),
      "old" or "stale" => items.Where(i => i.Type == BitwardenItemType.Login
          && !string.IsNullOrEmpty(i.Password)
          && DateTime.UtcNow - (i.PasswordRevisionDate ?? i.RevisionDate) > TimeSpan.FromDays(365)),
      "insecure" or "http" => items.Where(i => i.Type == BitwardenItemType.Login
          && i.Uris.Any(u => u.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))),
      "watchtower" or "flagged" => items.Where(i => i.Type == BitwardenItemType.Login && (
          (!string.IsNullOrEmpty(i.Password) && i.Password!.Length < 8)
          || (!string.IsNullOrEmpty(i.Password) && DateTime.UtcNow - (i.PasswordRevisionDate ?? i.RevisionDate) > TimeSpan.FromDays(365))
          || i.Uris.Any(u => u.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))),
      _ => items,
    },
    _ => items,
  };

  internal static int Relevance(BitwardenItem item, string query, Regex wordBoundaryRegex)
  {
    if (item.Name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
    if (item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
    if (wordBoundaryRegex.IsMatch(item.Name)) return 2;
    if (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 3;
    return 4;
  }

  public async Task RefreshCacheAsync()
  {
    if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
      return;

    try
    {
      var foldersTask = RunCliAsync("list folders");
      var itemsTask = RunCliAsync("list items");
      await Task.WhenAll(foldersTask, itemsTask);

      var folders = ParseFolders(await foldersTask);
      var items = ParseItems(await itemsTask);
      lock (_cacheLock)
      {
        _folders = folders;
        _cache = items;
        _cacheLoaded = true;
        _lastRefresh = DateTime.UtcNow;
      }

      CacheUpdated?.Invoke();
    }
    catch (InvalidOperationException)
    {
      throw;
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

  public async Task SyncVaultAsync()
  {
    await RunCliAsync("sync");
    Interlocked.Exchange(ref _refreshing, 0);
    await RefreshCacheAsync();
  }

  public void TriggerBackgroundRefreshIfStale()
  {
    var interval = RefreshInterval;
    if (interval == Timeout.InfiniteTimeSpan) return;
    if (_refreshing == 0 && DateTime.UtcNow - _lastRefresh > interval)
      _ = Task.Run(async () => { try { await RefreshCacheAsync(); } catch { } });
  }

  private Task _warmupTask = Task.CompletedTask;
  public Task WarmupTask => _warmupTask;

  public Task WarmCacheAsync()
  {
    // Set _warmupTask synchronously so InitializeAsync always awaits the real task,
    // but run the actual CLI work on ThreadPool to avoid blocking the COM activation thread.
    _warmupTask = Task.Run(RunWarmupAsync);
    return _warmupTask;
  }

  private async Task RunWarmupAsync()
  {
    var status = await GetVaultStatusAsync();
    if (status == VaultStatus.Unlocked)
      await RefreshCacheAsync();
    WarmupCompleted?.Invoke();
  }



  internal static bool Matches(BitwardenItem item, string query)
  {
    if (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
    if (item.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) return true;

    return item.Type switch
    {
      BitwardenItemType.Login =>
          (item.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
          || item.Uris.Any(u => u.Uri.Contains(query, StringComparison.OrdinalIgnoreCase)),
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

  private static async Task<(string Content, bool TwoFactorDetected)> ReadStderrWithTwoFactorDetectionAsync(Process process, CancellationToken token)
  {
    var sb = new System.Text.StringBuilder();
    var buffer = new char[256];
    while (true)
    {
      var count = await process.StandardError.ReadAsync(buffer.AsMemory(), token);
      if (count == 0) break;
      sb.Append(buffer, 0, count);
      var text = sb.ToString();
      if (text.Contains("Two-step", StringComparison.OrdinalIgnoreCase))
        return (text, true);
    }
    return (sb.ToString(), false);
  }

  private const int CliTimeoutMs = 30_000;

  private async Task<string> RunCliAsync(string args)
  {
    using var process = StartProcess(args);
    using var cts = new CancellationTokenSource(CliTimeoutMs);
    try
    {
      var stderrTask = ReadStderrWithSessionDetectionAsync(process, cts.Token);
      var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
      var (stderr, sessionInvalid) = await stderrTask;

      if (sessionInvalid)
      {
        try { process.Kill(); } catch { }
        HandleInvalidSession();
        throw new InvalidOperationException("Session expired — vault is locked");
      }

      await process.WaitForExitAsync(cts.Token);
      var output = await stdoutTask;
      var error = stderr.Trim();

      if (process.ExitCode != 0 && IsSessionInvalidError(error))
      {
        HandleInvalidSession();
        throw new InvalidOperationException("Session expired — vault is locked");
      }

      return output;
    }
    catch (OperationCanceledException)
    {
      try { process.Kill(); } catch { }
      throw new TimeoutException($"Bitwarden CLI timed out after {CliTimeoutMs / 1000}s running: bw {args.Split(' ')[0]}");
    }
  }

  private static async Task<(string Content, bool SessionInvalid)> ReadStderrWithSessionDetectionAsync(Process process, CancellationToken token)
  {
    var sb = new System.Text.StringBuilder();
    var buffer = new char[256];
    while (true)
    {
      var count = await process.StandardError.ReadAsync(buffer.AsMemory(), token);
      if (count == 0) break;
      sb.Append(buffer, 0, count);
      var text = sb.ToString();
      if (text.Contains("Master password", StringComparison.OrdinalIgnoreCase)
          || text.Contains("? Password", StringComparison.OrdinalIgnoreCase)
          || IsSessionInvalidError(text))
        return (text, true);
    }
    return (sb.ToString(), false);
  }

  internal static bool IsSessionInvalidError(string error) =>
      error.Contains("not logged in", StringComparison.OrdinalIgnoreCase)
      || error.Contains("vault is locked", StringComparison.OrdinalIgnoreCase)
      || error.Contains("invalid session", StringComparison.OrdinalIgnoreCase)
      || error.Contains("session key is invalid", StringComparison.OrdinalIgnoreCase);

  private void HandleInvalidSession()
  {
    _sessionKey = null;
    SessionStore.Clear();
    lock (_cacheLock)
    {
      _cache = [];
      _cacheLoaded = false;
    }
    SetStatus(VaultStatus.Locked);
    StatusChanged?.Invoke();
  }

  internal static List<BitwardenItem> ParseItems(string json)
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
        var favorite = node["favorite"]?.GetValue<bool>() ?? false;
        var folderId = node["folderId"]?.GetValue<string>();
        var organizationId = node["organizationId"]?.GetValue<string>();
        var reprompt = node["reprompt"]?.GetValue<int>() ?? 0;

        var item = type switch
        {
          BitwardenItemType.Login => ParseLogin(node["login"], id, name, notes, revisionDate, customFields, favorite, folderId, organizationId, reprompt),
          BitwardenItemType.SecureNote => new BitwardenItem { Id = id, Name = name, Type = type, Notes = notes, RevisionDate = revisionDate, CustomFields = customFields, Favorite = favorite, FolderId = folderId, OrganizationId = organizationId, Reprompt = reprompt },
          BitwardenItemType.Card => ParseCard(node["card"], id, name, notes, revisionDate, customFields, favorite, folderId, organizationId, reprompt),
          BitwardenItemType.Identity => ParseIdentity(node["identity"], id, name, notes, revisionDate, customFields, favorite, folderId, organizationId, reprompt),
          BitwardenItemType.SshKey => ParseSshKey(node["sshKey"], id, name, notes, revisionDate, customFields, favorite, folderId, organizationId, reprompt),
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

  private static BitwardenItem ParseLogin(JsonNode? login, string id, string name, string? notes, DateTime revisionDate, Dictionary<string, CustomField> customFields, bool favorite, string? folderId, string? organizationId, int reprompt)
  {
    var uris = login?["uris"]?.AsArray()
        ?.Select(u =>
        {
          var uri = u?["uri"]?.GetValue<string>();
          if (string.IsNullOrEmpty(uri)) return null;
          var matchVal = u?["match"];
          var match = matchVal is null
              ? UriMatchType.Default
              : (UriMatchType)matchVal.GetValue<int>();
          return new ItemUri(uri, match);
        })
        .Where(u => u != null)
        .Cast<ItemUri>()
        .ToList() ?? [];

    var passwordRevision = DateTime.TryParse(login?["passwordRevisionDate"]?.GetValue<string>(), out var prd) ? (DateTime?)prd.ToUniversalTime() : null;

    return new BitwardenItem
    {
      Id = id,
      Name = name,
      Type = BitwardenItemType.Login,
      Notes = notes,
      RevisionDate = revisionDate,
      CustomFields = customFields,
      Favorite = favorite,
      FolderId = folderId,
      OrganizationId = organizationId,
      Reprompt = reprompt,
      Username = login?["username"]?.GetValue<string>(),
      Password = login?["password"]?.GetValue<string>(),
      HasTotp = !string.IsNullOrEmpty(login?["totp"]?.GetValue<string>()),
      TotpSecret = login?["totp"]?.GetValue<string>(),
      HasPasskey = login?["fido2Credentials"] is JsonArray fido && fido.Count > 0,
      Uris = uris,
      PasswordRevisionDate = passwordRevision,
    };
  }

  private static BitwardenItem ParseCard(JsonNode? card, string id, string name, string? notes, DateTime revisionDate, Dictionary<string, CustomField> customFields, bool favorite, string? folderId, string? organizationId, int reprompt) => new()
  {
    Id = id,
    Name = name,
    Type = BitwardenItemType.Card,
    Notes = notes,
    RevisionDate = revisionDate,
    CustomFields = customFields,
    Favorite = favorite,
    FolderId = folderId,
    OrganizationId = organizationId,
    Reprompt = reprompt,
    CardholderName = card?["cardholderName"]?.GetValue<string>(),
    CardBrand = card?["brand"]?.GetValue<string>(),
    CardNumber = card?["number"]?.GetValue<string>(),
    CardExpMonth = card?["expMonth"]?.GetValue<string>(),
    CardExpYear = card?["expYear"]?.GetValue<string>(),
    CardCode = card?["code"]?.GetValue<string>(),
  };

  private static BitwardenItem ParseIdentity(JsonNode? id_node, string id, string name, string? notes, DateTime revisionDate, Dictionary<string, CustomField> customFields, bool favorite, string? folderId, string? organizationId, int reprompt)
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
      Favorite = favorite,
      FolderId = folderId,
      OrganizationId = organizationId,
      Reprompt = reprompt,
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

  private static BitwardenItem ParseSshKey(JsonNode? ssh, string id, string name, string? notes, DateTime revisionDate, Dictionary<string, CustomField> customFields, bool favorite, string? folderId, string? organizationId, int reprompt) => new()
  {
    Id = id,
    Name = name,
    Type = BitwardenItemType.SshKey,
    Notes = notes,
    RevisionDate = revisionDate,
    CustomFields = customFields,
    Favorite = favorite,
    FolderId = folderId,
    OrganizationId = organizationId,
    Reprompt = reprompt,
    SshPublicKey = ssh?["publicKey"]?.GetValue<string>(),
    SshFingerprint = ssh?["keyFingerprint"]?.GetValue<string>(),
    SshPrivateKey = ssh?["privateKey"]?.GetValue<string>(),
  };

  internal static Dictionary<string, CustomField> ParseCustomFields(JsonNode? fields)
  {
    var result = new Dictionary<string, CustomField>(StringComparer.OrdinalIgnoreCase);
    if (fields is not JsonArray arr) return result;

    foreach (var field in arr)
    {
      var fieldName = field?["name"]?.GetValue<string>();
      var fieldValue = field?["value"]?.GetValue<string>();
      var fieldType = field?["type"]?.GetValue<int>() ?? 0;
      if (!string.IsNullOrEmpty(fieldName) && fieldValue != null)
        result.TryAdd(fieldName, new CustomField(fieldValue, fieldType == 1));
    }

    return result;
  }

  internal static Dictionary<string, string> ParseFolders(string json)
  {
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    try
    {
      var array = JsonNode.Parse(json)?.AsArray();
      if (array == null) return result;

      foreach (var node in array)
      {
        var id = node?["id"]?.GetValue<string>();
        var name = node?["name"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
          result[id] = name;
      }
    }
    catch { }
    return result;
  }
}
