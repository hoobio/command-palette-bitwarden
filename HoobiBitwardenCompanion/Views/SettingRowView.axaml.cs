using Avalonia.Controls;
using Avalonia.Input;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class SettingRowView : UserControl
{
  public SettingRowView()
  {
    InitializeComponent();
    // SettingsCard toggles on any press, Enter or Space that bubbles to it, so input from its rows and text boxes stops before the card.
    GroupRows.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true);
    GroupRows.AddHandler(KeyDownEvent, StopActivation);
    GroupHeader.AddHandler(KeyDownEvent, (sender, e) =>
    {
      if (e.Source is TextBox)
        StopActivation(sender, e);
    });
  }

  protected override void OnDataContextChanged(EventArgs e)
  {
    base.OnDataContextChanged(e);
    if (DataContext is SettingRowViewModel { IsLink: true } row)
      LinkCard.ActionGlyph = this.FindResource(row.IsPageLink ? "Glyph.ChevronRight" : "Glyph.OpenInNewWindow") as string;
  }

  private static void StopActivation(object? sender, KeyEventArgs e)
  {
    if (e.Key is Key.Enter or Key.Space)
      e.Handled = true;
  }
}
