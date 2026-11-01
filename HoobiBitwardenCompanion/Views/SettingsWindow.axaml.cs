using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Hoobi.Design.Avalonia;
using HoobiBitwarden.Ipc;
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
    HoobiChrome.Attach(this);
    DataContext = viewModel;
    Topmost = ItemWindow.AlwaysOnTop;
    HoobiChrome.AttachDragHandle(AppTitleBar);
    AddHandler(GotFocusEvent, OnGotFocus);
    AddHandler(LostFocusEvent, OnLostFocus);
    AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    AddHandler(TextBox.TextChangedEvent, OnTextChanged);
    AddHandler(SettingsCard.ClickEvent, OnCardClick);
    SearchBox.TextChanged += OnSearchTextChanged;
    ShortcutsPage.Back.Click += (_, _) => ViewModel?.CloseShortcuts();
    if (viewModel is not null)
      viewModel.Confirm ??= ConfirmAsync;
  }

  private async Task<bool> ConfirmAsync(string title, string message, string primary, string close) =>
    await AppDialogs.ShowAsync(ItemView.CreateConfirmDialog(title, message, primary, close), this) == FAContentDialogResult.Primary;

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

  private void OnTextChanged(object? sender, TextChangedEventArgs e)
  {
    if (e.Source is TextBox { DataContext: SettingRowViewModel { Key: SettingKeys.ServerUrl } } box)
      ViewModel?.ServerUrlTyped(box.Text);
  }

  private static void OnCardClick(object? sender, RoutedEventArgs e)
  {
    switch ((e.Source as Control)?.DataContext)
    {
      case SettingRowViewModel { IsLink: true } row:
        row.OpenCommand.Execute(null);
        break;
      case LinkRowViewModel link:
        link.OpenCommand.Execute(null);
        break;
    }
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
    if (DialogWindow.HasOpenDialog(this))
      return;

    if (ViewModel is { IsShortcutsOpen: true } settings)
    {
      if (e.Key == Key.Escape)
      {
        settings.CloseShortcuts();
        e.Handled = true;
      }

      return;
    }

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
