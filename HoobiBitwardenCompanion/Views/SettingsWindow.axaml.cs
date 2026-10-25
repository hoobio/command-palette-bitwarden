using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class SettingsWindow : Window
{
  public SettingsWindow() : this(null)
  {
  }

  public SettingsWindow(SettingsViewModel? viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
    Topmost = ItemWindow.AlwaysOnTop;
    AppTitleBar.PointerPressed += OnTitleBarPointerPressed;
    AddHandler(GotFocusEvent, OnGotFocus);
    AddHandler(LostFocusEvent, OnLostFocus);
    AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    SearchBox.TextChanged += OnSearchTextChanged;
  }

  internal SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

  internal void FocusSearch()
  {
    SearchBox.Focus();
    SearchBox.SelectAll();
  }

  private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
  {
    if (string.IsNullOrEmpty(SearchBox.Text))
      Scroller.ScrollToHome();
  }

  private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (e.Source is Visual source && (source == SearchBox || SearchBox.IsVisualAncestorOf(source)))
      return;
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
      BeginMoveDrag(e);
  }

  private static void OnGotFocus(object? sender, RoutedEventArgs e)
  {
    if (e.Source is TextBox { DataContext: SettingRowViewModel row })
      row.IsEditing = true;
  }

  private static void OnLostFocus(object? sender, RoutedEventArgs e)
  {
    if (e.Source is not TextBox { DataContext: SettingRowViewModel row } box)
      return;
    row.IsEditing = false;
    _ = row.CommitTextAsync(box.Text ?? string.Empty);
  }

  private void OnKeyDown(object? sender, KeyEventArgs e)
  {
    if (e.Key == Key.Escape && !string.IsNullOrEmpty(SearchBox.Text))
    {
      SearchBox.Clear();
      e.Handled = true;
      return;
    }

    if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
    {
      FocusSearch();
      e.Handled = true;
      return;
    }

    if (e.Key != Key.Enter || e.Source is not TextBox { DataContext: SettingRowViewModel row } box)
      return;
    e.Handled = true;
    _ = row.CommitTextAsync(box.Text ?? string.Empty);
  }
}
