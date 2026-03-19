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
  private readonly CliProcessFactory _processFactory;
  private string? _sessionKey;
  private readonly System.Collections.Concurrent.ConcurrentDictionary<int, ICliProcess> _runningProcesses = new();
  private ICliProcess? _pendingDeviceVerificationProcess;
  private CancellationTokenSource? _pendingDeviceVerificationCts;
  private Task<(string Content, bool Detected)>? _pendingStdoutTask;
  private Task<(string Content, bool Detected)>? _pendingStderrTask;

#pragma warning disable CA1859 // delegate CliProcessFactory requires ICliProcess return type
  private static ICliProcess DefaultProcessFactory(ProcessStartInfo psi)
  {
    var process = Process.Start(psi)!;
    return new RealCliProcess(process);
  }
#pragma warning restore CA1859

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
  internal static string? IconsUrl { get; private set; }

  internal static void ResetStaticState()
  {
    ServerUrl = null;
    IconsUrl = null;
  }

  internal IReadOnlyDictionary<string, string> Folders
  {
    get { lock (_cacheLock) return _folders; }
  }

  public event Action? CacheUpdated;
  public event Action? StatusChanged;
  public event Action? WarmupCompleted;
  public event Action? AutoLocking;
  public event Action? AutoLocked;

  public BitwardenCliService(BitwardenSettingsManager? settings = null, CliProcessFactory? processFactory = null)
  {
    _settings = settings;
    _processFactory = processFactory ?? DefaultProcessFactory;
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

  private bool IsCliAvailable()
  {
    try
    {
      using var process = _processFactory(new ProcessStartInfo(BwCliPath, "--version")
      {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
      });
      var line = process.StandardOutput.ReadLine();
      var available = line != null;
      try { process.Kill(true); } catch { }
      return available;
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
      // Drain stderr in the background to prevent pipe buffer deadlock.
      _ = process.StandardError.ReadToEndAsync(cts.Token).ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
      string? line;
      while ((line = await process.StandardOutput.ReadLineAsync(cts.Token)) != null)
      {
        if (line.Contains("Syncing complete.", StringComparison.OrdinalIgnoreCase))
        {
          try { process.Kill(true); } catch { }
          await FetchServerUrlAsync();
          return true;
        }
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
      var psi = new ProcessStartInfo(BwCliPath, "unlock --passwordenv BW_MP --raw")
      {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        CreateNoWindow = true,
      };

      psi.Environment["BW_MP"] = masterPassword;
      if (_sessionKey != null)
        psi.Environment["BW_SESSION"] = _sessionKey;

      using var process = _processFactory(psi);
      process.StandardInput.Close();
      using var cts = new CancellationTokenSource(CliTimeoutMs);
      var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
      var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
      var stdout = await stdoutTask;
      var stderr = await stderrTask;
      try { process.Kill(true); } catch { }

      var key = stdout.Trim();
      if (!string.IsNullOrEmpty(key) && !key.Contains(' '))
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

  public async Task<(bool Success, string? Error, bool TwoFactorRequired, bool DeviceVerificationRequired)> LoginAsync(string email, string password, string? twoFactorCode = null, int? twoFactorMethod = null)
  {
    DisposeDeviceVerificationProcess();
    try
    {
      var sanitizedEmail = email.Replace("\"", "");
      var args = $"login \"{sanitizedEmail}\" --passwordenv BW_MP";
      if (!string.IsNullOrEmpty(twoFactorCode))
      {
        var sanitizedCode = twoFactorCode.Replace("\"", "");
        args += twoFactorMethod.HasValue
            ? $" --method {twoFactorMethod.Value} --code \"{sanitizedCode}\""
            : $" --code \"{sanitizedCode}\"";
      }
      args += " --raw";

      var psi = new ProcessStartInfo(BwCliPath, args)
      {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        CreateNoWindow = true,
      };

      psi.Environment["BW_MP"] = password;

      var process = _processFactory(psi);
      var cts = new CancellationTokenSource(CliTimeoutMs);

      var allPrompts = new[] { "device verification", "Two-step" };
      var stdoutTask = ReadStreamWithPromptDetectionAsync(process.StandardOutput, allPrompts, cts.Token);
      var stderrTask = ReadStreamWithPromptDetectionAsync(process.StandardError, allPrompts, cts.Token);

      var completed = await Task.WhenAny(stdoutTask, stderrTask);
      var result = await completed;

      if (result.Detected)
      {
        if (result.Content.Contains("device verification", StringComparison.OrdinalIgnoreCase))
        {
          _pendingDeviceVerificationProcess = process;
          _pendingDeviceVerificationCts = cts;
          _pendingStdoutTask = stdoutTask;
          _pendingStderrTask = stderrTask;
          return (false, "New device verification required — enter OTP sent to your email", false, true);
        }
        try { process.Kill(); } catch { }
        process.Dispose();
        cts.Dispose();
        return (false, "Two-factor authentication required — enter your 2FA code", true, false);
      }

      // Neither prompt detected during streaming — check the other stream too
      var otherTask = completed == stdoutTask ? stderrTask : stdoutTask;
      var other = await otherTask;
      if (other.Detected)
      {
        if (other.Content.Contains("device verification", StringComparison.OrdinalIgnoreCase))
        {
          _pendingDeviceVerificationProcess = process;
          _pendingDeviceVerificationCts = cts;
          _pendingStdoutTask = stdoutTask;
          _pendingStderrTask = stderrTask;
          return (false, "New device verification required — enter OTP sent to your email", false, true);
        }
        try { process.Kill(); } catch { }
        process.Dispose();
        cts.Dispose();
        return (false, "Two-factor authentication required — enter your 2FA code", true, false);
      }

      try { process.StandardInput.Close(); } catch { }
      await process.WaitForExitAsync(cts.Token);

      var stdout = (await stdoutTask).Content.Trim();
      var stderr = (await stderrTask).Content.Trim();
      var exitCode = process.ExitCode;

      process.Dispose();
      cts.Dispose();

      if (exitCode == 0 && !string.IsNullOrEmpty(stdout) && !stdout.Contains(' '))
      {
        _sessionKey = stdout;
        SetStatus(VaultStatus.Unlocked);
        if (_settings?.RememberSession.Value == true)
          SessionStore.Save(stdout);
        return (true, null, false, false);
      }

      var needs2fa = stderr.Contains("Two-step", StringComparison.OrdinalIgnoreCase)
          || stderr.Contains("two-factor", StringComparison.OrdinalIgnoreCase);

      var error = stderr;
      return (false, string.IsNullOrEmpty(error) ? "Login failed" : error, needs2fa, false);
    }
    catch (Exception ex)
    {
      DisposeDeviceVerificationProcess();
      return (false, ex.Message, false, false);
    }
  }

  public async Task<(bool Success, string? Error)> SubmitDeviceVerificationAsync(string otpCode)
  {
    var process = _pendingDeviceVerificationProcess;
    var oldCts = _pendingDeviceVerificationCts;
    var existingStdoutTask = _pendingStdoutTask;
    var existingStderrTask = _pendingStderrTask;
    _pendingDeviceVerificationProcess = null;
    _pendingDeviceVerificationCts = null;
    _pendingStdoutTask = null;
    _pendingStderrTask = null;

    if (process == null)
      return (false, "No pending device verification — please log in again");

    oldCts?.Dispose();
    using var cts = new CancellationTokenSource(CliTimeoutMs);

    try
    {
      await process.StandardInput.WriteLineAsync(otpCode.AsMemory(), cts.Token);
      process.StandardInput.Close();

      // For each stream: if its reader already returned (detected the prompt),
      // create a new ReadToEndAsync to capture post-OTP output (e.g. session key).
      // If its reader is still running (blocked waiting for data), await it —
      // it will complete once the process exits after processing the OTP.
      var stdoutRead = existingStdoutTask != null && existingStdoutTask.IsCompleted
          ? process.StandardOutput.ReadToEndAsync(cts.Token)
          : (existingStdoutTask ?? Task.FromResult((Content: "", Detected: false)))
              .ContinueWith(t => t.Result.Content, cts.Token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

      var stderrRead = existingStderrTask != null && existingStderrTask.IsCompleted
          ? process.StandardError.ReadToEndAsync(cts.Token)
          : (existingStderrTask ?? Task.FromResult((Content: "", Detected: false)))
              .ContinueWith(t => t.Result.Content, cts.Token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

      await Task.WhenAll(stdoutRead, stderrRead);
      await process.WaitForExitAsync(cts.Token);

      var stdout = (await stdoutRead).Trim();
      var stderr = (await stderrRead).Trim();

      if (process.ExitCode == 0 && !string.IsNullOrEmpty(stdout) && !stdout.Contains(' '))
      {
        _sessionKey = stdout;
        SetStatus(VaultStatus.Unlocked);
        if (_settings?.RememberSession.Value == true)
          SessionStore.Save(stdout);
        return (true, null);
      }

      return (false, string.IsNullOrEmpty(stderr) ? "Verification failed" : stderr);
    }
    catch (Exception ex)
    {
      return (false, ex.Message);
    }
    finally
    {
      try { process.Kill(); } catch { }
      process.Dispose();
    }
  }

  private void DisposeDeviceVerificationProcess()
  {
    var process = _pendingDeviceVerificationProcess;
    var cts = _pendingDeviceVerificationCts;
    _pendingDeviceVerificationProcess = null;
    _pendingDeviceVerificationCts = null;
    _pendingStdoutTask = null;
    _pendingStderrTask = null;
    if (process != null)
    {
      try { process.Kill(); } catch { }
      process.Dispose();
    }
    cts?.Dispose();
  }

  public async Task LogoutAsync()
  {
    DisposeDeviceVerificationProcess();
    KillAllRunning();
    _sessionKey = null;
    SetStatus(VaultStatus.Unauthenticated);
    lock (_cacheLock)
    {
      _cache = [];
      _cacheLoaded = false;
    }
    SessionStore.Clear();
    StatusChanged?.Invoke();

    try { await RunCliAsync("logout", CliLogoutTimeoutMs, "You have logged out."); } catch { }
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

    try { await RunCliAsync("lock", CliTimeoutMs, "Your vault is locked."); } catch { }
  }

  public async Task<string?> SetServerUrlAsync(ServerConfig config)
  {
    DisposeDeviceVerificationProcess();
    KillAllRunning();
    static string Sanitize(string url) => url.Replace("\"", "");
    var args = "config server \"" + Sanitize(config.BaseUrl) + "\"";
    if (config.WebVaultUrl != null) args += " --web-vault \"" + Sanitize(config.WebVaultUrl) + "\"";
    if (config.ApiUrl != null) args += " --api \"" + Sanitize(config.ApiUrl) + "\"";
    if (config.IdentityUrl != null) args += " --identity \"" + Sanitize(config.IdentityUrl) + "\"";
    if (config.IconsUrl != null) args += " --icons \"" + Sanitize(config.IconsUrl) + "\"";
    if (config.NotificationsUrl != null) args += " --notifications \"" + Sanitize(config.NotificationsUrl) + "\"";
    if (config.EventsUrl != null) args += " --events \"" + Sanitize(config.EventsUrl) + "\"";
    if (config.KeyConnectorUrl != null) args += " --key-connector \"" + Sanitize(config.KeyConnectorUrl) + "\"";

    using var process = StartProcess(args);
    using var cts = new CancellationTokenSource(CliTimeoutMs);
    var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
    try
    {
      string? line;
      while ((line = await process.StandardOutput.ReadLineAsync(cts.Token)) != null)
      {
        if (line.Contains("Saved", StringComparison.OrdinalIgnoreCase))
        {
          try { process.Kill(true); } catch { }
          ServerUrl = config.BaseUrl.TrimEnd('/');
          IconsUrl = config.IconsUrl?.TrimEnd('/');
          SetStatus(VaultStatus.Unauthenticated);
          StatusChanged?.Invoke();
          return null;
        }
      }
    }
    catch (OperationCanceledException) { }
    try { process.Kill(true); } catch { }

    var stderr = (await stderrTask.WaitAsync(TimeSpan.FromSeconds(2))).Trim();
    return string.IsNullOrEmpty(stderr) ? "Failed to set server URL" : stderr;
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
            var recentItem = sorted.FirstOrDefault(i => AccessTracker.IsLastCopied(i.Id));
            var pinnedIds = contextMatches.Select(i => i.Id).ToHashSet();
            if (recentItem != null)
              pinnedIds.Add(recentItem.Id);

            var remainder = sorted
                .Where(i => !pinnedIds.Contains(i.Id))
                .OrderByDescending(i => i.Favorite ? 1 : 0)
                .ThenByDescending(i => AccessTracker.GetLastAccess(i.Id))
                .ThenByDescending(i => i.RevisionDate)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase);

            var contextWithoutRecent = recentItem != null
                ? contextMatches.Where(i => i.Id != recentItem.Id)
                : contextMatches;

            return recentItem != null
                ? [recentItem, .. contextWithoutRecent, .. remainder]
                : [.. contextMatches, .. remainder];
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
    await RunCliAsync("sync", CliTimeoutMs, "Syncing complete.");
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

  internal const string BwCliPath = "bw";

  private ICliProcess StartProcess(string args)
  {
    var psi = new ProcessStartInfo(BwCliPath, args)
    {
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      RedirectStandardInput = true,
      CreateNoWindow = true,
    };

    if (_sessionKey != null)
      psi.Environment["BW_SESSION"] = _sessionKey;

    var process = _processFactory(psi);
    process.StandardInput.Close();
    _runningProcesses[process.Id] = process;
    process.Exited += (_, _) => _runningProcesses.TryRemove(process.Id, out _);
    process.EnableRaisingEvents = true;
    return process;
  }

  private void KillAllRunning()
  {
    foreach (var kvp in _runningProcesses)
    {
      try { kvp.Value.Kill(true); } catch { }
      _runningProcesses.TryRemove(kvp.Key, out _);
    }
  }

  private static async Task<(string Content, bool Detected)> ReadStreamWithPromptDetectionAsync(System.IO.StreamReader reader, string[] prompts, CancellationToken token)
  {
    var sb = new System.Text.StringBuilder();
    var buffer = new char[256];
    while (true)
    {
      var count = await reader.ReadAsync(buffer.AsMemory(), token);
      if (count == 0) break;
      sb.Append(buffer, 0, count);
      var text = sb.ToString();
      foreach (var prompt in prompts)
      {
        if (text.Contains(prompt, StringComparison.OrdinalIgnoreCase))
          return (text, true);
      }
    }
    return (sb.ToString(), false);
  }

  private static async Task<(string Content, bool Detected)> ReadUntilPromptAsync(System.IO.StreamReader reader, string prompt, CancellationToken token)
  {
    var sb = new System.Text.StringBuilder();
    var buffer = new char[256];
    while (true)
    {
      var count = await reader.ReadAsync(buffer.AsMemory(), token);
      if (count == 0) break;
      sb.Append(buffer, 0, count);
      if (sb.ToString().Contains(prompt, StringComparison.OrdinalIgnoreCase))
        return (sb.ToString(), true);
    }
    return (sb.ToString(), false);
  }

  private const int CliTimeoutMs = 30_000;
  private const int CliLogoutTimeoutMs = 3_000;

  // Reads stdout line-by-line. Returns immediately on the first valid JSON line (object or array),
  // or on the first line matching earlyExitText. Falls back to returning all accumulated stdout
  // if neither is found. Concurrent stderr session detection is active throughout.
  // The process is killed on early exit so that Process.Dispose() returns immediately.
  private async Task<string> RunCliAsync(string args, int timeoutMs = CliTimeoutMs, string? earlyExitText = null)
  {
    using var process = StartProcess(args);
    using var cts = new CancellationTokenSource(timeoutMs);
    try
    {
      var stderrTask = ReadStderrWithSessionDetectionAsync(process, cts.Token);
      var fallbackLines = new System.Text.StringBuilder();
      string? line;
      while ((line = await process.StandardOutput.ReadLineAsync(cts.Token)) != null)
      {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
          try
          {
            JsonNode.Parse(trimmed);
            _ = stderrTask.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            try { process.Kill(true); } catch { }
            return trimmed;
          }
          catch (System.Text.Json.JsonException) { }
        }
        if (earlyExitText != null && line.Contains(earlyExitText, StringComparison.OrdinalIgnoreCase))
        {
          _ = stderrTask.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
          try { process.Kill(true); } catch { }
          return line;
        }
        fallbackLines.AppendLine(line);
      }

      var (stderr, sessionInvalid) = await stderrTask;

      if (sessionInvalid || IsSessionInvalidError(stderr.Trim()))
      {
        HandleInvalidSession();
        throw new InvalidOperationException("Session expired — vault is locked");
      }

      return fallbackLines.ToString();
    }
    catch (OperationCanceledException)
    {
      try { process.Kill(); } catch { }
      throw new TimeoutException($"Bitwarden CLI timed out after {timeoutMs / 1000}s running: bw {args.Split(' ')[0]}");
    }
  }

  private static async Task<(string Content, bool SessionInvalid)> ReadStderrWithSessionDetectionAsync(ICliProcess process, CancellationToken token)
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
