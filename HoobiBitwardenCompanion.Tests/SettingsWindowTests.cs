using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.ViewModels;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Tests;

public class SettingsWindowTests(AvaloniaSession session)
{
  private sealed class FakeExtension
  {
    public List<SettingDto> Schema { get; } =
    [
      new("_section_a", SettingKinds.Section, "General", null, null, null),
      new("showIcons", SettingKinds.Toggle, "Show icons", "Download website icons", "true", null),
      new("limit", SettingKinds.Choice, "Limit", null, "3", [new("1 item", "1"), new("3 items", "3"), new("5 items", "5")]),
      new("_section_b", SettingKinds.Section, "Paths", null, null, null),
      new("cliPath", SettingKinds.Text, "CLI path", "Path to bw", string.Empty, null),
    ];

    public List<(string Key, string Value)> Sets { get; } = [];

    public List<SetServerRequest> Servers { get; } = [];

    public SetServerResponse ServerReply { get; set; } = new(true, null);

    public string? Reject { get; set; }

    public bool FailLoad { get; set; }

    public AboutResponse About { get; set; } = new("1.13.0", "626a78a", AppChannels.Release, false);

    public UpdateResponse? Update { get; set; }

    public string InstallState { get; set; } = InstallStates.Installed;

    public int Installs { get; private set; }

    public void Change(string key, string value)
    {
      lock (Schema)
      {
        var index = Schema.FindIndex(s => s.Key == key);
        Schema[index] = Schema[index] with { Value = value };
      }
    }

    public Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
    {
      switch (op)
      {
        case Ops.GetSettings:
          if (FailLoad)
            throw new IpcException(IpcErrors.Failed);
          lock (Schema)
            return Task.FromResult<JsonElement?>(IpcJson.ToElement(new SettingsResponse([.. Schema]), IpcJson.Default.SettingsResponse));
        case Ops.SetSetting:
          var request = IpcJson.Read(payload, IpcJson.Default.SetSettingRequest);
          lock (Sets)
            Sets.Add((request.Key, request.Value));
          if (Reject is not null)
            return Task.FromResult<JsonElement?>(IpcJson.ToElement(new SetSettingResponse(false, Reject), IpcJson.Default.SetSettingResponse));
          Change(request.Key, request.Value);
          return Task.FromResult<JsonElement?>(IpcJson.ToElement(new SetSettingResponse(true, null), IpcJson.Default.SetSettingResponse));
        case Ops.SetServer:
          lock (Servers)
            Servers.Add(IpcJson.Read(payload, IpcJson.Default.SetServerRequest));
          return Task.FromResult<JsonElement?>(IpcJson.ToElement(ServerReply, IpcJson.Default.SetServerResponse));
        case Ops.GetAbout:
          return Task.FromResult<JsonElement?>(IpcJson.ToElement(About, IpcJson.Default.AboutResponse));
        case Ops.CheckUpdates:
          return Update is { } update
            ? Task.FromResult<JsonElement?>(IpcJson.ToElement(update, IpcJson.Default.UpdateResponse))
            : throw new IpcException(IpcErrors.Failed);
        case Ops.InstallUpdate:
          Installs++;
          return Task.FromResult<JsonElement?>(IpcJson.ToElement(new InstallResponse(InstallState), IpcJson.Default.InstallResponse));
        default:
          throw new IpcException(IpcErrors.Invalid);
      }
    }
  }

  private static async Task<(SettingsViewModel Vm, List<string> Opened, ChannelPair Pair)> LoadedAsync(FakeExtension extension)
  {
    var pair = new ChannelPair(extension.HandleAsync, (_, _, _) => Task.FromResult<JsonElement?>(null));
    var opened = new List<string>();
    var vm = new SettingsViewModel(pair.Companion!, opened.Add);
    await vm.LoadAsync();
    return (vm, opened, pair);
  }

  private static T InfoRow<T>(SettingsViewModel vm) where T : InfoRowViewModel => vm.InfoSections.SelectMany(s => s.Rows).OfType<T>().Single();

  private static LinkRowViewModel Link(SettingsViewModel vm, string label) => vm.InfoSections.SelectMany(s => s.Rows).OfType<LinkRowViewModel>().Single(r => r.Label == label);

  private static T Find<T>(SettingsWindow window, string key) where T : Control
  {
    window.UpdateLayout();
    return window.GetVisualDescendants().OfType<T>().Single(c => c.IsEffectivelyVisible && c.DataContext is SettingRowViewModel row && row.Key == key);
  }

