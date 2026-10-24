using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class ItemViewModel : ObservableObject, IDisposable
{
  private readonly PipeChannel _channel;
  private readonly DispatcherTimer _totpTimer;
  private FieldViewModel? _totpField;
  private bool _totpFetching;

  public ItemViewModel(string id, PipeChannel channel)
  {
    Id = id;
    _channel = channel;
    _totpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    _totpTimer.Tick += (_, _) => _ = TickAsync();
  }

  public string Id { get; }

  public ObservableCollection<FieldGroupViewModel> Groups { get; } = [];

  [ObservableProperty]
  public partial string Title { get; set; } = "Loading";

  [ObservableProperty]
  public partial string? Subtitle { get; set; }

  [ObservableProperty]
  public partial string TypeGlyph { get; set; } = "";

  [ObservableProperty]
  public partial bool IsLoading { get; set; } = true;

  [ObservableProperty]
  public partial bool RepromptRequired { get; set; }

  [ObservableProperty]
  public partial string? ErrorMessage { get; set; }

  [ObservableProperty]
  public partial string? StatusMessage { get; set; }

  public async Task LoadAsync()
  {
    IsLoading = true;
    try
    {
      Apply(IpcJson.Read(await _channel.RequestAsync(Ops.GetItem, ItemPayload), IpcJson.Default.ItemDto));
      ErrorMessage = null;
    }
    catch (Exception ex) when (ex is IpcException or IOException)
    {
      ErrorMessage = Describe(ex);
    }
    finally
    {
      IsLoading = false;
    }

    if (_totpField is not null && !RepromptRequired)
      await RefreshTotpAsync();
  }

  internal void Apply(ItemDto item)
  {
    Title = item.Name;
    Subtitle = string.Join("  ·  ", new[] { item.FolderName, item.OrganizationName }.Where(s => !string.IsNullOrEmpty(s)));
    TypeGlyph = GlyphFor(item.Type);
    RepromptRequired = item.RepromptRequired;

    Groups.Clear();
    foreach (var group in item.Fields.GroupBy(f => f.Group))
      Groups.Add(new FieldGroupViewModel(group.Key, [.. group.Select(f => new FieldViewModel(this, f))]));

    var history = new List<FieldViewModel>();
    if (item.CreationDate is { } created)
      history.Add(HistoryField("Created", created));
    history.Add(HistoryField("Last edited", item.RevisionDate));
    if (item.PasswordRevisionDate is { } passwordUpdated)
      history.Add(HistoryField("Password updated", passwordUpdated));
    if (!item.RepromptRequired)
      Groups.Add(new FieldGroupViewModel("Item history", history));

    _totpField = Groups.SelectMany(g => g.Fields).FirstOrDefault(f => f.IsTotp);
    if (_totpField is null)
      _totpTimer.Stop();
  }

  public async Task ToggleRevealAsync(FieldViewModel field)
  {
    if (field.IsRevealed)
    {
      field.IsRevealed = false;
      field.RevealedValue = null;
      return;
    }

    var (ok, reply) = await SendAsync(Ops.Reveal, FieldPayload(field.Key));
    if (ok)
    {
      field.RevealedValue = IpcJson.Read(reply, IpcJson.Default.SecretResponse).Value;
      field.IsRevealed = true;
    }
  }

  public async Task CopyAsync(FieldViewModel field)
  {
    if ((await SendAsync(Ops.Copy, FieldPayload(field.Key))).Ok)
      StatusMessage = $"Copied {field.Label}";
  }

  public async Task OpenUriAsync(FieldViewModel field)
  {
    if (field.Key.StartsWith(ItemFields.UriPrefix, StringComparison.Ordinal)
      && int.TryParse(field.Key.AsSpan(ItemFields.UriPrefix.Length), CultureInfo.InvariantCulture, out var index))
      await SendAsync(Ops.OpenUri, IpcJson.ToElement(new UriRequest(Id, index), IpcJson.Default.UriRequest));
  }

  public async Task<bool> VerifyRepromptAsync(string password)
  {
    var (ok, reply) = await SendAsync(Ops.VerifyReprompt, IpcJson.ToElement(new RepromptRequest(Id, password), IpcJson.Default.RepromptRequest));
    if (!ok)
      return false;

    var result = IpcJson.Read(reply, IpcJson.Default.RepromptResponse);
    if (!result.Verified)
    {
      ErrorMessage = result.CooldownSeconds > 0
        ? $"Too many failed attempts. Try again in {result.CooldownSeconds}s."
        : "Incorrect master password.";
      return false;
    }

    ErrorMessage = null;
    await LoadAsync();
    return true;
  }

  internal async Task TickAsync()
  {
    if (_totpField is null)
      return;
    if (_totpField.TotpRemaining > 1)
      _totpField.TotpRemaining--;
    else
      await RefreshTotpAsync();
  }

  private async Task RefreshTotpAsync()
  {
    if (_totpField is not { } field || _totpFetching)
      return;
    _totpFetching = true;
    try
    {
      var (ok, reply) = await SendAsync(Ops.GetTotp, ItemPayload);
      if (!ok)
      {
        _totpTimer.Stop();
        return;
      }

      var totp = IpcJson.Read(reply, IpcJson.Default.TotpResponse);
      field.TotpPeriod = totp.Period;
      field.TotpRemaining = totp.Remaining;
      field.TotpCode = totp.Code;
      _totpTimer.Start();
    }
    finally
    {
      _totpFetching = false;
    }
  }

  private async Task<(bool Ok, JsonElement? Reply)> SendAsync(string op, JsonElement payload)
  {
    try
    {
      var reply = await _channel.RequestAsync(op, payload);
      StatusMessage = null;
      return (true, reply);
    }
    catch (Exception ex) when (ex is IpcException or IOException)
    {
      if (ex is IpcException { Code: IpcErrors.Reprompt })
        RepromptRequired = true;
      ErrorMessage = Describe(ex);
      return (false, null);
    }
  }

  private JsonElement ItemPayload => IpcJson.ToElement(new ItemRequest(Id), IpcJson.Default.ItemRequest);

  private JsonElement FieldPayload(string field) => IpcJson.ToElement(new FieldRequest(Id, field), IpcJson.Default.FieldRequest);

  private static FieldViewModel HistoryField(string label, DateTime value) =>
    new(null, new FieldDto(label, label, "Item history", value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), false), canCopy: false);

  internal static string Describe(Exception ex) => ex switch
  {
    IpcException { Code: IpcErrors.Reprompt } => "Master password verification is required.",
    IpcException { Code: IpcErrors.Locked } => "The vault is locked. Unlock it in Command Palette.",
    IpcException { Code: IpcErrors.NotFound } => "This item is no longer in the vault.",
    IpcException { Code: IpcErrors.Invalid } => "The request was not valid.",
    IOException => "The connection to Command Palette was lost.",
    _ => "Something went wrong.",
  };

  internal static string GlyphFor(int type) => type switch
  {
    1 => "",
    2 => "",
    3 => "",
    4 => "",
    5 => "",
    _ => "",
  };

  public void Dispose() => _totpTimer.Stop();
}
