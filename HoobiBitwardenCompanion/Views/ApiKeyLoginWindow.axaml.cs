using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class ApiKeyLoginWindow : DialogWindow
{
  public ApiKeyLoginWindow() : this(null)
  {
  }

  public ApiKeyLoginWindow(ApiKeyLoginViewModel? viewModel)
  {
    InitializeComponent();
    InitializeDialog(viewModel);
  }

  internal new ApiKeyLoginViewModel? ViewModel => DataContext as ApiKeyLoginViewModel;
}
