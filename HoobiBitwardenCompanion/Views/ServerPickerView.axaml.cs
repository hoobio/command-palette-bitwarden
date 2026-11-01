using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
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
      viewModel.SelfHostedRequested += dialog => _ = ShowSelfHostedAsync(dialog);
  }

  private async Task ShowSelfHostedAsync(SelfHostedServerViewModel viewModel)
  {
    OpenDialog = SelfHostedServerView.CreateDialog(viewModel);
    try
    {
      await OpenDialog.ShowAsync(TopLevel.GetTopLevel(this));
    }
    finally
    {
      OpenDialog = null;
    }
  }
}
