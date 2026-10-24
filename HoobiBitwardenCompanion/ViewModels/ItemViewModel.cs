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
  private bool _disposed;
  private DateTime _revisionDate;
  private (string Op, JsonElement Payload, Action<JsonElement?> OnSuccess)? _repromptRetry;

  public ItemViewModel(string id, PipeChannel channel)
  {
    Id = id;
    _channel = channel;
    _totpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    _totpTimer.Tick += (_, _) => _ = TickAsync();
  }

  public string Id { get; }

  internal bool IsTotpRunning => _totpTimer.IsEnabled;

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
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      ErrorMessage = Describe(ex);
    }
    finally
    {
      IsLoading = false;
    }

    await RefreshTotpIfShownAsync();
  }

  private async Task RefreshTotpIfShownAsync()
  {
    if (_totpField is not null && !RepromptRequired)
      await RefreshTotpAsync();
  }

  internal void OnLocked()
  {
    _totpTimer.Stop();
    foreach (var field in Groups.SelectMany(g => g.Fields))
      field.Wipe();
    Groups.Clear();
    _totpField = null;
    _repromptRetry = null;
    RepromptRequired = false;
    StatusMessage = null;
    ErrorMessage = Describe(new IpcException(IpcErrors.Locked));
  }

  internal void Apply(ItemDto item)
  {
    _repromptRetry = null;
    _revisionDate = item.RevisionDate;
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

    await RunAsync(Ops.Reveal, FieldPayload(field.Key), reply =>
    {
      field.RevealedValue = IpcJson.Read(reply, IpcJson.Default.SecretResponse).Value;
      field.IsRevealed = true;
    });
  }

  public Task CopyAsync(FieldViewModel field) =>
    RunAsync(Ops.Copy, FieldPayload(field.Key), _ => StatusMessage = $"Copied {field.Label}");

  public async Task OpenUriAsync(FieldViewModel field)
  {
    if (field.Key.StartsWith(ItemFields.UriPrefix, StringComparison.Ordinal)
      && int.TryParse(field.Key.AsSpan(ItemFields.UriPrefix.Length), CultureInfo.InvariantCulture, out var index))
      await RunAsync(Ops.OpenUri, IpcJson.ToElement(new UriRequest(Id, index, _revisionDate), IpcJson.Default.UriRequest), _ => { });
  }

  public async Task<bool> VerifyRepromptAsync(string password)
  {
    var (op, payload, onSuccess) = _repromptRetry ?? (Ops.GetItem, ItemPayload, reply => Apply(IpcJson.Read(reply, IpcJson.Default.ItemDto)));
    var (ok, reply, error) = await SendAsync(Ops.VerifyReprompt, IpcJson.ToElement(new RepromptRequest(Id, password, op, payload), IpcJson.Default.RepromptRequest));
    if (!ok)
    {
      if (error == IpcErrors.Stale)
        await ReloadStaleAsync();
      return false;
    }

    var result = IpcJson.Read(reply, IpcJson.Default.RepromptResponse);
    if (!result.Verified)
    {
      ErrorMessage = result.CooldownSeconds > 0
        ? $"Too many failed attempts. Try again in {result.CooldownSeconds}s."
        : "Incorrect master password.";
      return false;
    }

    ErrorMessage = null;
    _repromptRetry = null;
    RepromptRequired = false;
    onSuccess(result.Result);
    await RefreshTotpIfShownAsync();
    return true;
  }

  private async Task RunAsync(string op, JsonElement payload, Action<JsonElement?> onSuccess)
  {
    var (ok, reply, error) = await SendAsync(op, payload);
    if (ok)
    {
      onSuccess(reply);
      return;
    }

    if (error == IpcErrors.Reprompt)
    {
      _repromptRetry = (op, payload, onSuccess);
      RepromptRequired = true;
    }
    else if (error == IpcErrors.Stale)
    {
      await ReloadStaleAsync();
    }
  }

  private async Task ReloadStaleAsync()
  {
    await LoadAsync();
    StatusMessage = Describe(new IpcException(IpcErrors.Stale));
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
      var (ok, reply, error) = await SendAsync(Ops.GetTotp, ItemPayload);
      if (!ok)
      {
        _totpTimer.Stop();
        field.TotpCode = null;
        if (error == IpcErrors.Reprompt)
          ErrorMessage = null;
        return;
      }

      var totp = IpcJson.Read(reply, IpcJson.Default.TotpResponse);
      field.TotpPeriod = totp.Period;
      field.TotpRemaining = totp.Remaining;
      field.TotpCode = totp.Code;
      if (!_disposed)
        _totpTimer.Start();
    }
    finally
    {
      _totpFetching = false;
    }
  }

  private async Task<(bool Ok, JsonElement? Reply, string? Error)> SendAsync(string op, JsonElement payload)
  {
    try
    {
      var reply = await _channel.RequestAsync(op, payload);
      StatusMessage = null;
      return (true, reply, null);
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      ErrorMessage = Describe(ex);
      return (false, null, (ex as IpcException)?.Code);
    }
  }

  private JsonElement ItemPayload => IpcJson.ToElement(new ItemRequest(Id), IpcJson.Default.ItemRequest);

  private JsonElement FieldPayload(string field) => IpcJson.ToElement(new FieldRequest(Id, field, _revisionDate), IpcJson.Default.FieldRequest);

  private static FieldViewModel HistoryField(string label, DateTime value) =>
    new(null, new FieldDto(label, label, "Item history", value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), false), canCopy: false);

  internal static string Describe(Exception ex) => ex switch
  {
    IpcException { Code: IpcErrors.Reprompt } => "Master password verification is required.",
    IpcException { Code: IpcErrors.Locked } => "The vault is locked. Unlock it in Command Palette.",
    IpcException { Code: IpcErrors.NotFound } => "This item is no longer in the vault.",
    IpcException { Code: IpcErrors.Invalid } => "The request was not valid.",
    IpcException { Code: IpcErrors.Stale } => "This item changed in the vault. The window shows the new version.",
    IOException => "The connection to Command Palette was lost.",
    TimeoutException => "Command Palette did not respond.",
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

  public void Dispose()
  {
    _disposed = true;
    _totpTimer.Stop();
  }
}
