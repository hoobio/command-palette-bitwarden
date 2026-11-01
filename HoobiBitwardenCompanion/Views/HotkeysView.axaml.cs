using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Hoobi.Design.Avalonia;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class HotkeysView : UserControl
{
  private HotkeysViewModel? _subscribed;

  public HotkeysView()
  {
    InitializeComponent();
  }

  internal Button Back => BackButton;

  internal FAContentDialog? OpenDialog { get; private set; }

  protected override void OnDataContextChanged(EventArgs e)
  {
    base.OnDataContextChanged(e);
    if (_subscribed is not null)
      _subscribed.EditRequested -= OnEditRequested;
    _subscribed = DataContext as HotkeysViewModel;
    if (_subscribed is not null)
      _subscribed.EditRequested += OnEditRequested;
  }

  private void OnEditRequested(HotkeyRowViewModel row) => _ = EditAsync(_subscribed!, row);

  private async Task EditAsync(HotkeysViewModel viewModel, HotkeyRowViewModel row)
  {
    OpenDialog = HotkeyCaptureView.CreateDialog(new ChordCaptureViewModel(viewModel, row));
    try
    {
      await AppDialogs.ShowAsync(OpenDialog, TopLevel.GetTopLevel(this)!);
    }
    finally
    {
      OpenDialog = null;
    }
  }
}
