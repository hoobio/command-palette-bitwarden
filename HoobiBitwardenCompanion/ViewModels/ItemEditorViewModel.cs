using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed record Choice(string? Value, string Label)
{
  public override string ToString() => Label;
}

internal sealed partial class ItemEditorViewModel : ObservableObject
{
  public static readonly Choice[] MatchChoices =
    [new(null, "Default"), new("0", "Base domain"), new("1", "Host"), new("2", "Starts with"), new("3", "Exact"), new("4", "Regular expression"), new("5", "Never")];

  private readonly IReadOnlyList<FilterRef> _collections;
  private EditableItem _source;
  private string _original;

  public ItemEditorViewModel(EditableItem item, FiltersResponse filters)
  {
    _source = item;
    _collections = filters.Collections;
    Type = item.Type;
    Folders = [new Choice(null, "No folder"), .. filters.Folders.Select(f => new Choice(f.Id, f.Name))];
    Owners = [new Choice(null, "Me"), .. filters.Organizations.Select(o => new Choice(o.Id, o.Name))];
    LinkTargets = ItemRules.LinkTargets.TryGetValue(item.Type, out var targets) ? [.. targets.Select(t => new Choice(t.Id.ToString(CultureInfo.InvariantCulture), t.Label))] : [];

    Name = item.Name;
    SelectedFolder = Folders.FirstOrDefault(f => f.Value == item.FolderId) ?? Folders[0];
    Favorite = item.Favorite;
    Reprompt = item.Reprompt;
    Notes = item.Notes;
    Username = item.Login?.Username;
    Password = item.Login?.Password;
    Totp = item.Login?.Totp;
    Uris = [.. (item.Login?.Uris ?? []).Select(u => new UriEditViewModel(this, u.Uri, u.Match))];
    CardholderName = item.Card?.CardholderName;
    CardBrand = item.Card?.Brand;
    CardNumber = item.Card?.Number;
    CardExpMonth = item.Card?.ExpMonth;
    CardExpYear = item.Card?.ExpYear;
    CardCode = item.Card?.Code;
    var identity = item.Identity;
    Title = identity?.Title;
    FirstName = identity?.FirstName;
    MiddleName = identity?.MiddleName;
    LastName = identity?.LastName;
    Address1 = identity?.Address1;
    Address2 = identity?.Address2;
    Address3 = identity?.Address3;
    City = identity?.City;
    State = identity?.State;
    PostalCode = identity?.PostalCode;
    Country = identity?.Country;
    Company = identity?.Company;
    Email = identity?.Email;
    Phone = identity?.Phone;
    Ssn = identity?.Ssn;
    IdentityUsername = identity?.Username;
    PassportNumber = identity?.PassportNumber;
    LicenseNumber = identity?.LicenseNumber;
    PrivateKey = item.SshKey?.PrivateKey;
    PublicKey = item.SshKey?.PublicKey;
    Fingerprint = item.SshKey?.Fingerprint;
    Fields = [.. item.Fields.Select(f => new CustomFieldViewModel(this, f))];

    CanChangeOwner = item.Id is null || item.OrganizationId is null;
    SelectedOwner = Owners.FirstOrDefault(o => o.Value == item.OrganizationId) ?? Owners[0];
    RebuildCollections(item.CollectionIds);
    _original = Serialize(ToDto());
  }

  public static EditableItem Blank(int type) => new(
    null, type, string.Empty, null, null, [], false, false, null, null, [],
    Login: type == 1 ? new LoginEdit(null, null, null, []) : null,
    Card: type == 3 ? new CardEdit(null, null, null, null, null, null) : null,
    Identity: type == 4 ? new IdentityEdit(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null) : null,
    SshKey: type == 5 ? new SshKeyEdit(null, null, null) : null);

  public int Type { get; }

  public bool IsLogin => Type == 1;

  public bool IsCard => Type == 3;

  public bool IsIdentity => Type == 4;

  public bool IsSshKey => Type == 5;

  public bool CanLink => LinkTargets.Count > 0;

  public bool HasOrganizations => Owners.Count > 1;

  public bool CanChangeOwner { get; }

  public IReadOnlyList<Choice> Folders { get; }

  public IReadOnlyList<Choice> Owners { get; }

  public IReadOnlyList<Choice> LinkTargets { get; }

