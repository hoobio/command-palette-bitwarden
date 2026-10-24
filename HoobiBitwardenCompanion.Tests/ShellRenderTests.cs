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

  [Fact]
  public Task FolderDialog_FocusesAndSelectsTheNameBox_WhenItOpens() => session.RunAsync(() =>
  {
    var window = new MainWindow();
    window.Show();
    var input = new TextBox { Text = "Work" };
    var dialog = MainWindow.CreateTextDialog("Rename folder", "Rename", input);

    var shown = dialog.ShowAsync(window);

    AvaloniaSession.WaitFor(() => input.IsFocused);
    Assert.Equal(4, input.SelectionEnd - input.SelectionStart);
    dialog.Hide();
    AvaloniaSession.WaitFor(() => shown.IsCompleted);
    window.Close();
  });
}
