using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Hoobi.Design.Avalonia;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal abstract class DialogWindow : Window
{
  internal nint ReturnWindow { get; set; }

  internal DialogViewModel? ViewModel => DataContext as DialogViewModel;

  protected void InitializeDialog(DialogViewModel? viewModel)
  {
    HoobiChrome.Attach(this);
    DataContext = viewModel;
    Topmost = true;
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

  internal static bool HasOpenDialog(Visual window) =>
    OverlayLayer.GetOverlayLayer(window)?.GetVisualDescendants().OfType<FAContentDialog>().Any() == true;

  private void OnWindowKeyDown(object? sender, KeyEventArgs e)
  {
    if (HasOpenDialog(this))
      return;

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
