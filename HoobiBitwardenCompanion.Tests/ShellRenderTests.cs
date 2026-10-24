using Avalonia.Controls;
using Avalonia.Headless;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Tests;

public class ShellRenderTests(AvaloniaSession session)
{
  [Fact]
  public Task ItemWindow_RendersWithHoobiChrome() => session.RunAsync(() =>
  {
    var window = new ItemWindow();
    window.Show();

    Assert.NotNull(window.CaptureRenderedFrame());
    Assert.True(window.ExtendClientAreaToDecorationsHint);
    Assert.Equal(48, window.ExtendClientAreaTitleBarHeightHint);
    window.Close();
  });

  [Fact]
  public Task ContentDialog_RendersHeadlessly() => session.RunAsync(() =>
  {
    var window = new ItemWindow();
    window.Show();
    var dialog = ItemView.CreateConfirmDialog("Discard changes?", "Your changes to this item are not saved.", "Discard", "Keep editing");

    var shown = dialog.ShowAsync(window);
    AvaloniaSession.WaitFor(() => TopLevel.GetTopLevel(dialog) is not null && dialog.IsLoaded);

    Assert.NotNull(window.CaptureRenderedFrame());
    dialog.Hide();
    AvaloniaSession.WaitFor(() => shown.IsCompleted);
    window.Close();
  });
}
