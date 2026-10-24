using Avalonia.Controls;
using Avalonia.Interactivity;
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
      }
    };
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
