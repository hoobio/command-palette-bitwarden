using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.ViewModels;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Tests;

public class DesignGalleryTests(AvaloniaSession session)
{
  private const string Work = "11111111-1111-4111-8111-111111111111";
  private const string Personal = "22222222-2222-4222-8222-222222222222";
  private const string Finance = "33333333-3333-4333-8333-333333333333";
  private const string Acme = "44444444-4444-4444-8444-444444444444";
  private const string Engineering = "55555555-5555-4555-8555-555555555555";
  private const string Operations = "66666666-6666-4666-8666-666666666666";
  private const string LoginGroup = "Login credentials";
  private static readonly DateTime Revision = new(2025, 3, 14, 9, 30, 0, DateTimeKind.Utc);

  private static string? Dir => Environment.GetEnvironmentVariable("COMPANION_SCREENSHOT_DIR") is { Length: > 0 } dir ? dir : null;

  private sealed record Entry(ItemSummary Summary, ItemDto Detail, EditableItem Edit);

  private sealed class Gallery
  {
    public List<Entry> Entries { get; } = [];

    public bool Empty { get; set; }

    public string Id(string name) => Entries.Single(e => e.Summary.Name == name).Summary.Id;

    public Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken) => Task.FromResult<JsonElement?>(op switch
    {
      Ops.ListItems => IpcJson.ToElement(new ItemListResponse(Empty ? [] : [.. Entries.Select(e => e.Summary)]), IpcJson.Default.ItemListResponse),
      Ops.ListFilters => IpcJson.ToElement(new FiltersResponse(
        [new FilterRef(Finance, "Finance"), new FilterRef(Personal, "Personal"), new FilterRef(Work, "Work")],
        [new FilterRef(Engineering, "Engineering", Acme), new FilterRef(Operations, "Operations", Acme)],
        [new FilterRef(Acme, "Acme Ltd")]), IpcJson.Default.FiltersResponse),
      Ops.GetItem => IpcJson.ToElement(Find(IpcJson.Read(payload, IpcJson.Default.ItemRequest).Id).Detail, IpcJson.Default.ItemDto),
      Ops.GetEditableItem => IpcJson.ToElement(Find(IpcJson.Read(payload, IpcJson.Default.ItemRequest).Id).Edit, IpcJson.Default.EditableItem),
      Ops.GetTotp => IpcJson.ToElement(new TotpResponse("482 913", 18, 30), IpcJson.Default.TotpResponse),
      Ops.Reveal => IpcJson.ToElement(new SecretResponse("correct-Horse-7-battery"), IpcJson.Default.SecretResponse),
      Ops.GetPasswordHistory => IpcJson.ToElement(new PasswordHistoryResponse([Revision.AddDays(-12), Revision.AddDays(-95), Revision.AddDays(-400)]), IpcJson.Default.PasswordHistoryResponse),
      Ops.Generate => Generate(IpcJson.Read(payload, IpcJson.Default.GeneratorOptions)),
      Ops.ListGeneratorHistory => IpcJson.ToElement(new GeneratorHistoryResponse(
      [
        new GeneratedEntry(4, GeneratorKinds.Password, Revision.AddMinutes(-2)),
        new GeneratedEntry(3, GeneratorKinds.Passphrase, Revision.AddMinutes(-9)),
        new GeneratedEntry(2, GeneratorKinds.PlusAddress, Revision.AddHours(-3)),
      ]), IpcJson.Default.GeneratorHistoryResponse),
      _ => null,
    });

    private Entry Find(string id) => Entries.Single(e => e.Summary.Id == id);

