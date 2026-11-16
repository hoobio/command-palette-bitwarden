using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.ViewModels;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Tests;

public class ItemWindowTests(AvaloniaSession session)
{
  private static readonly string ItemId = Guid.NewGuid().ToString();
  private const string Notes = "line one\nline two";

  private sealed class FakeExtension
  {
    public bool RepromptRequired { get; set; }
    public bool RepromptEveryOp { get; set; }
    public bool StaleOnce { get; set; }
    public string? ErrorCode { get; set; }
    public int TotpRemaining { get; set; } = 3;
    public int TotpCalls { get; private set; }
    public List<string> Requests { get; } = [];

    public int Count(string op)
    {
      lock (Requests)
        return Requests.Count(r => r == op);
    }

    public async Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
    {
      if (op == Ops.VerifyReprompt && VerifyGate is { } gate)
        await gate.WaitAsync(cancellationToken);
      return Handle(op, payload, verified: false);
    }

    public bool WithNotes { get; set; }

    public int ExtraCount { get; set; }

    public EditableItem? Editable { get; set; }

    public IReadOnlyList<FieldDto>? ViewFields { get; set; }

    public List<JsonElement?> CopyPayloads { get; } = [];

    public List<JsonElement?> OpenPayloads { get; } = [];

    private JsonElement? Handle(string op, JsonElement? payload, bool verified)
    {
      lock (Requests)
      {
        Requests.Add(op);
        if (op == Ops.Copy)
          CopyPayloads.Add(payload);
        if (op == Ops.OpenUri)
          OpenPayloads.Add(payload);
      }
      if (ErrorCode is { } code)
        throw new IpcException(code);
      if (RepromptEveryOp && !verified && op is Ops.Reveal or Ops.Copy or Ops.GetTotp)
        throw new IpcException(IpcErrors.Reprompt);
      if (StaleOnce && op is Ops.Reveal)
      {
        StaleOnce = false;
        throw new IpcException(IpcErrors.Stale);
      }

      return op switch
      {
        Ops.GetItem => IpcJson.ToElement(Item(), IpcJson.Default.ItemDto),
        Ops.Reveal => IpcJson.ToElement(new SecretResponse(IpcJson.Read(payload, IpcJson.Default.FieldRequest).Field == ItemFields.Notes ? Notes : "hunter2"), IpcJson.Default.SecretResponse),
        Ops.GetTotp => IpcJson.ToElement(new TotpResponse($"00000{++TotpCalls}", TotpRemaining, 30), IpcJson.Default.TotpResponse),
        Ops.VerifyReprompt => VerifyReprompt(IpcJson.Read(payload, IpcJson.Default.RepromptRequest)),
        Ops.ListFilters => IpcJson.ToElement(new FiltersResponse([], [], []), IpcJson.Default.FiltersResponse),
        Ops.GetEditableItem => IpcJson.ToElement(
          Editable ?? new EditableItem(ItemId, 1, "GitHub", null, null, [], false, true, null, new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc), [],
            Login: new LoginEdit("octocat", "hunter2", null, [new UriEdit("https://github.com"), .. Enumerable.Range(1, ExtraCount).Select(i => new UriEdit($"https://e{i}.example"))])) with
          {
            Fields = [.. Enumerable.Range(0, ExtraCount).Select(i => new CustomFieldEdit($"f{i}", $"v{i}", 0))],
          },
          IpcJson.Default.EditableItem),
        _ => null,
      };
    }

    public bool BiometricAvailable { get; set; }

    public bool BiometricVerifies { get; set; }

    public List<RepromptRequest> Verifications { get; } = [];

    private JsonElement VerifyReprompt(RepromptRequest request)
    {
      lock (Requests)
        Verifications.Add(request);
      var ok = request.Biometric ? BiometricVerifies : request.Password == "correct";
      if (ok)
      {
        RepromptRequired = false;
        _grace = GraceSeconds > 0 ? new GraceDto(DateTime.UtcNow.AddSeconds(GraceSeconds), GraceSeconds) : null;
      }

      var result = ok && request.Op is { } op ? Handle(op, request.Payload, verified: true) : null;
      return IpcJson.ToElement(new RepromptResponse(ok, 0, result, _grace), IpcJson.Default.RepromptResponse);
    }

    private GraceDto? _grace;

    public int GraceSeconds { get; set; }

    public SemaphoreSlim? VerifyGate { get; set; }

