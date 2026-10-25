using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class UnlockWindow : DialogWindow
{
  public UnlockWindow() : this(null)
  {
  }

  public UnlockWindow(UnlockViewModel? viewModel)
  {
    InitializeComponent();
    InitializeDialog(viewModel);
  }

  internal new UnlockViewModel? ViewModel => DataContext as UnlockViewModel;
}
