using Avalonia;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class SelfHostedServerView : UserControl
{
  public SelfHostedServerView()
  {
    InitializeComponent();
  }

  private const double DialogChromeHeight = 200;

  internal static FAContentDialog CreateDialog(SelfHostedServerViewModel viewModel, Action<SetServerRequest> save)
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
    dialog.PrimaryButtonClick += (_, e) =>
    {
      if (viewModel.BuildRequest() is { } request)
        save(request);
      else
        e.Cancel = true;
    };
    return dialog;
  }
}