  private static void WaitForRows(SettingsWindow window) =>
    AvaloniaSession.WaitFor(() =>
    {
      window.UpdateLayout();
      return window.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsEffectivelyVisible && t.DataContext is SettingRowViewModel);
    });

  private static void UseDependentSchema(FakeExtension extension)
  {
    extension.Schema.Clear();
    extension.Schema.AddRange(
    [
      new("_section_a", SettingKinds.Section, "General", null, null, null),
      new("desktop", SettingKinds.Toggle, "Desktop app", "Use the desktop app", "false", null),
      new("biometrics", SettingKinds.Toggle, "Prompt for biometrics", "Ask for Windows Hello", "true", null, "desktop", "true"),
      new("other", SettingKinds.Toggle, "Other", "Something else", "true", null),
      new("_section_b", SettingKinds.Section, "Paths", null, null, null),
      new("cliPath", SettingKinds.Text, "CLI path", "Path to bw", string.Empty, null),
    ]);
  }

  private static void UseServerSchema(FakeExtension extension, string? confirm, string region = "cloud", string? apiUrl = null)
  {
    extension.Schema.Clear();
    extension.Schema.AddRange(
    [
      new("_section_server", SettingKinds.Section, "Server", null, null, null),
      new("serverRegion", SettingKinds.Choice, "Region", "Where your vault is hosted", region, [new("Bitwarden.com", "cloud"), new("Bitwarden.eu", "eu"), new("Self-hosted", "selfHosted")], Confirm: confirm),
      new("serverUrl", SettingKinds.Text, "Server URL", null, region == "selfHosted" ? "https://vault.example.com" : string.Empty, null, "serverRegion", "selfHosted", Confirm: confirm, ReadOnlyValues: new Dictionary<string, string> { ["cloud"] = "https://vault.bitwarden.com", ["eu"] = "https://vault.bitwarden.eu" }),
      new("serverAdvanced", SettingKinds.Expander, "Advanced", "Leave blank to derive from the server URL.", null, null, "serverRegion", "selfHosted"),
      new("serverWebVaultUrl", SettingKinds.Text, "Web Vault URL", null, string.Empty, null, "serverAdvanced", "true", Placeholder: "https://vault.example.com/", Confirm: confirm),
      new("serverApiUrl", SettingKinds.Text, "API URL", null, apiUrl ?? string.Empty, null, "serverAdvanced", "true", Placeholder: "https://vault.example.com/api", Confirm: confirm),
      new("serverIdentityUrl", SettingKinds.Text, "Identity URL", null, string.Empty, null, "serverAdvanced", "true", Placeholder: "https://vault.example.com/identity", Confirm: confirm),
      new("serverIconsUrl", SettingKinds.Text, "Icons URL", null, string.Empty, null, "serverAdvanced", "true", Placeholder: "https://vault.example.com/icons", Confirm: confirm),
      new("serverNotificationsUrl", SettingKinds.Text, "Notifications URL", null, string.Empty, null, "serverAdvanced", "true", Placeholder: "https://vault.example.com/notifications", Confirm: confirm),
      new("serverEventsUrl", SettingKinds.Text, "Events URL", null, string.Empty, null, "serverAdvanced", "true", Placeholder: "https://vault.example.com/events", Confirm: confirm),
      new("serverKeyConnectorUrl", SettingKinds.Text, "Key Connector URL", null, string.Empty, null, "serverAdvanced", "true", Placeholder: "https://vault.example.com/key-connector", Confirm: confirm),
      new("_section_a", SettingKinds.Section, "General", null, null, null),
      new("showIcons", SettingKinds.Toggle, "Show icons", null, "true", null),
    ]);
  }

  private static ChoiceDto Region(SettingsViewModel vm, string value) => vm.Sections.SelectMany(s => s.Rows).Single(r => r.Key == "serverRegion").Choices.Single(c => c.Value == value);

  private static SettingRowViewModel ServerRow(SettingsViewModel vm, string key) => vm.Sections.SelectMany(s => s.Rows).Single(r => r.Key == key);

  [Fact]
  public Task ServerRegion_LoggedOut_SendsTheRegionAtOnce_WithoutAsking() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    UseServerSchema(extension, confirm: null);
    var (vm, _, pair) = await LoadedAsync(extension);
    using var ownedPair = pair;
    var asked = 0;
    vm.Confirm = (_, _, _, _) =>
    {
      asked++;
      return Task.FromResult(true);
    };

    ServerRow(vm, "serverRegion").SelectedChoice = Region(vm, "eu");
    AvaloniaSession.WaitFor(() => extension.Servers.Count == 1);

    Assert.Equal(new SetServerRequest(ServerRegions.Eu, null), extension.Servers.Single());
    Assert.Equal(0, asked);
    Assert.Empty(extension.Sets);
  });

  [Fact]
  public Task ServerRegion_LoggedIn_AsksFirst_AndKeepsTheServerWhenDeclined() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    UseServerSchema(extension, confirm: "Changing the server logs you out of Bitwarden. Continue?");
    var (vm, _, pair) = await LoadedAsync(extension);
    using var ownedPair = pair;
    var messages = new List<string>();
    vm.Confirm = (_, message, _, _) =>
    {
      messages.Add(message);
      return Task.FromResult(false);
    };
    var row = ServerRow(vm, "serverRegion");

    row.SelectedChoice = Region(vm, "eu");
    AvaloniaSession.WaitFor(() => row.ErrorMessage is not null);

    Assert.Equal(["Changing the server logs you out of Bitwarden. Continue?"], messages);
    Assert.Equal("The server was not changed.", row.ErrorMessage);
    Assert.Equal("cloud", row.SelectedChoice!.Value);
    Assert.Empty(extension.Servers);
  });

  [Fact]
  public Task ServerRegion_LoggedIn_ChangesTheServerWhenConfirmed() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    UseServerSchema(extension, confirm: "Changing the server logs you out of Bitwarden. Continue?");
    var (vm, _, pair) = await LoadedAsync(extension);
    using var ownedPair = pair;
    vm.Confirm = (_, _, _, _) => Task.FromResult(true);

    ServerRow(vm, "serverRegion").SelectedChoice = Region(vm, "eu");
    AvaloniaSession.WaitFor(() => extension.Servers.Count == 1);

    Assert.Equal(new SetServerRequest(ServerRegions.Eu, null), extension.Servers.Single());
    Assert.Null(ServerRow(vm, "serverRegion").ErrorMessage);
  });

  [Fact]
  public Task ServerUrl_SelfHosted_SendsNothingUntilTheUrlIsCommitted_AndShowsTheExtensionsError() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { ServerReply = new SetServerResponse(false, "Enter a valid server URL starting with http:// or https://") };
    UseServerSchema(extension, confirm: null);
    var (vm, _, pair) = await LoadedAsync(extension);
    using var ownedPair = pair;
    var url = ServerRow(vm, "serverUrl");

    ServerRow(vm, "serverRegion").SelectedChoice = Region(vm, "selfHosted");
    await Task.Delay(100, TestContext.Current.CancellationToken);
    Assert.Empty(extension.Servers);
    Assert.True(url.IsShown);

    await url.CommitTextAsync("vault.example.com");

    Assert.Equal(new SetServerRequest(ServerRegions.SelfHosted, "vault.example.com"), extension.Servers.Single());
    Assert.Equal("Enter a valid server URL starting with http:// or https://", url.ErrorMessage);

    extension.ServerReply = new SetServerResponse(true, null);
    await url.CommitTextAsync("https://vault.example.com");

    Assert.Equal(new SetServerRequest(ServerRegions.SelfHosted, "https://vault.example.com"), extension.Servers[^1]);
    Assert.Null(url.ErrorMessage);
  });

  [Fact]
  public Task ServerUrl_ShowsThePresetReadOnly_AndFollowsTheLocalRegion() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    UseServerSchema(extension, confirm: null);
    var (vm, _, pair) = await LoadedAsync(extension);
    using var ownedPair = pair;
    var url = ServerRow(vm, "serverUrl");

    Assert.True(url.IsShown);
    Assert.True(url.IsReadOnly);
    Assert.Equal("https://vault.bitwarden.com", url.TextValue);

    await url.CommitTextAsync("https://other.example.com");
    Assert.Empty(extension.Servers);

    ServerRow(vm, "serverRegion").SelectedChoice = Region(vm, "eu");
    AvaloniaSession.WaitFor(() => url.TextValue == "https://vault.bitwarden.eu");
    Assert.True(url.IsReadOnly);

    ServerRow(vm, "serverRegion").SelectedChoice = Region(vm, "selfHosted");
    Assert.False(url.IsReadOnly);
    Assert.True(url.IsShown);
    Assert.Equal(string.Empty, url.TextValue);
  });

  private static readonly string[] AdvancedKeys = ["serverWebVaultUrl", "serverApiUrl", "serverIdentityUrl", "serverIconsUrl", "serverNotificationsUrl", "serverEventsUrl", "serverKeyConnectorUrl"];

  [Fact]
  public Task ServerAdvanced_IsHiddenUnlessSelfHosted_AndCollapsedByDefault() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    UseServerSchema(extension, confirm: null);
    var (vm, _, pair) = await LoadedAsync(extension);
    using var ownedPair = pair;
    var advanced = ServerRow(vm, "serverAdvanced");

    Assert.False(advanced.IsShown);
    Assert.All(AdvancedKeys, key => Assert.False(ServerRow(vm, key).IsShown));

    ServerRow(vm, "serverRegion").SelectedChoice = Region(vm, "selfHosted");

    Assert.True(advanced.IsShown);
    Assert.False(advanced.Expanded);
    Assert.True(advanced.IsGroup);
    Assert.Equal(AdvancedKeys, advanced.ShownChildren.Select(r => r.Key));

    ServerRow(vm, "serverRegion").SelectedChoice = Region(vm, "eu");
    Assert.False(advanced.IsShown);
    Assert.All(AdvancedKeys, key => Assert.False(ServerRow(vm, key).IsShown));
  });

  [Fact]
  public Task ServerAdvanced_LoadsTheCurrentValues_AndKeepsTheExpandedStateOnReload() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    UseServerSchema(extension, confirm: null, region: "selfHosted", apiUrl: "https://api.example.com");
    var (vm, _, pair) = await LoadedAsync(extension);
    using var ownedPair = pair;

    Assert.Equal("https://api.example.com", ServerRow(vm, "serverApiUrl").CurrentValue);
    Assert.Equal("https://vault.example.com/api", ServerRow(vm, "serverApiUrl").Placeholder);

    ServerRow(vm, "serverAdvanced").Expanded = true;
    extension.Schema[1] = extension.Schema[1] with { Description = "changed" };
    await vm.LoadAsync();

    Assert.True(ServerRow(vm, "serverAdvanced").Expanded);
    Assert.True(ServerRow(vm, "serverApiUrl").IsShown);
  });

  [Fact]
  public Task ServerAdvanced_Save_SendsTheOverridesWithTheServerUrl_AndBlankMeansNone() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    UseServerSchema(extension, confirm: null, region: "selfHosted", apiUrl: "https://api.example.com");
    var (vm, _, pair) = await LoadedAsync(extension);
    using var ownedPair = pair;

    await ServerRow(vm, "serverIdentityUrl").CommitTextAsync("https://id.example.com");

    Assert.Equal(new SetServerRequest(ServerRegions.SelfHosted, "https://vault.example.com", ApiUrl: "https://api.example.com", IdentityUrl: "https://id.example.com"), extension.Servers.Single());
    Assert.Null(ServerRow(vm, "serverIdentityUrl").ErrorMessage);

    await ServerRow(vm, "serverApiUrl").CommitTextAsync(string.Empty);

    Assert.Equal(new SetServerRequest(ServerRegions.SelfHosted, "https://vault.example.com", IdentityUrl: "https://id.example.com"), extension.Servers[^1]);
  });

  [Fact]
  public Task ServerAdvanced_InvalidOverride_ShowsTheExtensionsError() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { ServerReply = new SetServerResponse(false, "Invalid IdentityUrl: must start with https://") };
    UseServerSchema(extension, confirm: null, region: "selfHosted");
    var (vm, _, pair) = await LoadedAsync(extension);
    using var ownedPair = pair;
    var identity = ServerRow(vm, "serverIdentityUrl");

    await identity.CommitTextAsync("http://id.example.com");

    Assert.Equal("http://id.example.com", extension.Servers.Single().IdentityUrl);
    Assert.Equal("Invalid IdentityUrl: must start with https://", identity.ErrorMessage);
  });

  private static TextBox SearchBox(SettingsWindow window) =>
    window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SearchBox");

  private static List<string> ShownKeys(SettingsViewModel vm) =>
    vm.Sections.SelectMany(s => s.Rows).Where(r => r.IsShown).Select(r => r.Key).ToList();

  [Fact]
  public Task ShowSettings_IsOneNormalWindow_AndRendersEachKind() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });

    var reply = pair.Extension.RequestAsync(Ops.ShowSettings, IpcJson.ToElement(new DialogRequest(0), IpcJson.Default.DialogRequest));
    AvaloniaSession.WaitFor(() => reply.IsCompleted);
    var window = coordinator.ShowSettings();
    Assert.Same(window, coordinator.ShowSettings());
    Assert.IsNotAssignableFrom<DialogWindow>(window);
    Assert.Equal(ItemWindow.AlwaysOnTop, window.Topmost);
    var vm = window.ViewModel!;
    AvaloniaSession.WaitFor(() => vm.Sections.Count == 2);
    WaitForRows(window);

    Assert.Equal(["General", "Paths"], vm.Sections.Select(s => s.Title));
    Assert.Equal(["showIcons", "limit"], vm.Sections[0].Rows.Select(r => r.Key));
    Assert.True(Find<ToggleSwitch>(window, "showIcons").IsChecked);
    var combo = Find<ComboBox>(window, "limit");
    Assert.Equal("3", ((ChoiceDto)combo.SelectedItem!).Value);
    Assert.Equal(3, combo.ItemCount);
    Assert.True(string.IsNullOrEmpty(Find<TextBox>(window, "cliPath").Text));
    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Download website icons");
    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Paths");
    Assert.Empty(extension.Sets);

    coordinator.OnLocked();
    Assert.Same(window, coordinator.ShowSettings());
    window.Close();
    Assert.NotSame(window, coordinator.ShowSettings());
    coordinator.ShowSettings().Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task ShowSettings_ForcesTheForegroundOnlyWhenTheRequestAsksForIt() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    var taken = 0;
    var forced = 0;
    coordinator.TakeForeground = _ => taken++;
    coordinator.ForceForeground = _ => forced++;
    _ = coordinator.RunAsync(() => { });

    var plain = pair.Extension.RequestAsync(Ops.ShowSettings, IpcJson.ToElement(new DialogRequest(0), IpcJson.Default.DialogRequest));
    AvaloniaSession.WaitFor(() => plain.IsCompleted);
    Assert.Equal((1, 0), (taken, forced));

    var form = pair.Extension.RequestAsync(Ops.ShowSettings, IpcJson.ToElement(new DialogRequest(0, true), IpcJson.Default.DialogRequest));
    AvaloniaSession.WaitFor(() => form.IsCompleted);
    Assert.Equal((1, 1), (taken, forced));

    var legacy = pair.Extension.RequestAsync(Ops.ShowSettings, null);
    AvaloniaSession.WaitFor(() => legacy.IsCompleted);
    Assert.Equal((2, 1), (taken, forced));
    coordinator.ShowSettings().Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task ToggleAndChoice_SendSetSettingAtOnce() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var window = coordinator.ShowSettings();
    WaitForRows(window);

    Find<ToggleSwitch>(window, "showIcons").IsChecked = false;
    AvaloniaSession.WaitFor(() => extension.Sets.Count == 1);
    Assert.Equal(("showIcons", "false"), extension.Sets[0]);

    Find<ComboBox>(window, "limit").SelectedIndex = 2;
    AvaloniaSession.WaitFor(() => extension.Sets.Count == 2);
    Assert.Equal(("limit", "5"), extension.Sets[1]);
    Assert.All(window.ViewModel!.Sections.SelectMany(s => s.Rows), r => Assert.Null(r.ErrorMessage));
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task OverrideSwitch_KeepsTheCard_AndExpandsItWithTheSwitch() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    extension.Schema.Clear();
    extension.Schema.AddRange(
    [
      new("_section_cli", SettingKinds.Section, "Bitwarden CLI", null, null, null),
      new("useCliDirectoryOverride", SettingKinds.Toggle, "CLI Path Override", "Uses your own bw", "false", null, RevealsChildren: true),
      new("cliDirectoryOverride", SettingKinds.Text, "CLI path", "Path to bw", @"C:\tools\bw.exe", null, "useCliDirectoryOverride", "true", Placeholder: "bw"),
    ]);
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var window = coordinator.ShowSettings();
    var vm = window.ViewModel!;
    AvaloniaSession.WaitFor(() => vm.Sections.SelectMany(s => s.Rows).Any(r => r.Key == "useCliDirectoryOverride"));
    var toggle = vm.Sections.SelectMany(s => s.Rows).Single(r => r.Key == "useCliDirectoryOverride");
    var card = Find<Hoobi.Design.Avalonia.SettingsCard>(window, "useCliDirectoryOverride");

    Assert.False(card.IsExpanded);
    Assert.False(card.IsClickEnabled);
    Assert.False(vm.Sections.SelectMany(s => s.Rows).Single(r => r.Key == "cliDirectoryOverride").IsShown);

    toggle.BoolValue = true;
    AvaloniaSession.WaitFor(() => extension.Sets.Count == 1);
    window.UpdateLayout();

    Assert.Same(card, Find<Hoobi.Design.Avalonia.SettingsCard>(window, "useCliDirectoryOverride"));
    Assert.True(card.IsExpanded);
    Assert.True(vm.Sections.SelectMany(s => s.Rows).Single(r => r.Key == "cliDirectoryOverride").IsShown);
    Assert.Equal(("useCliDirectoryOverride", "true"), extension.Sets.Single());

    toggle.BoolValue = false;
    window.UpdateLayout();
    Assert.False(card.IsExpanded);
    Assert.True(card.IsEffectivelyVisible);
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task ServerAdvanced_PlaceholdersFollowTheTypedServerUrl_AndFallBackToTheExample() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    UseServerSchema(extension, confirm: null, region: "selfHosted");
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var window = coordinator.ShowSettings();
    var vm = window.ViewModel!;
    AvaloniaSession.WaitFor(() => vm.Sections.SelectMany(s => s.Rows).Any(r => r.Key == "serverRegion"));
    ServerRow(vm, "serverRegion").Expanded = true;
    WaitForRows(window);
    var box = Find<TextBox>(window, "serverUrl");

    box.Text = "https://vault.hoobi.io";
    AvaloniaSession.WaitFor(() => ServerRow(vm, "serverApiUrl").Placeholder == "https://vault.hoobi.io/api");
    Assert.Equal(["https://vault.hoobi.io/", "https://vault.hoobi.io/api", "https://vault.hoobi.io/identity", "https://vault.hoobi.io/icons", "https://vault.hoobi.io/notifications", "https://vault.hoobi.io/events", "https://vault.hoobi.io/key-connector"], AdvancedKeys.Select(k => ServerRow(vm, k).Placeholder));
    Assert.Empty(extension.Servers);

    box.Text = string.Empty;
    AvaloniaSession.WaitFor(() => ServerRow(vm, "serverApiUrl").Placeholder == "https://vault.example.com/api");
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Text_CommitsOnEnterAndLostFocus_NotOnTyping() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var window = coordinator.ShowSettings();
    WaitForRows(window);
    var box = Find<TextBox>(window, "cliPath");
    var row = window.ViewModel!.Sections[1].Rows[0];

    box.Text = @"C:\tools\bw";
    Assert.Empty(extension.Sets);
    box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = box });
    AvaloniaSession.WaitFor(() => row.TextValue == @"C:\tools\bw");
    Assert.Equal(("cliPath", @"C:\tools\bw"), Assert.Single(extension.Sets));

    box.Text = @"D:\bw";
    box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent) { Source = box });
    AvaloniaSession.WaitFor(() => row.TextValue == @"D:\bw");
    Assert.Equal(("cliPath", @"D:\bw"), extension.Sets[1]);

    box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent) { Source = box });
    await Task.Delay(100, TestContext.Current.CancellationToken);
    Assert.Equal(2, extension.Sets.Count);
    window.Close();
  });

  [Fact]
  public Task Rejection_RevertsTheControl_AndShowsTheErrorInline() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { Reject = "That value is not allowed" };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var window = coordinator.ShowSettings();
    WaitForRows(window);
    var toggle = Find<ToggleSwitch>(window, "showIcons");
    var box = Find<TextBox>(window, "cliPath");
    var vm = window.ViewModel!;
    var toggleRow = vm.Sections[0].Rows[0];
    var textRow = vm.Sections[1].Rows[0];

    toggle.IsChecked = false;
    AvaloniaSession.WaitFor(() => toggleRow.ErrorMessage == "That value is not allowed");
    Assert.True(toggle.IsChecked);
    window.UpdateLayout();
    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "That value is not allowed");

    box.Text = "bad";
    box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = box });
    AvaloniaSession.WaitFor(() => textRow.ErrorMessage is not null);
    Assert.True(string.IsNullOrEmpty(box.Text));

    extension.Reject = null;
    toggle.IsChecked = false;
    AvaloniaSession.WaitFor(() => toggleRow.ErrorMessage is null && extension.Sets.Count == 3);
    Assert.False(toggle.IsChecked);
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task SettingsChangedEvent_RefreshesInPlace_AndLeavesAnEditedTextAlone() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var window = coordinator.ShowSettings();
    WaitForRows(window);
    var toggle = Find<ToggleSwitch>(window, "showIcons");
    var combo = Find<ComboBox>(window, "limit");
    var box = Find<TextBox>(window, "cliPath");
    var textRow = window.ViewModel!.Sections[1].Rows[0];

    extension.Change("showIcons", "false");
    extension.Change("limit", "1");
    extension.Change("cliPath", "from-powertoys");
    await pair.Extension.SendEventAsync(Ops.SettingsChanged);
    AvaloniaSession.WaitFor(() => toggle.IsChecked == false && box.Text == "from-powertoys");
    Assert.Equal("1", ((ChoiceDto)combo.SelectedItem!).Value);
    Assert.Same(toggle, Find<ToggleSwitch>(window, "showIcons"));
    Assert.Empty(extension.Sets);

    textRow.IsEditing = true;
    box.Text = "half typed";
    extension.Change("cliPath", "changed-again");
    await coordinator.OnSettingsChangedAsync();
    Assert.Equal("half typed", box.Text);
    textRow.IsEditing = false;
    await coordinator.OnSettingsChangedAsync();
    Assert.Equal("changed-again", box.Text);
    Assert.Empty(extension.Sets);
    window.Close();
  });

  [Fact]
  public Task LoadFailure_ShowsTheError_AndTheChangeEventIsHarmlessWithoutAWindow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { FailLoad = true };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    await coordinator.OnSettingsChangedAsync();

    var window = coordinator.ShowSettings();
    AvaloniaSession.WaitFor(() => window.ViewModel!.ErrorMessage is not null);
    window.UpdateLayout();
    Assert.True(window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ErrorText").IsEffectivelyVisible);
    Assert.Empty(window.ViewModel!.Sections);
    window.Close();
  });

  [Fact]
  public Task Child_IsHiddenWhileTheParentIsOff_AndShownAfterTheToggle() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    UseDependentSchema(extension);
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var window = coordinator.ShowSettings();
    WaitForRows(window);
    var rows = window.ViewModel!.Sections.SelectMany(s => s.Rows).ToDictionary(r => r.Key);

    Assert.Equal(["desktop", "other", "cliPath"], ShownKeys(window.ViewModel!));
    Assert.DoesNotContain(window.GetVisualDescendants().OfType<ToggleSwitch>(), t => t.IsEffectivelyVisible && t.DataContext is SettingRowViewModel { Key: "biometrics" });

    Find<ToggleSwitch>(window, "desktop").IsChecked = true;
    AvaloniaSession.WaitFor(() => rows["biometrics"].IsShown);
    rows["desktop"].Expanded = true;
    Assert.True(Find<ToggleSwitch>(window, "biometrics").IsChecked);
    Assert.Equal(["desktop", "biometrics", "other", "cliPath"], ShownKeys(window.ViewModel!));

    Find<ToggleSwitch>(window, "desktop").IsChecked = false;
    AvaloniaSession.WaitFor(() => !rows["biometrics"].IsShown);
    Assert.Equal("true", rows["biometrics"].CurrentValue);
    AvaloniaSession.WaitFor(() => extension.Sets.Count == 2);
    Assert.Equal(("desktop", "true"), extension.Sets[0]);
    Assert.Equal(("desktop", "false"), extension.Sets[1]);
    window.Close();
    await Task.CompletedTask;
  });

  private static SettingsCard GroupCard(SettingsWindow window, string key)
  {
    window.UpdateLayout();
    return window.GetVisualDescendants().OfType<SettingsCard>().Single(c => c.IsEffectivelyVisible && c.DataContext is SettingRowViewModel row && row.Key == key);
  }

  private static void Click(SettingsWindow window, Control target)
  {
    window.UpdateLayout();
    var point = target.TranslatePoint(new Avalonia.Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
    window.MouseDown(point, MouseButton.Left);
    window.MouseUp(point, MouseButton.Left);
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static SettingsWindow OpenGroupedWindow(WindowCoordinator coordinator, FakeExtension extension)
  {
    UseDependentSchema(extension);
    extension.Change("desktop", "true");
    var window = coordinator.ShowSettings();
    window.Show();
    WaitForRows(window);
    AvaloniaSession.WaitFor(() => window.ViewModel!.Sections.SelectMany(s => s.Rows).Single(r => r.Key == "desktop").IsGroup);
    return window;
  }

  [Fact]
  public Task Group_IsCollapsedByDefault_AndHidesItsChildren() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput) { TakeForeground = _ => { } };
    _ = coordinator.RunAsync(() => { });
    var window = OpenGroupedWindow(coordinator, extension);

    var card = GroupCard(window, "desktop");
    Assert.False(card.IsExpanded);
    Assert.True(window.ViewModel!.Sections[0].Rows.Single(r => r.Key == "biometrics").IsShown);
    Assert.DoesNotContain(window.GetVisualDescendants().OfType<ToggleSwitch>(), t => t.IsEffectivelyVisible && t.DataContext is SettingRowViewModel { Key: "biometrics" });
    Assert.Null(window.GetVisualDescendants().OfType<SettingsCard>().FirstOrDefault(c => c.IsEffectivelyVisible && c.DataContext is SettingRowViewModel { Key: "other" }));
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Group_RowClick_ExpandsAndShowsTheChildren_AndCollapsesAgain() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput) { TakeForeground = _ => { } };
    _ = coordinator.RunAsync(() => { });
    var window = OpenGroupedWindow(coordinator, extension);
    var clock = new ManualClock();
    clock.Attach(window);
    var card = GroupCard(window, "desktop");
    var label = card.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.Text == "Desktop app");

    Click(window, label);
    clock.Settle();

    Assert.True(card.IsExpanded);
    AvaloniaSession.WaitFor(() => Find<ToggleSwitch>(window, "biometrics").IsChecked == true);
    var child = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.Text == "Prompt for biometrics");
    Click(window, child);
    Assert.True(card.IsExpanded);
    Assert.Empty(extension.Sets);

    Click(window, label);
    Assert.False(card.IsExpanded);
    clock.Settle();
    AvaloniaSession.WaitFor(() => { window.UpdateLayout(); return !window.GetVisualDescendants().OfType<ToggleSwitch>().Any(t => t.IsEffectivelyVisible && t.DataContext is SettingRowViewModel { Key: "biometrics" }); });
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Group_TogglingTheParentSwitch_DoesNotExpand() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput) { TakeForeground = _ => { } };
    _ = coordinator.RunAsync(() => { });
    var window = OpenGroupedWindow(coordinator, extension);
    var card = GroupCard(window, "desktop");

    Click(window, Find<ToggleSwitch>(window, "desktop"));

    AvaloniaSession.WaitFor(() => extension.Sets.Count == 1);
    Assert.Equal(("desktop", "false"), extension.Sets[0]);
    Assert.False(card.IsExpanded);
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Search_HitOnAChild_ExpandsItsGroup() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput) { TakeForeground = _ => { } };
    _ = coordinator.RunAsync(() => { });
    var window = OpenGroupedWindow(coordinator, extension);

    window.ViewModel!.SearchText = "windows hello";

    Assert.True(GroupCard(window, "desktop").IsExpanded);
    Assert.True(Find<ToggleSwitch>(window, "biometrics").IsChecked);
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Search_FiltersRowsByLabelAndDescription_AndHidesEmptySections() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    UseDependentSchema(extension);
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var window = coordinator.ShowSettings();
    WaitForRows(window);
    var vm = window.ViewModel!;

    vm.SearchText = "SOMETHING";
    Assert.Equal(["other"], ShownKeys(vm));
    Assert.Equal([true, false], vm.Sections.Select(s => s.IsShown));

    vm.SearchText = "path";
    Assert.Equal(["cliPath"], ShownKeys(vm));
    Assert.Equal([false, true], vm.Sections.Select(s => s.IsShown));

    vm.SearchText = "nothing matches this";
    Assert.Empty(ShownKeys(vm));
    Assert.All(vm.Sections, s => Assert.False(s.IsShown));

    vm.SearchText = string.Empty;
    Assert.Equal(["desktop", "other", "cliPath"], ShownKeys(vm));
    Assert.All(vm.Sections, s => Assert.True(s.IsShown));
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Search_HitOnAHiddenChild_ShowsItsParent() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    UseDependentSchema(extension);
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var window = coordinator.ShowSettings();
    WaitForRows(window);
    var vm = window.ViewModel!;

    vm.SearchText = "windows hello";
    Assert.Equal(["desktop"], ShownKeys(vm));
    Assert.Equal([true, false], vm.Sections.Select(s => s.IsShown));
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Escape_ClearsTheSearch_AndCtrlFFocusesTheBox() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    UseDependentSchema(extension);
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var window = coordinator.ShowSettings();
    WaitForRows(window);
    var vm = window.ViewModel!;
    var box = SearchBox(window);

    box.Text = "something";
    AvaloniaSession.WaitFor(() => vm.SearchText == "something");
    Assert.Equal(["other"], ShownKeys(vm));

    box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = box });
    AvaloniaSession.WaitFor(() => vm.SearchText.Length == 0);
    Assert.True(string.IsNullOrEmpty(box.Text));
    Assert.Equal(["desktop", "other", "cliPath"], ShownKeys(vm));

    Find<TextBox>(window, "cliPath").Focus();
    window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control, Source = window });
    AvaloniaSession.WaitFor(() => box.IsFocused);
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public async Task HelpAndAbout_ListTheStaticRows_ShowTheBuildInfo_AndTakePartInTheSearch()
  {
    var extension = new FakeExtension { About = new("1.13.0", "626a78a", AppChannels.Prerelease, false) };
    var (vm, _, pair) = await LoadedAsync(extension);
    using var channelPair = pair;

    Assert.Equal(["Help and feedback", "About"], vm.InfoSections.Select(s => s.Title));
    Assert.Equal(["Report a bug", "Request a feature", "Project on GitHub"], vm.InfoSections[0].Rows.Select(r => r.Label));
    Assert.Equal(["Command Palette Extension for Bitwarden", "Support", "Check for updates"], vm.InfoSections[1].Rows.Select(r => r.Label));
    Assert.Equal("Version 1.13.0 (Pre-release) - 626a78a\nCopyright (c) Hoobi", InfoRow<AboutRowViewModel>(vm).Description);

    vm.SearchText = "BUG";
    Assert.Equal([true, false], vm.InfoSections.Select(s => s.IsShown));
    Assert.Equal(["Report a bug"], vm.InfoSections.SelectMany(s => s.Rows).Where(r => r.IsShown).Select(r => r.Label));

    vm.SearchText = "donation";
    Assert.Equal(["Support"], vm.InfoSections.SelectMany(s => s.Rows).Where(r => r.IsShown).Select(r => r.Label));

    vm.SearchText = "nothing matches this";
    Assert.All(vm.InfoSections, s => Assert.False(s.IsShown));

    vm.SearchText = string.Empty;
    Assert.All(vm.InfoSections, s => Assert.True(s.IsShown));
  }

  [Fact]
  public async Task AboutWithoutACommit_OmitsIt_AndAFailedAboutLeavesTheRowsBlank()
  {
    Assert.Equal("Version 1.13.0 (Dev)\nCopyright (c) Hoobi", AboutRowViewModel.Describe(new("1.13.0", null, AppChannels.Dev, false)));

    var extension = new FakeExtension();
    var pair = new ChannelPair((op, payload, token) => op == Ops.GetAbout ? throw new IpcException(IpcErrors.Failed) : extension.HandleAsync(op, payload, token), (_, _, _) => Task.FromResult<JsonElement?>(null));
    using var channelPair = pair;
    var vm = new SettingsViewModel(pair.Companion!, _ => { });
    await vm.LoadAsync();

    Assert.Null(InfoRow<AboutRowViewModel>(vm).Description);
    Assert.NotEmpty(vm.Sections);
  }

  [Fact]
  public async Task LinkRows_OpenTheirTargets()
  {
    var (vm, opened, pair) = await LoadedAsync(new FakeExtension());
    using var channelPair = pair;

    Link(vm, "Report a bug").OpenCommand.Execute(null);
    Link(vm, "Request a feature").OpenCommand.Execute(null);
    Link(vm, "Project on GitHub").OpenCommand.Execute(null);
    Link(vm, "Support").OpenCommand.Execute(null);

    Assert.Equal(
    [
      "https://github.com/hoobio/command-palette-bitwarden/issues/new?template=bug_report.yml",
      "https://github.com/hoobio/command-palette-bitwarden/issues/new?template=feature_request.yml",
      "https://github.com/hoobio/command-palette-bitwarden",
      "https://www.paypal.com/donate/?hosted_button_id=E2UBLTYAT4K8Y",
    ], opened);
  }

  [Fact]
  public async Task LogsLink_ComesFromTheExtension_OpensTheLogFolder_AndFollowsTheSearch()
  {
    var extension = new FakeExtension();
    extension.Schema.AddRange(
    [
      new("_section_c", SettingKinds.Section, "Diagnostics", null, null, null),
      new("debugLogging", SettingKinds.Toggle, "Debug Logging", "Writes debug logs", "false", null),
      new(SettingKeys.OpenLogs, SettingKinds.Link, "Logs", "Opens the log folder", null, null),
    ]);
    var (vm, opened, pair) = await LoadedAsync(extension);
    using var channelPair = pair;
    var row = vm.Sections[^1].Rows[^1];

    Assert.Equal(SettingKinds.Link, row.Kind);
    Assert.True(row.IsLink);
    row.OpenCommand.Execute(null);

    Assert.Equal([SettingsViewModel.LogsDirectory], opened);
    Assert.True(Directory.Exists(SettingsViewModel.LogsDirectory));
    vm.SearchText = "log folder";
    Assert.Equal([SettingKeys.OpenLogs], ShownKeys(vm));
  }

  [Fact]
  public async Task OpeningATargetThatFails_ShowsTheError()
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync, (_, _, _) => Task.FromResult<JsonElement?>(null));
    var vm = new SettingsViewModel(pair.Companion!, _ => throw new InvalidOperationException("no browser"));
    await vm.LoadAsync();

    Link(vm, "Support").OpenCommand.Execute(null);

    Assert.Contains("no browser", vm.ErrorMessage);
  }

  [Fact]
  public async Task UpdateRow_IsOff_ForDevBuilds()
  {
    var (vm, _, pair) = await LoadedAsync(new FakeExtension { About = new("1.13.0", null, AppChannels.Dev, false) });
    using var channelPair = pair;
    var row = InfoRow<UpdateRowViewModel>(vm);

    Assert.False(row.CanCheck);
    Assert.Equal(UpdateRowViewModel.DevText, row.Description);
    await row.CheckCommand.ExecuteAsync(null);
    Assert.Equal(UpdateRowViewModel.DevText, row.Description);
  }

  [Theory]
  [InlineData(UpdateStates.UpToDate, false, "You are on the latest version.", false)]
  [InlineData(UpdateStates.Available, false, "Update available: v1.14.0", true)]
  [InlineData(UpdateStates.Available, true, "An update is available in the Microsoft Store.", true)]
  [InlineData(UpdateStates.Failed, false, "Could not check for updates. Try again later.", false)]
  public async Task UpdateRow_ShowsTheOutcomeOfACheck(string state, bool store, string text, bool actionShown)
  {
    var extension = new FakeExtension { Update = new(state, "v1.14.0", "https://github.com/hoobio/command-palette-bitwarden/releases/tag/v1.14.0", store) };
    var (vm, _, pair) = await LoadedAsync(extension);
    using var channelPair = pair;
    var row = InfoRow<UpdateRowViewModel>(vm);

    Assert.True(row.CanCheck);
    Assert.Null(row.Description);
    await row.CheckCommand.ExecuteAsync(null);

    Assert.Equal((text, actionShown, true), (row.Description, row.UpdateAvailable, row.CanCheck));
    Assert.Equal(store ? "Install update" : "View release", row.ActionText);
  }

  [Fact]
  public async Task UpdateRow_ReportsAFailedRequest_AndTheActionOpensTheReleaseOrInstalls()
  {
    var extension = new FakeExtension();
    var (vm, opened, pair) = await LoadedAsync(extension);
    using var channelPair = pair;
    var row = InfoRow<UpdateRowViewModel>(vm);

    await row.CheckCommand.ExecuteAsync(null);
    Assert.Equal(UpdateRowViewModel.FailedText, row.Description);
    Assert.False(row.UpdateAvailable);

    extension.Update = new(UpdateStates.Available, "v1.14.0", "https://github.com/hoobio/command-palette-bitwarden/releases/tag/v1.14.0", false);
    await row.CheckCommand.ExecuteAsync(null);
    await row.ActionCommand.ExecuteAsync(null);
    Assert.Equal(["https://github.com/hoobio/command-palette-bitwarden/releases/tag/v1.14.0"], opened);
    Assert.Equal(0, extension.Installs);

    extension.Update = new(UpdateStates.Available, null, null, true);
    await row.CheckCommand.ExecuteAsync(null);
    await row.ActionCommand.ExecuteAsync(null);
    Assert.Equal(1, extension.Installs);
    Assert.Single(opened);
    Assert.Equal((UpdateRowViewModel.InstalledText, false), (row.Description, row.UpdateAvailable));
  }

  [Theory]
  [InlineData(InstallStates.OpenStore, UpdateRowViewModel.StoreOpenedText, true)]
  [InlineData(InstallStates.Failed, UpdateRowViewModel.InstallNotCompletedText, false)]
  public async Task UpdateRow_ReportsEachStoreInstallOutcome(string state, string text, bool opensStore)
  {
    var extension = new FakeExtension { Update = new(UpdateStates.Available, null, null, true), InstallState = state };
    var (vm, opened, pair) = await LoadedAsync(extension);
    using var channelPair = pair;
    var row = InfoRow<UpdateRowViewModel>(vm);

    await row.CheckCommand.ExecuteAsync(null);
    await row.ActionCommand.ExecuteAsync(null);

    Assert.Equal(text, row.Description);
    Assert.Equal(opensStore ? [UpdateRowViewModel.StoreUpdatesUri] : [], opened);
  }

  [Fact]
  public async Task NonEmptyParent_ShowsTheChildOnlyWhileTheParentHasText()
  {
    var extension = new FakeExtension();
    extension.Schema.Clear();
    extension.Schema.AddRange(
    [
      new("_section_a", SettingKinds.Section, "CLI", null, null, null),
      new("cliPath", SettingKinds.Text, "CLI path", "Path to bw", string.Empty, null, Placeholder: "bw"),
      new("portable", SettingKinds.Toggle, "Use CLI path as data directory", "Keeps CLI data next to the CLI", "false", null, "cliPath", ParentValues.NonEmpty),
    ]);
    var (vm, _, pair) = await LoadedAsync(extension);
    using var channelPair = pair;
    var path = vm.Sections[0].Rows[0];

    Assert.Equal(["cliPath"], ShownKeys(vm));
    Assert.Equal("bw", path.Placeholder);

    await path.CommitTextAsync(@"C:\tools\bw.exe");
    Assert.Equal(["cliPath", "portable"], ShownKeys(vm));

    extension.Change("cliPath", string.Empty);
    extension.Schema[1] = extension.Schema[1] with { Placeholder = @"C:\Users\x\AppData\Roaming\Bitwarden CLI" };
    await vm.LoadAsync();
    Assert.Equal(["cliPath"], ShownKeys(vm));
    Assert.Equal(@"C:\Users\x\AppData\Roaming\Bitwarden CLI", path.Placeholder);
  }

  [Fact]
  public async Task Choices_CarryTheirTooltipText()
  {
    var extension = new FakeExtension();
    extension.Schema.Clear();
    extension.Schema.Add(new("style", SettingKinds.Choice, "Style", null, "static", [new("Off", "off"), new("Static", "static", "Shows a 2FA badge")]));
    var (vm, _, pair) = await LoadedAsync(extension);
    using var channelPair = pair;

    var choices = vm.Sections[0].Rows[0].Choices;
    Assert.Equal([null, "Shows a 2FA badge"], choices.Select(c => c.Description));
  }

  [Fact]
  public Task Dropdown_SizesToItsWidestOption() => session.RunAsync(async () =>
  {
    async Task<double> MeasureAsync(params ChoiceDto[] choices)
    {
      var extension = new FakeExtension();
      extension.Schema.Clear();
      extension.Schema.Add(new("style", SettingKinds.Choice, "Style", null, choices[0].Value, choices));
      using var pair = new ChannelPair(extension.HandleAsync);
      using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
      _ = coordinator.RunAsync(() => { });
      var window = coordinator.ShowSettings();
      AvaloniaSession.WaitFor(() => window.ViewModel!.Sections.Count == 1);
      window.UpdateLayout();
      var combo = Find<ComboBox>(window, "style");
      var result = combo.Bounds.Width;
      window.Close();
      await Task.CompletedTask;
      return result;
    }

    var narrow = await MeasureAsync(new ChoiceDto("Never", "0"));
    var wide = await MeasureAsync(new ChoiceDto("Never", "0"), new ChoiceDto("A much longer option title", "1"));
    var wideOtherWayRound = await MeasureAsync(new ChoiceDto("A much longer option title", "1"), new ChoiceDto("Never", "0"));

    Assert.True(wide > narrow + 40, $"narrow {narrow}, wide {wide}");
    Assert.Equal(wide, wideOtherWayRound);
    Assert.True(wide < 300);
  });

  [Fact]
  public Task PlaceholderAndLinkRow_RenderInTheWindow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    extension.Schema.Add(new("openLogs", SettingKinds.Link, "Logs", "Opens the log folder", null, null));
    extension.Schema[4] = extension.Schema[4] with { Placeholder = "bw" };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });
    var window = coordinator.ShowSettings();
    WaitForRows(window);

    Assert.Equal("bw", Find<TextBox>(window, "cliPath").PlaceholderText);
    Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.DataContext is SettingRowViewModel { Key: "openLogs" });
    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Help and feedback");
    Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Content is "Check now");
    window.Close();
    await Task.CompletedTask;
  });
}
