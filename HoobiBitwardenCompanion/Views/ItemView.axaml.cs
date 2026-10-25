using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class ItemView : UserControl
{
  private ItemViewModel? _subscribed;

  public ItemView()
  {
    InitializeComponent();
    DataContextChanged += (_, _) =>
    {
      if (_subscribed is not null)
        _subscribed.PropertyChanged -= OnPromptingChanged;
      _subscribed = DataContext as ItemViewModel;
      if (DataContext is ItemViewModel viewModel)
      {
        viewModel.PropertyChanged += OnPromptingChanged;
        if (viewModel.IsPrompting)
          FocusPasswordBox();
        viewModel.Confirm ??= ConfirmAsync;
        viewModel.ConfirmAction ??= request => ConfirmActionAsync(TopLevel.GetTopLevel(this), request);
        viewModel.PickSavePath ??= PickSavePathAsync;
        viewModel.PickOpenPath ??= PickOpenPathAsync;
        viewModel.Toast ??= (message, success) => ToastWindow.Show(TopLevel.GetTopLevel(this), message, success);
        viewModel.OpenGenerator ??= () => GeneratorView.ShowAsync(TopLevel.GetTopLevel(this), viewModel.Channel);
        viewModel.OpenRotateGenerator ??= () => GeneratorView.ShowForRotateAsync(TopLevel.GetTopLevel(this), viewModel);
      }
    };
    AddHandler(SelectableTextBlock.CopyingToClipboardEvent, OnCopyingToClipboard);
  }

  // The extension copies the selection from its own copy of the secret, so the plaintext never reaches the clipboard from here.
  private void OnCopyingToClipboard(object? sender, RoutedEventArgs e)
  {
    if (e.Source is not SelectableTextBlock { DataContext: FieldViewModel { IsSecret: true } field } block || DataContext is not ItemViewModel viewModel)
      return;
    e.Handled = true;
    var start = Math.Min(block.SelectionStart, block.SelectionEnd);
    _ = viewModel.CopySelectionAsync(field, start, Math.Abs(block.SelectionEnd - block.SelectionStart));
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
    var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false, Classes = { "Warn" } };
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

  private void OnVerifyClick(object? sender, RoutedEventArgs e) => _ = SubmitPasswordAsync();

  private void OnBiometricClick(object? sender, RoutedEventArgs e) => _ = (DataContext as ItemViewModel)?.SubmitBiometricAsync();

  private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
  {
    if (e.Key == Key.Enter)
    {
      e.Handled = true;
      _ = SubmitPasswordAsync();
    }
    else if (e.Key == Key.Escape && DataContext is ItemViewModel viewModel)
    {
      e.Handled = true;
      viewModel.CancelPrompt();
    }
  }

  internal async Task SubmitPasswordAsync()
  {
    if (DataContext is not ItemViewModel viewModel)
      return;
    await viewModel.SubmitPasswordAsync(PasswordBox.Text);
    if (viewModel.PasswordError is null)
    {
      PasswordBox.Text = null;
      return;
    }

    FocusPasswordBox();
    PasswordBox.SelectAll();
  }

  private void FocusPasswordBox() => Dispatcher.UIThread.Post(() => PasswordBox.Focus(), DispatcherPriority.Input);

  private void OnPromptingChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(ItemViewModel.IsPrompting) && sender is ItemViewModel { IsPrompting: true })
      FocusPasswordBox();
  }
}
