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

public class HotkeysPageTests(AvaloniaSession session)
{
  private const int Ctrl = HotkeyModifiers.Ctrl;
  private const int Shift = HotkeyModifiers.Shift;

  private sealed class FakeExtension
  {
    private static readonly ChordDto CopyUsername = new(0x43, Ctrl, "Ctrl+C");
    private static readonly ChordDto CopyPassword = new(0x43, Ctrl | Shift, "Ctrl+Shift+C");

    public Dictionary<string, ChordDto?> Current { get; } = new()
    {
      ["all.openWindow"] = new(0x4F, Ctrl, "Ctrl+O"),
      ["login.copyUsername"] = CopyUsername,
      ["login.copyPassword"] = CopyPassword,
      ["login.rotatePassword"] = null,
    };

    public Dictionary<string, ChordDto?> Defaults { get; } = new()
    {
      ["all.openWindow"] = new(0x4F, Ctrl, "Ctrl+O"),
      ["login.copyUsername"] = CopyUsername,
      ["login.copyPassword"] = CopyPassword,
      ["login.rotatePassword"] = null,
    };

    public string LoginEnter { get; set; } = "login.openBrowser";

    public string LoginInherited { get; set; } = "login.openBrowser";

    public List<string?> EnterRequests { get; } = [];

    public bool FailSetHotkey { get; set; }

    public int Resets { get; private set; }

    public List<string> Calls { get; } = [];

    public bool FailLoad { get; set; }

    private HotkeyActionDto Action(string id, string title) => new(id, title, Defaults[id], Current[id]);

    public Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
    {
      lock (Calls)
        Calls.Add(op);
      switch (op)
      {
        case Ops.GetHotkeys:
          if (FailLoad)
            throw new IpcException(IpcErrors.Failed);
          var groups = new List<HotkeyGroupDto>
          {
            new("all", "All items", "all.openWindow", "all.openWindow", [new("Open in window", "all.openWindow")], [Action("all.openWindow", "Open in window")]),
            new("login", "Login", LoginInherited, LoginEnter, [new("Open in Browser", "login.openBrowser"), new("Copy Password", "login.copyPassword")],
              [Action("login.copyUsername", "Copy Username"), Action("login.copyPassword", "Copy Password"), Action("login.rotatePassword", "Rotate password")]),
          };
          return Reply(new HotkeysResponse(groups), IpcJson.Default.HotkeysResponse);
        case Ops.SetHotkey:
          var set = IpcJson.Read(payload, IpcJson.Default.SetHotkeyRequest);
          if (FailSetHotkey)
            return Reply(new HotkeyResponse(false, "Could not save the shortcuts"), IpcJson.Default.HotkeyResponse);
          if (set.Chord is { } chord && Current.Any(c => c.Key != set.ActionId && c.Value?.Key == chord.Key && c.Value.Modifiers == chord.Modifiers))
            return Reply(new HotkeyResponse(false, "Ctrl+C is already used by Copy Username (Login)"), IpcJson.Default.HotkeyResponse);
          Current[set.ActionId] = set.Chord is { } value ? new ChordDto(value.Key, value.Modifiers, HotkeyKeys.Display(value.Key, value.Modifiers)) : null;
          return Reply(new HotkeyResponse(true, null), IpcJson.Default.HotkeyResponse);
        case Ops.SetEnterAction:
          var enterRequest = IpcJson.Read(payload, IpcJson.Default.SetEnterActionRequest);
          EnterRequests.Add(enterRequest.ActionId);
          LoginEnter = enterRequest.ActionId ?? LoginInherited;
          return Reply(new HotkeyResponse(true, null), IpcJson.Default.HotkeyResponse);
        case Ops.ResetHotkeys:
          Resets++;
          foreach (var key in Defaults.Keys)
            Current[key] = Defaults[key];
          LoginEnter = "login.openBrowser";
          return Reply(new HotkeyResponse(true, null), IpcJson.Default.HotkeyResponse);
        case Ops.GetSettings:
          return Reply(new SettingsResponse([new("hotkeys", SettingKinds.Link, "Keyboard shortcuts", "Change item shortcuts and the Enter action", null, null)]), IpcJson.Default.SettingsResponse);
        case Ops.GetAbout:
          return Reply(new AboutResponse("1.0.0", null, AppChannels.Release, false), IpcJson.Default.AboutResponse);
        default:
          throw new IpcException(IpcErrors.Invalid);
      }
    }