  public ObservableCollection<UriEditViewModel> Uris { get; }

  public ObservableCollection<CustomFieldViewModel> Fields { get; }

  public ObservableCollection<CollectionChoiceViewModel> Collections { get; } = [];

  [ObservableProperty]
  public partial string Name { get; set; } = string.Empty;

  [ObservableProperty]
  public partial Choice? SelectedFolder { get; set; }

  [ObservableProperty]
  public partial Choice? SelectedOwner { get; set; }

  [ObservableProperty]
  public partial bool Favorite { get; set; }

  [ObservableProperty]
  public partial bool Reprompt { get; set; }

  [ObservableProperty]
  public partial string? Notes { get; set; }

  [ObservableProperty]
  public partial string? Username { get; set; }

  [ObservableProperty]
  public partial string? Password { get; set; }

  [ObservableProperty]
  public partial string? Totp { get; set; }

  [ObservableProperty]
  public partial string? CardholderName { get; set; }

  [ObservableProperty]
  public partial string? CardBrand { get; set; }

  [ObservableProperty]
  public partial string? CardNumber { get; set; }

  [ObservableProperty]
  public partial string? CardExpMonth { get; set; }

  [ObservableProperty]
  public partial string? CardExpYear { get; set; }

  [ObservableProperty]
  public partial string? CardCode { get; set; }

  [ObservableProperty]
  public partial string? Title { get; set; }

  [ObservableProperty]
  public partial string? FirstName { get; set; }

  [ObservableProperty]
  public partial string? MiddleName { get; set; }

  [ObservableProperty]
  public partial string? LastName { get; set; }

  [ObservableProperty]
  public partial string? Address1 { get; set; }

  [ObservableProperty]
  public partial string? Address2 { get; set; }

  [ObservableProperty]
  public partial string? Address3 { get; set; }

  [ObservableProperty]
  public partial string? City { get; set; }

  [ObservableProperty]
  public partial string? State { get; set; }

  [ObservableProperty]
  public partial string? PostalCode { get; set; }

  [ObservableProperty]
  public partial string? Country { get; set; }

  [ObservableProperty]
  public partial string? Company { get; set; }

  [ObservableProperty]
  public partial string? Email { get; set; }

  [ObservableProperty]
  public partial string? Phone { get; set; }

  [ObservableProperty]
  public partial string? Ssn { get; set; }

  [ObservableProperty]
  public partial string? IdentityUsername { get; set; }

  [ObservableProperty]
  public partial string? PassportNumber { get; set; }

  [ObservableProperty]
  public partial string? LicenseNumber { get; set; }

  [ObservableProperty]
  public partial string? PrivateKey { get; set; }

  [ObservableProperty]
  public partial string? PublicKey { get; set; }

  [ObservableProperty]
  public partial string? Fingerprint { get; set; }

  [ObservableProperty]
  public partial string? ValidationMessage { get; set; }

  internal string? Id => _source.Id;

  internal string? OriginalOrganizationId => _source.OrganizationId;

  internal IReadOnlyList<string> OriginalCollectionIds => _source.CollectionIds;

  public bool IsDirty => Serialize(ToDto()) != _original;

  partial void OnSelectedOwnerChanged(Choice? value) => RebuildCollections([.. Collections.Where(c => c.IsChecked).Select(c => c.Id)]);

  public bool HasCollections => Collections.Count > 0;

  private void RebuildCollections(IReadOnlyList<string> checkedIds)
  {
    Collections.Clear();
    foreach (var collection in _collections.Where(c => SelectedOwner?.Value is { } org && c.OrganizationId == org))
      Collections.Add(new CollectionChoiceViewModel(collection.Id, collection.Name) { IsChecked = checkedIds.Contains(collection.Id, StringComparer.OrdinalIgnoreCase) });
    OnPropertyChanged(nameof(HasCollections));
  }

  internal void MarkSaved(string id, DateTime revision, string? organizationId, IReadOnlyList<string> collectionIds)
  {
    _source = ToDto() with { Id = id, RevisionDate = revision, OrganizationId = organizationId, CollectionIds = collectionIds };
    _original = Serialize(_source);
  }

  internal void MarkMoved(DateTime revision)
  {
    _source = ToDto() with { RevisionDate = revision };
    _original = Serialize(_source);
  }