    private static JsonElement Generate(GeneratorOptions options) => IpcJson.ToElement(new GeneratedResponse(options.Kind switch
    {
      GeneratorKinds.Passphrase => "Granite-Velvet-Harbor-Lantern",
      GeneratorKinds.PlusAddress => "jane+k3v9q2xa@example.com",
      GeneratorKinds.CatchAll => "k3v9q2xa@example.com",
      _ => "rT8#mQ2v$Lx9pW4n!Zc7",
    }, 5), IpcJson.Default.GeneratedResponse);
  }

  private static Gallery Build()
  {
    var g = new Gallery();
    var github = new EditableItem(null, 1, "GitHub", Work, null, [], true, false, "Recovery codes are in the safe.\nSSO through the company IdP.", Revision,
      [new CustomFieldEdit("Account ID", "octo-4821", 0), new CustomFieldEdit("API token", "ghp_x8Kq", 1), new CustomFieldEdit("Two-factor on", "true", 2), new CustomFieldEdit("Linked password", null, 3, 101)],
      Login: new LoginEdit("octocat@example.com", "correct-Horse-7-battery", "JBSWY3DPEHPK3PXP", [new UriEdit("https://github.com/login", 1), new UriEdit("https://gist.github.com")]));
    Add(g, github, "octocat@example.com", hasTotp: true,
    [
      new(ItemFields.Username, "Username", LoginGroup, "octocat@example.com", false),
      new(ItemFields.Password, "Password", LoginGroup, null, true),
      new(ItemFields.Totp, "Verification code (TOTP)", LoginGroup, null, true),
      new(ItemFields.UriPrefix + "0", "Website", "Autofill options", "https://github.com/login", false, CanOpen: true),
      new(ItemFields.UriPrefix + "1", "Website", "Autofill options", "https://gist.github.com", false, CanOpen: true),
      new(ItemFields.CustomPrefix + "0", "Account ID", "Custom fields", "octo-4821", false),
      new(ItemFields.CustomPrefix + "1", "API token", "Custom fields", null, true),
      new(ItemFields.CustomPrefix + "2", "Two-factor on", "Custom fields", "true", false),
      new(ItemFields.Notes, "Notes", "Additional options", null, true, IsMultiline: true),
    ], attachments: [new("a1", "recovery-codes.pdf", "48 KB"), new("a2", "ssh-config.txt", "2 KB")], passwordUpdated: Revision.AddDays(-12));

    Login(g, "Google", "jane.doe@gmail.com", Personal, favorite: true);
    Login(g, "Amazon", "jane@example.com", null);
    Login(g, "Netflix", "family@example.com", Personal);
    Login(g, "Microsoft 365", "jane.doe@acme.example", null, org: Acme, collections: [Engineering]);
    Login(g, "AWS Console", "jane.doe", null, org: Acme, collections: [Engineering], totp: true);
    Login(g, "Jira", "jdoe", null, org: Acme, collections: [Operations]);
    Login(g, "Slack", "jane@acme.example", Work);
    Login(g, "Bank of Example", "jdoe-4471", Finance, reprompt: true);
    Login(g, "PayPal", "jane@example.com", Finance);
    Login(g, "Steam", "jdoe_games", null, favorite: true);
    Login(g, "Dropbox", "jane@example.com", Personal);

    Card(g, "Visa Platinum", "Visa", "4242", Finance, favorite: true);
    Card(g, "Mastercard Business", "Mastercard", "5100", null, org: Acme, collections: [Operations]);
    Card(g, "Amex Gold", "American Express", "1005", Finance);

    Identity(g, "Jane Doe", "jane@example.com", Personal);
    Identity(g, "Work identity", "jane.doe@acme.example", Work);
    Identity(g, "Passport details", "jane@example.com", null);

    Note(g, "Wi-Fi codes", Personal);
    Note(g, "Server notes", Work);
    Note(g, "Door codes", null);
    Note(g, "Insurance policy", Finance);

    Ssh(g, "Build server", null, org: Acme, collections: [Engineering]);
    Ssh(g, "Home lab", Work);
    Ssh(g, "Raspberry Pi", Personal);

    Login(g, "Old forum", "former-user", null, deleted: true);
    Card(g, "Expired card", "Visa", "0005", null, deleted: true);
    Login(g, "Retired VPN", "vpn-user", Work, archived: true);
    return g;
  }

  private static void Add(Gallery g, EditableItem edit, string? subtext, bool hasTotp, IReadOnlyList<FieldDto> fields,
    IReadOnlyList<AttachmentDto>? attachments = null, DateTime? passwordUpdated = null, bool deleted = false, bool archived = false, bool reprompt = false)
  {
    var id = Guid.NewGuid().ToString();
    var folderName = edit.FolderId switch { Work => "Work", Personal => "Personal", Finance => "Finance", _ => null };
    g.Entries.Add(new Entry(
      new ItemSummary(id, edit.Type, edit.Name, subtext, edit.Favorite, edit.FolderId, edit.OrganizationId, edit.CollectionIds, deleted, archived, Revision, hasTotp, reprompt, edit.Notes is not null, []),
      new ItemDto(id, edit.Name, edit.Type, folderName, edit.OrganizationId is null ? null : "Acme Ltd", edit.Favorite, reprompt, reprompt, Revision.AddDays(-700), Revision, passwordUpdated,
        reprompt ? [] : fields, deleted, archived, reprompt ? null : attachments ?? []),
      edit with { Id = id, Reprompt = reprompt }));
  }

  private static void Login(Gallery g, string name, string username, string? folder, bool favorite = false, string? org = null, string[]? collections = null,
    bool totp = false, bool reprompt = false, bool deleted = false, bool archived = false)
  {
    var host = name.ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal) + ".com";
    var edit = new EditableItem(null, 1, name, folder, org, collections ?? [], favorite, reprompt, null, Revision, [],
      Login: new LoginEdit(username, "s3cret-Value-12", totp ? "JBSWY3DPEHPK3PXP" : null, [new UriEdit("https://" + host)]));
    List<FieldDto> fields = [new(ItemFields.Username, "Username", LoginGroup, username, false), new(ItemFields.Password, "Password", LoginGroup, null, true)];
    if (totp)
      fields.Add(new(ItemFields.Totp, "Verification code (TOTP)", LoginGroup, null, true));
    fields.Add(new(ItemFields.UriPrefix + "0", "Website", "Autofill options", "https://" + host, false, CanOpen: true));
    Add(g, edit, username, totp, fields, deleted: deleted, archived: archived, reprompt: reprompt);
  }

  private static void Card(Gallery g, string name, string brand, string last4, string? folder, bool favorite = false, string? org = null, string[]? collections = null, bool deleted = false)
  {
    var edit = new EditableItem(null, 3, name, folder, org, collections ?? [], favorite, false, null, Revision, [],
      Card: new CardEdit("Jane Doe", brand, "424242424242" + last4, "09", "2029", "123"));
    Add(g, edit, $"{brand} ····{last4}", false,
    [
      new(ItemFields.CardholderName, "Cardholder name", "Card details", "Jane Doe", false),
      new(ItemFields.CardBrand, "Brand", "Card details", brand, false),
      new(ItemFields.CardNumber, "Number", "Card details", null, true),
      new(ItemFields.CardExpiration, "Expiration date", "Card details", "09 / 2029", false),
      new(ItemFields.CardCode, "Security code", "Card details", null, true),
    ], deleted: deleted);
  }

  private static void Identity(Gallery g, string name, string email, string? folder)
  {
    var edit = new EditableItem(null, 4, name, folder, null, [], false, false, null, Revision, [],
      Identity: new IdentityEdit("Ms", "Jane", "Q", "Doe", "1 Park Row", "Flat 2", null, "Leeds", "West Yorkshire", "LS1 5AB", "GB", "Acme Ltd", email, "0113 496 0000", "QQ123456C", "jdoe", "502135326", "DOE99901019JQ9AB"));
    Add(g, edit, email, false,
    [
      new(ItemFields.IdentityName, "Name", "Personal details", "Jane Q Doe", false),
      new(ItemFields.IdentityUsername, "Username", "Personal details", "jdoe", false),
      new(ItemFields.IdentityCompany, "Company", "Personal details", "Acme Ltd", false),
      new(ItemFields.IdentityEmail, "Email", "Contact info", email, false),
      new(ItemFields.IdentityPhone, "Phone", "Contact info", "0113 496 0000", false),
      new(ItemFields.IdentityAddress, "Address", "Contact info", "1 Park Row, Flat 2\nLeeds, West Yorkshire, LS1 5AB\nGB", false, IsMultiline: true),
    ]);
  }

  private static void Note(Gallery g, string name, string? folder)
  {
    var edit = new EditableItem(null, 2, name, folder, null, [], false, false, "Network: Home-5G\nPassword: in the router drawer", Revision, []);
    Add(g, edit, null, false, [new(ItemFields.Notes, "Notes", "Additional options", null, true, IsMultiline: true)]);
  }

  private static void Ssh(Gallery g, string name, string? folder, string? org = null, string[]? collections = null)
  {
    var edit = new EditableItem(null, 5, name, folder, org, collections ?? [], false, false, null, Revision, [new CustomFieldEdit("host", "build.acme.example", 0)],
      SshKey: new SshKeyEdit("-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAABG5vbmU=\n-----END OPENSSH PRIVATE KEY-----", "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIB7 jane@acme", "SHA256:9x3Kf2Lq7hVb0YtRz8mN1pQwEeC4uAiJsDgHkL5oPZw"));
    Add(g, edit, "SHA256:9x3Kf2Lq7hVb0YtRz8mN1pQwEeC4uAiJsDgHkL5oPZw", false,
    [
      new(ItemFields.SshPrivateKey, "Private key", "SSH key", null, true, IsMultiline: true),
      new(ItemFields.SshPublicKey, "Public key", "SSH key", "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIB7 jane@acme", false),
      new(ItemFields.SshFingerprint, "Fingerprint", "SSH key", "SHA256:9x3Kf2Lq7hVb0YtRz8mN1pQwEeC4uAiJsDgHkL5oPZw", false),
      new(ItemFields.CustomPrefix + "0", "host", "Custom fields", "build.acme.example", false),
    ]);
  }

  private sealed class Host : IDisposable
  {
    private readonly ChannelPair _pair;

    public Host(Gallery gallery)
    {
      Gallery = gallery;
      _pair = new ChannelPair(gallery.HandleAsync);
      Channel = new PipeChannel(_pair.CompanionInput, _pair.CompanionOutput, (_, _, _) => Task.FromResult<JsonElement?>(null));
      _ = Task.Run(Channel.RunAsync);
    }

    public Gallery Gallery { get; }

    public PipeChannel Channel { get; }

    public void Dispose()
    {
      Channel.Dispose();
      _pair.Dispose();
    }
  }

  private static void Settle(TopLevel top)
  {
    for (var i = 0; i < 40; i++)
    {
      AvaloniaHeadlessPlatform.ForceRenderTimerTick();
      Dispatcher.UIThread.RunJobs();
    }

    top.UpdateLayout();
  }

  private static void Save(TopLevel top, string name)
  {
    Settle(top);
    var frame = top.CaptureRenderedFrame();
    Assert.NotNull(frame);
    frame.Save(Path.Combine(Dir!, name), new PngBitmapEncoderOptions());
  }

  private static async Task<MainWindow> VaultAsync(Host host, int height = 800)
  {
    var vm = new VaultViewModel(host.Channel, _ => { });
    var window = new MainWindow(vm) { Width = 1280, Height = height };
    window.Show();
    await vm.ReloadAsync();
    Settle(window);
    return window;
  }

  private static async Task<(ItemWindow Window, ItemViewModel Vm)> ItemAsync(Host host, string name, double height = 760)
  {
    var vm = new ItemViewModel(host.Gallery.Id(name), host.Channel);
    var window = new ItemWindow(vm) { Width = 440, Height = height };
    window.Show();
    await vm.LoadAsync();
    Settle(window);
    return (window, vm);
  }

  private static void Select(VaultViewModel vm, string name)
  {
    vm.SelectedRow = vm.Rows.Single(r => r.Name == name);
    AvaloniaSession.WaitFor(() => vm.Detail is { IsLoading: false } detail && detail.Title == name);
  }

  private static void ShowDialog(TopLevel window, FAContentDialog dialog, string name)
  {
    var shown = dialog.ShowAsync(window);
    AvaloniaSession.WaitFor(() => dialog.IsAttachedToVisualTree());
    Save(window, name);
    dialog.Hide();
    AvaloniaSession.WaitFor(() => shown.IsCompleted);
  }

  private static void OpenFlyoutInOverlay(PopupFlyoutBase flyout, Control target)
  {
    var popup = (Popup)typeof(PopupFlyoutBase).GetProperty("Popup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.GetValue(flyout)!;
    popup.ShouldUseOverlayLayer = true;
    flyout.ShowAt(target);
  }

  private static void OpenComboInOverlay(TopLevel window, ComboBox box, string name)
  {
    box.ApplyTemplate();
    box.GetVisualDescendants().OfType<Popup>().Single().ShouldUseOverlayLayer = true;
    box.IsDropDownOpen = true;
    AvaloniaSession.WaitFor(() => box.GetVisualDescendants().OfType<Popup>().Single().IsOpen);
    Save(window, name);
    box.IsDropDownOpen = false;
  }

  [Fact]
  public Task Gallery_VaultWindow() => Dir is null ? Task.CompletedTask : session.RunAsync(async () =>
  {
    using var host = new Host(Build());
    var window = await VaultAsync(host);
    var vm = window.ViewModel!;

    Select(vm, "GitHub");
    Save(window, "01-vault-all-items.png");

    vm.SearchText = "git";
    vm.ApplySearch();
    Save(window, "02-vault-search-results.png");
    vm.SearchText = "zzzz";
    vm.ApplySearch();
    Save(window, "03-vault-search-no-results.png");
    vm.ClearSearch();

    var filters = new[] { ("Favourites", "04-vault-filter-favourites.png"), ("Card", "05-vault-filter-type-card.png"), ("Work", "06-vault-filter-folder-work.png"),
      ("Engineering", "07-vault-filter-collection.png"), ("Trash", "08-vault-filter-trash.png"), ("Archive", "09-vault-filter-archive.png") };
    foreach (var (label, file) in filters)
    {
      vm.SelectedFilter = vm.Filters.Single(f => f.Label == label && !f.IsHeader);
      if (vm.Rows.Count > 0)
        Select(vm, vm.Rows[0].Name);
      Save(window, file);
    }

    vm.SelectedFilter = vm.Filters[0];
    Select(vm, "GitHub");
    await vm.Detail!.EditCommand.ExecuteAsync(null);
    Save(window, "10-vault-edit-in-right-pane.png");
    await vm.Detail.CancelEditCommand.ExecuteAsync(null);

    var newItem = window.FindControl<DropDownButton>("NewItemButton")!;
    OpenFlyoutInOverlay((PopupFlyoutBase)newItem.Flyout!, newItem);
    Save(window, "11-vault-new-item-type-picker.png");
    newItem.Flyout!.Hide();

    await vm.NewItemCommand.ExecuteAsync("1");
    Save(window, "12-vault-new-login-form.png");
    await vm.Detail!.CancelEditCommand.ExecuteAsync(null);

    Select(vm, "GitHub");
    await vm.CloneCommand.ExecuteAsync(null);
    Save(window, "13-vault-clone-form.png");
    await vm.Detail!.CancelEditCommand.ExecuteAsync(null);

    var rail = window.FindControl<ListBox>("FilterList")!;
    var work = rail.GetVisualDescendants().OfType<StackPanel>().Single(s => s.DataContext is FilterViewModel { Label: "Work" } && s.Orientation == Avalonia.Layout.Orientation.Horizontal);
    work.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
    var menu = (MenuFlyout)FlyoutBase.GetAttachedFlyout(work)!;
    menu.Hide();
    OpenFlyoutInOverlay(menu, work);
    Save(window, "14-vault-folder-context-menu.png");
    menu.Hide();

    ShowDialog(window, MainWindow.CreateTextDialog("New folder", "Create", new TextBox { PlaceholderText = "Work", MinWidth = 320 }), "15-dialog-folder-create.png");
    ShowDialog(window, MainWindow.CreateTextDialog("Rename folder", "Rename", new TextBox { Text = "Work", MinWidth = 320 }), "16-dialog-folder-rename.png");
    ShowDialog(window, ItemView.CreateActionDialog(new ActionDialogRequest("Delete folder", "Delete the folder \"Work\"? Its items move to No folder. You cannot undo this.", "Delete folder")).Dialog,
      "17-dialog-folder-delete.png");

    vm.OnLocked();
    Save(window, "18-vault-locked.png");
    window.Close();

    host.Gallery.Empty = true;
    var empty = await VaultAsync(host);
    Save(empty, "19-vault-empty.png");
    empty.Close();
  });

  [Fact]
  public Task Gallery_ItemWindows() => Dir is null ? Task.CompletedTask : session.RunAsync(async () =>
  {
    using var host = new Host(Build());
    var (full, _) = await ItemAsync(host, "GitHub", 1500);
    Save(full, "20-item-login-full.png");
    full.Close();

    var (login, loginVm) = await ItemAsync(host, "GitHub");
    Save(login, "21-item-login.png");
    await loginVm.ToggleRevealAsync(loginVm.Groups[0].Fields.Single(f => f.Key == ItemFields.Password));
    Save(login, "22-item-login-password-revealed.png");
    await loginVm.ShowPasswordHistoryCommand.ExecuteAsync(null);
    login.Height = 1700;
    Save(login, "23-item-password-history.png");
    login.Close();

    foreach (var (name, file) in new[] { ("Visa Platinum", "24-item-card.png"), ("Jane Doe", "25-item-identity.png"), ("Wi-Fi codes", "26-item-secure-note.png"),
      ("Build server", "27-item-ssh-key.png"), ("Bank of Example", "28-item-reprompt-locked.png") })
    {
      var (window, _) = await ItemAsync(host, name);
      Save(window, file);
      window.Close();
    }

    var (errorWindow, errorVm) = await ItemAsync(host, "Google");
    errorVm.ErrorMessage = ItemViewModel.Describe(new IpcException(IpcErrors.Conflict));
    Save(errorWindow, "29-item-error-line.png");
    errorVm.ErrorMessage = null;
    ShowDialog(errorWindow, ItemView.CreateRepromptDialog(new TextBox { PasswordChar = '•', PlaceholderText = "Master password", MinWidth = 320 }), "30-dialog-reprompt.png");
    ShowDialog(errorWindow, ItemView.CreateActionDialog(new ActionDialogRequest("Remove item",
      "Archive \"Google\" or move it to the trash to keep it recoverable, or delete it permanently. You cannot undo a permanent delete.", "Permanently delete") { PrimaryLabel = "Archive" }).Dialog,
      "31-dialog-delete.png");
    ShowDialog(errorWindow, ItemView.CreateActionDialog(new ActionDialogRequest("Delete permanently", "\"Old forum\" will be deleted permanently. You cannot undo this.", "Permanently delete")).Dialog,
      "32-dialog-delete-from-trash.png");
    ShowDialog(errorWindow, ItemView.CreateConfirmDialog("Item changed", "This item changed in the vault after you started editing.", "Reload", "Keep editing"), "33-dialog-conflict.png");
    ShowDialog(errorWindow, ItemView.CreateConfirmDialog("Discard changes?", "Your changes to this item are not saved.", "Discard", "Keep editing"), "34-dialog-unsaved-changes.png");
    ShowDialog(errorWindow, ItemView.CreateConfirmDialog("Rotate password", "Replace the password for Google? The old password is kept in password history.", "Rotate", "Cancel"),
      "35-dialog-quick-rotate.png");
    ShowDialog(errorWindow, ItemView.CreateActionDialog(new ActionDialogRequest("Delete attachment", "\"recovery-codes.pdf\" will be deleted permanently. You cannot undo this.", "Delete attachment")).Dialog,
      "36-dialog-attachment-delete.png");
    errorWindow.Close();

    var (attachments, _) = await ItemAsync(host, "GitHub", 1500);
    var scroll = attachments.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.IsEffectivelyVisible);
    scroll.Offset = new Avalonia.Vector(0, scroll.Extent.Height);
    attachments.Height = 760;
    Settle(attachments);
    scroll.Offset = new Avalonia.Vector(0, scroll.Extent.Height);
    Save(attachments, "37-item-attachments-group.png");
    attachments.Close();
  });

  [Fact]
  public Task Gallery_EditForms() => Dir is null ? Task.CompletedTask : session.RunAsync(async () =>
  {
    using var host = new Host(Build());
    foreach (var (name, file) in new[] { ("GitHub", "40-edit-login-full.png"), ("Visa Platinum", "41-edit-card-full.png"), ("Jane Doe", "42-edit-identity-full.png"),
      ("Wi-Fi codes", "43-edit-secure-note-full.png"), ("Build server", "44-edit-ssh-key-full.png") })
    {
      var (window, vm) = await ItemAsync(host, name, 1800);
      await vm.EditCommand.ExecuteAsync(null);
      Save(window, file);
      if (name == "GitHub")
      {
        vm.Editor!.Name = string.Empty;
        vm.Editor.Validate();
        Save(window, "45-edit-validation-error.png");
        vm.Editor.Name = name;
        vm.Editor.Validate();
        window.Height = 760;
        Settle(window);
        OpenComboInOverlay(window, window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "FolderBox"), "46-edit-folder-dropdown.png");
        var match = window.GetVisualDescendants().OfType<ComboBox>().First(c => c.Name is null && c.ItemsSource == ItemEditorViewModel.MatchChoices);
        match.BringIntoView();
        Settle(window);
        OpenComboInOverlay(window, match, "47-edit-uri-match-dropdown.png");
      }

      await vm.CancelEditCommand.ExecuteAsync(null);
      window.Close();
    }
  });

  [Fact]
  public Task Gallery_Generator() => Dir is null ? Task.CompletedTask : session.RunAsync(async () =>
  {
    File.Delete(CompanionSettings.FilePath);
    using var host = new Host(Build());
    foreach (var (tab, file) in new[] { (0, "50-generator-password.png"), (1, "51-generator-passphrase.png"), (2, "52-generator-username.png") })
    {
      var vm = new GeneratorViewModel(host.Channel) { SelectedTab = tab, Special = true, Email = "jane@example.com" };
      if (tab == 2)
        await vm.RegenerateCommand.ExecuteAsync(null);
      await vm.InitializeAsync();
      var window = new Window { Width = 480, Height = 1000, Content = new ScrollViewer { Content = new GeneratorView { DataContext = vm, Margin = new Avalonia.Thickness(24) } } };
      window.Show();
      Save(window, file);
      window.Close();
    }

    var dialogHost = new Window { Width = 600, Height = 900 };
    dialogHost.Show();
    var dialogVm = new GeneratorViewModel(host.Channel, canUse: true);
    await dialogVm.InitializeAsync();
    ShowDialog(dialogHost, GeneratorView.CreateDialog(dialogVm), "53-generator-dialog-from-edit-form.png");
    dialogHost.Close();
    File.Delete(CompanionSettings.FilePath);
  });
}
