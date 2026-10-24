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
  private readonly Func<Task<VaultExtras?>> _loadExtras;
  private readonly SemaphoreSlim _extrasGate = new(1, 1);
  private readonly Lock _extrasLock = new();
  private VaultExtras? _extras;
  private (DateTime Refresh, int Writes) _extrasStamp;

  public CompanionOps(
    BitwardenCliService service,
    Func<string, Task<bool>>? verifyPassword = null,
    Action<ProtectedSecret>? copySecret = null,
    Action<string, bool>? copyText = null,
    Action<string>? openUrl = null,
    Func<Task<VaultExtras?>>? loadExtras = null)
  {
    _service = service;
    _verifyPassword = verifyPassword ?? service.VerifyMasterPasswordAsync;
    _copySecret = copySecret ?? SecureClipboardService.CopySensitive;
    _copyText = copyText ?? CopyText;
    _openUrl = openUrl ?? OpenUrl;
    _loadExtras = loadExtras ?? service.LoadVaultExtrasAsync;
    _service.StatusChanged += RaiseIfLocked;
    _service.AutoLocked += RaiseIfLocked;
    _service.CacheUpdated += RaiseVaultChanged;
  }

  internal event Action? Locked;

  internal event Action? VaultChanged;

  internal void RaiseIfLocked()
  {
    if (_service.IsUnlocked)
      return;
    ClearExtras();
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
          return IpcJson.ToElement(await VerifyRepromptAsync(verify.Id, verify.Password, verify.Op, verify.Payload), IpcJson.Default.RepromptResponse);
        case Ops.ListItems:
          return IpcJson.ToElement(await ListItemsAsync(), IpcJson.Default.ItemListResponse);
        case Ops.ListFilters:
          return IpcJson.ToElement(await ListFiltersAsync(), IpcJson.Default.FiltersResponse);
        case Ops.CreateItem:
          return IpcJson.ToElement(await CreateItemAsync(IpcJson.Read(payload, IpcJson.Default.EditableItem)), IpcJson.Default.SavedResponse);
        case Ops.UpdateItem:
          return IpcJson.ToElement(await UpdateItemAsync(IpcJson.Read(payload, IpcJson.Default.EditableItem)), IpcJson.Default.SavedResponse);
        case Ops.DeleteItem or Ops.RestoreItem or Ops.ArchiveItem or Ops.MoveItem or Ops.SetCollections:
          return await WriteAsync(op, IpcJson.Read(payload, IpcJson.Default.WriteRequest)) is { } saved ? IpcJson.ToElement(saved, IpcJson.Default.SavedResponse) : null;
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
      throw new IpcException(ex.Error switch { VaultWriteError.Conflict => IpcErrors.Conflict, VaultWriteError.NotFound => IpcErrors.NotFound, _ => IpcErrors.Failed }, ex);
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
          await CopyPasswordHistoryAsync(copy.Id, copy.Field, copy.RevisionDate, IsVerified(copy.Id, verifiedId));
        else
          Copy(copy.Id, copy.Field, copy.RevisionDate, IsVerified(copy.Id, verifiedId));
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
      default:
        throw new IpcException(IpcErrors.Invalid);
    }
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
      item.DeletedDate != null,
      item.ArchivedDate != null);
  }

  internal async Task<ItemListResponse> ListItemsAsync()
  {
    var extras = await EnsureExtrasAsync();
    var showIcons = _service.ShowWebsiteIcons;
    return new ItemListResponse(_service.GetCachedItems().Concat(extras.Items).Select(i => Summarize(i, showIcons)).ToList());
  }

  internal async Task<FiltersResponse> ListFiltersAsync()
  {
    var extras = await EnsureExtrasAsync();
    return new FiltersResponse(
      ToRefs(_service.GetFolders()),
      extras.Collections.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).Select(c => new FilterRef(c.Id, c.Name, c.OrganizationId)).ToList(),
      ToRefs(_service.GetOrganizations()));
  }

  private static List<FilterRef> ToRefs(Dictionary<string, string> names) =>
    [.. names.OrderBy(n => n.Value, StringComparer.CurrentCultureIgnoreCase).Select(n => new FilterRef(n.Key, n.Value))];

  internal static ItemSummary Summarize(BitwardenItem item, bool showIcons = false) => new(
    item.Id,
    (int)item.Type,
    item.Name,
    SubtextOf(item),
    item.Favorite,
    item.FolderId,
    item.OrganizationId,
    item.CollectionIds,
    item.DeletedDate != null,
    item.ArchivedDate != null,
    item.RevisionDate,
    item.HasTotp,
    item.Reprompt == 1,
    item.Notes != null,
    item.Uris.Select(u => u.Host).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
    showIcons && item.Type == BitwardenItemType.Login && item.FirstHost is { Length: > 0 } host ? FaviconService.TryGetCachedIconPath(host) : null);

  // The palette subtitle of a secure note is the first line of its notes, which are secret.
  private static string? SubtextOf(BitwardenItem item) =>
    item.Type == BitwardenItemType.SecureNote || string.IsNullOrEmpty(item.Subtitle) ? null : item.Subtitle;

  private async Task<VaultExtras> EnsureExtrasAsync()
  {
    if (!_service.IsUnlocked)
      throw new IpcException(IpcErrors.Locked);
    await _extrasGate.WaitAsync();
    try
    {
      var stamp = (_service.LastRefresh, _service.WriteVersion);
      lock (_extrasLock)
      {
        if (_extras is { } current && _extrasStamp == stamp)
          return current;
      }

      VaultExtras? loaded;
      try
      {
        loaded = await _loadExtras();
      }
      catch (Exception ex) when (ex is not IpcException && _service.IsUnlocked)
      {
        DebugLogService.Log("Companion", $"Trash, archive and collections load failed: {ex.GetType().Name}");
        return new VaultExtras([], []);
      }

      lock (_extrasLock)
      {
        if (loaded is null || !_service.IsUnlocked)
        {
          loaded?.Items.ForEach(i => i.DisposeSecrets());
          throw new IpcException(IpcErrors.Locked);
        }

        _extras = loaded;
        _extrasStamp = stamp;
        return loaded;
      }
    }
    finally
    {
      _extrasGate.Release();
    }
  }

  private void ClearExtras()
  {
    lock (_extrasLock)
    {
      _extras?.Items.ForEach(i => i.DisposeSecrets());
      _extras = null;
    }
  }

  private BitwardenItem? FindExtra(string id)
  {
    lock (_extrasLock)
      return _extras?.Items.Find(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
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

  internal void Copy(string? id, string? field, DateTime? revisionDate = null, bool verified = false)
  {
    var item = FindGranted(id, revisionDate, verified);
    if (field == ItemFields.Totp)
    {
      _copyText(ComputeTotp(item).Code, true);
      return;
    }

    var (secret, text) = Resolve(item, field);
    if (secret != null)
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

  internal async Task<RepromptResponse> VerifyRepromptAsync(string? id, string? password, string? op = null, JsonElement? payload = null)
  {
    var item = Find(id);
    if (item.Reprompt == 1)
    {
      var cooldown = RepromptPage.GetCooldownSecondsRemaining();
      if (cooldown > 0 || string.IsNullOrEmpty(password))
        return new RepromptResponse(false, cooldown);

      if (!await _verifyPassword(password))
      {
        RepromptPage.RecordFailure();
        return new RepromptResponse(false, RepromptPage.GetCooldownSecondsRemaining());
      }

      RepromptPage.RecordVerification(item.Id);
    }

    return new RepromptResponse(true, 0, op is null ? null : await DispatchAsync(op, payload, item.Id));
  }

  private BitwardenItem Find(string? id)
  {
    if (!Guid.TryParse(id, out _))
      throw new IpcException(IpcErrors.Invalid);
    if (!_service.IsUnlocked)
      throw new IpcException(IpcErrors.Locked);
    return _service.GetCachedItem(id) ?? FindExtra(id) ?? throw new IpcException(IpcErrors.NotFound);
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
    _extrasGate.Dispose();
  }
}
