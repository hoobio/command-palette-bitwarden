using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class RepromptWindow : DialogWindow
{
  public RepromptWindow() : this(null)
  {
  }

  public RepromptWindow(RepromptViewModel? viewModel)
  {
    InitializeComponent();
    InitializeDialog(viewModel);
  }

  internal new RepromptViewModel? ViewModel => DataContext as RepromptViewModel;
}
