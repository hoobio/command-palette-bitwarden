using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.ViewModels;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Tests;

public class EditTests(AvaloniaSession session)
{
  private static readonly DateTime Revision = new(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
  private static readonly string FolderId = Guid.NewGuid().ToString();
  private static readonly string OrgId = Guid.NewGuid().ToString();
  private static readonly string CollectionId = Guid.NewGuid().ToString();

  private static readonly FiltersResponse Filters = new(
    [new FilterRef(FolderId, "Work"), new FilterRef(Guid.NewGuid().ToString(), "Personal")],
    [new FilterRef(CollectionId, "Engineering", OrgId), new FilterRef(Guid.NewGuid().ToString(), "Elsewhere", Guid.NewGuid().ToString())],
    [new FilterRef(OrgId, "Acme")]);

  internal static EditableItem Sample(int type, string? id = null) => new(
    id ?? Guid.NewGuid().ToString(), type, "Sample " + type, FolderId, null, [], true, false, "a note", Revision,
    [new CustomFieldEdit("pin", "1234", 1), new CustomFieldEdit("flag", "true", 2), new CustomFieldEdit("text", "v", 0)],
    Login: type == 1 ? new LoginEdit("octocat", "hunter2", "JBSWY3DPEHPK3PXP", [new UriEdit("https://github.com", 3), new UriEdit("gist.github.com")]) : null,
    Card: type == 3 ? new CardEdit("Jane Doe", "Visa", "4111111111111111", "04", "2030", "123") : null,
    Identity: type == 4 ? new IdentityEdit("Ms", "Jane", "Q", "Doe", "1 Road", "Flat 2", null, "Leeds", "West Yorkshire", "LS1 1AA", "GB", "Acme", "jane@example.com", "0113", "AB123456C", "jdoe", "P123", "L123") : null,
    SshKey: type == 5 ? new SshKeyEdit("-----BEGIN OPENSSH PRIVATE KEY-----\nAAAA\n-----END OPENSSH PRIVATE KEY-----", "ssh-ed25519 AAAA", "SHA256:abc") : null);

  private sealed class FakeExtension
  {
    public Dictionary<string, EditableItem> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Op, JsonElement? Payload)> Requests { get; } = [];
    public string? FailWith { get; set; }
    public string? FailOp { get; set; }
    public TimeSpan Delay { get; set; }

    public List<JsonElement?> Payloads(string op)
    {
      lock (Requests)
        return [.. Requests.Where(r => r.Op == op).Select(r => r.Payload)];
    }

    public async Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
    {
      lock (Requests)
        Requests.Add((op, payload));
      if (FailWith is { } code && (FailOp is null || FailOp == op))
        throw new IpcException(code);
      if (Delay > TimeSpan.Zero)
        await Task.Delay(Delay, cancellationToken);
      if (op == Ops.VerifyReprompt)
      {
        var verify = IpcJson.Read(payload, IpcJson.Default.RepromptRequest);
        if (verify.Password != "correct")
          return IpcJson.ToElement(new RepromptResponse(false, 0), IpcJson.Default.RepromptResponse);
        FailWith = null;
        return IpcJson.ToElement(new RepromptResponse(true, 0, await HandleAsync(verify.Op!, verify.Payload, cancellationToken)), IpcJson.Default.RepromptResponse);
      }

      return op switch
      {
        Ops.ListFilters => IpcJson.ToElement(Filters, IpcJson.Default.FiltersResponse),
        Ops.GetItem => IpcJson.ToElement(Dto(IpcJson.Read(payload, IpcJson.Default.ItemRequest).Id), IpcJson.Default.ItemDto),
        Ops.GetEditableItem => IpcJson.ToElement(Items[IpcJson.Read(payload, IpcJson.Default.ItemRequest).Id], IpcJson.Default.EditableItem),
        Ops.UpdateItem => Save(IpcJson.Read(payload, IpcJson.Default.EditableItem)),
        Ops.MoveItem or Ops.SetCollections or Ops.DeleteItem or Ops.ArchiveItem =>
          IpcJson.ToElement(new SavedResponse(IpcJson.Read(payload, IpcJson.Default.WriteRequest).Id, Revision.AddDays(2)), IpcJson.Default.SavedResponse),
        Ops.GetPasswordHistory => IpcJson.ToElement(new PasswordHistoryResponse([Revision.AddDays(-1), Revision.AddDays(-30)]), IpcJson.Default.PasswordHistoryResponse),
        _ => null,
      };
    }

    private JsonElement Save(EditableItem item)
    {
      Items[item.Id!] = item with { RevisionDate = Revision.AddDays(1) };
      return IpcJson.ToElement(new SavedResponse(item.Id!, Revision.AddDays(1)), IpcJson.Default.SavedResponse);
    }

    private ItemDto Dto(string id)
    {
      var item = Items[id];
      return new ItemDto(id, item.Name, item.Type, null, item.OrganizationId is null ? null : "Acme", item.Favorite, false, false, Revision, item.RevisionDate ?? Revision, null,
        [new FieldDto(ItemFields.Username, "Username", "Login credentials", item.Login?.Username, false)]);
    }
  }

