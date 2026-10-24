using Avalonia.Controls;
using Avalonia.Headless;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Tests;

public class ShellRenderTests(AvaloniaSession session)
{
  [Fact]
  public Task MainWindow_RendersWithHoobiChrome() => session.RunAsync(() =>
  {
    var window = new MainWindow();
    window.Show();

    Assert.NotNull(window.CaptureRenderedFrame());
    Assert.True(window.ExtendClientAreaToDecorationsHint);
    Assert.Equal(48, window.ExtendClientAreaTitleBarHeightHint);
    window.Close();
  });

  [Fact]
  public Task ContentDialog_RendersHeadlessly() => session.RunAsync(() =>
  {
    var window = new MainWindow();
    window.Show();
    var password = new TextBox();
    var dialog = ItemView.CreateRepromptDialog("GitHub", password);

    var shown = dialog.ShowAsync(window);
    AvaloniaSession.WaitFor(() => TopLevel.GetTopLevel(password) is not null);

    Assert.NotNull(window.CaptureRenderedFrame());
    dialog.Hide();
    AvaloniaSession.WaitFor(() => shown.IsCompleted);
    window.Close();
  });
}
