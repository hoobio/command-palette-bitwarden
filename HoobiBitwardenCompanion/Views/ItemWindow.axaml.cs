using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

  // The title row takes the User role so a right-click reaches the context menu instead of the system menu.
  private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
      BeginMoveDrag(e);
  }

  private void OnAlwaysOnTopClick(object? sender, RoutedEventArgs e) => Topmost = AlwaysOnTop = !Topmost;
}
