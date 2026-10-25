using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.ViewModels;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Tests;

public class VaultToolsTests(AvaloniaSession session)
{
  private const string FolderId = "6f1c6b37-2a43-4a48-9a55-1d5c1f7c0a11";
  private static readonly DateTime Revision = new(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
  private static readonly string ItemId = Guid.NewGuid().ToString();

  private sealed class FakeExtension
  {
    private long _entry;

    public List<(string Op, JsonElement? Payload)> Requests { get; } = [];
    public List<AttachmentDto> Attachments { get; } = [new("att1", "report.pdf", "4 KB"), new("att2", "scan.png", "1.2 MB")];
    public List<GeneratedEntry> History { get; } = [];
    public Dictionary<string, string> FailWith { get; } = [];
    public bool Reprompt { get; set; }
    public int Type { get; set; } = 1;

    public bool HasPassword { get; set; } = true;

    public List<JsonElement?> Payloads(string op)
    {
      lock (Requests)
        return [.. Requests.Where(r => r.Op == op).Select(r => r.Payload)];
    }

    public async Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
    {
      lock (Requests)
        Requests.Add((op, payload));
      if (FailWith.TryGetValue(op, out var code))
        throw new IpcException(code);
      if (op == Ops.VerifyReprompt)
      {
        var verify = IpcJson.Read(payload, IpcJson.Default.RepromptRequest);
        if (verify.Password != "correct")
          return IpcJson.ToElement(new RepromptResponse(false, 0), IpcJson.Default.RepromptResponse);
        Reprompt = false;
        return IpcJson.ToElement(new RepromptResponse(true, 0, await HandleAsync(verify.Op!, verify.Payload, cancellationToken)), IpcJson.Default.RepromptResponse);
      }

      if (Reprompt && op is Ops.DownloadAttachment or Ops.UploadAttachment or Ops.DeleteAttachment or Ops.RotatePassword or Ops.Copy)
        throw new IpcException(IpcErrors.Reprompt);

      switch (op)
      {
        case Ops.GetItem:
          return IpcJson.ToElement(new ItemDto(ItemId, "GitHub", Type, "Work", null, false, false, false, Revision, Revision, null,
            [new FieldDto(ItemFields.Username, "Username", "Login credentials", "octocat", false), new FieldDto(ItemFields.Password, "Password", "Login credentials", null, true)],
            Attachments: [.. Attachments], HasPassword: HasPassword), IpcJson.Default.ItemDto);
        case Ops.Reveal:
          return IpcJson.ToElement(new SecretResponse("hunter2"), IpcJson.Default.SecretResponse);
        case Ops.ListFilters:
          return IpcJson.ToElement(new FiltersResponse([new FilterRef(FolderId, "Work"), new FilterRef(Guid.NewGuid().ToString(), "Personal")], [], []), IpcJson.Default.FiltersResponse);
        case Ops.GetEditableItem:
          return IpcJson.ToElement(EditTests.Sample(1, ItemId), IpcJson.Default.EditableItem);
        case Ops.Generate:
          var options = IpcJson.Read(payload, IpcJson.Default.GeneratorOptions);
          var value = options.Kind == GeneratorKinds.Passphrase ? "correct-horse-battery-staple" : new string('x', options.Length);
          var entry = new GeneratedEntry(++_entry, options.Kind, Revision);
          History.Insert(0, entry);
          return IpcJson.ToElement(new GeneratedResponse(value, entry.Id), IpcJson.Default.GeneratedResponse);
        case Ops.ListGeneratorHistory:
          return IpcJson.ToElement(new GeneratorHistoryResponse([.. History]), IpcJson.Default.GeneratorHistoryResponse);
        case Ops.UploadAttachment or Ops.DeleteAttachment or Ops.RotatePassword:
          return IpcJson.ToElement(new SavedResponse(ItemId, Revision.AddDays(1)), IpcJson.Default.SavedResponse);
        default:
          return null;
      }
    }
  }

  private sealed class Harness : IDisposable
  {
    private readonly ChannelPair _pair;

    public Harness()
    {
      _pair = new ChannelPair(Extension.HandleAsync);
      Channel = new PipeChannel(_pair.CompanionInput, _pair.CompanionOutput, (_, _, _) => Task.FromResult<JsonElement?>(null));
      _ = Task.Run(Channel.RunAsync);
    }

    public FakeExtension Extension { get; } = new();

    public PipeChannel Channel { get; }

    public List<string> Prompts { get; } = [];

    public bool Answer { get; set; } = true;

    public string? Password { get; set; }

    public string? Picked { get; set; }

    public string? DangerFailure { get; private set; }

    public List<string> Toasts { get; } = [];

    public List<string?> BusyMessages { get; } = [];

    public long? RotateEntry { get; set; }

    public ItemViewModel Item()
    {
      var vm = new ItemViewModel(ItemId, Channel)
      {
        Confirm = (title, message, primary, close) =>
        {
          Prompts.Add($"{title}|{message}|{primary}|{close}");
          return Task.FromResult(Answer);
        },
        ConfirmAction = ConfirmActionAsync,
        PickSavePath = _ => Task.FromResult(Picked),
        PickOpenPath = () => Task.FromResult(Picked),
        Toast = (message, _) => Toasts.Add(message),
        OpenRotateGenerator = () => Task.FromResult(RotateEntry),
      };
      vm.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(ItemViewModel.BusyMessage))
          BusyMessages.Add(vm.BusyMessage);
      };
      RepromptDriver.Attach(vm, () => Password, _ => { });
      return vm;
    }

