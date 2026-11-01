using Avalonia.Controls;
using Hoobi.Design.Avalonia;
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
    HoobiChrome.Attach(this);
    DataContext = viewModel;
    Topmost = ItemWindow.AlwaysOnTop;
    HoobiChrome.AttachDragHandle(AppTitleBar);
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
      await AppDialogs.ShowAsync(OpenDialog, this);
    }
    finally
    {
      OpenDialog = null;
    }
  }
}