  private sealed class Harness : IDisposable
  {
    private readonly ChannelPair _pair;

    public Harness(params EditableItem[] items)
      : this(null, items)
    {
    }

    public Harness(TimeSpan? requestTimeout, params EditableItem[] items)
    {
      foreach (var item in items)
        Extension.Items[item.Id!] = item;
      _pair = new ChannelPair(Extension.HandleAsync);
      Channel = new PipeChannel(_pair.CompanionInput, _pair.CompanionOutput, (_, _, _) => Task.FromResult<JsonElement?>(null), requestTimeout: requestTimeout);
      _ = Task.Run(Channel.RunAsync);
    }

    public FakeExtension Extension { get; } = new();

    public PipeChannel Channel { get; }

    public List<string> Prompts { get; } = [];

    public bool Answer { get; set; }

    public string? Password { get; set; }

    public string? PasswordFailure { get; private set; }

    public ItemViewModel Item(string id)
    {
      var vm = new ItemViewModel(id, Channel) { Confirm = Ask, ConfirmAction = ConfirmActionAsync, Toast = (message, _) => Toasts.Add(message) };
      RepromptDriver.Attach(vm, () => Password, failure => PasswordFailure = failure);
      return vm;
    }

    public List<string?> ActionFailures { get; } = [];

    public List<string> Toasts { get; } = [];

    public string? Pick { get; set; }

    public ActionDialogRequest? LastRequest { get; private set; }

    public async Task<bool> ConfirmActionAsync(ActionDialogRequest request)
    {
      LastRequest = request;
      Prompts.Add($"{request.Title}|{request.PrimaryLabel}|{request.DangerLabel}");
      if (Pick is null)
        return false;
      var failure = await (Pick == "danger" ? request.Danger : request.Primary!)();
      ActionFailures.Add(failure);
      return failure is null;
    }

    public Task<bool> Ask(string title, string message, string primary, string close)
    {
      Prompts.Add($"{title}|{primary}|{close}");
      return Task.FromResult(Answer);
    }

    public void Dispose()
    {
      Channel.Dispose();
      _pair.Dispose();
    }
  }

  private static async Task UntilAsync(Func<bool> condition)
  {
    for (var i = 0; i < 500 && !condition(); i++)
      await Task.Delay(10);
    Assert.True(condition());
  }

  [Fact]
  public Task Edit_UsesTheFiltersPrefetchedOnLoad() => session.RunAsync(async () =>
  {
    var item = Sample(1);
    using var h = new Harness(item);
    using var vm = h.Item(item.Id!);
    await vm.LoadAsync();
    await UntilAsync(() => h.Extension.Payloads(Ops.ListFilters).Count == 1);
    await Task.Delay(50);

    await vm.EditCommand.ExecuteAsync(null);

    Assert.NotNull(vm.Editor);
    Assert.Single(h.Extension.Payloads(Ops.ListFilters));
    Assert.Single(h.Extension.Payloads(Ops.GetEditableItem));

    await vm.CancelEditCommand.ExecuteAsync(null);
    vm.OnLocked();
    await vm.EditCommand.ExecuteAsync(null);
    Assert.Equal(2, h.Extension.Payloads(Ops.ListFilters).Count);
  });

