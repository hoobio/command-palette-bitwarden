using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class SetServerWindow : DialogWindow
{
  public SetServerWindow() : this(null)
  {
  }

  public SetServerWindow(SetServerViewModel? viewModel)
  {
    InitializeComponent();
    InitializeDialog(viewModel);
  }

  internal new SetServerViewModel? ViewModel => DataContext as SetServerViewModel;
}
