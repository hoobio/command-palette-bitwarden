using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class SelfHostedServerView : UserControl
{
  public SelfHostedServerView()
  {
    InitializeComponent();
  }

  internal static FAContentDialog CreateDialog(SelfHostedServerViewModel viewModel)
  {
    var dialog = new FAContentDialog
    {
      Title = "Self-hosted environment",
      Content = new SelfHostedServerView { DataContext = viewModel },
      PrimaryButtonText = "Save",
      CloseButtonText = "Cancel",
      DefaultButton = FAContentDialogButton.Primary,
    };
    dialog.PrimaryButtonClick += (sender, e) => _ = SaveDeferredAsync(viewModel, e);
    viewModel.PropertyChanged += (_, e) =>
    {
      if (e.PropertyName == nameof(SelfHostedServerViewModel.IsSaving))
        dialog.IsPrimaryButtonEnabled = !viewModel.IsSaving;
    };
    dialog.Closing += (_, e) => e.Cancel |= viewModel.IsSaving;
    return dialog;
  }

  private static async Task SaveDeferredAsync(SelfHostedServerViewModel viewModel, FAContentDialogButtonClickEventArgs e)
  {
    var deferral = e.GetDeferral();
    try
    {
      e.Cancel = !await viewModel.SaveAsync();
    }
    finally
    {
      deferral.Complete();
    }
  }
}
