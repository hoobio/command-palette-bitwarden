using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class ItemViewModel : ObservableObject, IDisposable
{
  private const string HistoryGroup = "Password history";

  private readonly PipeChannel _channel;
  private readonly DispatcherTimer _totpTimer;
  private FieldViewModel? _totpField;
  private bool _totpFetching;
  private bool _disposed;
  private DateTime _revisionDate;
  private (string Op, JsonElement Payload, Action<JsonElement?> OnSuccess)? _repromptRetry;
  private bool _createUncertain;
  private bool _inOrganization;
  private bool _archiveUnavailable;
  private FiltersResponse? _filters;
  private readonly DispatcherTimer _graceTimer;
  private GraceDto? _grace;
  private (string Op, JsonElement Payload, TaskCompletionSource<(bool Ok, JsonElement? Reply, string? Error)> Done)? _pendingWrite;

  public ItemViewModel(string id, PipeChannel channel)
  {
    Id = id;
    _channel = channel;
    _graceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    _graceTimer.Tick += (_, _) => TickGrace();
    _totpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    _totpTimer.Tick += (_, _) => _ = TickAsync();
  }

  internal static ItemViewModel ForNew(PipeChannel channel, EditableItem item, FiltersResponse filters) => new(string.Empty, channel)
  {
    IsNew = true,
    IsLoading = false,
    ItemType = item.Type,
    Title = item.Name.Length > 0 ? item.Name : "New item",
    TypeGlyph = GlyphFor(item.Type),
    Editor = new ItemEditorViewModel(item, filters),
  };

  public string Id { get; private set; }

  internal bool IsTotpRunning => _totpTimer.IsEnabled;

  internal Func<string, string, string, string, Task<bool>>? Confirm { get; set; }

  internal Func<ActionDialogRequest, Task<bool>>? ConfirmAction { get; set; }

  internal Action<EditableItem, FiltersResponse>? CloneRequested { get; set; }

  internal Func<string, Task<string?>>? PickSavePath { get; set; }

  internal Func<Task<string?>>? PickOpenPath { get; set; }

  internal Func<Task<string?>>? OpenGenerator { get; set; }

  internal Action<string, bool>? Toast { get; set; }

  internal PipeChannel Channel => _channel;

  public ObservableCollection<AttachmentViewModel> Attachments { get; } = [];

  public bool ShowAttachments => IsReadOnly && !IsPrompting && (Attachments.Count > 0 || CanAttach);

  public bool CanAttach => IsReadOnly && !IsDeleted && !IsArchived && !IsPrompting && ItemType is >= 1 and <= 5;

  public bool CanRotate => IsReadOnly && ItemType == 1 && !IsDeleted && !IsArchived;

  internal event Action? Removed;

  internal event Action? Saved;

  internal event Action? Discarded;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsReadOnly), nameof(CanEdit), nameof(CanRestore), nameof(CanDelete), nameof(CanShowHistory), nameof(CanRotate), nameof(CanAttach), nameof(ShowAttachments))]
  public partial ItemEditorViewModel? Editor { get; private set; }

  partial void OnEditorChanged(ItemEditorViewModel? value)
  {
    if (value is not null)
      value.Generate = () => OpenGenerator?.Invoke() ?? Task.FromResult<string?>(null);
  }

  public bool IsEditing => Editor is not null;

  public bool IsReadOnly => Editor is null && !IsNew;

  public bool CanEdit => IsReadOnly && !IsDeleted && !IsArchived && ItemType is >= 1 and <= 5;

  public bool CanRestore => IsReadOnly && (IsDeleted || IsArchived);

  public bool CanDelete => IsReadOnly;

  public string DeleteLabel => IsDeleted || IsArchived ? "Delete permanently" : "Delete";

  public bool CanShowHistory => IsReadOnly && ItemType == 1;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(IsReadOnly), nameof(CanEdit), nameof(CanRestore), nameof(CanDelete), nameof(CanShowHistory), nameof(CanRotate), nameof(CanAttach), nameof(ShowAttachments))]
  public partial bool IsNew { get; private set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanRestore), nameof(DeleteLabel), nameof(CanRotate), nameof(CanAttach), nameof(ShowAttachments))]
  public partial bool IsDeleted { get; private set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanRestore), nameof(DeleteLabel), nameof(CanRotate), nameof(CanAttach), nameof(ShowAttachments))]
  public partial bool IsArchived { get; private set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(CanShowHistory), nameof(CanEdit), nameof(CanRotate), nameof(CanAttach), nameof(ShowAttachments))]
  public partial int ItemType { get; private set; }

  public ObservableCollection<FieldGroupViewModel> Groups { get; } = [];

  [ObservableProperty]
  public partial string Title { get; set; } = "Loading";

  [ObservableProperty]
  public partial string? FolderName { get; set; }

  [ObservableProperty]
  public partial string? OrganizationName { get; set; }

  [ObservableProperty]
  public partial bool IsFavorite { get; set; }

  [ObservableProperty]
  public partial bool IsProtected { get; set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(Icon), nameof(HasIcon))]
  public partial string? IconPath { get; set; }

  public Avalonia.Media.Imaging.Bitmap? Icon => IconPath is { } path ? ItemRowViewModel.LoadIcon(path) : null;

  public bool HasIcon => Icon is not null;

  [ObservableProperty]
  public partial string TypeGlyph { get; set; } = "";

  [ObservableProperty]
  public partial bool IsLoading { get; set; } = true;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(IsPrompting), nameof(CanAttach), nameof(ShowAttachments))]
  public partial bool RepromptRequired { get; set; }

  public bool IsPrompting => RepromptRequired || _pendingWrite is not null;

  [ObservableProperty]
  public partial bool IsVerifying { get; private set; }

  [ObservableProperty]
  public partial string? PasswordError { get; private set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(GraceVisible), nameof(GraceCaption), nameof(GraceFraction))]
  public partial TimeSpan GraceRemaining { get; private set; }

  public bool GraceVisible => _grace is not null && !IsPrompting;

  public string GraceCaption => $"Locks in {(int)Math.Ceiling(GraceRemaining.TotalSeconds) / 60}:{(int)Math.Ceiling(GraceRemaining.TotalSeconds) % 60:00}";

  public double GraceFraction => _grace is { Seconds: > 0 } grace ? Math.Clamp(GraceRemaining.TotalSeconds / grace.Seconds, 0, 1) : 0;

  internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

  partial void OnRepromptRequiredChanged(bool value) => OnPropertyChanged(nameof(GraceVisible));

  private void SetGrace(GraceDto? grace)
  {
    _grace = grace;
    OnPropertyChanged(nameof(GraceVisible));
    if (grace is null)
    {
      _graceTimer.Stop();
      GraceRemaining = TimeSpan.Zero;
      return;
    }

    GraceRemaining = grace.ExpiresUtc - UtcNow();
    _graceTimer.Start();
  }

  internal void TickGrace()
  {
    if (_grace is not { } grace)
      return;
    GraceRemaining = grace.ExpiresUtc - UtcNow();
    if (GraceRemaining > TimeSpan.Zero)
      return;
    GraceRemaining = TimeSpan.Zero;
    SetGrace(null);
    if (Editor is null)
      Relock();
  }

  private void Relock()
  {
    _totpTimer.Stop();
    foreach (var field in Groups.SelectMany(g => g.Fields))
      field.Wipe();
    Groups.Clear();
    Attachments.Clear();
    _totpField = null;
    _repromptRetry = null;
    RepromptRequired = true;
  }

  private void SetPending((string Op, JsonElement Payload, TaskCompletionSource<(bool Ok, JsonElement? Reply, string? Error)> Done)? pending)
  {
    _pendingWrite = pending;
    PasswordError = null;
    OnPropertyChanged(nameof(IsPrompting));
    OnPropertyChanged(nameof(CanAttach));
    OnPropertyChanged(nameof(ShowAttachments));
    OnPropertyChanged(nameof(GraceVisible));
  }

  internal void CancelPrompt()
  {
    ErrorMessage = null;
    _pendingWrite?.Done.TrySetResult((false, null, IpcErrors.Reprompt));
  }

  internal async Task SubmitPasswordAsync(string? password)
  {
    if (IsVerifying || string.IsNullOrEmpty(password))
      return;
    IsVerifying = true;
    PasswordError = null;
    try
    {
      if (_pendingWrite is { } pending)
        await VerifyPendingAsync(pending, password);
      else
        await VerifyRepromptAsync(password);
    }
    finally
    {
      IsVerifying = false;
    }
  }

  private async Task VerifyPendingAsync((string Op, JsonElement Payload, TaskCompletionSource<(bool Ok, JsonElement? Reply, string? Error)> Done) pending, string password)
  {
    var (ok, reply, _) = await SendAsync(Ops.VerifyReprompt, IpcJson.ToElement(new RepromptRequest(Id, password, pending.Op, pending.Payload), IpcJson.Default.RepromptRequest), Timeout.InfiniteTimeSpan);
    if (!ok)
    {
      FailPassword(ErrorMessage);
      return;
    }

    var result = IpcJson.Read(reply, IpcJson.Default.RepromptResponse);
    if (!result.Verified)
    {
      FailPassword(DescribeFailedReprompt(result));
      return;
    }

    ErrorMessage = null;
    SetGrace(result.Grace);
    pending.Done.TrySetResult((true, result.Result, null));
  }

  private void FailPassword(string? message)
  {
    ErrorMessage = null;
    PasswordError = message;
  }

  [ObservableProperty]
  public partial string? ErrorMessage { get; set; }

  [ObservableProperty]
  public partial string? StatusMessage { get; set; }

  public async Task LoadAsync()
  {
    if (IsEditing || IsNew)
      return;
    IsLoading = true;
    try
    {
      Apply(IpcJson.Read(await _channel.RequestAsync(Ops.GetItem, ItemPayload), IpcJson.Default.ItemDto));
      ErrorMessage = null;
      if (CanEdit)
        _ = PrefetchFiltersAsync();
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
    Attachments.Clear();
    OnPropertyChanged(nameof(ShowAttachments));
    _totpField = null;
    _repromptRetry = null;
    _filters = null;
    Editor = null;
    RepromptRequired = false;
    SetGrace(null);
    _pendingWrite?.Done.TrySetResult((false, null, IpcErrors.Locked));
    StatusMessage = null;
    ErrorMessage = Describe(new IpcException(IpcErrors.Locked));
  }

  internal void Apply(ItemDto item)
  {
    _repromptRetry = null;
    _revisionDate = item.RevisionDate;
    Title = item.Name;
    OrganizationName = item.OrganizationName;
    FolderName = item.FolderName ?? (item.OrganizationName is null ? "No folder" : null);
    IsFavorite = item.Favorite;
    IsProtected = item.Reprompt;
    TypeGlyph = GlyphFor(item.Type);
    IconPath = item.IconPath;
    ItemType = item.Type;
    IsDeleted = item.Deleted;
    IsArchived = item.Archived;
    _inOrganization = item.OrganizationName is not null;
    RepromptRequired = item.RepromptRequired;
    SetGrace(item.Grace);

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

    Attachments.Clear();
    foreach (var attachment in item.Attachments ?? [])
      Attachments.Add(new AttachmentViewModel(this, attachment));
    OnPropertyChanged(nameof(ShowAttachments));

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
    RunAsync(Ops.Copy, FieldPayload(field.Key), _ => Notify($"Copied {field.Label}"));

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
      else
        FailPassword(ErrorMessage);
      return false;
    }

    var result = IpcJson.Read(reply, IpcJson.Default.RepromptResponse);
    if (!result.Verified)
    {
      PasswordError = DescribeFailedReprompt(result);
      return false;
    }

    ErrorMessage = null;
    _repromptRetry = null;
    RepromptRequired = false;
    onSuccess(result.Result);
    SetGrace(result.Grace);
    await RefreshTotpIfShownAsync();
    return true;
  }

  [RelayCommand]
  private async Task EditAsync()
  {
    if (await LoadFiltersAsync() is { } filters)
      await RunAsync(Ops.GetEditableItem, ItemPayload, reply => Editor = new ItemEditorViewModel(IpcJson.Read(reply, IpcJson.Default.EditableItem), filters));
  }

  [RelayCommand]
  private async Task CloneAsync()
  {
    if (await LoadFiltersAsync() is { } filters)
    {
      await RunAsync(Ops.GetEditableItem, ItemPayload, reply =>
      {
        var source = IpcJson.Read(reply, IpcJson.Default.EditableItem);
        CloneRequested?.Invoke(source with { Id = null, Name = source.Name + " - Clone", RevisionDate = null }, filters);
      });
    }
  }

  [RelayCommand]
  private async Task SaveAsync()
  {
    if (Editor is not { } editor || editor.Validate() is not null)
      return;
    var creating = editor.Id is null;
    if (creating && _createUncertain
      && !await AskAsync("Save another copy?", "Bitwarden may have saved this item already. Check the vault list before you save it again.", "Save copy", "Cancel"))
      return;
    var item = editor.ToDto();
    var (ok, reply, error) = await WriteAsync(creating ? Ops.CreateItem : Ops.UpdateItem, IpcJson.ToElement(item, IpcJson.Default.EditableItem));
    if (!ok)
    {
      if (creating && error is null or IpcErrors.Unknown)
      {
        _createUncertain = true;
        ErrorMessage = Describe(new IpcException(IpcErrors.Unknown));
        Saved?.Invoke();
        return;
      }

      await OnEditFailedAsync(error);
      return;
    }

    var saved = IpcJson.Read(reply, IpcJson.Default.SavedResponse);
    _createUncertain = false;
    Id = saved.Id;
    IsNew = false;
    editor.MarkSaved(saved.Id, saved.RevisionDate, creating ? item.OrganizationId : editor.OriginalOrganizationId, creating ? item.CollectionIds : editor.OriginalCollectionIds);
    if (!creating && item.OrganizationId is { } organization
      && (organization != editor.OriginalOrganizationId || !item.CollectionIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(editor.OriginalCollectionIds)))
    {
      var request = new WriteRequest(saved.Id, saved.RevisionDate, OrganizationId: organization, CollectionIds: item.CollectionIds);
      (ok, reply, error) = await WriteAsync(editor.OriginalOrganizationId is null ? Ops.MoveItem : Ops.SetCollections, IpcJson.ToElement(request, IpcJson.Default.WriteRequest));
      if (!ok)
      {
        await OnEditFailedAsync(error);
        return;
      }

      editor.MarkMoved(IpcJson.Read(reply, IpcJson.Default.SavedResponse).RevisionDate);
    }

    Editor = null;
    await LoadAsync();
    Notify("Saved");
    Saved?.Invoke();
  }

  private async Task OnEditFailedAsync(string? error)
  {
    if (error == IpcErrors.Failed)
      ErrorMessage = "Bitwarden did not save the change.";
    if (error is not (IpcErrors.Conflict or IpcErrors.Unknown))
      return;
    ErrorMessage = null;
    var (title, message) = error == IpcErrors.Conflict
      ? ("Item changed", "This item changed in the vault after you started editing.")
      : ("Save not confirmed", "Bitwarden did not confirm the change. Reload the item to see what the vault holds.");
    if (await AskAsync(title, message, "Reload", "Keep editing"))
    {
      Editor = null;
      await LoadAsync();
    }
    else
    {
      ErrorMessage = Describe(new IpcException(error));
    }
  }

  [RelayCommand]
  private async Task CancelEditAsync() => await TryEndEditAsync();

  internal async Task<bool> TryEndEditAsync()
  {
    if (Editor is { IsDirty: true } && !await AskAsync("Discard changes?", "Your changes to this item are not saved.", "Discard", "Keep editing"))
      return false;
    Editor = null;
    ErrorMessage = null;
    if (IsNew)
      Discarded?.Invoke();
    return true;
  }

  [RelayCommand]
  private async Task DeleteAsync()
  {
    if (ConfirmAction is null)
      return;
    (string Op, JsonElement Payload)? repromptFor = null;
    var active = !IsDeleted && !IsArchived;
    var request = new ActionDialogRequest(
      active ? "Remove item" : "Delete permanently",
      active
        ? $"Archive \"{Title}\" or move it to the trash to keep it recoverable, or delete it permanently. You cannot undo a permanent delete."
        : $"\"{Title}\" will be deleted permanently. You cannot undo this.",
      "Permanently delete");
    request.Danger = () => AttemptAsync(Ops.DeleteItem, permanent: true);
    if (active)
    {
      request.PrimaryLabel = ArchiveLabel;
      request.Primary = () => _archiveUnavailable || _inOrganization ? AttemptAsync(Ops.DeleteItem, permanent: false) : AttemptAsync(Ops.ArchiveItem, permanent: false);
    }

    if (!await ConfirmAction(request))
      return;
    if (repromptFor is { } retry)
    {
      var (ok, _, error) = await RetryWithPasswordAsync(retry.Op, retry.Payload);
      if (!ok)
      {
        if (error is IpcErrors.Conflict or IpcErrors.Unknown)
          await ReloadStaleAsync();
        return;
      }
    }

    Removed?.Invoke();

    async Task<string?> AttemptAsync(string op, bool permanent)
    {
      var payload = IpcJson.ToElement(new WriteRequest(Id, _revisionDate, permanent), IpcJson.Default.WriteRequest);
      var (ok, _, error) = await SendAsync(op, payload, Timeout.InfiniteTimeSpan);
      var message = ErrorMessage;
      ErrorMessage = null;
      if (ok || error == IpcErrors.Reprompt)
      {
        repromptFor = ok ? null : (op, payload);
        return null;
      }

      if (op == Ops.ArchiveItem && error == IpcErrors.Failed)
      {
        _archiveUnavailable = true;
        request.PrimaryLabel = ArchiveLabel;
        return "Bitwarden could not archive this item. Move it to the trash instead.";
      }

      if (error is IpcErrors.Conflict or IpcErrors.Unknown)
        await ReloadStaleAsync();
      return error == IpcErrors.Failed ? "Bitwarden did not save the change." : message ?? Describe(new IpcException(error ?? IpcErrors.Failed));
    }
  }

  private string ArchiveLabel => _archiveUnavailable || _inOrganization ? "Move to Trash" : "Archive";

  [RelayCommand]
  private Task RestoreAsync() => WriteStateAsync(Ops.RestoreItem);

  private async Task WriteStateAsync(string op)
  {
    var (ok, _, error) = await WriteAsync(op, IpcJson.ToElement(new WriteRequest(Id, _revisionDate), IpcJson.Default.WriteRequest));
    if (ok)
      Removed?.Invoke();
    else if (error is IpcErrors.Conflict or IpcErrors.Unknown)
      await ReloadStaleAsync();
  }

  private async Task<(bool Ok, JsonElement? Reply, string? Error)> WriteAsync(string op, JsonElement payload)
  {
    var sent = await SendAsync(op, payload, Timeout.InfiniteTimeSpan);
    return sent.Error == IpcErrors.Reprompt ? await RetryWithPasswordAsync(op, payload) : sent;
  }

  private async Task<(bool Ok, JsonElement? Reply, string? Error)> RetryWithPasswordAsync(string op, JsonElement payload)
  {
    var done = new TaskCompletionSource<(bool Ok, JsonElement? Reply, string? Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
    ErrorMessage = null;
    SetPending((op, payload, done));
    try
    {
      return await done.Task;
    }
    finally
    {
      SetPending(null);
    }
  }

  [RelayCommand]
  private async Task RotateAsync()
  {
    if (!await AskAsync("Rotate password", $"Replace the password for {Title}? The old password is kept in password history.", "Rotate", "Cancel"))
      return;
    var options = CompanionSettings.LoadGenerator();
    if (options.Kind is not (GeneratorKinds.Password or GeneratorKinds.Passphrase))
      options = options with { Kind = GeneratorKinds.Password };
    var (ok, _, error) = await WriteAsync(Ops.RotatePassword, IpcJson.ToElement(new RotateRequest(Id, _revisionDate, options), IpcJson.Default.RotateRequest));
    if (ok)
    {
      await LoadAsync();
      Notify("Password replaced and copied");
      Saved?.Invoke();
      return;
    }

    if (error is IpcErrors.Reprompt or null)
      return;
    if (error is IpcErrors.Conflict or IpcErrors.Unknown or IpcErrors.Stale)
      await LoadAsync();
    ErrorMessage = error switch
    {
      IpcErrors.Unknown => "Bitwarden did not confirm the new password, so nothing was copied. Check the item before you use the password.",
      IpcErrors.Conflict or IpcErrors.Stale => "This item changed in the vault. The password was not replaced and nothing was copied.",
      IpcErrors.Failed => "Bitwarden did not save the new password. Nothing was copied.",
      _ => ErrorMessage,
    };
  }

  internal async Task RotateAfterLoadAsync()
  {
    await LoadAsync();
    if (CanRotate && !IsLoading)
      await RotateCommand.ExecuteAsync(null);
  }

  [RelayCommand]
  private async Task AddAttachmentAsync()
  {
    if (PickOpenPath is null || await PickOpenPath() is not { Length: > 0 } path)
      return;
    var (ok, _, error) = await WriteAsync(Ops.UploadAttachment, IpcJson.ToElement(new AttachmentRequest(Id, _revisionDate, Path: path), IpcJson.Default.AttachmentRequest));
    if (ok)
    {
      await LoadAsync();
      Notify($"Attached {Path.GetFileName(path)}");
    }
    else if (error is IpcErrors.Conflict or IpcErrors.Unknown)
    {
      await ReloadStaleAsync();
    }
  }

  internal async Task DownloadAttachmentAsync(AttachmentViewModel attachment)
  {
    if (PickSavePath is null || await PickSavePath(attachment.FileName) is not { Length: > 0 } path)
      return;
    var (ok, _, error) = await WriteAsync(Ops.DownloadAttachment, IpcJson.ToElement(new AttachmentRequest(Id, _revisionDate, attachment.Id, path), IpcJson.Default.AttachmentRequest));
    if (ok)
      Notify($"Saved {attachment.FileName}");
    else if (error == IpcErrors.Stale)
      await ReloadStaleAsync();
    else if (error == IpcErrors.Failed)
      ErrorMessage = "Bitwarden did not save the attachment.";
  }

  internal async Task DeleteAttachmentAsync(AttachmentViewModel attachment)
  {
    if (ConfirmAction is null)
      return;
    JsonElement? repromptFor = null;
    var request = new ActionDialogRequest("Delete attachment", $"\"{attachment.FileName}\" will be deleted permanently. You cannot undo this.", "Delete attachment");
    request.Danger = async () =>
    {
      var payload = IpcJson.ToElement(new AttachmentRequest(Id, _revisionDate, attachment.Id), IpcJson.Default.AttachmentRequest);
      var (ok, _, error) = await SendAsync(Ops.DeleteAttachment, payload, Timeout.InfiniteTimeSpan);
      var message = ErrorMessage;
      ErrorMessage = null;
      if (ok || error == IpcErrors.Reprompt)
      {
        repromptFor = ok ? null : payload;
        return null;
      }

      if (error is IpcErrors.Conflict or IpcErrors.Unknown)
        await ReloadStaleAsync();
      return error == IpcErrors.Failed ? "Bitwarden did not delete the attachment." : message;
    };
    if (!await ConfirmAction(request))
      return;
    if (repromptFor is { } retry && !(await RetryWithPasswordAsync(Ops.DeleteAttachment, retry)).Ok)
      return;
    await LoadAsync();
    Notify($"Deleted {attachment.FileName}");
  }

  [RelayCommand]
  private Task ShowPasswordHistoryAsync() => RunAsync(Ops.GetPasswordHistory, ItemPayload, reply =>
  {
    var history = IpcJson.Read(reply, IpcJson.Default.PasswordHistoryResponse);
    var existing = Groups.FirstOrDefault(g => g.Name == HistoryGroup);
    if (existing is not null)
      Groups.Remove(existing);
    if (history.LastUsed.Count == 0)
    {
      Notify("This item has no password history.", success: false);
      return;
    }

    Groups.Add(new FieldGroupViewModel(HistoryGroup, [.. history.LastUsed.Select((used, i) => new FieldViewModel(this, new FieldDto(
      ItemFields.HistoryPrefix + i.ToString(CultureInfo.InvariantCulture), used.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), HistoryGroup, FieldViewModel.Mask, false)))]));
  });

  private async Task<FiltersResponse?> LoadFiltersAsync()
  {
    if (_filters is { } prefetched)
      return prefetched;
    var (ok, reply, _) = await SendAsync(Ops.ListFilters, null);
    return ok ? _filters = IpcJson.Read(reply, IpcJson.Default.FiltersResponse) : null;
  }

  // The extension lists trash, archive and collections with bw for the filters, so Edit would otherwise wait on three bw processes.
  private async Task PrefetchFiltersAsync()
  {
    try
    {
      _filters = IpcJson.Read(await _channel.RequestAsync(Ops.ListFilters, null), IpcJson.Default.FiltersResponse);
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      _filters = null;
    }
  }

  private void Notify(string message, bool success = true) => Toast?.Invoke(message, success);

  private Task<bool> AskAsync(string title, string message, string primary, string close) =>
    Confirm?.Invoke(title, message, primary, close) ?? Task.FromResult(false);

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
      ErrorMessage = null;
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

  private async Task<(bool Ok, JsonElement? Reply, string? Error)> SendAsync(string op, JsonElement? payload, TimeSpan? timeout = null)
  {
    try
    {
      var reply = await (timeout is { } limit ? _channel.RequestAsync(op, payload, limit) : _channel.RequestAsync(op, payload));
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
    IpcException { Code: IpcErrors.Conflict } => "This item changed in the vault. Cancel and edit again to apply your changes.",
    IpcException { Code: IpcErrors.Unknown } => "Bitwarden did not confirm the save. Check the vault before you save again.",
    IpcException { Code: IpcErrors.Premium } => "Attachments need Bitwarden Premium or an organisation that allows them.",
    IpcException { Code: IpcErrors.TooLarge } => "The file is larger than the 500 MB that Bitwarden allows.",
    IpcException { Code: IpcErrors.NoStorage } => "There is not enough Bitwarden storage for this file.",
    IOException => "The connection to Command Palette was lost.",
    TimeoutException => "Command Palette did not respond.",
    _ => "Something went wrong.",
  };

  private static string DescribeFailedReprompt(RepromptResponse result) => result.CooldownSeconds > 0
    ? $"Too many failed attempts. Try again in {result.CooldownSeconds}s."
    : "Incorrect master password.";

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
    _graceTimer.Stop();
    _pendingWrite?.Done.TrySetResult((false, null, IpcErrors.Reprompt));
    Editor = null;
  }
}

internal sealed partial class AttachmentViewModel(ItemViewModel owner, AttachmentDto attachment)
{
  public string Id => attachment.Id;

  public string FileName => attachment.FileName;

  public string? SizeName => attachment.SizeName;

  [RelayCommand]
  private Task DownloadAsync() => owner.DownloadAttachmentAsync(this);

  [RelayCommand]
  private Task DeleteAsync() => owner.DeleteAttachmentAsync(this);
}

internal sealed class ActionDialogRequest(string title, string message, string dangerLabel)
{
  public string Title { get; } = title;

  public string Message { get; } = message;

  public string DangerLabel { get; } = dangerLabel;

  public Func<Task<string?>> Danger { get; set; } = () => Task.FromResult<string?>(null);

  public string? PrimaryLabel { get; set; }

  public Func<Task<string?>>? Primary { get; set; }
}
