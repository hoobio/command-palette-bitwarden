using Avalonia.Controls;
using Avalonia.Input;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class HotkeysWindow : Window
{
  public HotkeysWindow() : this(null)
  {
  }

  public HotkeysWindow(HotkeysViewModel? viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
    Topmost = ItemWindow.AlwaysOnTop;
    AppTitleBar.PointerPressed += OnTitleBarPointerPressed;
    if (viewModel is not null)
      viewModel.EditRequested += row => _ = EditAsync(viewModel, row);
  }

  internal HotkeysViewModel? ViewModel => DataContext as HotkeysViewModel;

  internal FluentAvalonia.UI.Controls.FAContentDialog? OpenDialog { get; private set; }

  private async Task EditAsync(HotkeysViewModel viewModel, HotkeyRowViewModel row)
  {
    OpenDialog = HotkeyCaptureView.CreateDialog(new ChordCaptureViewModel(viewModel, row));
    try
    {
      await OpenDialog.ShowAsync(this);
    }
    finally
    {
      OpenDialog = null;
    }
  }

  private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
      BeginMoveDrag(e);
  }
}
