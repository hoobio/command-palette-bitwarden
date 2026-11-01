using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class HotkeyCaptureView : UserControl
{
  public HotkeyCaptureView()
  {
    InitializeComponent();
  }

  internal static FAContentDialog CreateDialog(ChordCaptureViewModel capture)
  {
    var dialog = new FAContentDialog
    {
      Title = "Shortcut",
      Content = new HotkeyCaptureView { DataContext = capture },
      PrimaryButtonText = "Save",
      CloseButtonText = "Cancel",
      DefaultButton = FAContentDialogButton.Close,
      IsPrimaryButtonEnabled = capture.IsValid,
    };
    capture.PropertyChanged += (_, _) => dialog.IsPrimaryButtonEnabled = capture.IsValid;
    dialog.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) =>
    {
      if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        return;
      capture.Press(e.Key, e.KeyModifiers);
      e.Handled = true;
    }, RoutingStrategies.Tunnel);
    dialog.PrimaryButtonClick += (sender, e) => _ = SaveDeferredAsync(capture, e);
    return dialog;
  }

  private static async Task SaveDeferredAsync(ChordCaptureViewModel capture, FAContentDialogButtonClickEventArgs e)
  {
    var deferral = e.GetDeferral();
    try
    {
      e.Cancel = !await capture.SaveAsync();
    }
    finally
    {
      deferral.Complete();
    }
  }
}
