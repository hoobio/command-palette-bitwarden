using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class ItemWindow : Window
{
  internal static bool AlwaysOnTop { get; set; }

  public ItemWindow() : this(null)
  {
  }

  public ItemWindow(ItemViewModel? viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
    Topmost = AlwaysOnTop;
    AppTitleBar.PointerPressed += OnTitleBarPointerPressed;
  }

  internal ItemViewModel? ViewModel => DataContext as ItemViewModel;

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
  {
    base.OnPropertyChanged(change);
    if (change.Property == TopmostProperty)
      AlwaysOnTop = Topmost;
  }

  // The title row takes the User role so a right-click reaches the context menu instead of the system menu.
  private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
      BeginMoveDrag(e);
  }

  private void OnAlwaysOnTopClick(object? sender, RoutedEventArgs e) => Topmost = !Topmost;

  private void OnVerifyClick(object? sender, RoutedEventArgs e) => _ = PromptRepromptAsync();

  internal static FAContentDialog CreateRepromptDialog(TextBox password) => new()
  {
    Title = "Master password required",
    Content = password,
    PrimaryButtonText = "Verify",
    CloseButtonText = "Cancel",
    DefaultButton = FAContentDialogButton.Primary,
  };

  private async Task PromptRepromptAsync()
  {
    if (ViewModel is not { } viewModel)
      return;

    var password = new TextBox { PasswordChar = '•', PlaceholderText = "Master password", MinWidth = 320 };
    var dialog = CreateRepromptDialog(password);
    password.AttachedToVisualTree += (_, _) => password.Focus();
    if (await dialog.ShowAsync(this) == FAContentDialogResult.Primary && !string.IsNullOrEmpty(password.Text))
      await viewModel.VerifyRepromptAsync(password.Text);
    password.Text = null;
  }
}
