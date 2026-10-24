using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class ItemView : UserControl
{
  public ItemView()
  {
    InitializeComponent();
    DataContextChanged += (_, _) =>
    {
      if (DataContext is ItemViewModel viewModel)
      {
        viewModel.Confirm ??= ConfirmAsync;
        viewModel.AskPassword ??= AskPasswordAsync;
        viewModel.ConfirmAction ??= request => ConfirmActionAsync(TopLevel.GetTopLevel(this), request);
        viewModel.PickSavePath ??= PickSavePathAsync;
        viewModel.PickOpenPath ??= PickOpenPathAsync;
        viewModel.OpenGenerator ??= () => GeneratorView.ShowAsync(TopLevel.GetTopLevel(this), viewModel.Channel, canUse: true);
      }
    };
  }

  private async Task<string?> PickSavePathAsync(string suggestedName)
  {
    if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
      return null;
    var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save attachment", SuggestedFileName = suggestedName, ShowOverwritePrompt = true });
    return file?.TryGetLocalPath();
  }

  private async Task<string?> PickOpenPathAsync()
  {
    if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
      return null;
    var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Add attachment", AllowMultiple = false });
    return files.Count > 0 ? files[0].TryGetLocalPath() : null;
  }

  internal static (FAContentDialog Dialog, TextBlock Error, ProgressBar Busy) CreateActionDialog(ActionDialogRequest request)
  {
    var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false, Classes = { "Danger" } };
    var busy = new ProgressBar { IsIndeterminate = true, IsVisible = false };
    var dialog = new FAContentDialog
    {
      Title = request.Title,
      Content = new StackPanel { Spacing = 12, Children = { new TextBlock { Text = request.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, busy, error } },
      PrimaryButtonText = request.PrimaryLabel ?? string.Empty,
      SecondaryButtonText = request.DangerLabel,
      CloseButtonText = "Cancel",
      DefaultButton = FAContentDialogButton.Close,
      Classes = { "Action" },
    };
    dialog.Closing += (_, e) => e.Cancel |= busy.IsVisible;
    return (dialog, error, busy);
  }

  internal static async Task<bool> RunActionAsync(FAContentDialog dialog, TextBlock error, ProgressBar busy, ActionDialogRequest request, Func<Task<string?>> run)
  {
    error.IsVisible = false;
    busy.IsVisible = true;
    dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = false;
    try
    {
      var failure = await run();
      error.Text = failure;
      error.IsVisible = failure is not null;
      return failure is null;
    }
    finally
    {
      busy.IsVisible = false;
      dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = true;
      dialog.PrimaryButtonText = request.PrimaryLabel ?? string.Empty;
    }
  }

  internal static async Task<bool> ConfirmActionAsync(TopLevel? owner, ActionDialogRequest request)
  {
    var (dialog, error, busy) = CreateActionDialog(request);
    var done = false;
    dialog.PrimaryButtonClick += (sender, e) => _ = RunDeferredAsync(e, request.Primary ?? request.Danger);
    dialog.SecondaryButtonClick += (sender, e) => _ = RunDeferredAsync(e, request.Danger);
    await dialog.ShowAsync(owner);
    return done;

    async Task RunDeferredAsync(FAContentDialogButtonClickEventArgs e, Func<Task<string?>> run)
    {
      var deferral = e.GetDeferral();
      try
      {
        done = await RunActionAsync(dialog, error, busy, request, run);
        e.Cancel = !done;
      }
      finally
      {
        deferral.Complete();
      }
    }
  }

  internal static FAContentDialog CreateRepromptDialog(TextBox password) => new()
  {
    Title = "Master password required",
    Content = password,
    PrimaryButtonText = "Verify",
    CloseButtonText = "Cancel",
    DefaultButton = FAContentDialogButton.Primary,
  };

  internal static FAContentDialog CreateConfirmDialog(string title, string message, string primary, string close) => new()
  {
    Title = title,
    Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
    PrimaryButtonText = primary,
    CloseButtonText = close,
    DefaultButton = FAContentDialogButton.Close,
  };

  private async Task<bool> ConfirmAsync(string title, string message, string primary, string close) =>
    await CreateConfirmDialog(title, message, primary, close).ShowAsync(TopLevel.GetTopLevel(this)) == FAContentDialogResult.Primary;

  private void OnVerifyClick(object? sender, RoutedEventArgs e) => _ = PromptRepromptAsync();

  private async Task PromptRepromptAsync()
  {
    if (DataContext is ItemViewModel viewModel && await AskPasswordAsync() is { Length: > 0 } password)
      await viewModel.VerifyRepromptAsync(password);
  }

  private async Task<string?> AskPasswordAsync()
  {
    var password = new TextBox { PasswordChar = '•', PlaceholderText = "Master password", MinWidth = 320 };
    var dialog = CreateRepromptDialog(password);
    password.AttachedToVisualTree += (_, _) => password.Focus();
    var text = await dialog.ShowAsync(TopLevel.GetTopLevel(this)) == FAContentDialogResult.Primary ? password.Text : null;
    password.Text = null;
    return text;
  }
}