  [Fact]
  public Task FailedFilterPrefetch_StaysQuiet_AndEditAsksAgain() => session.RunAsync(async () =>
  {
    var item = Sample(1);
    using var h = new Harness(item);
    h.Extension.FailWith = IpcErrors.Locked;
    h.Extension.FailOp = Ops.ListFilters;
    using var vm = h.Item(item.Id!);
    await vm.LoadAsync();
    await UntilAsync(() => h.Extension.Payloads(Ops.ListFilters).Count == 1);
    await Task.Delay(50);
    Assert.Null(vm.ErrorMessage);

    h.Extension.FailWith = null;
    await vm.EditCommand.ExecuteAsync(null);

    Assert.NotNull(vm.Editor);
    Assert.Equal(2, h.Extension.Payloads(Ops.ListFilters).Count);
  });

  [Fact]
  public Task EditCancel_TracksDirtyState_AndPromptsOnlyWhenDirty() => session.RunAsync(async () =>
  {
    var item = Sample(1);
    using var h = new Harness(item);
    using var vm = h.Item(item.Id!);
    await vm.LoadAsync();
    Assert.True(vm.CanEdit);

    await vm.EditCommand.ExecuteAsync(null);
    var editor = Assert.IsType<ItemEditorViewModel>(vm.Editor);
    Assert.True(vm.IsEditing);
    Assert.False(vm.CanEdit);
    Assert.False(editor.IsDirty);
    Assert.Equal("Work", editor.SelectedFolder!.Label);

    await vm.CancelEditCommand.ExecuteAsync(null);
    Assert.Null(vm.Editor);
    Assert.Empty(h.Prompts);

    await vm.EditCommand.ExecuteAsync(null);
    vm.Editor!.Name = "Changed";
    Assert.True(vm.Editor.IsDirty);
    vm.Editor.Name = item.Name;
    Assert.False(vm.Editor.IsDirty);
    vm.Editor.Uris[1].SelectedMatch = ItemEditorViewModel.MatchChoices[3];
    Assert.True(vm.Editor.IsDirty);

    await vm.CancelEditCommand.ExecuteAsync(null);
    Assert.NotNull(vm.Editor);
    Assert.Equal(["Discard changes?|Discard|Keep editing"], h.Prompts);

    h.Answer = true;
    await vm.CancelEditCommand.ExecuteAsync(null);
    Assert.Null(vm.Editor);
    Assert.Empty(h.Extension.Payloads(Ops.UpdateItem));
  });

  [Fact]
  public Task Save_SendsTheTypedItem_ThenMovesItIntoTheOrganisation() => session.RunAsync(async () =>
  {
    var item = Sample(1);
    using var h = new Harness(item);
    using var vm = h.Item(item.Id!);
    await vm.LoadAsync();
    await vm.EditCommand.ExecuteAsync(null);
    var editor = vm.Editor!;

    editor.Password = "rotated";
    editor.AddUriCommand.Execute(null);
    editor.Uris[^1].Uri = "example.com";
    editor.Uris[^1].MoveUpCommand.Execute(null);
    editor.Uris[0].RemoveCommand.Execute(null);
    editor.AddFieldCommand.Execute("3");
    editor.Fields[^1].Name = "user";
    editor.Fields[^1].SelectedLink = editor.LinkTargets[0];
    editor.Fields[0].MoveDownCommand.Execute(null);
    Assert.True(editor.HasOrganizations);
    Assert.True(editor.CanChangeOwner);
    editor.SelectedOwner = editor.Owners[1];
    Assert.Equal(["Engineering"], editor.Collections.Select(c => c.Name));

    await vm.SaveCommand.ExecuteAsync(null);
    Assert.Equal("Choose at least one collection for the organisation.", editor.ValidationMessage);
    Assert.Empty(h.Extension.Payloads(Ops.UpdateItem));

    editor.Collections[0].IsChecked = true;
    await vm.SaveCommand.ExecuteAsync(null);

    var sent = IpcJson.Read(Assert.Single(h.Extension.Payloads(Ops.UpdateItem)), IpcJson.Default.EditableItem);
    Assert.Equal(item.Id, sent.Id);
    Assert.Equal(Revision, sent.RevisionDate);
    Assert.Equal("rotated", sent.Login!.Password);
    Assert.Equal(["example.com", "gist.github.com"], sent.Login.Uris.Select(u => u.Uri));
    Assert.Equal(["flag", "pin", "text", "user"], sent.Fields.Select(f => f.Name));
    Assert.Equal(100, sent.Fields[^1].LinkedId);
    var move = IpcJson.Read(Assert.Single(h.Extension.Payloads(Ops.MoveItem)), IpcJson.Default.WriteRequest);
    Assert.Equal((item.Id, OrgId, Revision.AddDays(1)), (move.Id, move.OrganizationId, move.RevisionDate));
    Assert.Equal([CollectionId], move.CollectionIds);
    Assert.Null(vm.Editor);
    Assert.Equal(["Saved"], h.Toasts);
  });

