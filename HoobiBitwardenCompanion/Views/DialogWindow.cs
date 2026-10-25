using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal abstract class DialogWindow : Window
{
  internal nint ReturnWindow { get; set; }

  internal DialogViewModel? ViewModel => DataContext as DialogViewModel;

  protected void InitializeDialog(DialogViewModel? viewModel)
  {
    DataContext = viewModel;
    Topmost = true;
    this.FindControl<Control>("AppTitleBar")!.PointerPressed += OnTitleBarPointerPressed;
    KeyDown += OnWindowKeyDown;
    if (viewModel is not null)
    {
      viewModel.Completed += Close;
      viewModel.InputFocusRequested += FocusFirstInput;
    }
  }

  internal void FocusFirstInput() =>
    Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsEffectivelyVisible && t.IsEnabled)?.Focus(), DispatcherPriority.Loaded);

  protected override void OnClosed(EventArgs e)
  {
    if (ViewModel is { } viewModel)
    {
      viewModel.Completed -= Close;
      viewModel.InputFocusRequested -= FocusFirstInput;
      viewModel.ClearSecrets();
      viewModel.OnWindowClosed();
    }

    base.OnClosed(e);
  }

  protected void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

  internal void CentreOnScreenOf(PixelPoint anchor)
  {
    if (Screens.ScreenFromPoint(anchor) is not { } screen)
      return;
    var area = screen.WorkingArea;
    var size = PixelSize.FromSize(ClientSize, screen.Scaling);
    Position = new PixelPoint(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2);
  }

  private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
      BeginMoveDrag(e);
  }

  private void OnWindowKeyDown(object? sender, KeyEventArgs e)
  {
    if (e.Key == Key.Enter && e.Source is TextBox)
    {
      e.Handled = true;
      _ = ViewModel?.SubmitAsync();
    }
    else if (e.Key == Key.Escape && ViewModel is not { IsBusy: true })
    {
      e.Handled = true;
      Close();
    }
  }
}
