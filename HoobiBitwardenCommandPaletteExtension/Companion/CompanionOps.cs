using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Commands;
using HoobiBitwardenCommandPaletteExtension.Helpers;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Pages;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps : IDisposable
{
  private const string LoginGroup = "Login credentials";
  private const string AutofillGroup = "Autofill options";
  private const string CardGroup = "Card details";
  private const string PersonalGroup = "Personal details";
  private const string ContactGroup = "Contact info";
  private const string KeyGroup = "SSH key";
  private const string CustomGroup = "Custom fields";
  private const string NotesGroup = "Additional options";

  private readonly BitwardenCliService _service;
  private readonly Func<string, Task<bool>> _verifyPassword;
  private readonly Action<ProtectedSecret> _copySecret;
  private readonly Action<string, bool> _copyText;
  private readonly Action<string> _openUrl;
  private readonly Func<Task<List<VaultCollection>?>> _loadCollections;
  private readonly Func<Task<bool>> _verifyBiometric;
  private readonly Func<bool> _biometricEnabled;
  private readonly SemaphoreSlim _collectionsGate = new(1, 1);
  private List<VaultCollection>? _collections;
  private (DateTime Refresh, int Writes) _collectionsStamp;

  public CompanionOps(
    BitwardenCliService service,
    Func<string, Task<bool>>? verifyPassword = null,
    Action<ProtectedSecret>? copySecret = null,
    Action<string, bool>? copyText = null,
    Action<string>? openUrl = null,
    Func<Task<List<VaultCollection>?>>? loadCollections = null,
    Func<Task<bool>>? verifyBiometric = null,
    Func<bool>? biometricEnabled = null)
  {
    _service = service;
    _verifyBiometric = verifyBiometric ?? (async () => (await service.VerifyWithBiometricsAsync()).Success);
    _biometricEnabled = biometricEnabled ?? (() => service.Settings?.UseDesktopIntegration.Value == true);
    _verifyPassword = verifyPassword ?? service.VerifyMasterPasswordAsync;
    _copySecret = copySecret ?? SecureClipboardService.CopySensitive;
    _copyText = copyText ?? CopyText;
    _openUrl = openUrl ?? OpenUrl;
    _loadCollections = loadCollections ?? service.LoadCollectionsAsync;
    _service.StatusChanged += RaiseIfLocked;
    _service.AutoLocked += RaiseIfLocked;
    _service.CacheUpdated += RaiseVaultChanged;
    _service.WarmupCompleted += RaisePrewarmNeeded;
    _service.StatusChanged += RaisePrewarmNeeded;
    if (_service.Settings is { } settings)
      settings.Settings.SettingsChanged += RaiseSettingsChanged;
  }

  internal event Action? PrewarmNeeded;

  private void RaisePrewarmNeeded() => PrewarmNeeded?.Invoke();

  internal bool IsUnlocked => _service.IsUnlocked;

  internal event Action? Locked;

  internal event Action? VaultChanged;

  internal void RaiseIfLocked()
  {
    if (_service.IsUnlocked)
      return;
    _collections = null;
    _pendingReprompts.Clear();
    ClearGeneratorHistory();
    Locked?.Invoke();
  }

  internal void RaiseVaultChanged() => VaultChanged?.Invoke();

  public async Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
  {
    try
    {
      switch (op)
      {
        case Ops.VerifyReprompt:
          var verify = IpcJson.Read(payload, IpcJson.Default.RepromptRequest);
          return IpcJson.ToElement(await VerifyRepromptAsync(verify.Id, verify.Password, verify.Op, verify.Payload, verify.Biometric), IpcJson.Default.RepromptResponse);
        case Ops.ApiKeyLogin:
          var login = IpcJson.Read(payload, IpcJson.Default.ApiKeyLoginRequest);
          return IpcJson.ToElement(await ApiKeyLoginAsync(login.ClientId, login.ClientSecret), IpcJson.Default.ApiKeyLoginResponse);
        case Ops.Unlock:
          var unlock = IpcJson.Read(payload, IpcJson.Default.UnlockRequest);
          return IpcJson.ToElement(await UnlockAsync(unlock.Password, unlock.RememberSession, unlock.Biometric), IpcJson.Default.UnlockResponse);
        case Ops.SetServer:
          return IpcJson.ToElement(await SetServerAsync(IpcJson.Read(payload, IpcJson.Default.SetServerRequest)), IpcJson.Default.SetServerResponse);
        case Ops.Login:
          return IpcJson.ToElement(await LoginAsync(IpcJson.Read(payload, IpcJson.Default.LoginRequest)), IpcJson.Default.LoginResponse);
        case Ops.SubmitDeviceVerification:
          return IpcJson.ToElement(await SubmitDeviceVerificationAsync(IpcJson.Read(payload, IpcJson.Default.DeviceVerificationRequest).Code), IpcJson.Default.LoginResponse);
        case Ops.CancelLogin:
          CancelLogin();
          return null;
        case Ops.ConfirmReprompt:
          var confirm = IpcJson.Read(payload, IpcJson.Default.RepromptConfirmRequest);
          return IpcJson.ToElement(await ConfirmRepromptAsync(confirm.Token, confirm.Password, confirm.Biometric), IpcJson.Default.RepromptConfirmResponse);
        case Ops.CancelReprompt:
          CancelReprompt(IpcJson.Read(payload, IpcJson.Default.RepromptCancelRequest).Token);
          return null;
        case Ops.ListFilters:
          return IpcJson.ToElement(await ListFiltersAsync(), IpcJson.Default.FiltersResponse);
        case Ops.CreateItem:
          return IpcJson.ToElement(await CreateItemAsync(IpcJson.Read(payload, IpcJson.Default.EditableItem)), IpcJson.Default.SavedResponse);
        case Ops.Generate:
          return IpcJson.ToElement(Generate(IpcJson.Read(payload, IpcJson.Default.GeneratorOptions)), IpcJson.Default.GeneratedResponse);
        case Ops.ListGeneratorHistory:
          return IpcJson.ToElement(ListGeneratorHistory(), IpcJson.Default.GeneratorHistoryResponse);
        case Ops.CopyGenerated:
          CopyGenerated(IpcJson.Read(payload, IpcJson.Default.HistoryEntryRequest).Id);
          return null;
        case Ops.GetSettings:
          return IpcJson.ToElement(GetSettings(), IpcJson.Default.SettingsResponse);
        case Ops.SetSetting:
          return IpcJson.ToElement(SetSetting(IpcJson.Read(payload, IpcJson.Default.SetSettingRequest)), IpcJson.Default.SetSettingResponse);
        default:
          return await DispatchAsync(op, payload, null);
      }
    }
    catch (ObjectDisposedException ex)
    {
      throw new IpcException(IpcErrors.Locked, ex);
    }
    catch (VaultWriteException ex)
    {
      throw new IpcException(ex.Error switch
      {
        VaultWriteError.Conflict => IpcErrors.Conflict,
        VaultWriteError.NotFound => IpcErrors.NotFound,
        VaultWriteError.Unknown => IpcErrors.Unknown,
        VaultWriteError.Premium => IpcErrors.Premium,
        VaultWriteError.TooLarge => IpcErrors.TooLarge,
        VaultWriteError.NoStorage => IpcErrors.NoStorage,
        _ => IpcErrors.Failed,
      }, ex);
    }
    catch (ArgumentException ex)
    {
      throw new IpcException(IpcErrors.Invalid, ex);
    }
    catch (Exception ex) when (ex is not IpcException)
    {
      DebugLogService.Log("Companion", $"{op} failed: {ex.GetType().Name}");
      throw;
    }
  }

  private async Task<JsonElement?> DispatchAsync(string op, JsonElement? payload, string? verifiedId)
  {
    switch (op)
    {
      case Ops.GetItem:
        var get = IpcJson.Read(payload, IpcJson.Default.ItemRequest);
        return IpcJson.ToElement(GetItem(get.Id, IsVerified(get.Id, verifiedId)), IpcJson.Default.ItemDto);
      case Ops.Reveal:
        var reveal = IpcJson.Read(payload, IpcJson.Default.FieldRequest);
        return IpcJson.ToElement(new SecretResponse(Reveal(reveal.Id, reveal.Field, reveal.RevisionDate, IsVerified(reveal.Id, verifiedId))), IpcJson.Default.SecretResponse);
      case Ops.Copy:
        var copy = IpcJson.Read(payload, IpcJson.Default.FieldRequest);
        if (copy.Field?.StartsWith(ItemFields.HistoryPrefix, StringComparison.Ordinal) == true)
          await CopyPasswordHistoryAsync(copy.Id, copy.Start is null && copy.Length is null ? copy.Field : throw new IpcException(IpcErrors.Invalid), copy.RevisionDate, IsVerified(copy.Id, verifiedId));
        else
          Copy(copy.Id, copy.Field, copy.RevisionDate, IsVerified(copy.Id, verifiedId), copy.Start, copy.Length);
        return null;
      case Ops.GetEditableItem:
        var editable = IpcJson.Read(payload, IpcJson.Default.ItemRequest);
        return IpcJson.ToElement(await GetEditableItemAsync(editable.Id, IsVerified(editable.Id, verifiedId)), IpcJson.Default.EditableItem);
      case Ops.GetPasswordHistory:
        var history = IpcJson.Read(payload, IpcJson.Default.ItemRequest);
        return IpcJson.ToElement(await GetPasswordHistoryAsync(history.Id, IsVerified(history.Id, verifiedId)), IpcJson.Default.PasswordHistoryResponse);
      case Ops.GetTotp:
        var totp = IpcJson.Read(payload, IpcJson.Default.ItemRequest);
        return IpcJson.ToElement(GetTotp(totp.Id, IsVerified(totp.Id, verifiedId)), IpcJson.Default.TotpResponse);
      case Ops.OpenUri:
        var open = IpcJson.Read(payload, IpcJson.Default.UriRequest);
        OpenUri(open.Id, open.Index, open.RevisionDate, IsVerified(open.Id, verifiedId));
        return null;
      case Ops.UpdateItem:
        var update = IpcJson.Read(payload, IpcJson.Default.EditableItem);
        return IpcJson.ToElement(await UpdateItemAsync(update, IsVerified(update.Id, verifiedId)), IpcJson.Default.SavedResponse);
      case Ops.DeleteItem or Ops.ArchiveItem or Ops.MoveItem or Ops.SetCollections:
        var write = IpcJson.Read(payload, IpcJson.Default.WriteRequest);
        return await WriteAsync(op, write, IsVerified(write.Id, verifiedId)) is { } saved ? IpcJson.ToElement(saved, IpcJson.Default.SavedResponse) : null;
      case Ops.DownloadAttachment:
        var download = IpcJson.Read(payload, IpcJson.Default.AttachmentRequest);
        await DownloadAttachmentAsync(download, IsVerified(download.Id, verifiedId));
        return null;
      case Ops.UploadAttachment or Ops.DeleteAttachment:
        var attachment = IpcJson.Read(payload, IpcJson.Default.AttachmentRequest);
        var verified = IsVerified(attachment.Id, verifiedId);
        return IpcJson.ToElement(op == Ops.UploadAttachment ? await UploadAttachmentAsync(attachment, verified) : await DeleteAttachmentAsync(attachment, verified), IpcJson.Default.SavedResponse);
      case Ops.RotatePassword:
        var rotate = IpcJson.Read(payload, IpcJson.Default.RotateRequest);
        return IpcJson.ToElement(await RotatePasswordAsync(rotate, IsVerified(rotate.Id, verifiedId)), IpcJson.Default.SavedResponse);
      default:
        throw new IpcException(IpcErrors.Invalid);
    }
  }

  internal async Task<ApiKeyLoginResponse> ApiKeyLoginAsync(string? clientId, string? clientSecret)
  {
    var id = clientId?.Trim();
    var secret = clientSecret?.Trim();
    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret))
      return new ApiKeyLoginResponse(false, "Enter both the client ID and the client secret");
    var (success, error) = await _service.LoginWithApiKeyAsync(id, secret);
    return new ApiKeyLoginResponse(success, success ? null : error ?? "Login failed");
  }

  private static bool IsVerified(string? id, string? verifiedId) =>
    verifiedId is not null
    && (string.Equals(id, verifiedId, StringComparison.OrdinalIgnoreCase) ? true : throw new IpcException(IpcErrors.Invalid));

  internal ItemDto GetItem(string? id, bool verified = false)
  {
    var item = Find(id);
    var locked = item.Reprompt == 1 && !verified && !RepromptPage.IsWithinGracePeriod(item.Id);
    return new ItemDto(
      item.Id,
      item.Name,
      (int)item.Type,
      _service.GetFolderName(item.FolderId),
      _service.GetOrganizationName(item.OrganizationId),
      item.Favorite,
      item.Reprompt == 1,
      locked,
      item.CreationDate,
      item.RevisionDate,
      item.PasswordRevisionDate,
      locked ? [] : MapFields(item),
      locked ? null : item.Attachments.Select(a => new AttachmentDto(a.Id, a.FileName, a.SizeName)).ToList(),
      _service.ShowWebsiteIcons ? CachedIconPath(item) : null,
      item.Reprompt == 1 && !locked ? RepromptPage.GetGrace(item.Id) : null,
      item.Password is not null,
      item.Reprompt == 1 && _biometricEnabled(),
      locked ? null : item.PasswordHistory?.Count);
  }

  internal async Task<FiltersResponse> ListFiltersAsync()
  {
    var collections = await EnsureCollectionsAsync();
    return new FiltersResponse(
      ToRefs(_service.GetFolders()),
      collections.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).Select(c => new FilterRef(c.Id, c.Name, c.OrganizationId)).ToList(),
      ToRefs(_service.GetOrganizations()));
  }

  private static List<FilterRef> ToRefs(Dictionary<string, string> names) =>
    [.. names.OrderBy(n => n.Value, StringComparer.CurrentCultureIgnoreCase).Select(n => new FilterRef(n.Key, n.Value))];

  private static string? CachedIconPath(BitwardenItem item) =>
    VaultItemHelper.IconCacheKey(item) is { Length: > 0 } key ? FaviconService.TryGetCachedIconPath(key) : null;

  private async Task<List<VaultCollection>> EnsureCollectionsAsync()
  {
    if (!_service.IsUnlocked)
      throw new IpcException(IpcErrors.Locked);
    await _collectionsGate.WaitAsync();
    try
    {
      var stamp = (_service.LastRefresh, _service.WriteVersion);
      if (_collections is { } current && _collectionsStamp == stamp)
        return current;

      List<VaultCollection>? loaded;
      try
      {
        loaded = await _loadCollections();
      }
      catch (Exception ex) when (ex is not IpcException && _service.IsUnlocked)
      {
        DebugLogService.Log("Companion", $"Collections load failed: {ex.GetType().Name}");
        return [];
      }

      if (loaded is null || !_service.IsUnlocked)
        throw new IpcException(IpcErrors.Locked);
      _collections = loaded;
      _collectionsStamp = stamp;
      return loaded;
    }
    finally
    {
      _collectionsGate.Release();
    }
  }

  internal static List<FieldDto> MapFields(BitwardenItem item)
  {
    var fields = new List<FieldDto>();
    switch (item.Type)
    {
      case BitwardenItemType.Login:
        AddText(fields, ItemFields.Username, "Username", LoginGroup, item.Username);
        AddSecret(fields, ItemFields.Password, "Password", LoginGroup, item.Password);
        AddSecret(fields, ItemFields.Totp, "Verification code (TOTP)", LoginGroup, item.TotpSecret);
        for (var i = 0; i < item.Uris.Count; i++)
        {
          fields.Add(new FieldDto(ItemFields.UriPrefix + i.ToString(CultureInfo.InvariantCulture), "Website", AutofillGroup, item.Uris[i].Uri, false,
            CanOpen: VaultItemHelper.ToSafeWebUrl(item.Uris[i].Uri) != null));
        }
        break;
      case BitwardenItemType.Card:
        AddText(fields, ItemFields.CardholderName, "Cardholder name", CardGroup, item.CardholderName);
        AddText(fields, ItemFields.CardBrand, "Brand", CardGroup, item.CardBrand);
        AddSecret(fields, ItemFields.CardNumber, "Number", CardGroup, item.CardNumber);
        AddText(fields, ItemFields.CardExpiration, "Expiration date", CardGroup, item.CardExpiration);
        AddSecret(fields, ItemFields.CardCode, "Security code", CardGroup, item.CardCode);
        break;
      case BitwardenItemType.Identity:
        AddText(fields, ItemFields.IdentityName, "Name", PersonalGroup, item.IdentityFullName);
        AddText(fields, ItemFields.IdentityUsername, "Username", PersonalGroup, item.IdentityUsername);
        AddText(fields, ItemFields.IdentityCompany, "Company", PersonalGroup, item.IdentityCompany);
        AddText(fields, ItemFields.IdentityEmail, "Email", ContactGroup, item.IdentityEmail);
        AddText(fields, ItemFields.IdentityPhone, "Phone", ContactGroup, item.IdentityPhone);
        AddText(fields, ItemFields.IdentityAddress, "Address", ContactGroup, item.IdentityAddress, multiline: true);
        break;
      case BitwardenItemType.SshKey:
        AddSecret(fields, ItemFields.SshPrivateKey, "Private key", KeyGroup, item.SshPrivateKey, multiline: true);
        AddText(fields, ItemFields.SshPublicKey, "Public key", KeyGroup, item.SshPublicKey);
        AddText(fields, ItemFields.SshFingerprint, "Fingerprint", KeyGroup, item.SshFingerprint);
        break;
    }

    var index = 0;
    foreach (var (name, field) in item.CustomFields)
    {
      var key = ItemFields.CustomPrefix + index++.ToString(CultureInfo.InvariantCulture);
      if (field.Secret is { } secret)
        AddSecret(fields, key, name, CustomGroup, secret);
      else
        AddText(fields, key, name, CustomGroup, field.Value);
    }

    AddSecret(fields, ItemFields.Notes, "Notes", NotesGroup, item.Notes, multiline: true);
    return fields;
  }

  internal string Reveal(string? id, string? field, DateTime? revisionDate = null, bool verified = false)
  {
    var item = FindGranted(id, revisionDate, verified);
    if (field == ItemFields.Totp)
      throw new IpcException(IpcErrors.Invalid);
    var (secret, _) = Resolve(item, field);
    return (secret ?? throw new IpcException(IpcErrors.Invalid)).Reveal();
  }

  internal void Copy(string? id, string? field, DateTime? revisionDate = null, bool verified = false, int? start = null, int? length = null)
  {
    var item = FindGranted(id, revisionDate, verified);
    var ranged = start is not null || length is not null;
    if (field == ItemFields.Totp && !ranged)
    {
      _copyText(ComputeTotp(item).Code, true);
      return;
    }

    var (secret, text) = field == ItemFields.Totp ? (null, null) : Resolve(item, field);
    if (ranged)
    {
      if (secret is null || start is not { } from || length is not { } count || from < 0 || count <= 0 || from > secret.Length - count)
        throw new IpcException(IpcErrors.Invalid);
      _copyText(secret.Use(value => new string(value.Slice(from, count))), true);
    }
    else if (secret != null)
      _copySecret(secret);
    else
      _copyText(text!, item.Reprompt == 1);
  }

  internal TotpResponse GetTotp(string? id, bool verified = false) => ComputeTotp(FindGranted(id, null, verified));

  internal void OpenUri(string? id, int index, DateTime? revisionDate = null, bool verified = false)
  {
    var item = FindGranted(id, revisionDate, verified);
    if (index < 0 || index >= item.Uris.Count)
      throw new IpcException(IpcErrors.Invalid);
    _openUrl(VaultItemHelper.ToSafeWebUrl(item.Uris[index].Uri) ?? throw new IpcException(IpcErrors.Invalid));
  }

  internal async Task<RepromptResponse> VerifyRepromptAsync(string? id, string? password, string? op = null, JsonElement? payload = null, bool biometric = false)
  {
    if (biometric && !_biometricEnabled())
      throw new IpcException(IpcErrors.Invalid);
    var item = Find(id);
    if (item.Reprompt == 1)
    {
      var cooldown = RepromptPage.GetCooldownSecondsRemaining();
      if (cooldown > 0 || (!biometric && string.IsNullOrEmpty(password)))
        return new RepromptResponse(false, cooldown);

      if (!await (biometric ? _verifyBiometric() : _verifyPassword(password!)))
      {
        RepromptPage.RecordFailure();
        return new RepromptResponse(false, RepromptPage.GetCooldownSecondsRemaining());
      }

      RepromptPage.RecordVerification(item.Id);
    }

    return new RepromptResponse(true, 0, op is null ? null : await DispatchAsync(op, payload, item.Id), RepromptPage.GetGrace(item.Id));
  }

  private BitwardenItem Find(string? id)
  {
    if (!Guid.TryParse(id, out _))
      throw new IpcException(IpcErrors.Invalid);
    if (!_service.IsUnlocked)
      throw new IpcException(IpcErrors.Locked);
    return _service.GetCachedItem(id) ?? throw new IpcException(IpcErrors.NotFound);
  }

  private BitwardenItem FindGranted(string? id, DateTime? revisionDate, bool verified)
  {
    var item = Find(id);
    if (revisionDate is { } revision && revision != item.RevisionDate)
      throw new IpcException(IpcErrors.Stale);
    return item.Reprompt == 1 && !verified && !RepromptPage.IsWithinGracePeriod(item.Id) ? throw new IpcException(IpcErrors.Reprompt) : item;
  }

  internal static (ProtectedSecret? Secret, string? Text) Resolve(BitwardenItem item, string? field)
  {
    (ProtectedSecret? Secret, string? Text) value = field switch
    {
      ItemFields.Username => (null, item.Username),
      ItemFields.Password => (item.Password, null),
      ItemFields.Notes => (item.Notes, null),
      ItemFields.CardholderName => (null, item.CardholderName),
      ItemFields.CardBrand => (null, item.CardBrand),
      ItemFields.CardNumber => (item.CardNumber, null),
      ItemFields.CardExpiration => (null, item.CardExpiration),
      ItemFields.CardCode => (item.CardCode, null),
      ItemFields.IdentityName => (null, item.IdentityFullName),
      ItemFields.IdentityUsername => (null, item.IdentityUsername),
      ItemFields.IdentityCompany => (null, item.IdentityCompany),
      ItemFields.IdentityEmail => (null, item.IdentityEmail),
      ItemFields.IdentityPhone => (null, item.IdentityPhone),
      ItemFields.IdentityAddress => (null, item.IdentityAddress),
      ItemFields.SshPrivateKey => (item.SshPrivateKey, null),
      ItemFields.SshPublicKey => (null, item.SshPublicKey),
      ItemFields.SshFingerprint => (null, item.SshFingerprint),
      _ when TryIndex(field, ItemFields.UriPrefix, item.Uris.Count, out var uri) => (null, item.Uris[uri].Uri),
      _ when TryIndex(field, ItemFields.CustomPrefix, item.CustomFields.Count, out var custom) =>
        item.CustomFields.ElementAt(custom).Value is var f ? (f.Secret, f.Value) : default,
      _ => throw new IpcException(IpcErrors.Invalid),
    };
    return value.Secret != null || !string.IsNullOrEmpty(value.Text) ? value : throw new IpcException(IpcErrors.NotFound);
  }

  private static bool TryIndex(string? field, string prefix, int count, out int index)
  {
    index = -1;
    return field != null
      && field.StartsWith(prefix, StringComparison.Ordinal)
      && int.TryParse(field.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out index)
      && index < count;
  }

  private static TotpResponse ComputeTotp(BitwardenItem item)
  {
    var secret = item.TotpSecret ?? throw new IpcException(IpcErrors.NotFound);
    return secret.Use(static s =>
    {
      var (code, remaining) = CopyOtpCommand.ComputeCode(s);
      return new TotpResponse(code, remaining, CopyOtpCommand.IsSteamSecret(s) ? 30 : PeriodOf(s));
    });
  }

  private static int PeriodOf(ReadOnlySpan<char> secret)
  {
    var (key, _, period, _) = CopyOtpCommand.ParseTotpSecret(secret);
    CryptographicOperations.ZeroMemory(key);
    return period;
  }

  private static void AddText(List<FieldDto> fields, string key, string label, string group, string? value, bool multiline = false)
  {
    if (!string.IsNullOrEmpty(value))
      fields.Add(new FieldDto(key, label, group, value, false, IsMultiline: multiline));
  }

  private static void AddSecret(List<FieldDto> fields, string key, string label, string group, ProtectedSecret? secret, bool multiline = false)
  {
    if (secret != null)
      fields.Add(new FieldDto(key, label, group, null, true, IsMultiline: multiline));
  }

  private static void CopyText(string text, bool sensitive)
  {
    if (sensitive)
      SecureClipboardService.CopySensitive(text);
    else
      SecureClipboardService.CopyNonSensitive(text);
  }

  private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

  public void Dispose()
  {
    _service.StatusChanged -= RaiseIfLocked;
    _service.AutoLocked -= RaiseIfLocked;
    _service.CacheUpdated -= RaiseVaultChanged;
    _service.WarmupCompleted -= RaisePrewarmNeeded;
    _service.StatusChanged -= RaisePrewarmNeeded;
    if (_service.Settings is { } settings)
      settings.Settings.SettingsChanged -= RaiseSettingsChanged;
    ClearGeneratorHistory();
    _collectionsGate.Dispose();
  }
}