  public EditableItem ToDto() => new(
    _source.Id,
    Type,
    Name,
    SelectedFolder?.Value,
    SelectedOwner?.Value,
    HasCollections || SelectedOwner?.Value != _source.OrganizationId ? [.. Collections.Where(c => c.IsChecked).Select(c => c.Id)] : _source.CollectionIds,
    Favorite,
    Reprompt,
    Empty(Notes),
    _source.RevisionDate,
    [.. Fields.Select(f => f.ToDto())],
    Login: IsLogin ? new LoginEdit(Empty(Username), Empty(Password), Empty(Totp), [.. Uris.Select(u => new UriEdit(Empty(u.Uri?.Trim()), u.Match))]) : null,
    Card: IsCard ? new CardEdit(Empty(CardholderName), Empty(CardBrand), Empty(CardNumber), Empty(CardExpMonth), Empty(CardExpYear), Empty(CardCode)) : null,
    Identity: IsIdentity
      ? new IdentityEdit(Empty(Title), Empty(FirstName), Empty(MiddleName), Empty(LastName), Empty(Address1), Empty(Address2), Empty(Address3), Empty(City), Empty(State),
        Empty(PostalCode), Empty(Country), Empty(Company), Empty(Email), Empty(Phone), Empty(Ssn), Empty(IdentityUsername), Empty(PassportNumber), Empty(LicenseNumber))
      : null,
    SshKey: IsSshKey ? new SshKeyEdit(Empty(PrivateKey), Empty(PublicKey), Empty(Fingerprint)) : null);

  public string? Validate()
  {
    ValidationMessage = Name.Trim().Length == 0 ? "Name is required."
      : IsCard && !ItemRules.IsValidMonth(Empty(CardExpMonth)) ? "Expiration month must be a number from 1 to 12."
      : IsSshKey && (Empty(PrivateKey) is null || Empty(PublicKey) is null || Empty(Fingerprint) is null) ? "An SSH key needs the private key, public key and fingerprint."
      : Fields.Any(f => f.IsAdded && f.Name.Trim().Length == 0) ? "Every new custom field needs a name."
      : Fields.Any(f => f.IsLinked && f.SelectedLink is null) ? "Choose the linked field for every linked custom field."
      : SelectedOwner?.Value is { } org && (HasCollections || org != OriginalOrganizationId) && !Collections.Any(c => c.IsChecked) ? "Choose at least one collection for the organisation."
      : null;
    return ValidationMessage;
  }

  [RelayCommand]
  private void AddUri() => Uris.Add(new UriEditViewModel(this, string.Empty, null));

  [RelayCommand]
  private void AddField(string type) => Fields.Add(new CustomFieldViewModel(this, new CustomFieldEdit(string.Empty, type == "2" ? "false" : null, int.Parse(type, CultureInfo.InvariantCulture))) { IsAdded = true });

  [RelayCommand]
  private void ImportSshKey()
  {
    if (SshKeyParser.TryParse(PrivateKey, out var publicKey, out var fingerprint))
    {
      PublicKey = publicKey;
      Fingerprint = fingerprint;
      ValidationMessage = null;
    }
    else
    {
      ValidationMessage = "Paste an OpenSSH private key (BEGIN OPENSSH PRIVATE KEY) into the private key field.";
    }
  }

  internal static void Move<T>(ObservableCollection<T> list, T item, int delta)
  {
    var index = list.IndexOf(item);
    var target = index + delta;
    if (index >= 0 && target >= 0 && target < list.Count)
      list.Move(index, target);
  }

  private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;

  private static string Serialize(EditableItem item) => JsonSerializer.Serialize(item, IpcJson.Default.EditableItem);
}

internal sealed partial class UriEditViewModel(ItemEditorViewModel owner, string? uri, int? match) : ObservableObject
{
  [ObservableProperty]
  public partial string? Uri { get; set; } = uri;

  [ObservableProperty]
  public partial Choice SelectedMatch { get; set; } = ItemEditorViewModel.MatchChoices.FirstOrDefault(c => c.Value == match?.ToString(CultureInfo.InvariantCulture)) ?? ItemEditorViewModel.MatchChoices[0];

  public int? Match => SelectedMatch.Value is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : null;

  [RelayCommand]
  private void Remove() => owner.Uris.Remove(this);

  [RelayCommand]
  private void MoveUp() => ItemEditorViewModel.Move(owner.Uris, this, -1);

