using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class GeneratorWindow : Window
{
  public GeneratorWindow() : this(null)
  {
  }

  public GeneratorWindow(GeneratorViewModel? viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
    Topmost = ItemWindow.AlwaysOnTop;
    AppTitleBar.PointerPressed += OnTitleBarPointerPressed;
  }

  internal GeneratorViewModel? ViewModel => DataContext as GeneratorViewModel;

  internal Func<string, Task>? SaveAsNewItem { get; set; }

  private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
      BeginMoveDrag(e);
  }

  internal Task SaveAsNewItemAsync() =>
    ViewModel?.Value is { Length: > 0 } value && SaveAsNewItem is { } save ? save(value) : Task.CompletedTask;

  private void OnSaveAsNewItemClick(object? sender, RoutedEventArgs e) => _ = SaveAsNewItemAsync();
}
