using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class MainWindow : Window
{
  public MainWindow() : this(null)
  {
  }

  public MainWindow(VaultViewModel? viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
    AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
  }

  internal VaultViewModel? ViewModel => DataContext as VaultViewModel;

  // Tunnel, because the search box handles Up, Down and Enter itself before they bubble.
  private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
  {
    if (ViewModel is not { } vm)
      return;
    if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
    {
      SearchBox.Focus();
      SearchBox.SelectAll();
      e.Handled = true;
      return;
    }

    if (!SearchBox.IsKeyboardFocusWithin && !ItemList.IsKeyboardFocusWithin)
      return;
    switch (e.Key)
    {
      case Key.Down or Key.Up:
        vm.MoveSelection(e.Key == Key.Down ? 1 : -1);
        e.Handled = true;
        break;
      case Key.Enter when vm.OpenSelectedCommand.CanExecute(null):
        vm.OpenSelectedCommand.Execute(null);
        e.Handled = true;
        break;
      case Key.Escape when vm.SearchText.Length > 0:
        vm.ClearSearch();
        e.Handled = true;
        break;
    }
  }

  private void OnItemDoubleTapped(object? sender, TappedEventArgs e) => ViewModel?.OpenSelectedCommand.Execute(null);

  protected override void OnClosing(WindowClosingEventArgs e)
  {
    base.OnClosing(e);
    ItemWindow.GuardUnsavedEdits(this, ViewModel?.Detail, e);
  }
}
