using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class ApiKeyLoginWindow : Window
{
  public ApiKeyLoginWindow() : this(null)
  {
  }

  public ApiKeyLoginWindow(ApiKeyLoginViewModel? viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
    Topmost = ItemWindow.AlwaysOnTop;
    AppTitleBar.PointerPressed += OnTitleBarPointerPressed;
    KeyDown += OnWindowKeyDown;
    if (viewModel is not null)
      viewModel.LoggedIn += Close;
  }

  internal nint ReturnWindow { get; set; }

  internal ApiKeyLoginViewModel? ViewModel => DataContext as ApiKeyLoginViewModel;

  protected override void OnClosed(EventArgs e)
  {
    if (ViewModel is { } viewModel)
      viewModel.LoggedIn -= Close;
    base.OnClosed(e);
  }

  private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
      BeginMoveDrag(e);
  }

  private void OnInputKeyDown(object? sender, KeyEventArgs e)
  {
    if (e.Key != Key.Enter)
      return;
    e.Handled = true;
    _ = ViewModel?.SubmitAsync();
  }

  private void OnWindowKeyDown(object? sender, KeyEventArgs e)
  {
    if (e.Key != Key.Escape || ViewModel is { IsBusy: true })
      return;
    e.Handled = true;
    Close();
  }

  private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