    private static Task<JsonElement?> Reply<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
      Task.FromResult<JsonElement?>(IpcJson.ToElement(value, info));
  }

  private static async Task<(HotkeysViewModel Vm, ChannelPair Pair)> LoadedAsync(FakeExtension extension)
  {
    var pair = new ChannelPair(extension.HandleAsync, (_, _, _) => Task.FromResult<JsonElement?>(null));
    var vm = new HotkeysViewModel(pair.Companion!);
    await vm.LoadAsync();
    return (vm, pair);
  }

  [Fact]
  public Task Load_BuildsOneGroupPerItemType_WithTheCurrentChords() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;

    Assert.Equal(["All items", "Login"], vm.Groups.Select(g => g.Title));
    var login = vm.Groups[1];
    Assert.Equal("login.openBrowser", login.SelectedEnter?.Value);
    Assert.False(login.IsEnterModified);
    Assert.Equal(["Ctrl", "C"], login.Rows[0].Caps);
    Assert.Equal(["Ctrl", "Shift", "C"], login.Rows[1].Caps);
    Assert.False(login.Rows[2].HasChord);
    Assert.Empty(login.Rows[2].Caps);
    Assert.All(login.Rows, r => Assert.False(r.IsModified));
    Assert.True(login.HasRows);
  });

  [Fact]
  public Task Load_Failure_ShowsTheError() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { FailLoad = true };
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;

    Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    Assert.Empty(vm.Groups);
  });

  [Fact]
  public Task ChangingTheEnterAction_SendsItAndMarksTheGroupModified() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    var login = vm.Groups[1];

    login.SelectedEnter = login.EnterChoices[1];
    AvaloniaSession.WaitFor(() => extension.LoginEnter == "login.copyPassword" && login.IsEnterModified);

    await login.ResetEnterCommand.ExecuteAsync(null);
    Assert.Equal("login.openBrowser", extension.LoginEnter);
    Assert.False(login.IsEnterModified);
    Assert.Equal("login.openBrowser", login.SelectedEnter?.Value);
  });

  [Fact]
  public Task RowReset_SendsTheDefaultChord_AndClearsTheModifiedMarker() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    extension.Current["login.copyUsername"] = new ChordDto(0x55, Ctrl, "Ctrl+U");
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    var row = vm.Groups[1].Rows[0];
    Assert.True(row.IsModified);

    await row.ResetCommand.ExecuteAsync(null);

    Assert.False(row.IsModified);
    Assert.Equal(["Ctrl", "C"], row.Caps);
  });

  [Fact]
  public Task RowReset_Failure_ShowsTheErrorOnThePage() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { FailSetHotkey = true };
    extension.Current["login.copyUsername"] = new ChordDto(0x55, Ctrl, "Ctrl+U");
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;

    await vm.Groups[1].Rows[0].ResetCommand.ExecuteAsync(null);

    Assert.Equal("Could not save the shortcuts", vm.ErrorMessage);
    Assert.True(vm.Groups[1].Rows[0].IsModified);
  });

  [Fact]
  public Task EnterReset_SendsNoAction_AndComparesAgainstTheInheritedValue() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { LoginInherited = "login.copyPassword", LoginEnter = "login.copyPassword" };
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    var login = vm.Groups[1];
    Assert.False(login.IsEnterModified);

    login.SelectedEnter = login.EnterChoices[0];
    AvaloniaSession.WaitFor(() => extension.LoginEnter == "login.openBrowser" && login.IsEnterModified);

    await login.ResetEnterCommand.ExecuteAsync(null);

    Assert.Null(extension.EnterRequests[^1]);
    Assert.Equal("login.copyPassword", login.SelectedEnter?.Value);
    Assert.False(login.IsEnterModified);
  });

  [Fact]
  public Task Press_AKeyWithoutAVirtualKey_IsRejectedWithAnExplanation() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    var capture = new ChordCaptureViewModel(vm, vm.Groups[1].Rows[2]);

    capture.Press(Key.OemClear, KeyModifiers.None);

    Assert.False(capture.IsValid);
    Assert.False(capture.IsNone);
    Assert.Equal("This key cannot be used.", capture.ErrorMessage);
    capture.Press(Key.LeftCtrl, KeyModifiers.Control);
    Assert.Null(capture.ErrorMessage);
  });

  [Theory]
  [InlineData(Key.Escape, 0x1B)]
  [InlineData(Key.Multiply, 0x6A)]
  [InlineData(Key.Add, 0x6B)]
  [InlineData(Key.Subtract, 0x6D)]
  [InlineData(Key.Decimal, 0x6E)]
  [InlineData(Key.Divide, 0x6F)]
  [InlineData(Key.OemBackslash, 0xE2)]
  public void VirtualKeyOf_MapsEveryKeyTheExtensionCanDisplay(Key key, int expected)
  {
    Assert.Equal(expected, HotkeyKeyMap.VirtualKeyOf(key));
    Assert.True(HotkeyKeys.IsKey(expected));
  }

  [Fact]
  public Task ResetAll_AsksTheExtensionAndReloads() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    extension.Current["login.copyUsername"] = null;
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    Assert.False(vm.Groups[1].Rows[0].HasChord);

    await vm.ResetAllCommand.ExecuteAsync(null);

    Assert.Equal(1, extension.Resets);
    Assert.True(vm.Groups[1].Rows[0].HasChord);
  });

  [Fact]
  public Task EditCommand_RaisesEditRequestedForThatRow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    HotkeyRowViewModel? requested = null;
    vm.EditRequested += row => requested = row;

    vm.Groups[1].Rows[1].EditCommand.Execute(null);

    Assert.Same(vm.Groups[1].Rows[1], requested);
  });

  [Theory]
  [InlineData(Key.C, KeyModifiers.Control, "Ctrl+C", true)]
  [InlineData(Key.C, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+C", true)]
  [InlineData(Key.Enter, KeyModifiers.Alt, "Alt+Enter", true)]
  [InlineData(Key.F5, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, "Ctrl+Alt+Shift+F5", true)]
  [InlineData(Key.OemComma, KeyModifiers.Control, "Ctrl+,", true)]
  [InlineData(Key.D7, KeyModifiers.Alt, "Alt+7", true)]
  [InlineData(Key.C, KeyModifiers.None, "C", false)]
  [InlineData(Key.C, KeyModifiers.Shift, "Shift+C", false)]
  [InlineData(Key.Enter, KeyModifiers.None, "Enter", false)]
  [InlineData(Key.LeftCtrl, KeyModifiers.Control, "Ctrl", false)]
  [InlineData(Key.LeftShift, KeyModifiers.Shift, "Shift", false)]
  public Task Press_ShowsTheChordLive_AndOnlyAcceptsCtrlOrAltCombinations(Key key, KeyModifiers modifiers, string display, bool valid) => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    var capture = new ChordCaptureViewModel(vm, vm.Groups[1].Rows[2]);
    Assert.True(capture.IsNone);
    Assert.True(capture.IsValid);

    capture.Press(key, modifiers);

    Assert.Equal(display, string.Join('+', capture.Caps));
    Assert.Equal(valid, capture.IsValid);
    Assert.False(capture.IsNone);
  });

  [Fact]
  public Task Press_WindowsKey_IsRejectedWithAnExplanation() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    var capture = new ChordCaptureViewModel(vm, vm.Groups[1].Rows[0]);

    capture.Press(Key.C, KeyModifiers.Control | KeyModifiers.Meta);

    Assert.False(capture.IsValid);
    Assert.Equal("The Windows key cannot be used.", capture.ErrorMessage);
  });

  [Fact]
  public Task ClearAndResetButtons_ChangeThePendingChord() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    extension.Current["login.copyUsername"] = new ChordDto(0x55, Ctrl, "Ctrl+U");
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    var capture = new ChordCaptureViewModel(vm, vm.Groups[1].Rows[0]);
    Assert.Equal(["Ctrl", "U"], capture.Caps);

    capture.ResetToDefaultCommand.Execute(null);
    Assert.Equal(["Ctrl", "C"], capture.Caps);

    capture.ClearCommand.Execute(null);
    Assert.True(capture.IsNone);
    Assert.True(capture.IsValid);
    Assert.Null(capture.Chord);

    capture.Press(Key.X, KeyModifiers.None);
    Assert.False(capture.IsValid);
  });

  [Fact]
  public Task Save_SendsTheChord_AndKeepsTheConflictErrorInline() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    var row = vm.Groups[1].Rows[2];
    var capture = new ChordCaptureViewModel(vm, row);

    capture.Press(Key.C, KeyModifiers.Control);
    Assert.False(await capture.SaveAsync());
    Assert.Equal("Ctrl+C is already used by Copy Username (Login)", capture.ErrorMessage);
    Assert.False(row.HasChord);

    capture.Press(Key.R, KeyModifiers.Control);
    Assert.True(await capture.SaveAsync());
    Assert.Null(capture.ErrorMessage);
    Assert.True(row.HasChord);
    Assert.Equal(["Ctrl", "R"], row.Caps);
  });

  [Fact]
  public Task Save_WhileInvalid_SendsNothing() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    var capture = new ChordCaptureViewModel(vm, vm.Groups[1].Rows[2]);
    var before = extension.Calls.Count(c => c == Ops.SetHotkey);

    capture.Press(Key.C, KeyModifiers.Shift);

    Assert.False(await capture.SaveAsync());
    Assert.Equal(before, extension.Calls.Count(c => c == Ops.SetHotkey));
  });

  [Fact]
  public Task Save_Clear_SendsAnUnbind() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    var row = vm.Groups[1].Rows[0];
    var capture = new ChordCaptureViewModel(vm, row);

    capture.ClearCommand.Execute(null);
    Assert.True(await capture.SaveAsync());

    Assert.False(row.HasChord);
    Assert.Null(extension.Current["login.copyUsername"]);
  });

  [Fact]
  public Task CaptureDialog_FollowsTheCaptureAndCapturesKeys() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    var (vm, pair) = await LoadedAsync(extension);
    using var _ = pair;
    var capture = new ChordCaptureViewModel(vm, vm.Groups[1].Rows[2]);
    var dialog = HotkeyCaptureView.CreateDialog(capture);
    Assert.Equal("Shortcut", dialog.Title);
    Assert.Equal("Save", dialog.PrimaryButtonText);
    Assert.Equal("Cancel", dialog.CloseButtonText);
    Assert.True(dialog.IsPrimaryButtonEnabled);

    var press = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Shift, Source = dialog };
    dialog.RaiseEvent(press);

    Assert.True(press.Handled);
    Assert.Equal(["Shift", "C"], capture.Caps);
    Assert.False(dialog.IsPrimaryButtonEnabled);

    var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = dialog };
    dialog.RaiseEvent(escape);
    Assert.False(escape.Handled);
    Assert.Equal(["Shift", "C"], capture.Caps);

    dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Control, Source = dialog });
    Assert.True(dialog.IsPrimaryButtonEnabled);

    var chord = capture.Caps.ToList();
    foreach (var tabModifiers in new[] { KeyModifiers.None, KeyModifiers.Shift })
    {
      var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, KeyModifiers = tabModifiers, Source = dialog };
      dialog.RaiseEvent(args);
      Assert.Equal(chord, capture.Caps);
    }
  });

  private static (SettingsWindow Window, HotkeysView Page) OpenPage(WindowCoordinator coordinator)
  {
    var window = coordinator.ShowSettings();
    window.Show();
    var settings = window.ViewModel!;
    AvaloniaSession.WaitFor(() => settings.Sections.Count == 1);
    var link = settings.Sections[0].Rows.Single(r => r.Key == "hotkeys");
    Assert.True(link.IsPageLink);
    link.OpenCommand.Execute(null);
    window.UpdateLayout();
    var page = window.GetVisualDescendants().OfType<HotkeysView>().Single();
    AvaloniaSession.WaitFor(() => page.IsEffectivelyVisible && settings.Shortcuts!.Groups.Count == 2);
    return (window, page);
  }

  private static void Press(Window window, Key key, PhysicalKey physical)
  {
    window.KeyPress(key, RawInputModifiers.None, physical, null);
    window.KeyRelease(key, RawInputModifiers.None, physical, null);
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  [Fact]
  public Task Page_RendersKeyCapsAndEnterActionPerGroup_InsideTheSettingsWindow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput) { TakeForeground = _ => { } };
    _ = coordinator.RunAsync(() => { });

    var (window, page) = OpenPage(coordinator);
    var vm = window.ViewModel!.Shortcuts!;
    Assert.False(window.FindControl<ScrollViewer>("Scroller")!.IsEffectivelyVisible);
    Assert.False(window.FindControl<TextBox>("SearchBox")!.IsEffectivelyVisible);
    Assert.Same(window, TopLevel.GetTopLevel(page));
    AvaloniaSession.WaitFor(() =>
    {
      window.UpdateLayout();
      return window.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("KeyCap") && b.IsEffectivelyVisible) == 7;
    });

    var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
    Assert.Contains("All items", texts);
    Assert.Contains("Login", texts);
    Assert.Contains("Copy Password", texts);
    Assert.Contains("None", texts);
    Assert.Equal(2, texts.Count(t => t == "Enter action"));
    Assert.Equal(2, window.GetVisualDescendants().OfType<ComboBox>().Count(c => c.IsEffectivelyVisible));
    var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
    Assert.Contains(buttons, b => b.Content as string == "Reset all");
    Assert.Equal(4, buttons.Count(b => b.Content is string glyph && glyph.Length == 1 && glyph[0] > 0xE000));
    Assert.Contains(buttons, b => b.Name == "BackButton");

    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task BackButton_ReturnsToSettings() => session.RunAsync(async () =>
  {
    using var pair = new ChannelPair(new FakeExtension().HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput) { TakeForeground = _ => { } };
    _ = coordinator.RunAsync(() => { });
    var (window, page) = OpenPage(coordinator);

    var back = page.Back;
    window.UpdateLayout();
    var point = back.TranslatePoint(new Avalonia.Point(back.Bounds.Width / 2, back.Bounds.Height / 2), window)!.Value;
    window.MouseDown(point, MouseButton.Left);
    window.MouseUp(point, MouseButton.Left);
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    Assert.Null(window.ViewModel!.Shortcuts);
    window.UpdateLayout();
    Assert.False(page.IsEffectivelyVisible);
    Assert.True(window.FindControl<ScrollViewer>("Scroller")!.IsEffectivelyVisible);
    Assert.True(window.FindControl<TextBox>("SearchBox")!.IsEffectivelyVisible);
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Escape_OnThePage_ReturnsToSettings_AndKeepsTheWindowOpen() => session.RunAsync(async () =>
  {
    using var pair = new ChannelPair(new FakeExtension().HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput) { TakeForeground = _ => { } };
    _ = coordinator.RunAsync(() => { });
    var (window, _) = OpenPage(coordinator);

    Press(window, Key.Escape, PhysicalKey.Escape);

    Assert.Null(window.ViewModel!.Shortcuts);
    Assert.True(window.IsVisible);
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task EditButton_OpensTheCaptureDialog_AndEscapeCancelsTheCaptureOnly() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput) { TakeForeground = _ => { } };
    _ = coordinator.RunAsync(() => { });
    var (window, page) = OpenPage(coordinator);
    var vm = window.ViewModel!.Shortcuts!;

    vm.Groups[1].Rows[2].EditCommand.Execute(null);
    AvaloniaSession.WaitFor(() => page.OpenDialog is { IsLoaded: true });
    Assert.Equal("Shortcut", page.OpenDialog!.Title);
    var settled = DateTime.UtcNow.AddMilliseconds(300);
    AvaloniaSession.WaitFor(() => DateTime.UtcNow > settled);

    var capture = (ChordCaptureViewModel)((HotkeyCaptureView)page.OpenDialog.Content!).DataContext!;
    window.KeyPress(Key.R, RawInputModifiers.Control, PhysicalKey.R, null);
    window.KeyRelease(Key.R, RawInputModifiers.Control, PhysicalKey.R, null);
    Assert.Equal(["Ctrl", "R"], capture.Caps);
    Press(window, Key.Escape, PhysicalKey.Escape);

    AvaloniaSession.WaitFor(() => page.OpenDialog is null);
    Assert.Same(vm, window.ViewModel.Shortcuts);
    Assert.DoesNotContain(Ops.SetHotkey, extension.Calls);
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task ReopeningThePage_OpensOneCaptureDialogPerEdit() => session.RunAsync(async () =>
  {
    using var pair = new ChannelPair(new FakeExtension().HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput) { TakeForeground = _ => { } };
    _ = coordinator.RunAsync(() => { });
    var (window, page) = OpenPage(coordinator);
    window.ViewModel!.CloseShortcuts();
    (window, page) = OpenPage(coordinator);

    window.ViewModel!.Shortcuts!.Groups[1].Rows[2].EditCommand.Execute(null);
    AvaloniaSession.WaitFor(() => page.OpenDialog is { IsLoaded: true });
    page.OpenDialog!.Hide();
    AvaloniaSession.WaitFor(() => page.OpenDialog is null);
    var settled = DateTime.UtcNow.AddMilliseconds(500);
    AvaloniaSession.WaitFor(() => DateTime.UtcNow > settled);

    Assert.Null(page.OpenDialog);
    window.Close();
    await Task.CompletedTask;
  });
}
