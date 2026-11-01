using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Hoobi.Design.Avalonia;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class ServerPickerView : UserControl
{
  public ServerPickerView()
  {
    InitializeComponent();
  }

  internal FAContentDialog? OpenDialog { get; private set; }

  protected override void OnDataContextChanged(EventArgs e)
  {
    base.OnDataContextChanged(e);
    if (DataContext is ServerPickerViewModel viewModel)
      viewModel.SelfHostedRequested += dialog => _ = ShowSelfHostedAsync(viewModel, dialog);
  }

  private async Task ShowSelfHostedAsync(ServerPickerViewModel picker, SelfHostedServerViewModel viewModel)
  {
    SetServerRequest? saved = null;
    OpenDialog = SelfHostedServerView.CreateDialog(viewModel, request => saved = request);
    try
    {
      await AppDialogs.ShowAsync(OpenDialog, TopLevel.GetTopLevel(this)!);
    }
    finally
    {
      OpenDialog = null;
    }

    if (saved is not null)
      await picker.SaveSelfHostedAsync(saved);
  }
}
