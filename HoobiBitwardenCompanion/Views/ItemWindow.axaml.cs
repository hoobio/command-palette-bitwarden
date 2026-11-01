using Avalonia.Controls;
using Avalonia.Interactivity;
using Hoobi.Design.Avalonia;
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
    HoobiChrome.Attach(this);
    DataContext = viewModel;
    Topmost = AlwaysOnTop;
    // The title row takes the User role so a right-click reaches the context menu instead of the system menu.
    HoobiChrome.AttachDragHandle(AppTitleBar);
    if (viewModel is not null)
      viewModel.WriteFailed += () => WindowAlert.Raise(this);
  }

  internal ItemViewModel? ViewModel => DataContext as ItemViewModel;

  private void OnAlwaysOnTopClick(object? sender, RoutedEventArgs e) => Topmost = AlwaysOnTop = !Topmost;

  protected override void OnClosing(WindowClosingEventArgs e)
  {
    base.OnClosing(e);
    GuardUnsavedEdits(this, ViewModel, e);
  }

  internal static void GuardUnsavedEdits(Window window, ItemViewModel? item, WindowClosingEventArgs e)
  {
    if (item?.Editor is not { IsDirty: true })
      return;
    e.Cancel = true;
    _ = CloseAfterDiscardAsync(window, item);
  }

  private static async Task CloseAfterDiscardAsync(Window window, ItemViewModel item)
  {
    if (await item.TryEndEditAsync())
      window.Close();
  }
}