  [Fact]
  public Task Conflict_OffersReloadOrKeepEditing() => session.RunAsync(async () =>
  {
    var item = Sample(3);
    using var h = new Harness(item);
    using var vm = h.Item(item.Id!);
    await vm.LoadAsync();
    await vm.EditCommand.ExecuteAsync(null);
    vm.Editor!.CardCode = "999";
    h.Extension.FailWith = IpcErrors.Conflict;
    h.Extension.FailOp = Ops.UpdateItem;

    await vm.SaveCommand.ExecuteAsync(null);
    Assert.Equal(["Item changed|Reload|Keep editing"], h.Prompts);
    Assert.NotNull(vm.Editor);
    Assert.Equal("999", vm.Editor.CardCode);
    Assert.Contains("changed in the vault", vm.ErrorMessage, StringComparison.Ordinal);

    h.Answer = true;
    var loads = h.Extension.Payloads(Ops.GetItem).Count;
    await vm.SaveCommand.ExecuteAsync(null);
    Assert.Null(vm.Editor);
    Assert.Equal(loads + 1, h.Extension.Payloads(Ops.GetItem).Count);

    h.Extension.FailWith = IpcErrors.Failed;
    await vm.EditCommand.ExecuteAsync(null);
    h.Extension.FailOp = null;
    await vm.SaveCommand.ExecuteAsync(null);
    Assert.Equal("Bitwarden did not save the change.", vm.ErrorMessage);
  });

  [Fact]
  public void Validation_CoversNameUriMonthSshAndFields()
  {
    var editor = new ItemEditorViewModel(Sample(1), Filters);
    Assert.Null(editor.Validate());
    editor.Name = " ";
    Assert.Equal("Name is required.", editor.Validate());
    editor.Name = "x";
    editor.Uris[0].Uri = "My router";
    editor.Uris[1].Uri = null;
    Assert.Null(editor.Validate());
    Assert.Equal(["My router", null], editor.ToDto().Login!.Uris.Select(u => u.Uri));
    editor.AddFieldCommand.Execute("0");
    Assert.Equal("Every new custom field needs a name.", editor.Validate());
    editor.Fields[^1].Name = "n";
    editor.AddFieldCommand.Execute("3");
    editor.Fields[^1].Name = "l";
    Assert.Equal("Choose the linked field for every linked custom field.", editor.Validate());

    var card = new ItemEditorViewModel(Sample(3), Filters) { CardExpMonth = "13" };
    Assert.Equal("Expiration month must be a number from 1 to 12.", card.Validate());
    card.CardExpMonth = "12";
    Assert.Null(card.Validate());

    var ssh = new ItemEditorViewModel(Sample(5), Filters) { Fingerprint = "" };
    Assert.Equal("An SSH key needs the private key, public key and fingerprint.", ssh.Validate());
    Assert.False(new ItemEditorViewModel(Sample(2), Filters).CanLink);
  }

  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  [InlineData(4)]
  [InlineData(5)]
  public void EveryTypeForm_RoundTripsToTheDtoAndBack(int type)
  {
    var item = Sample(type) with { OrganizationId = OrgId, CollectionIds = [CollectionId] };
    var editor = new ItemEditorViewModel(item, Filters);

    Assert.Equal(JsonSerializer.Serialize(item, IpcJson.Default.EditableItem), JsonSerializer.Serialize(editor.ToDto(), IpcJson.Default.EditableItem));
    Assert.False(editor.IsDirty);
    Assert.False(editor.CanChangeOwner);
    Assert.Equal(JsonSerializer.Serialize(editor.ToDto(), IpcJson.Default.EditableItem),
      JsonSerializer.Serialize(new ItemEditorViewModel(editor.ToDto(), Filters).ToDto(), IpcJson.Default.EditableItem));
  }

