using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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

      if (Reprompt && op is Ops.DownloadAttachment or Ops.UploadAttachment or Ops.DeleteAttachment or Ops.RotatePassword)
        throw new IpcException(IpcErrors.Reprompt);

      switch (op)
      {
        case Ops.GetItem:
          return IpcJson.ToElement(new ItemDto(ItemId, "GitHub", Type, "Work", null, false, false, false, Revision, Revision, null,
            [new FieldDto(ItemFields.Username, "Username", "Login credentials", "octocat", false), new FieldDto(ItemFields.Password, "Password", "Login credentials", null, true)],
            Attachments: [.. Attachments]), IpcJson.Default.ItemDto);
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
    Assert.Equal(((decimal?)20m, true, false), (vm.Length, vm.Numbers, vm.Special));

    vm.Length = 24;
    vm.Special = true;
    await vm.InitializeAsync();

    Assert.Equal(new string('x', 24), vm.Value);
    var sent = IpcJson.Read(Assert.Single(h.Extension.Payloads(Ops.Generate)), IpcJson.Default.GeneratorOptions);
    Assert.Equal((GeneratorKinds.Password, 24, true), (sent.Kind, sent.Length, sent.Special));
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

    h.Extension.Reprompt = true;
    await vm.Attachments[1].DeleteCommand.ExecuteAsync(null);
    Assert.Equal("Delete attachment|\"scan.png\" will be deleted permanently. You cannot undo this.||Delete attachment", h.Prompts[^1]);
    Assert.Equal(Ops.DeleteAttachment, IpcJson.Read(h.Extension.Payloads(Ops.VerifyReprompt)[^1], IpcJson.Default.RepromptRequest).Op);
    Assert.Equal("Deleted scan.png", h.Toasts[^1]);

    h.Extension.FailWith[Ops.DeleteAttachment] = IpcErrors.Failed;
    await vm.Attachments[0].DeleteCommand.ExecuteAsync(null);
    Assert.Equal("Bitwarden did not delete the attachment.", h.DangerFailure);

    h.Extension.FailWith[Ops.UploadAttachment] = IpcErrors.TooLarge;
    await vm.AddAttachmentCommand.ExecuteAsync(null);
    Assert.Equal("The file is larger than the 500 MB that Bitwarden allows.", vm.ErrorMessage);
  });

  [Fact]
  public Task Rotate_ConfirmsFirst_AndReportsEveryOutcome() => session.RunAsync(async () =>
  {
    File.Delete(CompanionSettings.FilePath);
    using var h = new Harness();
    var vm = h.Item();
    await vm.LoadAsync();
    Assert.True(vm.CanRotate);

    h.Answer = false;
    await vm.RotateCommand.ExecuteAsync(null);
    Assert.Equal("Rotate password|Replace the password for GitHub? The old password is kept in password history.|Rotate|Cancel", Assert.Single(h.Prompts));
    Assert.Empty(h.Extension.Payloads(Ops.RotatePassword));

    h.Answer = true;
    await vm.RotateCommand.ExecuteAsync(null);
    var rotate = IpcJson.Read(Assert.Single(h.Extension.Payloads(Ops.RotatePassword)), IpcJson.Default.RotateRequest);
    Assert.Equal((ItemId, Revision, GeneratorKinds.Password), (rotate.Id, rotate.RevisionDate, rotate.Options.Kind));
    Assert.Equal("Password replaced and copied", h.Toasts[^1]);

    CompanionSettings.SaveGenerator(new GeneratorOptions("username"));
    h.Extension.FailWith[Ops.RotatePassword] = IpcErrors.Unknown;
    await vm.RotateCommand.ExecuteAsync(null);
    Assert.Equal(GeneratorKinds.Password, IpcJson.Read(h.Extension.Payloads(Ops.RotatePassword)[^1], IpcJson.Default.RotateRequest).Options.Kind);
    Assert.Contains("nothing was copied", vm.ErrorMessage, StringComparison.Ordinal);

    h.Extension.FailWith[Ops.RotatePassword] = IpcErrors.Conflict;
    await vm.RotateCommand.ExecuteAsync(null);
    Assert.Equal("This item changed in the vault. The password was not replaced and nothing was copied.", vm.ErrorMessage);

    h.Extension.FailWith[Ops.RotatePassword] = IpcErrors.Failed;
    await vm.RotateCommand.ExecuteAsync(null);
    Assert.Equal("Bitwarden did not save the new password. Nothing was copied.", vm.ErrorMessage);

    h.Extension.FailWith.Clear();
    h.Extension.Reprompt = true;
    h.Password = "correct";
    await vm.RotateCommand.ExecuteAsync(null);
    Assert.Equal(Ops.RotatePassword, IpcJson.Read(h.Extension.Payloads(Ops.VerifyReprompt)[^1], IpcJson.Default.RepromptRequest).Op);

    h.Extension.Type = 3;
    await vm.LoadAsync();
    Assert.False(vm.CanRotate);
    File.Delete(CompanionSettings.FilePath);
  });

  [Fact]
  public Task Coordinator_RotateItem_OpensTheWindow_AndConfirmsAfterLoading() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });

    var reply = pair.Extension.RequestAsync(Ops.RotateItem, IpcJson.ToElement(new ItemRequest(ItemId), IpcJson.Default.ItemRequest));
    AvaloniaSession.WaitFor(() => reply.IsCompleted);
    AvaloniaSession.WaitFor(() => coordinator.ItemWindows.Count == 1);
    var window = coordinator.ItemWindows.Single();
    var prompts = new List<string>();
    window.ViewModel!.Confirm = (title, _, _, _) =>
    {
      prompts.Add(title);
      return Task.FromResult(false);
    };
    AvaloniaSession.WaitFor(() => prompts.Count == 1);
    Assert.Equal("Rotate password", prompts[0]);
    Assert.Single(extension.Payloads(Ops.GetItem));
    Assert.Empty(extension.Payloads(Ops.RotatePassword));
    window.Close();
    await Task.CompletedTask;
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