    public async Task<bool> ConfirmActionAsync(ActionDialogRequest request)
    {
      Prompts.Add($"{request.Title}|{request.Message}|{request.PrimaryLabel}|{request.DangerLabel}");
      if (!Answer)
        return false;
      DangerFailure = await request.Danger();
      return DangerFailure is null;
    }

    public void Dispose()
    {
      Channel.Dispose();
      _pair.Dispose();
    }
  }

  private static void SaveScreenshot(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
  {
    if (Environment.GetEnvironmentVariable("COMPANION_SCREENSHOT_DIR") is { Length: > 0 } dir)
      frame.Save(Path.Combine(dir, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
  }

  [Fact]
  public Task Generator_GeneratesFromTypedOptions_SavesThem_AndCopiesThroughTheExtension() => session.RunAsync(async () =>
  {
    File.Delete(CompanionSettings.FilePath);
    using var h = new Harness();
    var toasts = new List<string>();
    var vm = new GeneratorViewModel(h.Channel) { Toast = toasts.Add };
    string? used = null;
    vm.UseRequested = v => used = v;
    Assert.Equal(((decimal?)20m, true, true, true), (vm.Length, vm.Numbers, vm.Special, vm.AvoidAmbiguous));

    vm.Length = 24;
    vm.Special = false;
    await vm.InitializeAsync();

    Assert.Equal(new string('x', 24), vm.Value);
    var sent = IpcJson.Read(Assert.Single(h.Extension.Payloads(Ops.Generate)), IpcJson.Default.GeneratorOptions);
    Assert.Equal((GeneratorKinds.Password, 24, false), (sent.Kind, sent.Length, sent.Special));
    Assert.Equal(24, CompanionSettings.LoadGenerator().Length);
    Assert.Single(vm.History);
    Assert.True(vm.HasHistory);

    await vm.CopyCommand.ExecuteAsync(null);
    await vm.History[0].CopyCommand.ExecuteAsync(null);
    Assert.All(h.Extension.Payloads(Ops.CopyGenerated), p => Assert.Equal(1, IpcJson.Read(p, IpcJson.Default.HistoryEntryRequest).Id));
    Assert.Equal(["Copied", "Copied"], toasts);
    Assert.Null(vm.StatusMessage);
    vm.UseCommand.Execute(null);
    Assert.Equal(new string('x', 24), used);

    vm.SelectedTab = 1;
    Assert.Null(vm.Value);
    await vm.RegenerateCommand.ExecuteAsync(null);
    Assert.Equal("correct-horse-battery-staple", vm.Value);
    Assert.Equal(GeneratorKinds.Passphrase, new GeneratorViewModel(h.Channel).Kind);

    h.Extension.FailWith[Ops.Generate] = IpcErrors.Invalid;
    await vm.RegenerateCommand.ExecuteAsync(null);
    Assert.StartsWith("Check the generator options", vm.ErrorMessage, StringComparison.Ordinal);

    vm.OnLocked();
    Assert.Null(vm.Value);
    Assert.Empty(vm.History);
    File.Delete(CompanionSettings.FilePath);
  });

  [Fact]
  public Task GeneratorPanel_Renders_WithMaskedHistory() => session.RunAsync(async () =>
  {
    using var h = new Harness();
    var vm = new GeneratorViewModel(h.Channel) { Special = true };
    await vm.InitializeAsync();
    await vm.RegenerateCommand.ExecuteAsync(null);
    var window = new Window { Width = 460, Height = 900, Background = Avalonia.Media.Brushes.Transparent, Content = new GeneratorView { DataContext = vm, Margin = new Avalonia.Thickness(24) } };
    window.Show();
    window.UpdateLayout();

    var view = window.GetVisualDescendants().OfType<GeneratorView>().Single();
    Assert.Equal(vm.Value, view.FindControl<SelectableTextBlock>("ValueText")!.Text);
    Assert.Equal("Use password", GeneratorView.CreateDialog(vm).PrimaryButtonText);
    Assert.True(view.FindControl<StackPanel>("PasswordOptions")!.IsEffectivelyVisible);
    Assert.False(view.FindControl<StackPanel>("PassphraseOptions")!.IsEffectivelyVisible);
    var history = view.FindControl<ItemsControl>("HistoryList")!;
    Assert.Equal(2, history.ItemCount);
    Assert.DoesNotContain(history.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vm.Value);
    Assert.Equal(2, history.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Text == FieldViewModel.Mask));
    var frame = window.CaptureRenderedFrame();
    Assert.NotNull(frame);
    SaveScreenshot(frame, "generator.png");

    Assert.Equal("Generator", GeneratorView.CreateDialog(vm).Title);
    vm.SelectedTab = 1;
    window.UpdateLayout();
    Assert.True(view.FindControl<StackPanel>("PassphraseOptions")!.IsEffectivelyVisible);
    window.Close();
    File.Delete(CompanionSettings.FilePath);
  });

  [Fact]
  public Task GeneratorDialog_ReturnsTheUsedValue_AndBlanksOnLock() => session.RunAsync(async () =>
  {
    using var h = new Harness();
    var window = new Window { Width = 600, Height = 900 };
    window.Show();

    var shown = GeneratorView.ShowAsync(window, h.Channel);
    AvaloniaSession.WaitFor(() => window.GetVisualDescendants().OfType<GeneratorView>().FirstOrDefault()?.DataContext is GeneratorViewModel { HasValue: true });
    var vm = (GeneratorViewModel)window.GetVisualDescendants().OfType<GeneratorView>().Single().DataContext!;
    await vm.CopyCommand.ExecuteAsync(null);
    Assert.Equal("Copied", ToastWindow.Current!.Text);
    GeneratorView.OnLocked();
    Assert.False(vm.HasValue);
    await vm.RegenerateCommand.ExecuteAsync(null);
    var use = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PrimaryButton");
    Assert.Equal("Use password", use.Content);
    use.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    AvaloniaSession.WaitFor(() => shown.IsCompleted);
    Assert.Equal(new string('x', 20), await shown);
    window.Close();
    File.Delete(CompanionSettings.FilePath);
  });

  [Fact]
  public Task RotateDialog_HoldsToShowTheCurrentPassword_AndSavesTheShownEntry() => session.RunAsync(async () =>
  {
    using var h = new Harness();
    var item = h.Item();
    await item.LoadAsync();
    var window = new Window { Width = 600, Height = 900 };
    window.Show();

    var shown = GeneratorView.ShowForRotateAsync(window, item);
    AvaloniaSession.WaitFor(() => window.GetVisualDescendants().OfType<GeneratorView>().FirstOrDefault()?.DataContext is GeneratorViewModel { HasValue: true });
    var view = window.GetVisualDescendants().OfType<GeneratorView>().Single();
    var vm = (GeneratorViewModel)view.DataContext!;
    window.UpdateLayout();
    Assert.Equal("Rotate password", window.GetVisualDescendants().OfType<FluentAvalonia.UI.Controls.FAContentDialog>().Single().Title);
    Assert.Equal("Cancel", window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseButton").Content);
    Assert.True(view.FindControl<Border>("CurrentPasswordGroup")!.IsEffectivelyVisible);
    var text = view.FindControl<TextBlock>("CurrentPasswordText")!;
    Assert.Equal(FieldViewModel.Mask, text.Text);

    var peek = view.FindControl<Button>("PeekButton")!;
    var centre = peek.TranslatePoint(new Point(peek.Bounds.Width / 2, peek.Bounds.Height / 2), window)!.Value;
    window.MouseDown(centre, MouseButton.Left);
    AvaloniaSession.WaitFor(() => text.Text == "hunter2");
    window.MouseUp(centre, MouseButton.Left);
    Assert.Equal(FieldViewModel.Mask, text.Text);
    Assert.Null(vm.CurrentPassword!.RevealedValue);

    await vm.CurrentPassword.CopyCommand.ExecuteAsync(null);
    Assert.Equal(ItemFields.Password, IpcJson.Read(Assert.Single(h.Extension.Payloads(Ops.Copy)), IpcJson.Default.FieldRequest).Field);

    await vm.RegenerateCommand.ExecuteAsync(null);
    var save = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PrimaryButton");
    Assert.Equal("Save", save.Content);
    save.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    AvaloniaSession.WaitFor(() => shown.IsCompleted);
    Assert.Equal(h.Extension.History[0].Id, await shown);

    h.Extension.Reprompt = true;
    var prompted = GeneratorView.ShowForRotateAsync(window, item);
    AvaloniaSession.WaitFor(() => window.GetVisualDescendants().OfType<GeneratorView>().FirstOrDefault()?.DataContext is GeneratorViewModel { HasValue: true });
    var second = (GeneratorViewModel)window.GetVisualDescendants().OfType<GeneratorView>().Single().DataContext!;
    await second.CurrentPassword!.CopyCommand.ExecuteAsync(null);
    AvaloniaSession.WaitFor(() => prompted.IsCompleted);
    Assert.Null(await prompted);
    Assert.True(item.IsPrompting);
    window.Close();
    File.Delete(CompanionSettings.FilePath);
  });

  [Fact]
  public Task Attachments_RenderAsAStackedGroup_AndDownloadToThePickedPath() => session.RunAsync(async () =>
  {
    using var h = new Harness();
    var vm = h.Item();
    var window = new ItemWindow(vm) { Height = 700 };
    window.Show();
    await vm.LoadAsync();
    window.UpdateLayout();

    var group = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "AttachmentsGroup");
    Assert.True(group.IsEffectivelyVisible);
    Assert.Equal(2, window.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "AttachmentList").ItemCount);
    Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddAttachmentButton").IsEffectivelyVisible);
    var frame = window.CaptureRenderedFrame();
    Assert.NotNull(frame);
    SaveScreenshot(frame, "attachments.png");

    h.Picked = null;
    await vm.Attachments[0].DownloadCommand.ExecuteAsync(null);
    Assert.Empty(h.Extension.Payloads(Ops.DownloadAttachment));

    h.Picked = @"C:\Users\jane\Downloads\report.pdf";
    await vm.Attachments[0].DownloadCommand.ExecuteAsync(null);
    var request = IpcJson.Read(Assert.Single(h.Extension.Payloads(Ops.DownloadAttachment)), IpcJson.Default.AttachmentRequest);
    Assert.Equal((ItemId, "att1", h.Picked, Revision), (request.Id, request.AttachmentId, request.Path, request.RevisionDate));
    Assert.Equal("Saved report.pdf", h.Toasts[^1]);
    Assert.Equal(["Downloading attachment…", null], h.BusyMessages);

    h.Extension.FailWith[Ops.DownloadAttachment] = IpcErrors.Premium;
    await vm.Attachments[0].DownloadCommand.ExecuteAsync(null);
    Assert.Equal("Attachments need Bitwarden Premium or an organisation that allows them.", vm.ErrorMessage);
    window.Close();
  });

  [Fact]
  public Task Attachments_UploadAndDelete_AreRepromptGated_AndDeleteUsesTheDangerConfirm() => session.RunAsync(async () =>
  {
    using var h = new Harness();
    h.Extension.Reprompt = true;
    var vm = h.Item();
    await vm.LoadAsync();

    h.Picked = @"C:\Users\jane\notes.txt";
    h.Password = "correct";
    await vm.AddAttachmentCommand.ExecuteAsync(null);
    var upload = IpcJson.Read(h.Extension.Payloads(Ops.VerifyReprompt)[0], IpcJson.Default.RepromptRequest);
    Assert.Equal(Ops.UploadAttachment, upload.Op);
    Assert.Equal(h.Picked, IpcJson.Read(upload.Payload, IpcJson.Default.AttachmentRequest).Path);
    Assert.Equal("Attached notes.txt", h.Toasts[^1]);
    Assert.Equal(["Uploading attachment…", null], h.BusyMessages);

    h.Extension.Reprompt = true;
    await vm.Attachments[1].DeleteCommand.ExecuteAsync(null);
    Assert.Equal("Delete attachment|\"scan.png\" will be deleted permanently. You cannot undo this.||Delete attachment", h.Prompts[^1]);
    Assert.Equal(Ops.DeleteAttachment, IpcJson.Read(h.Extension.Payloads(Ops.VerifyReprompt)[^1], IpcJson.Default.RepromptRequest).Op);
    Assert.Equal("Deleted scan.png", h.Toasts[^1]);
    Assert.Contains("Deleting attachment…", h.BusyMessages);

    h.Extension.FailWith[Ops.DeleteAttachment] = IpcErrors.Failed;
    await vm.Attachments[0].DeleteCommand.ExecuteAsync(null);
    Assert.Equal("Bitwarden did not delete the attachment.", h.DangerFailure);

    h.Extension.FailWith[Ops.UploadAttachment] = IpcErrors.TooLarge;
    await vm.AddAttachmentCommand.ExecuteAsync(null);
    Assert.Equal("The file is larger than the 500 MB that Bitwarden allows.", vm.ErrorMessage);
  });

  [Fact]
  public Task Rotate_SavesTheGeneratorValue_AndReportsEveryOutcome() => session.RunAsync(async () =>
  {
    using var h = new Harness();
    var vm = h.Item();
    await vm.LoadAsync();
    Assert.True(vm.CanRotate);
    var password = vm.Groups.SelectMany(g => g.Fields).Single(f => f.Key == ItemFields.Password);
    Assert.True(password.CanRotate);
    Assert.False(vm.Groups.SelectMany(g => g.Fields).Single(f => f.Key == ItemFields.Username).CanRotate);

    await password.RotateCommand.ExecuteAsync(null);
    Assert.Empty(h.Extension.Payloads(Ops.RotatePassword));
    Assert.Empty(h.Prompts);
    Assert.Empty(h.BusyMessages);

    h.RotateEntry = 7;
    await vm.RotateCommand.ExecuteAsync(null);
    var rotate = IpcJson.Read(Assert.Single(h.Extension.Payloads(Ops.RotatePassword)), IpcJson.Default.RotateRequest);
    Assert.Equal((ItemId, Revision, 7L, null), (rotate.Id, rotate.RevisionDate, rotate.EntryId, rotate.Options));
    Assert.Equal("Password replaced and copied", h.Toasts[^1]);
    Assert.Equal(["Saving the new password to Bitwarden…", null], h.BusyMessages);
    Assert.False(vm.IsBusy);
    var alerts = 0;
    vm.WriteFailed += () => alerts++;
    Assert.False(vm.IsWriteFailure);

    h.Extension.FailWith[Ops.RotatePassword] = IpcErrors.Unknown;
    await vm.RotateCommand.ExecuteAsync(null);
    Assert.Contains("nothing was copied", vm.ErrorMessage, StringComparison.Ordinal);
    Assert.True(vm.IsWriteFailure);
    Assert.Equal(1, alerts);

    h.Extension.FailWith[Ops.RotatePassword] = IpcErrors.Conflict;
    await vm.RotateCommand.ExecuteAsync(null);
    Assert.Equal("This item changed in the vault. The password was not replaced and nothing was copied.", vm.ErrorMessage);

    h.Extension.FailWith[Ops.RotatePassword] = IpcErrors.Failed;
    await vm.RotateCommand.ExecuteAsync(null);
    Assert.Equal("Bitwarden did not save the new password. Nothing was copied.", vm.ErrorMessage);
    Assert.Equal(3, alerts);
    vm.ErrorMessage = null;
    Assert.False(vm.IsWriteFailure);

    h.Extension.FailWith.Clear();
    h.Extension.Reprompt = true;
    h.Password = "correct";
    await vm.RotateCommand.ExecuteAsync(null);
    Assert.Equal(Ops.RotatePassword, IpcJson.Read(h.Extension.Payloads(Ops.VerifyReprompt)[^1], IpcJson.Default.RepromptRequest).Op);

    h.Extension.HasPassword = false;
    await vm.LoadAsync();
    Assert.False(vm.CanRotate);
    Assert.False(vm.Groups.SelectMany(g => g.Fields).Single(f => f.Key == ItemFields.Password).CanRotate);

    h.Extension.HasPassword = true;
    h.Extension.Type = 3;
    await vm.LoadAsync();
    Assert.False(vm.CanRotate);
  });

  [Fact]
  public Task Coordinator_RotateItem_OpensTheWindow_AndTheGeneratorAfterLoading() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });

    var reply = pair.Extension.RequestAsync(Ops.RotateItem, IpcJson.ToElement(new ItemRequest(ItemId), IpcJson.Default.ItemRequest));
    AvaloniaSession.WaitFor(() => reply.IsCompleted);
    AvaloniaSession.WaitFor(() => coordinator.ItemWindows.Count == 1);
    var window = coordinator.ItemWindows.Single();
    var opened = 0;
    window.ViewModel!.OpenRotateGenerator = () =>
    {
      opened++;
      return Task.FromResult<long?>(null);
    };
    AvaloniaSession.WaitFor(() => opened == 1);
    Assert.Single(extension.Payloads(Ops.GetItem));
    Assert.Empty(extension.Payloads(Ops.RotatePassword));
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Coordinator_ShowGenerator_IsOneWindow_AndSavesTheValueAsANewLogin() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });

    var reply = pair.Extension.RequestAsync(Ops.ShowGenerator, null);
    AvaloniaSession.WaitFor(() => reply.IsCompleted);
    var window = coordinator.ShowGenerator();
    Assert.Same(window, coordinator.ShowGenerator());
    var vm = window.ViewModel!;
    AvaloniaSession.WaitFor(() => vm.HasValue);
    Assert.Single(extension.Payloads(Ops.Generate));
    window.UpdateLayout();
    Assert.False(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "CurrentPasswordGroup").IsEffectivelyVisible);
    var save = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveAsNewItemButton");
    Assert.True(save.IsEffectivelyEnabled);

    save.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    AvaloniaSession.WaitFor(() => coordinator.NewItemWindows.Count == 1);
    var created = coordinator.NewItemWindows.Single().ViewModel!;
    Assert.True(created.Editor!.IsLogin);
    Assert.Equal(vm.Value, created.Editor.Password);
    Assert.False(created.Editor.IsDirty);

    coordinator.OnLocked();
    Assert.False(vm.HasValue);
    Assert.Empty(coordinator.NewItemWindows);
    window.Close();
    Assert.NotSame(window, coordinator.ShowGenerator());
    coordinator.ShowGenerator().Close();
    await Task.CompletedTask;
    File.Delete(CompanionSettings.FilePath);
  });

  [Fact]
  public Task EditForm_GenerateButton_FillsThePassword() => session.RunAsync(async () =>
  {
    using var h = new Harness();
    var vm = h.Item();
    vm.OpenGenerator = () => Task.FromResult<string?>("generated-value-1");
    var window = new ItemWindow(vm) { Height = 700 };
    window.Show();
    await vm.LoadAsync();
    await vm.EditCommand.ExecuteAsync(null);
    window.UpdateLayout();

    var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "GeneratePasswordButton");
    Assert.True(button.IsEffectivelyVisible);
    await vm.Editor!.GeneratePasswordCommand.ExecuteAsync(null);
    Assert.Equal("generated-value-1", vm.Editor.Password);

    vm.OpenGenerator = () => Task.FromResult<string?>(null);
    await vm.Editor.GeneratePasswordCommand.ExecuteAsync(null);
    Assert.Equal("generated-value-1", vm.Editor.Password);
    h.Answer = true;
    window.Close();
  });

  [Fact]
  public void CompanionSettings_FallBackToDefaults_AndRoundTrip()
  {
    File.WriteAllText(CompanionSettings.FilePath, "{not json");
    Assert.Equal(new GeneratorOptions(), CompanionSettings.LoadGenerator());
    CompanionSettings.SaveGenerator(new GeneratorOptions(GeneratorKinds.Passphrase, Words: 6));
    Assert.Equal(new GeneratorOptions(GeneratorKinds.Passphrase, Words: 6), CompanionSettings.LoadGenerator());
    Assert.DoesNotContain("correct-horse", File.ReadAllText(CompanionSettings.FilePath), StringComparison.Ordinal);
    File.Delete(CompanionSettings.FilePath);
    Assert.Equal(new GeneratorOptions(), CompanionSettings.LoadGenerator());
  }
}