    private ItemDto Item() => new(
      ItemId, "GitHub", 1, "Work", "Acme", false, true, RepromptRequired,
      new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc), null,
      RepromptRequired ? [] : ViewFields is { } custom ? [.. custom] :
      [
        new FieldDto(ItemFields.Username, "Username", "Login credentials", "octocat", false),
        new FieldDto(ItemFields.Password, "Password", "Login credentials", null, true),
        new FieldDto(ItemFields.Totp, "Verification code (TOTP)", "Login credentials", null, true),
        new FieldDto(ItemFields.UriPrefix + "0", "Website", "Autofill options", "https://github.com", false, CanOpen: true),
        .. Enumerable.Range(1, ExtraCount).Select(i => new FieldDto(ItemFields.UriPrefix + i, "Website", "Autofill options", $"https://e{i}.example", false)),
        .. Enumerable.Range(0, ExtraCount).Select(i => new FieldDto(ItemFields.CustomPrefix + i, $"f{i}", "Custom fields", $"v{i}", false)),
        .. WithNotes ? [new FieldDto(ItemFields.Notes, "Notes", "Additional options", null, true, IsMultiline: true)] : Array.Empty<FieldDto>(),
      ],
      Grace: RepromptRequired ? null : _grace,
      HasPassword: true,
      BiometricAvailable: BiometricAvailable);
  }

  private static FieldViewModel Field(ItemViewModel vm, string key) => vm.Groups.SelectMany(g => g.Fields).Single(f => f.Key == key);

  [Fact]
  public Task ItemWindow_HasCloseOnlyChrome_AndMenuTogglesAlwaysOnTop() => session.RunAsync(() =>
  {
    ItemWindow.AlwaysOnTop = false;
    var window = new ItemWindow();
    window.Show();

    Assert.False(window.CanMinimize);
    Assert.False(window.CanMaximize);
    Assert.True(window.CanResize);
    Assert.Same(window.FindResource("HoobiCloseOnlyDecorations"), window.WindowDecorationsTheme);
    Assert.NotNull(window.CaptureRenderedFrame());

    var root = window.FindControl<Grid>("Root")!;
    root.ContextMenu!.Open(root);
    var item = window.FindControl<MenuItem>("AlwaysOnTopItem")!;
    Assert.False(item.IsChecked);
    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Assert.True(window.Topmost);
    Assert.True(item.IsChecked);
    Assert.True(ItemWindow.AlwaysOnTop);

    var second = new ItemWindow();
    Assert.True(second.Topmost);

    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Assert.False(window.Topmost);
    Assert.False(ItemWindow.AlwaysOnTop);
    window.Close();
  });

  private static Border FieldRow(ItemWindow window, string key) =>
    window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("Field") && b.DataContext is FieldViewModel field && field.Key == key);

  private static List<MenuItem> OpenRowMenu(Border row)
  {
    row.ContextMenu!.Open(row);
    return [.. row.ContextMenu.Items.OfType<MenuItem>()];
  }

  [Fact]
  public Task FieldRow_ContextMenu_OffersCopyEditOpenAndAlwaysOnTop() => session.RunAsync(async () =>
  {
    ItemWindow.AlwaysOnTop = false;
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var vm = new ItemViewModel(ItemId, channel) { Toast = (_, _) => { } };
    var window = new ItemWindow(vm);
    window.Show();
    await vm.LoadAsync();
    window.UpdateLayout();

    var username = FieldRow(window, ItemFields.Username);
    var items = OpenRowMenu(username);
    Assert.Equal(["Copy", "Edit", "Open URL", "Always on top"], items.Select(i => i.Header as string));
    Assert.Contains(username.ContextMenu!.Items, i => i is Separator);
    Assert.True(vm.CanEdit);
    Assert.True(items[1].IsVisible);
    Assert.False(items[2].IsVisible);
    username.ContextMenu.Close();

    var website = FieldRow(window, ItemFields.UriPrefix + "0");
    var websiteItems = OpenRowMenu(website);
    Assert.True(websiteItems[2].IsVisible);
    websiteItems[2].Command!.Execute(null);
    AvaloniaSession.WaitFor(() => extension.Count(Ops.OpenUri) == 1);
    Assert.Equal(ItemFields.UriPrefix + "0", IpcJson.Read(Assert.Single(extension.OpenPayloads), IpcJson.Default.UriRequest).Field);
    website.ContextMenu!.Close();

    items = OpenRowMenu(username);
    items[0].Command!.Execute(null);
    AvaloniaSession.WaitFor(() => extension.Count(Ops.Copy) == 1);
    Assert.Equal(ItemFields.Username, IpcJson.Read(Assert.Single(extension.CopyPayloads), IpcJson.Default.FieldRequest).Field);

    var onTop = items[3];
    Assert.False(onTop.IsChecked);
    onTop.Command!.Execute(null);
    Assert.True(window.Topmost);
    Assert.True(ItemWindow.AlwaysOnTop);
    Assert.True(onTop.IsChecked);
    onTop.Command.Execute(null);
    Assert.False(window.Topmost);
    Assert.False(ItemWindow.AlwaysOnTop);

    items[1].Command!.Execute(null);
    AvaloniaSession.WaitFor(() => vm.IsEditing);
    Assert.Equal(1, extension.Count(Ops.GetEditableItem));
    window.Close();
  });

  private static async Task<(ItemWindow Window, ItemViewModel ViewModel)> ShowLongItemAsync(FakeExtension extension, ChannelPair pair)
  {
    extension.ExtraCount = 12;
    var vm = new ItemViewModel(ItemId, CompanionChannel(pair)) { Toast = (_, _) => { } };
    var window = new ItemWindow(vm) { Height = 480 };
    window.Show();
    await vm.LoadAsync();
    window.UpdateLayout();
    return (window, vm);
  }

  private static ScrollViewer EditorScroll(ItemWindow window) => window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "EditorScroll");

  private static void EditFromRow(ItemWindow window, ItemViewModel vm, string key)
  {
    var row = FieldRow(window, key);
    OpenRowMenu(row)[1].Command!.Execute(null);
    AvaloniaSession.WaitFor(() => vm.IsEditing);
    window.UpdateLayout();
  }

  private static void AssertFocusedAndInView(ItemWindow window, Func<TextBox, bool> match)
  {
    AvaloniaSession.WaitFor(() => window.FocusManager!.GetFocusedElement() is TextBox box && match(box));
    var focused = (TextBox)window.FocusManager!.GetFocusedElement()!;
    var scroll = EditorScroll(window);
    var top = focused.TranslatePoint(default, scroll)!.Value.Y;
    Assert.True(top >= 0 && top + focused.Bounds.Height <= scroll.Viewport.Height, $"editor at {top} outside viewport {scroll.Viewport.Height}");
  }

  [Fact]
  public Task FieldRow_Edit_ScrollsToAndFocusesThatFieldsEditor() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    var (window, vm) = await ShowLongItemAsync(extension, pair);

    EditFromRow(window, vm, ItemFields.CustomPrefix + "10");

    AssertFocusedAndInView(window, box => box.Text == "v10");
    Assert.True(EditorScroll(window).Offset.Y > 0);
    window.Close();
  });

  [Fact]
  public Task FieldRow_Edit_OnASecretField_FocusesTheMaskedBox() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    var (window, vm) = await ShowLongItemAsync(extension, pair);

    EditFromRow(window, vm, ItemFields.Password);

    AssertFocusedAndInView(window, box => box.Name == "PasswordBox");
    Assert.Equal('•', ((TextBox)window.FocusManager!.GetFocusedElement()!).PasswordChar);
    window.Close();
  });

  [Fact]
  public Task FieldRow_Edit_OnALaterWebsite_FocusesThatWebsiteEditor() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    var (window, vm) = await ShowLongItemAsync(extension, pair);

    EditFromRow(window, vm, ItemFields.UriPrefix + "5");

    AssertFocusedAndInView(window, box => box.Text == "https://e5.example");
    window.Close();
  });

  private static EditableItem LiveItem(int type, CustomFieldEdit[] fields, LoginEdit? login = null, SshKeyEdit? ssh = null) =>
    new(ItemId, type, "GitHub", null, null, [], false, true, null, new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc), fields, Login: login, SshKey: ssh);

  private static FieldDto CustomRow(int index, string name, string? value) => new(ItemFields.CustomPrefix + index, name, "Custom fields", value, false);

  [Fact]
  public Task FieldRow_Edit_SkipsLinkedFieldsThatTheViewDoesNotList() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension
    {
      Editable = LiveItem(1, [new("a", null, 3, 100), new("b", "vb", 0), new("c", "vc", 0), new("flag", "true", 2)], new LoginEdit("octocat", "hunter2", null, [])),
      ViewFields = [CustomRow(0, "b", "vb"), CustomRow(1, "c", "vc"), CustomRow(2, "flag", "true")],
    };
    using var pair = new ChannelPair(extension.HandleAsync);
    var (window, vm) = await ShowLongItemAsync(extension, pair);

    EditFromRow(window, vm, ItemFields.CustomPrefix + "1");
    AssertFocusedAndInView(window, box => box.Text == "vc");
    await vm.CancelEditCommand.ExecuteAsync(null);

    EditFromRow(window, vm, ItemFields.CustomPrefix + "2");
    AvaloniaSession.WaitFor(() => window.FocusManager!.GetFocusedElement() is ToggleSwitch { IsChecked: true });
    var toggle = (ToggleSwitch)window.FocusManager!.GetFocusedElement()!;
    Assert.Contains(":focus-visible", toggle.Classes);
    window.Close();
  });

  [Fact]
  public Task FieldRow_Edit_SkipsEmptyWebsitesThatTheViewDoesNotList() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension
    {
      Editable = LiveItem(1, [], new LoginEdit("octocat", "hunter2", null, [new(""), new("https://x.example"), new("https://y.example")])),
      ViewFields =
      [
        new FieldDto(ItemFields.UriPrefix + "0", "Website", "Autofill options", "https://x.example", false),
        new FieldDto(ItemFields.UriPrefix + "1", "Website", "Autofill options", "https://y.example", false),
      ],
    };
    using var pair = new ChannelPair(extension.HandleAsync);
    var (window, vm) = await ShowLongItemAsync(extension, pair);

    EditFromRow(window, vm, ItemFields.UriPrefix + "1");

    AssertFocusedAndInView(window, box => box.Text == "https://y.example");
    window.Close();
  });

  [Fact]
  public Task FieldRow_Edit_OnAnSshKey_SkipsTheHostBoxWhenMappingCustomFields() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension
    {
      Editable = LiveItem(5, [new("a", "va", 0), new("host", "user@h", 0), new("b", "vb", 0)], ssh: new SshKeyEdit("private", "public", "fingerprint")),
      ViewFields = [CustomRow(0, "a", "va"), CustomRow(1, "host", "user@h"), CustomRow(2, "b", "vb")],
    };
    using var pair = new ChannelPair(extension.HandleAsync);
    var (window, vm) = await ShowLongItemAsync(extension, pair);

    EditFromRow(window, vm, ItemFields.CustomPrefix + "2");
    AssertFocusedAndInView(window, box => box.Text == "vb");
    await vm.CancelEditCommand.ExecuteAsync(null);

    EditFromRow(window, vm, ItemFields.CustomPrefix + "1");
    AssertFocusedAndInView(window, box => box.Name == "SshHostBox");
    window.Close();
  });

  [Fact]
  public Task Edit_WithoutAField_FocusesNothingAndStaysAtTheTop() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    var (window, vm) = await ShowLongItemAsync(extension, pair);

    await vm.EditCommand.ExecuteAsync(null);
    window.UpdateLayout();
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    Assert.True(vm.IsEditing);
    Assert.DoesNotContain(EditorScroll(window).GetVisualDescendants(), v => ReferenceEquals(v, window.FocusManager!.GetFocusedElement()));
    Assert.Equal(0, EditorScroll(window).Offset.Y);
    window.Close();
  });

  [Fact]
  public Task ItemViewModel_LoadsRevealsCopiesOpensAndTicksTotp() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var toasts = new List<string>();
    using var vm = new ItemViewModel(ItemId, channel) { Toast = (message, _) => toasts.Add(message) };

    await vm.LoadAsync();

    Assert.Equal("GitHub", vm.Title);
    Assert.Equal(("Work", "Acme"), (vm.FolderName, vm.OrganizationName));
    Assert.True(vm.IsProtected);
    Assert.False(vm.IsFavorite);
    Assert.Equal(["Login credentials", "Autofill options", "Item history"], vm.Groups.Select(g => g.Name));
    var password = Field(vm, ItemFields.Password);
    Assert.Equal(FieldViewModel.Mask, password.DisplayValue);
    Assert.Equal("octocat", Field(vm, ItemFields.Username).DisplayValue);

    await password.ToggleRevealCommand.ExecuteAsync(null);
    Assert.Equal("hunter2", password.DisplayValue);
    await password.ToggleRevealCommand.ExecuteAsync(null);
    Assert.Equal(FieldViewModel.Mask, password.DisplayValue);
    Assert.Null(password.RevealedValue);

    await Field(vm, ItemFields.Username).CopyCommand.ExecuteAsync(null);
    Assert.Equal(["Copied Username"], toasts);
    await Field(vm, ItemFields.UriPrefix + "0").OpenCommand.ExecuteAsync(null);
    Assert.Contains(Ops.OpenUri, extension.Requests);

    var totp = Field(vm, ItemFields.Totp);
    Assert.Equal("000001", totp.DisplayValue);
    Assert.Equal(3, totp.TotpRemaining);
    Assert.Equal(36, totp.TotpSweep);
    Assert.True(totp.IsTotpExpiring);
    Assert.False(totp.IsPlain || totp.IsMono);
    Assert.True(Field(vm, ItemFields.Username).IsPlain);
    await vm.TickAsync();
    await vm.TickAsync();
    Assert.Equal(1, totp.TotpRemaining);
    Assert.Equal(1, extension.TotpCalls);
    await vm.TickAsync();
    Assert.Equal(2, extension.TotpCalls);
    Assert.Equal("000002", totp.DisplayValue);
  });

  [Fact]
  public Task ItemWindow_RevealedSecretsAndTotp_AreNotSelectable() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var vm = new ItemViewModel(ItemId, channel);
    var window = new ItemWindow(vm);
    window.Show();
    await vm.LoadAsync();
    await Field(vm, ItemFields.Password).ToggleRevealCommand.ExecuteAsync(null);
    Assert.NotNull(window.CaptureRenderedFrame());

    var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
    Assert.Contains(texts, t => t is not SelectableTextBlock && t.Text == "hunter2");
    Assert.Contains(texts, t => t is not SelectableTextBlock && t.Text == "000001");
    Assert.DoesNotContain(texts, t => t is SelectableTextBlock && t.Text is "hunter2" or "000001");
    Assert.Contains(texts, t => t is SelectableTextBlock && t.Text == "octocat");
    window.Close();
  });

  [Fact]
  public Task ItemWindow_RevealedNotes_CopyASelectionThroughTheExtensionOnly() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { WithNotes = true };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var toasts = new List<string>();
    var vm = new ItemViewModel(ItemId, channel) { Toast = (message, _) => toasts.Add(message) };
    var window = new ItemWindow(vm);
    window.Show();
    await vm.LoadAsync();
    var notes = Field(vm, ItemFields.Notes);
    Assert.False(notes.IsSelectableSecret);
    await notes.ToggleRevealCommand.ExecuteAsync(null);
    await Field(vm, ItemFields.Password).ToggleRevealCommand.ExecuteAsync(null);
    window.UpdateLayout();

    var visible = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
    Assert.DoesNotContain(visible, t => t is SelectableTextBlock && t.Text == "hunter2");
    var block = visible.OfType<SelectableTextBlock>().Single(t => t.Text == Notes);
    var clipboard = window.Clipboard!;
    await clipboard.SetTextAsync("untouched");
    block.SelectionStart = 9;
    block.SelectionEnd = 5;
    block.Copy();

    AvaloniaSession.WaitFor(() => extension.Count(Ops.Copy) == 1);
    var request = IpcJson.Read(Assert.Single(extension.CopyPayloads), IpcJson.Default.FieldRequest);
    Assert.Equal((ItemFields.Notes, 5, 4), (request.Field, request.Start, request.Length));
    AvaloniaSession.WaitFor(() => toasts.Count == 1);
    Assert.Equal("Copied selected text", toasts[0]);
    Assert.Equal("untouched", await clipboard.TryGetTextAsync());

    var username = visible.OfType<SelectableTextBlock>().Single(t => t.Text == "octocat");
    username.SelectionStart = 0;
    username.SelectionEnd = 3;
    username.Copy();
    var copied = await clipboard.TryGetTextAsync();
    for (var i = 0; i < 100 && copied != "oct"; i++)
    {
      await Task.Delay(10);
      copied = await clipboard.TryGetTextAsync();
    }

    Assert.Equal("oct", copied);
    Assert.Equal(1, extension.Count(Ops.Copy));
    window.Close();
  });

  [Fact]
  public Task PasswordPanel_OffersWindowsHello_OnlyWhenTheExtensionAllowsIt() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { RepromptRequired = true };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var vm = new ItemViewModel(ItemId, channel);
    var window = new ItemWindow(vm);
    window.Show();
    await vm.LoadAsync();
    window.UpdateLayout();
    var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BiometricButton");
    Assert.False(button.IsEffectivelyVisible);

    extension.BiometricAvailable = true;
    await vm.LoadAsync();
    window.UpdateLayout();
    Assert.True(button.IsEffectivelyVisible);

    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    AvaloniaSession.WaitFor(() => vm.PasswordError is not null);
    Assert.Equal("Windows Hello verification was cancelled or denied.", vm.PasswordError);
    var request = Assert.Single(extension.Verifications);
    Assert.True(request.Biometric);
    Assert.Null(request.Password);
    Assert.True(vm.RepromptRequired);

    extension.BiometricVerifies = true;
    await vm.SubmitBiometricAsync();
    Assert.False(vm.RepromptRequired);
    Assert.NotEmpty(vm.Groups);
    Assert.Null(vm.PasswordError);
    window.Close();
  });

  [Fact]
  public Task ItemViewModel_RepromptFlow_UnlocksAfterVerification() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { RepromptRequired = true };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new ItemViewModel(ItemId, channel);

    await vm.LoadAsync();
    Assert.True(vm.RepromptRequired);
    Assert.Empty(vm.Groups);

    Assert.False(await vm.VerifyRepromptAsync("wrong"));
    Assert.Equal("Incorrect master password.", vm.PasswordError);

    Assert.True(await vm.VerifyRepromptAsync("correct"));
    Assert.False(vm.RepromptRequired);
    Assert.Null(vm.ErrorMessage);
    Assert.NotEmpty(vm.Groups);
  });

  [Fact]
  public Task PasswordPanel_ShowsAlone_FocusesTheBox_ShowsBusy_ReportsAWrongPassword_AndUnlocks() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { RepromptRequired = true, VerifyGate = new SemaphoreSlim(0) };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var vm = new ItemViewModel(ItemId, channel);
    var window = new ItemWindow(vm);
    window.Show();
    await vm.LoadAsync();
    var panel = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PasswordPanel");
    var box = window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "PasswordBox");
    var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "VerifyButton");
    var ring = window.GetLogicalDescendants().OfType<FluentAvalonia.UI.Controls.FAProgressRing>().Single(r => r.Name == "VerifyRing");
    var error = window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "PasswordErrorText");

    AvaloniaSession.WaitFor(() => box.IsFocused);
    Assert.True(panel.IsEffectivelyVisible);
    Assert.False(window.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "GroupList").IsEffectivelyVisible);
    Assert.False(window.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "ErrorAlert").IsEffectivelyVisible);

    box.Text = "wrong";
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    AvaloniaSession.WaitFor(() => vm.IsVerifying);
    Assert.False(box.IsEnabled || button.IsEnabled);
    Assert.True(ring.IsEffectivelyVisible);
    extension.VerifyGate.Release();

    AvaloniaSession.WaitFor(() => !vm.IsVerifying && box.IsFocused);
    Assert.True(box.IsEnabled && button.IsEnabled);
    Assert.False(ring.IsEffectivelyVisible);
    Assert.Equal("Incorrect master password.", error.Text);
    Assert.True(error.IsEffectivelyVisible);
    Assert.Equal("wrong".Length, box.SelectionEnd - box.SelectionStart);
    Assert.True(vm.RepromptRequired);

    box.Text = "correct";
    extension.VerifyGate.Release();
    box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
    AvaloniaSession.WaitFor(() => !vm.RepromptRequired && vm.Groups.Count > 0);
    Assert.False(panel.IsEffectivelyVisible);
    Assert.True(window.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "GroupList").IsEffectivelyVisible);
    Assert.Equal(2, extension.Count(Ops.VerifyReprompt));
    window.Close();
  });

  [Fact]
  public Task Grace_CountsDownFromTheExpiry_AndRelocksAtZero() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { RepromptRequired = true, GraceSeconds = 60 };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var now = DateTime.UtcNow;
    using var vm = new ItemViewModel(ItemId, channel) { UtcNow = () => now };
    await vm.LoadAsync();
    Assert.False(vm.GraceVisible);

    Assert.True(await vm.VerifyRepromptAsync("correct"));
    now = DateTime.UtcNow;
    vm.TickGrace();
    Assert.True(vm.GraceVisible);
    Assert.Equal(1, vm.GraceFraction, 2);
    Assert.Equal("Locks in 1:00", vm.GraceCaption);

    now = now.AddSeconds(18);
    vm.TickGrace();
    Assert.Equal(0.7, vm.GraceFraction, 2);
    Assert.Equal("Locks in 0:42", vm.GraceCaption);
    Assert.NotEmpty(vm.Groups);

    var password = Field(vm, ItemFields.Password);
    await password.ToggleRevealCommand.ExecuteAsync(null);
    Assert.Equal("hunter2", password.RevealedValue);
    now = now.AddSeconds(60);
    vm.TickGrace();
    Assert.True(vm.RepromptRequired);
    Assert.True(vm.IsPrompting);
    Assert.False(vm.GraceVisible);
    Assert.Empty(vm.Groups);
    Assert.Null(password.RevealedValue);

    extension.GraceSeconds = 0;
    Assert.True(await vm.VerifyRepromptAsync("correct"));
    Assert.False(vm.GraceVisible);
    Assert.NotEmpty(vm.Groups);
  });

  [Fact]
  public Task Grace_SurvivesAReload() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { RepromptRequired = true, GraceSeconds = 60 };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new ItemViewModel(ItemId, channel);
    await vm.LoadAsync();
    await vm.VerifyRepromptAsync("correct");

    await vm.LoadAsync();

    Assert.True(vm.GraceVisible);
    Assert.InRange(vm.GraceRemaining.TotalSeconds, 50, 60);

    vm.KeepOpenCommand.Execute(null);
    Assert.False(vm.GraceVisible);
    await vm.LoadAsync();
    Assert.False(vm.GraceVisible);
    Assert.False(vm.RepromptRequired);

    vm.RepromptRequired = true;
    await vm.VerifyRepromptAsync("correct");
    Assert.True(vm.GraceVisible);
    vm.KeepOpenCommand.Execute(null);

    vm.OnLocked();
    await vm.LoadAsync();
    await vm.VerifyRepromptAsync("correct");
    Assert.True(vm.GraceVisible);
  });

  [Fact]
  public Task ItemViewModel_MapsErrorsToMessages() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new ItemViewModel(ItemId, channel);
    await vm.LoadAsync();

    extension.ErrorCode = IpcErrors.Reprompt;
    await Field(vm, ItemFields.Password).ToggleRevealCommand.ExecuteAsync(null);
    Assert.True(vm.RepromptRequired);

    extension.ErrorCode = IpcErrors.Locked;
    await vm.LoadAsync();
    Assert.Equal(ItemViewModel.Describe(new IpcException(IpcErrors.Locked)), vm.ErrorMessage);
    Assert.Equal("This item is no longer in the vault.", ItemViewModel.Describe(new IpcException(IpcErrors.NotFound)));
    Assert.Equal("The connection to Command Palette was lost.", ItemViewModel.Describe(new IOException()));
    Assert.Equal("Something went wrong.", ItemViewModel.Describe(new IpcException(IpcErrors.Failed)));
    Assert.Equal("", ItemViewModel.GlyphFor(5));
  });

  [Fact]
  public Task Coordinator_ShowItem_ReusesTheOpenWindow_AndValidatesIds() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    var closed = false;
    var run = coordinator.RunAsync(() => closed = true);

    await pair.Extension.RequestAsync(Ops.ShowItem, IpcJson.ToElement(new ItemRequest(ItemId), IpcJson.Default.ItemRequest), TestContext.Current.CancellationToken);
    await pair.Extension.RequestAsync(Ops.ShowItem, IpcJson.ToElement(new ItemRequest(ItemId.ToUpperInvariant()), IpcJson.Default.ItemRequest), TestContext.Current.CancellationToken);
    var window = Assert.Single(coordinator.ItemWindows);
    AvaloniaSession.WaitFor(() => extension.Count(Ops.GetItem) == 2);
    Assert.Same(window, coordinator.ShowItem(ItemId));

    var invalid = await Assert.ThrowsAsync<IpcException>(() => pair.Extension.RequestAsync(Ops.ShowItem, IpcJson.ToElement(new ItemRequest("nope"), IpcJson.Default.ItemRequest), TestContext.Current.CancellationToken));
    Assert.Equal(IpcErrors.Invalid, invalid.Code);
    var unknown = await Assert.ThrowsAsync<IpcException>(() => pair.Extension.RequestAsync("getItem", null, TestContext.Current.CancellationToken));
    Assert.Equal(IpcErrors.Invalid, unknown.Code);

    window.Close();
    Assert.Empty(coordinator.ItemWindows);

    pair.Extension.Dispose();
    await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    AvaloniaSession.WaitFor(() => closed);
  });

  [Fact]
  public Task Coordinator_LockedEvent_WipesRevealedValuesInEveryWindow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var vm = coordinator.ShowItem(ItemId).ViewModel!;
    AvaloniaSession.WaitFor(() => vm.Groups.Count > 0 && Field(vm, ItemFields.Totp).TotpCode is not null);
    var password = Field(vm, ItemFields.Password);
    var totp = Field(vm, ItemFields.Totp);
    await password.ToggleRevealCommand.ExecuteAsync(null);
    Assert.Equal("hunter2", password.RevealedValue);
    Assert.True(vm.IsTotpRunning);

    await pair.Extension.SendEventAsync(Ops.Locked);
    AvaloniaSession.WaitFor(() => vm.Groups.Count == 0);

    Assert.Null(password.RevealedValue);
    Assert.False(password.IsRevealed);
    Assert.Null(totp.TotpCode);
    Assert.False(vm.IsTotpRunning);
    Assert.Equal(ItemViewModel.Describe(new IpcException(IpcErrors.Locked)), vm.ErrorMessage);
    foreach (var window in coordinator.ItemWindows.ToList())
      window.Close();
  });

  [Fact]
  public Task ItemViewModel_GraceZero_PromptsForEveryRevealAndRunsTheOpAfterVerify() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new ItemViewModel(ItemId, channel);
    await vm.LoadAsync();
    extension.RepromptEveryOp = true;
    var password = Field(vm, ItemFields.Password);

    await password.ToggleRevealCommand.ExecuteAsync(null);
    Assert.True(vm.RepromptRequired);
    Assert.Null(password.RevealedValue);

    Assert.True(await vm.VerifyRepromptAsync("correct"));
    Assert.False(vm.RepromptRequired);
    Assert.Equal("hunter2", password.DisplayValue);

    await password.ToggleRevealCommand.ExecuteAsync(null);
    await password.ToggleRevealCommand.ExecuteAsync(null);
    Assert.True(vm.RepromptRequired);
    Assert.False(password.IsRevealed);
    Assert.Equal(1, extension.Count(Ops.VerifyReprompt));
  });

  [Fact]
  public Task FieldDragText_UsesTheShownValue_AndRevealsAMaskedSecretWithoutShowingIt() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new ItemViewModel(ItemId, channel);
    await vm.LoadAsync();
    var password = Field(vm, ItemFields.Password);

    Assert.Equal("octocat", await Field(vm, ItemFields.Username).GetDragTextAsync());
    Assert.Equal("000001", await Field(vm, ItemFields.Totp).GetDragTextAsync());
    Assert.Equal(0, extension.Count(Ops.Reveal));

    Assert.Equal("hunter2", await password.GetDragTextAsync());
    Assert.Equal(1, extension.Count(Ops.Reveal));
    Assert.Equal(FieldViewModel.Mask, password.DisplayValue);

    await password.ToggleRevealCommand.ExecuteAsync(null);
    Assert.Equal("hunter2", await password.GetDragTextAsync());
    Assert.Equal(2, extension.Count(Ops.Reveal));
  });

  [Fact]
  public Task FieldDragText_GraceZero_RevealsTheFieldAfterVerify() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new ItemViewModel(ItemId, channel);
    await vm.LoadAsync();
    extension.RepromptEveryOp = true;
    var password = Field(vm, ItemFields.Password);

    Assert.Null(await password.GetDragTextAsync());
    Assert.True(vm.RepromptRequired);

    Assert.True(await vm.VerifyRepromptAsync("correct"));
    Assert.True(password.IsRevealed);
    Assert.Equal("hunter2", await password.GetDragTextAsync());
  });

  [Fact]
  public Task ItemWindow_DraggingAFieldLabel_PastTheThreshold_FetchesTheSecret() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var vm = new ItemViewModel(ItemId, channel);
    var window = new ItemWindow(vm);
    window.Show();
    await vm.LoadAsync();
    window.UpdateLayout();
    var label = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("FieldLabel") && t.Text == "Password");
    var start = label.TranslatePoint(new Avalonia.Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;

    window.MouseDown(start, Avalonia.Input.MouseButton.Left);
    window.MouseMove(start + new Avalonia.Point(2, 2), Avalonia.Input.RawInputModifiers.LeftMouseButton);
    window.MouseUp(start, Avalonia.Input.MouseButton.Left);
    window.MouseMove(start + new Avalonia.Point(12, 0));
    Assert.Equal(0, extension.Count(Ops.Reveal));

    window.MouseDown(start, Avalonia.Input.MouseButton.Left);
    window.MouseMove(start + new Avalonia.Point(12, 0), Avalonia.Input.RawInputModifiers.LeftMouseButton);
    AvaloniaSession.WaitFor(() => extension.Count(Ops.Reveal) == 1);
    window.MouseUp(start, Avalonia.Input.MouseButton.Left);
    Assert.Equal(FieldViewModel.Mask, Field(vm, ItemFields.Password).DisplayValue);
    window.Close();
  });

  [Fact]
  public Task ItemViewModel_Disposed_DoesNotStartTheTotpTimer() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var vm = new ItemViewModel(ItemId, channel);
    vm.Dispose();

    await vm.LoadAsync();

    Assert.Equal(1, extension.TotpCalls);
    Assert.False(vm.IsTotpRunning);
  });

  [Fact]
  public Task ItemViewModel_StaleRevision_ReloadsTheWindow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new ItemViewModel(ItemId, channel);
    await vm.LoadAsync();
    extension.StaleOnce = true;

    await Field(vm, ItemFields.Password).ToggleRevealCommand.ExecuteAsync(null);

    Assert.Equal(2, extension.Count(Ops.GetItem));
    Assert.Equal(ItemViewModel.Describe(new IpcException(IpcErrors.Stale)), vm.StatusMessage);
    Assert.Null(vm.ErrorMessage);
    Assert.False(Field(vm, ItemFields.Password).IsRevealed);
  });

  [Fact]
  public Task ItemViewModel_StaleResultFromVerify_ReloadsTheWindow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new ItemViewModel(ItemId, channel);
    await vm.LoadAsync();
    extension.RepromptEveryOp = true;
    await Field(vm, ItemFields.Password).ToggleRevealCommand.ExecuteAsync(null);
    extension.StaleOnce = true;

    Assert.False(await vm.VerifyRepromptAsync("correct"));

    Assert.Equal(2, extension.Count(Ops.GetItem));
    Assert.Equal(ItemViewModel.Describe(new IpcException(IpcErrors.Stale)), vm.StatusMessage);
  });

  [Fact]
  public Task Copy_ToastsTheLabel_NeverTheValue() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var toasts = new List<(string Message, bool Success)>();
    using var vm = new ItemViewModel(ItemId, channel) { Toast = (message, success) => toasts.Add((message, success)) };
    await vm.LoadAsync();
    var password = Field(vm, ItemFields.Password);
    await password.ToggleRevealCommand.ExecuteAsync(null);

    await password.CopyCommand.ExecuteAsync(null);

    Assert.Equal([("Copied Password", true)], toasts);
    Assert.DoesNotContain(toasts, t => t.Message.Contains("hunter2", StringComparison.Ordinal));
    Assert.Null(vm.StatusMessage);
    Assert.Null(vm.ErrorMessage);
  });

  [Fact]
  public Task CopyFailure_StaysInline_AndShowsNoToast() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var toasts = new List<string>();
    using var vm = new ItemViewModel(ItemId, channel) { Toast = (message, _) => toasts.Add(message) };
    await vm.LoadAsync();
    extension.ErrorCode = IpcErrors.NotFound;

    await Field(vm, ItemFields.Username).CopyCommand.ExecuteAsync(null);

    Assert.Empty(toasts);
    Assert.Equal(ItemViewModel.Describe(new IpcException(IpcErrors.NotFound)), vm.ErrorMessage);
  });

  [Fact]
  public Task ItemWindow_Copy_ShowsTheSharedToast_AndANewMessageReplacesIt() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new ItemViewModel(ItemId, channel);
    var window = new ItemWindow(vm);
    window.Show();
    await vm.LoadAsync();

    await Field(vm, ItemFields.Username).CopyCommand.ExecuteAsync(null);

    var toast = ToastWindow.Current!;
    Assert.True(toast.IsVisible);
    Assert.False(toast.ShowActivated);
    Assert.False(toast.ShowInTaskbar);
    Assert.True(toast.Topmost);
    Assert.Equal(("Copied Username", true), (toast.Text, toast.IsSuccess));
    Assert.True(toast.IsHideScheduled);
    Assert.Equal(1, toast.CardOpacity);

    toast.FadeOut();
    Assert.True(toast.IsFading);
    Assert.False(toast.IsHideScheduled);
    Assert.Equal(0, toast.CardOpacity);

    ToastWindow.Show(window, "No history", success: false);

    Assert.Same(toast, ToastWindow.Current);
    Assert.Equal(("No history", false), (toast.Text, toast.IsSuccess));
    Assert.False(toast.IsFading);
    Assert.True(toast.IsHideScheduled);
    Assert.Equal(1, toast.CardOpacity);
    window.Close();
  });

  [Fact]
  public Task Coordinator_ReadLoopFault_StillShutsDownTheCompanion() => session.RunAsync(async () =>
  {
    using var coordinator = new WindowCoordinator(new FaultingStream(), new MemoryStream());
    var closed = false;

    await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(() => closed = true));

    AvaloniaSession.WaitFor(() => closed);
  });

  private sealed class FaultingStream : Stream
  {
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("fault");
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }

  private static PipeChannel CompanionChannel(ChannelPair pair)
  {
    var channel = new PipeChannel(pair.CompanionInput, pair.CompanionOutput, (_, _, _) => Task.FromResult<JsonElement?>(null));
    _ = Task.Run(channel.RunAsync);
    return channel;
  }
}
