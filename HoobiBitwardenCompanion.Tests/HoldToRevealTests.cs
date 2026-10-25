using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HoobiBitwardenCompanion.Styles;

namespace HoobiBitwardenCompanion.Tests;

public class HoldToRevealTests(AvaloniaSession session)
{
  [Fact]
  public Task RevealButton_ShowsThePasswordOnlyWhileHeld() => session.RunAsync(() =>
  {
    var box = new TextBox { Classes = { "revealPasswordButton" }, PasswordChar = '•', Text = "hunter2", Width = 240 };
    var window = new Window { Width = 300, Height = 120, Content = box };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    Assert.True(HoldToReveal.GetIsEnabled(box));

    var button = box.GetVisualDescendants().OfType<ToggleButton>().Single();
    var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;

    window.MouseDown(centre, MouseButton.Left);
    Dispatcher.UIThread.RunJobs();
    Assert.True(box.RevealPassword);

    window.MouseUp(centre, MouseButton.Left);
    Dispatcher.UIThread.RunJobs();
    Assert.False(box.RevealPassword);
    Assert.False(button.IsChecked);

    window.MouseDown(new Point(20, 60), MouseButton.Left);
    window.MouseUp(new Point(20, 60), MouseButton.Left);
    Dispatcher.UIThread.RunJobs();
    Assert.False(box.RevealPassword);
    window.Close();
    return Task.CompletedTask;
  });
}