  [Fact]
  public void SshKeyParser_DerivesThePublicKeyAndFingerprint()
  {
    static byte[] Str(byte[] value) => [.. BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value.Length)), .. value];
    var blob = Str("ssh-ed25519"u8.ToArray()).Concat(Str(new byte[32])).ToArray();
    var body = "openssh-key-v1\0"u8.ToArray().Concat(Str("none"u8.ToArray())).Concat(Str("none"u8.ToArray())).Concat(Str([]))
      .Concat(BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(1))).Concat(Str(blob)).Concat(Str(new byte[8])).ToArray();
    var pem = $"-----BEGIN OPENSSH PRIVATE KEY-----\n{Convert.ToBase64String(body)}\n-----END OPENSSH PRIVATE KEY-----\n";

    var editor = new ItemEditorViewModel(Sample(5) with { Name = string.Empty, SshKey = new SshKeyEdit(null, null, null) }, Filters) { PrivateKey = pem };
    editor.ImportSshKeyCommand.Execute(null);

    Assert.Equal("ssh-ed25519 " + Convert.ToBase64String(blob), editor.PublicKey);
    Assert.Equal("SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('='), editor.Fingerprint);
    Assert.Null(editor.ValidationMessage);

    editor.PrivateKey = "-----BEGIN RSA PRIVATE KEY-----\nabc\n-----END RSA PRIVATE KEY-----";
    editor.ImportSshKeyCommand.Execute(null);
    Assert.NotNull(editor.ValidationMessage);
    Assert.False(editor.IsNameMissing);
    Assert.Equal(editor.ValidationMessage, editor.FormMessage);
    editor.Validate();
    Assert.True(editor.IsNameMissing);
    Assert.Null(editor.FormMessage);
    Assert.False(SshKeyParser.TryParse("-----BEGIN OPENSSH PRIVATE KEY-----\n!!\n-----END OPENSSH PRIVATE KEY-----", out _, out _));
    Assert.False(SshKeyParser.TryParse($"-----BEGIN OPENSSH PRIVATE KEY-----\n{Convert.ToBase64String(Encoding.ASCII.GetBytes("not-openssh"))}\n-----END OPENSSH PRIVATE KEY-----", out _, out _));
  }

  [Fact]
  public Task DeleteArchiveAndHistory_SendTheirOps() => session.RunAsync(async () =>
  {
    var item = Sample(1);
    using var h = new Harness(item);
    using var vm = h.Item(item.Id!);
    var removed = 0;
    vm.Removed += () => removed++;
    await vm.LoadAsync();

    await vm.DeleteCommand.ExecuteAsync(null);
    Assert.Equal("Remove item|Archive|Permanently delete", Assert.Single(h.Prompts));
    Assert.Empty(h.Extension.Payloads(Ops.ArchiveItem));
    h.Pick = "primary";
    await vm.DeleteCommand.ExecuteAsync(null);
    Assert.Single(h.Extension.Payloads(Ops.ArchiveItem));
    Assert.Equal(1, removed);

    h.Extension.FailWith = IpcErrors.Failed;
    h.Extension.FailOp = Ops.ArchiveItem;
    await vm.DeleteCommand.ExecuteAsync(null);
    Assert.Equal("Bitwarden could not archive this item. Move it to the trash instead.", h.ActionFailures[^1]);
    Assert.Equal("Move to Trash", h.LastRequest!.PrimaryLabel);
    Assert.Null(vm.ErrorMessage);
    h.Extension.FailWith = null;
    await vm.DeleteCommand.ExecuteAsync(null);
    Assert.Equal("Remove item|Move to Trash|Permanently delete", h.Prompts[^1]);
    Assert.False(IpcJson.Read(Assert.Single(h.Extension.Payloads(Ops.DeleteItem)), IpcJson.Default.WriteRequest).Permanent);
    Assert.Equal(2, removed);

    Assert.True(vm.CanShowHistory);
    await vm.ShowPasswordHistoryCommand.ExecuteAsync(null);
    Assert.False(vm.CanShowHistory);
    var history = vm.Groups.Single(g => g.Name == "Password history");
    Assert.Equal(2, history.Fields.Count);
    Assert.All(history.Fields, f => Assert.Equal(FieldViewModel.Mask, f.DisplayValue));
    await history.Fields[1].CopyCommand.ExecuteAsync(null);
    Assert.Equal(ItemFields.HistoryPrefix + "1", IpcJson.Read(h.Extension.Payloads(Ops.Copy).Single(), IpcJson.Default.FieldRequest).Field);

    h.Pick = "danger";
    await vm.DeleteCommand.ExecuteAsync(null);
    Assert.True(IpcJson.Read(h.Extension.Payloads(Ops.DeleteItem)[^1], IpcJson.Default.WriteRequest).Permanent);
    Assert.Equal(3, removed);

    h.Extension.FailWith = IpcErrors.Failed;
    h.Extension.FailOp = Ops.DeleteItem;
    await vm.DeleteCommand.ExecuteAsync(null);
    Assert.Equal("Bitwarden did not save the change.", h.ActionFailures[^1]);
    Assert.Null(vm.ErrorMessage);
    Assert.Equal(3, removed);
  });

  [Fact]
  public Task Lock_WipesTheEditor() => session.RunAsync(async () =>
  {
    var item = Sample(1);
    using var h = new Harness(item);
    using var vm = h.Item(item.Id!);
    await vm.LoadAsync();
    await vm.EditCommand.ExecuteAsync(null);
    vm.Editor!.Password = "typed-secret";

    vm.OnLocked();

    Assert.Null(vm.Editor);
  });

  [Theory]
  [InlineData(1, "edit-login.png")]
  [InlineData(2, null)]
  [InlineData(3, "edit-card.png")]
  [InlineData(4, null)]
  [InlineData(5, null)]
  public Task EditMode_RendersEveryType(int type, string? screenshot) => session.RunAsync(async () =>
  {
    var item = Sample(type) with { OrganizationId = null };
    using var h = new Harness(item);
    var vm = h.Item(item.Id!);
    var window = new ItemWindow(vm) { Height = 900 };
    window.Show();
    await vm.LoadAsync();
    await vm.EditCommand.ExecuteAsync(null);
    window.UpdateLayout();

    var edit = window.GetVisualDescendants().OfType<ItemEditView>().Single();
    Assert.True(edit.IsEffectivelyVisible);
    Assert.Equal(item.Name, edit.FindControl<TextBox>("NameBox")!.Text);
    var favouriteLabel = edit.FindControl<TextBlock>("FavoriteLabel")!;
    Assert.Equal(14, favouriteLabel.FontSize);
    Assert.Equal(Avalonia.Media.Color.Parse("#CBCCC6"), ((Avalonia.Media.ISolidColorBrush)favouriteLabel.Foreground!).Color);
    Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveButton").IsEffectivelyVisible);
    var frame = window.CaptureRenderedFrame();
    Assert.NotNull(frame);
    if (screenshot is not null)
      SaveScreenshot(frame, screenshot);

    h.Answer = false;
    vm.Editor!.Name = "dirty";
    window.Close();
    Assert.True(window.IsVisible);
    h.Answer = true;
    window.Close();
    AvaloniaSession.WaitFor(() => !window.IsVisible);
  });

  [Fact]
  public Task FolderDropdown_RendersTheRestyledPopup() => session.RunAsync(async () =>
  {
    var item = Sample(1);
    using var h = new Harness(item);
    var vm = h.Item(item.Id!);
    var window = new ItemWindow(vm) { Height = 700 };
    window.Show();
    await vm.LoadAsync();
    await vm.EditCommand.ExecuteAsync(null);
    window.UpdateLayout();

    var folder = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "FolderBox");
    folder.ApplyTemplate();
    var popup = folder.GetVisualDescendants().OfType<Popup>().Single();
    popup.ShouldUseOverlayLayer = true;
    folder.IsDropDownOpen = true;
    window.UpdateLayout();
    AvaloniaSession.WaitFor(() => folder.GetLogicalDescendants().OfType<ComboBoxItem>().Count() == 3);

    var items = folder.GetLogicalDescendants().OfType<ComboBoxItem>().ToList();
    Assert.True(items.Single(i => i.IsSelected).Content is Choice { Label: "Work" });
    var frame = window.CaptureRenderedFrame();
    Assert.NotNull(frame);
    SaveScreenshot(frame, "dropdown.png");
    folder.IsDropDownOpen = false;
    window.Close();
  });

  [Fact]
  public Task Coordinator_ClosesTheItemWindowWhenTheItemIsRemoved() => session.RunAsync(async () =>
  {
    var item = Sample(1);
    using var h = new Harness(item);
    using var pair = new ChannelPair(h.Extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var window = coordinator.ShowItem(item.Id!);
    var vm = window.ViewModel!;
    vm.ConfirmAction = h.ConfirmActionAsync;
    h.Pick = "primary";
    AvaloniaSession.WaitFor(() => vm.Title == item.Name);

    await vm.DeleteCommand.ExecuteAsync(null);

    AvaloniaSession.WaitFor(() => coordinator.ItemWindows.Count == 0);
  });

  [Fact]
  public Task Writes_OutlastTheDefaultRequestTimeout() => session.RunAsync(async () =>
  {
    var item = Sample(2);
    using var h = new Harness(TimeSpan.FromMilliseconds(100), item);
    using var vm = h.Item(item.Id!);
    await vm.LoadAsync();
    await vm.EditCommand.ExecuteAsync(null);
    vm.Editor!.Notes = "slow save";
    h.Extension.Delay = TimeSpan.FromMilliseconds(400);

    await vm.SaveCommand.ExecuteAsync(null);

    Assert.Single(h.Extension.Payloads(Ops.UpdateItem));
    Assert.Equal(["Saved"], h.Toasts);
  });

  [Fact]
  public Task RepromptOnAWrite_AsksForThePassword_AndWrapsTheWrite() => session.RunAsync(async () =>
  {
    var item = Sample(1);
    using var h = new Harness(item);
    using var vm = h.Item(item.Id!);
    var removed = 0;
    vm.Removed += () => removed++;
    await vm.LoadAsync();
    h.Pick = "danger";
    h.Extension.FailWith = IpcErrors.Reprompt;
    h.Extension.FailOp = Ops.DeleteItem;

    h.Password = "wrong";
    await vm.DeleteCommand.ExecuteAsync(null);
    Assert.Equal(0, removed);
    Assert.Equal("Incorrect master password.", h.PasswordFailure);
    Assert.False(vm.IsPrompting);

    h.Password = "correct";
    await vm.DeleteCommand.ExecuteAsync(null);
    var verify = IpcJson.Read(h.Extension.Payloads(Ops.VerifyReprompt)[^1], IpcJson.Default.RepromptRequest);
    Assert.Equal((item.Id, Ops.DeleteItem), (verify.Id, verify.Op));
    Assert.Equal(1, removed);
  });

  [Fact]
  public void BooleanField_KeepsItsStoredString_UntilToggled()
  {
    var editor = new ItemEditorViewModel(Sample(2) with { Fields = [new CustomFieldEdit("flag", "1", 2)] }, Filters);
    Assert.Equal("1", editor.ToDto().Fields[0].Value);
    Assert.False(editor.IsDirty);
    editor.Fields[0].BoolValue = true;
    Assert.Equal("true", editor.ToDto().Fields[0].Value);
    editor.Fields[0].BoolValue = false;
    Assert.Equal("1", editor.ToDto().Fields[0].Value);
  }

  [Fact]
  public void OrgItem_NeedsAtLeastOneCollection()
  {
    var editor = new ItemEditorViewModel(Sample(2) with { OrganizationId = OrgId, CollectionIds = [CollectionId] }, Filters);
    Assert.Null(editor.Validate());
    editor.Collections[0].IsChecked = false;
    Assert.Equal("Choose at least one collection for the organisation.", editor.Validate());
  }

  [Fact]
  public Task UnsupportedType_IsReadOnly() => session.RunAsync(async () =>
  {
    var item = Sample(6);
    using var h = new Harness(item);
    using var vm = h.Item(item.Id!);
    await vm.LoadAsync();

    Assert.Equal(6, vm.ItemType);
    Assert.False(vm.CanEdit);
    Assert.True(vm.IsReadOnly);
  });

  [Fact]
  public Task EditForm_MasksTheSshKey_AndMatchesComboBoxTextToTextBoxText() => session.RunAsync(async () =>
  {
    var item = Sample(5);
    using var h = new Harness(item);
    var vm = h.Item(item.Id!);
    var window = new ItemWindow(vm) { Height = 900 };
    window.Show();
    await vm.LoadAsync();
    await vm.EditCommand.ExecuteAsync(null);
    window.UpdateLayout();

    var key = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PrivateKeyBox");
    Assert.Equal('•', key.PasswordChar);
    Assert.Contains("revealPasswordButton", key.Classes);
    Assert.Contains('\n', key.Text!);

    var folder = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "FolderBox");
    var selected = folder.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Work");
    var name = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "NameBox").GetVisualDescendants().OfType<TextPresenter>().Single();
    Assert.Equal((name.FontWeight, name.FontSize, name.FontFamily), (selected.FontWeight, selected.FontSize, selected.FontFamily));
    Assert.Equal((name.Foreground as Avalonia.Media.ISolidColorBrush)?.Color, (selected.Foreground as Avalonia.Media.ISolidColorBrush)?.Color);
    window.Close();
  });

  [Fact]
  public Task OrgItem_OffersMoveToTrash_InPlaceOfArchive() => session.RunAsync(async () =>
  {
    var item = Sample(1) with { OrganizationId = OrgId, CollectionIds = [CollectionId] };
    using var h = new Harness(item);
    using var vm = h.Item(item.Id!);
    await vm.LoadAsync();
    h.Pick = "primary";

    await vm.DeleteCommand.ExecuteAsync(null);

    Assert.Equal("Remove item|Move to Trash|Permanently delete", Assert.Single(h.Prompts));
    Assert.Empty(h.Extension.Payloads(Ops.ArchiveItem));
    Assert.False(IpcJson.Read(Assert.Single(h.Extension.Payloads(Ops.DeleteItem)), IpcJson.Default.WriteRequest).Permanent);
  });

  [Fact]
  public Task Toolbar_DeleteIsARedTrashButton_AndItsDialogRunsTheWrite() => session.RunAsync(async () =>
  {
    var item = Sample(1);
    using var h = new Harness(item);
    var vm = h.Item(item.Id!);
    var window = new ItemWindow(vm) { Height = 500 };
    window.Show();
    await vm.LoadAsync();
    window.UpdateLayout();

    var delete = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DeleteButton");
    Assert.True(delete.IsEffectivelyVisible);
    Assert.Equal("Delete", ToolTip.GetTip(delete));
    Assert.Equal("Delete", Avalonia.Automation.AutomationProperties.GetName(delete));
    Assert.Equal(Avalonia.Media.Color.Parse("#F28779"), ((Avalonia.Media.ISolidColorBrush)delete.Foreground!).Color);
    Assert.Null(window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == "ArchiveButton"));
    var frame = window.CaptureRenderedFrame();
    Assert.NotNull(frame);
    SaveScreenshot(frame, "item-toolbar.png");
    window.Close();

    var request = new ActionDialogRequest("Remove item", "Archive or delete?", "Permanently delete") { PrimaryLabel = "Archive" };
    var (dialog, error, busy) = ItemView.CreateActionDialog(request);
    Assert.Contains("Action", dialog.Classes);
    Assert.Equal(("Archive", "Permanently delete", "Cancel"), (dialog.PrimaryButtonText, dialog.SecondaryButtonText, dialog.CloseButtonText));
    using var gate = new SemaphoreSlim(0);
    var running = ItemView.RunActionAsync(dialog, error, busy, request, async () =>
    {
      await gate.WaitAsync();
      request.PrimaryLabel = "Move to Trash";
      return "Bitwarden could not archive this item. Move it to the trash instead.";
    });
    Assert.True(busy.IsVisible);
    Assert.False(dialog.IsPrimaryButtonEnabled || dialog.IsSecondaryButtonEnabled);
    gate.Release();
    Assert.False(await running);
    Assert.False(busy.IsVisible);
    Assert.True(dialog.IsPrimaryButtonEnabled && dialog.IsSecondaryButtonEnabled);
    Assert.True(error.IsVisible);
    Assert.Equal("Move to Trash", dialog.PrimaryButtonText);
    Assert.True(await ItemView.RunActionAsync(dialog, error, busy, request, () => Task.FromResult<string?>(null)));
    Assert.False(error.IsVisible);

    var (permanent, _, _) = ItemView.CreateActionDialog(new ActionDialogRequest("Delete attachment", "Gone?", "Delete attachment"));
    Assert.Equal(string.Empty, permanent.PrimaryButtonText);
  });

  private static void SaveScreenshot(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
  {
    if (Environment.GetEnvironmentVariable("COMPANION_SCREENSHOT_DIR") is { Length: > 0 } dir)
      frame.Save(Path.Combine(dir, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
  }
}
