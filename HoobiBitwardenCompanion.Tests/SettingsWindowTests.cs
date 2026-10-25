using System.Text.Json;
using Avalonia.Controls;
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

    public string? Reject { get; set; }

    public bool FailLoad { get; set; }

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
        default:
          throw new IpcException(IpcErrors.Invalid);
      }
    }
  }

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

  private static TextBox SearchBox(SettingsWindow window) =>
    window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SearchBox");

  private static IEnumerable<string> ShownKeys(SettingsViewModel vm) =>
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
}