  [RelayCommand]
  private void MoveDown() => ItemEditorViewModel.Move(owner.Uris, this, 1);
}

internal sealed partial class CustomFieldViewModel : ObservableObject
{
  private readonly ItemEditorViewModel _owner;
  private readonly bool _originalBool;

  public CustomFieldViewModel(ItemEditorViewModel owner, CustomFieldEdit field)
  {
    _owner = owner;
    Type = field.Type;
    Name = field.Name;
    Value = field.Value;
    BoolValue = _originalBool = string.Equals(field.Value, "true", StringComparison.OrdinalIgnoreCase);
    SelectedLink = owner.LinkTargets.FirstOrDefault(t => t.Value == field.LinkedId?.ToString(CultureInfo.InvariantCulture));
  }

  public int Type { get; }

  internal bool IsAdded { get; init; }

  public string TypeLabel => Type switch { 1 => "Hidden", 2 => "Boolean", 3 => "Linked", _ => "Text" };

  public bool IsText => Type == 0;

  public bool IsHidden => Type == 1;

  public bool IsBoolean => Type == 2;

  public bool IsLinked => Type == 3;

  public IReadOnlyList<Choice> LinkTargets => _owner.LinkTargets;

  [ObservableProperty]
  public partial string Name { get; set; }

  [ObservableProperty]
  public partial string? Value { get; set; }

  [ObservableProperty]
  public partial bool BoolValue { get; set; }

  [ObservableProperty]
  public partial Choice? SelectedLink { get; set; }

  public CustomFieldEdit ToDto() => Type switch
  {
    2 => new CustomFieldEdit(Name, BoolValue == _originalBool ? Value : BoolValue ? "true" : "false", 2),
    3 => new CustomFieldEdit(Name, null, 3, SelectedLink?.Value is { } link ? int.Parse(link, CultureInfo.InvariantCulture) : null),
    _ => new CustomFieldEdit(Name, string.IsNullOrEmpty(Value) ? null : Value, Type),
  };

  [RelayCommand]
  private void Remove() => _owner.Fields.Remove(this);

  [RelayCommand]
  private void MoveUp() => ItemEditorViewModel.Move(_owner.Fields, this, -1);

  [RelayCommand]
  private void MoveDown() => ItemEditorViewModel.Move(_owner.Fields, this, 1);
}

internal sealed partial class CollectionChoiceViewModel(string id, string name) : ObservableObject
{
  public string Id { get; } = id;

  public string Name { get; } = name;

  [ObservableProperty]
  public partial bool IsChecked { get; set; }
}

internal static class SshKeyParser
{
  private const string Header = "-----BEGIN OPENSSH PRIVATE KEY-----";
  private const string Footer = "-----END OPENSSH PRIVATE KEY-----";
  private static readonly byte[] Magic = "openssh-key-v1\0"u8.ToArray();

  public static bool TryParse(string? privateKey, out string? publicKey, out string? fingerprint)
  {
    publicKey = fingerprint = null;
    var text = privateKey?.Trim();
    if (text is null || !text.StartsWith(Header, StringComparison.Ordinal) || !text.EndsWith(Footer, StringComparison.Ordinal))
      return false;
    try
    {
      var data = Convert.FromBase64String(text[Header.Length..^Footer.Length]);
      if (!data.AsSpan().StartsWith(Magic))
        return false;
      var offset = Magic.Length;
      for (var i = 0; i < 3; i++)
        ReadString(data, ref offset);
      if (ReadUInt32(data, ref offset) < 1)
        return false;
      var blob = ReadString(data, ref offset);
      var blobOffset = 0;
      var keyType = System.Text.Encoding.ASCII.GetString(ReadString(blob, ref blobOffset));
      publicKey = $"{keyType} {Convert.ToBase64String(blob)}";
      fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');
      return true;
    }
    catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or IndexOutOfRangeException)
    {
      publicKey = fingerprint = null;
      return false;
    }
  }

  private static uint ReadUInt32(byte[] data, ref int offset)
  {
    var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    offset += 4;
    return value;
  }

  private static byte[] ReadString(byte[] data, ref int offset)
  {
    var length = (int)ReadUInt32(data, ref offset);
    var value = data.AsSpan(offset, length).ToArray();
    offset += length;
    return value;
  }
}
