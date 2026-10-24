using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
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
    if (viewModel is not null)
    {
      viewModel.AskText ??= AskTextAsync;
      viewModel.ConfirmAction ??= request => ItemView.ConfirmActionAsync(this, request);
      viewModel.OpenGenerator ??= () => GeneratorView.ShowAsync(this, viewModel.Channel, canUse: false);
      viewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName is nameof(VaultViewModel.SelectedFilter) or nameof(VaultViewModel.RailFilters) or nameof(VaultViewModel.FootFilters))
          SyncRailSelection();
      };
      DetailView.OpenInWindowCommand = viewModel.OpenSelectedCommand;
    }
  }

  private void SyncRailSelection()
  {
    var selected = ViewModel?.SelectedFilter;
    FilterList.SelectedItem = selected is not null && ViewModel!.RailFilters.Contains(selected) ? selected : null;
    FootList.SelectedItem = selected is not null && ViewModel!.FootFilters.Contains(selected) ? selected : null;
  }

  private void OnRailSelectionChanged(object? sender, SelectionChangedEventArgs e)
  {
    if (sender is ListBox { SelectedItem: FilterViewModel filter } && ViewModel is { } vm)
      vm.SelectedFilter = filter;
  }

  private void OnClearSearchClick(object? sender, RoutedEventArgs e) => ViewModel?.ClearSearch();

  private void OnEmptyNewItemClick(object? sender, RoutedEventArgs e) => NewItemButton.Flyout?.ShowAt((Control)sender!);

  internal VaultViewModel? ViewModel => DataContext as VaultViewModel;

  internal static FAContentDialog CreateTextDialog(string title, string primary, TextBox input)
  {
    var dialog = new FAContentDialog
    {
      Title = title,
      Content = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "Folder name", Classes = { "FieldLabel" } }, input } },
      PrimaryButtonText = primary,
      CloseButtonText = "Cancel",
      DefaultButton = FAContentDialogButton.Primary,
    };
    dialog.Opened += (_, _) =>
    {
      input.Focus();
      input.SelectAll();
    };
    return dialog;
  }

  private async Task<string?> AskTextAsync(string title, string primary, string? current)
  {
    var input = new TextBox { Text = current, PlaceholderText = "Work" };
    return await CreateTextDialog(title, primary, input).ShowAsync(this) == FAContentDialogResult.Primary ? input.Text : null;
  }

  private void OnNewFolderClick(object? sender, RoutedEventArgs e) => ViewModel?.NewFolderCommand.Execute(null);

  private void OnFilterContextRequested(object? sender, ContextRequestedEventArgs e)
  {
    e.Handled = true;
    if (sender is not Control { DataContext: FilterViewModel { IsUserFolder: true } folder } target || ViewModel is not { } vm)
      return;
    var rename = new MenuItem { Header = "Rename", Icon = MenuIcon("Glyph.Rename"), Command = vm.RenameFolderCommand, CommandParameter = folder };
    var delete = new MenuItem { Header = "Delete", Icon = MenuIcon("Glyph.Delete"), Command = vm.DeleteFolderCommand, CommandParameter = folder, Classes = { "Danger" } };
    var menu = new MenuFlyout { Items = { rename, new Separator(), delete } };
    FlyoutBase.SetAttachedFlyout(target, menu);
    menu.ShowAt(target, showAtPointer: true);
  }

  private TextBlock MenuIcon(string key) => new() { Text = this.FindResource(key) as string, Classes = { "Icon" } };

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
