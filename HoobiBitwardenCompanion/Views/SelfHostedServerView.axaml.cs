using Avalonia;
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

  private const double DialogChromeHeight = 200;

  internal static FAContentDialog CreateDialog(SelfHostedServerViewModel viewModel)
  {
    var scroll = new ScrollViewer { Margin = new Thickness(0, 0, -24, 0), Content = new SelfHostedServerView { DataContext = viewModel, Margin = new Thickness(0, 0, 24, 0) } };
    scroll.AttachedToVisualTree += (_, _) => scroll.MaxHeight = Math.Max(150, (TopLevel.GetTopLevel(scroll)?.Bounds.Height ?? 800) - DialogChromeHeight);
    var dialog = new FAContentDialog
    {
      Title = "Self-hosted environment",
      Content = scroll,
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
