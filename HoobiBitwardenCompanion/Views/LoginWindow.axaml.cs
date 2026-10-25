using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class LoginWindow : DialogWindow
{
  public LoginWindow() : this(null)
  {
  }

  public LoginWindow(LoginViewModel? viewModel)
  {
    InitializeComponent();
    InitializeDialog(viewModel);
  }

  internal new LoginViewModel? ViewModel => DataContext as LoginViewModel;
}
