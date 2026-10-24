using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using HoobiBitwardenCompanion.Styles;

namespace HoobiBitwardenCompanion.Tests;

public class ScrollTests(AvaloniaSession session)
{
  private static readonly Point Inside = new(100, 100);

  private static (Window Window, ScrollViewer Viewer) Open(double contentHeight)
  {
    var viewer = new ScrollViewer { Content = new Border { Height = contentHeight } };
    var window = new Window { Width = 300, Height = 300, Content = viewer };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    return (window, viewer);
  }

  private static void Tick(Func<bool> condition)
  {
    for (var i = 0; i < 200 && !condition(); i++)
    {
      AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
      Dispatcher.UIThread.RunJobs();
      Thread.Sleep(5);
    }

    Assert.True(condition());
  }

  [Fact]
  public void Wheel_target_is_clamped_to_the_scroll_range()
  {
    var animation = new WheelAnimation();

    animation.Wheel(offset: 10, pixels: -48, max: 100, TimeSpan.Zero);
    Assert.Equal(0, animation.Target);

    animation.Cancel();
    animation.Wheel(offset: 90, pixels: 48, max: 100, TimeSpan.Zero);
    Assert.Equal(100, animation.Target);
  }

  [Fact]
  public void Wheel_accumulates_on_the_running_target()
  {
    var animation = new WheelAnimation();

    animation.Wheel(0, 48, 1000, TimeSpan.Zero);
    animation.Wheel(20, 48, 1000, TimeSpan.FromMilliseconds(50));

    Assert.Equal(96, animation.Target);
    Assert.Equal(96, animation.Aim(20));
  }

  [Fact]
  public void Step_eases_to_the_target_and_finishes()
  {
    var animation = new WheelAnimation();
    animation.Wheel(0, 48, 1000, TimeSpan.Zero);

    var midway = animation.Step(WheelAnimation.Duration / 2, 1000);
    Assert.InRange(midway, 1, 47);
    Assert.True(animation.IsActive);

    Assert.Equal(48, animation.Step(WheelAnimation.Duration, 1000));
    Assert.False(animation.IsActive);
  }

  [Fact]
  public void Step_clamps_when_the_range_shrinks()
  {
    var animation = new WheelAnimation();
    animation.Wheel(0, 48, 1000, TimeSpan.Zero);

    Assert.Equal(20, animation.Step(WheelAnimation.Duration, 20));
  }

  [Fact]
  public void CanMove_stops_at_both_ends_and_follows_the_target()
  {
    var animation = new WheelAnimation();

    Assert.False(animation.CanMove(0, -48, 100));
    Assert.True(animation.CanMove(0, 48, 100));
    Assert.False(animation.CanMove(100, 48, 100));
    Assert.False(animation.CanMove(50, 0, 100));

    animation.Wheel(0, 48, 48, TimeSpan.Zero);
    Assert.False(animation.CanMove(0, 48, 48));
    animation.Cancel();
    Assert.False(animation.IsActive);
  }

  [Fact]
  public void Fractional_deltas_are_precise()
  {
    Assert.True(WheelAnimation.IsPrecise(0.4));
    Assert.False(WheelAnimation.IsPrecise(-1));
  }

  [Fact]
  public async Task Smooth_scroll_is_on_for_every_scroll_viewer() => await session.RunAsync(() =>
  {
    var (window, viewer) = Open(1000);

    Assert.True(SmoothScroll.GetIsEnabled(viewer));
    Assert.True(AutoScroll.GetIsEnabled(viewer));
    SmoothScroll.SetIsEnabled(viewer, false);
    Assert.False(SmoothScroll.GetIsEnabled(viewer));
    window.Close();
  });

  [Fact]
  public async Task Precise_wheel_scrolls_at_once_and_stops_at_the_end() => await session.RunAsync(() =>
  {
    var (window, viewer) = Open(1000);
    var max = viewer.Extent.Height - viewer.Viewport.Height;

    window.MouseWheel(Inside, new Vector(0, -0.5));
    Assert.Equal(24, viewer.Offset.Y);

    viewer.Offset = viewer.Offset.WithY(max);
    window.MouseWheel(Inside, new Vector(0, -0.5));
    Assert.Equal(max, viewer.Offset.Y);
    window.Close();
  });

  [Fact]
  public async Task Notch_wheel_animates_towards_the_target() => await session.RunAsync(() =>
  {
    var (window, viewer) = Open(1000);

    window.MouseWheel(Inside, new Vector(0, -1));
    Tick(() => viewer.Offset.Y == 48);
    window.Close();
  });

  [Fact]
  public async Task Wheel_with_a_modifier_is_left_to_the_viewer() => await session.RunAsync(() =>
  {
    var (window, viewer) = Open(1000);

    window.MouseWheel(Inside, new Vector(0, -0.5), RawInputModifiers.Control);
    Assert.NotEqual(24, viewer.Offset.Y);
    window.Close();
  });

  [Theory]
  [InlineData(0)]
  [InlineData(12)]
  [InlineData(-12)]
  public void Autoscroll_has_no_speed_inside_the_dead_zone(double distance) =>
    Assert.Equal(0, AutoScroll.Speed(distance));

  [Fact]
  public void Autoscroll_speed_grows_with_distance_in_both_directions()
  {
    Assert.True(AutoScroll.Speed(48) > 0);
    Assert.True(AutoScroll.Speed(96) > AutoScroll.Speed(48));
    Assert.Equal(-AutoScroll.Speed(48), AutoScroll.Speed(-48));
  }

  [Fact]
  public void Autoscroll_advance_scales_with_time_and_clamps()
  {
    Assert.Equal(Math.Round(2 * AutoScroll.Speed(48), 6), Math.Round(AutoScroll.Advance(0, 48, 40, 1000), 6));
    Assert.Equal(0, AutoScroll.Advance(5, -480, 100, 1000));
    Assert.Equal(1000, AutoScroll.Advance(995, 480, 100, 1000));
  }

  [Fact]
  public async Task Middle_click_without_overflow_does_nothing() => await session.RunAsync(() =>
  {
    var (window, _) = Open(100);

    window.MouseDown(Inside, MouseButton.Middle);
    Assert.False(AutoScroll.IsActive);
    window.MouseUp(Inside, MouseButton.Middle);
    window.Close();
  });

  [Fact]
  public async Task Middle_click_starts_autoscroll_and_a_click_stops_it() => await session.RunAsync(() =>
  {
    var (window, viewer) = Open(1000);

    window.MouseDown(Inside, MouseButton.Middle);
    window.MouseUp(Inside, MouseButton.Middle);
    Assert.True(AutoScroll.IsActive);
    Assert.NotNull(viewer.Cursor);

    window.MouseDown(Inside, MouseButton.Left);
    window.MouseUp(Inside, MouseButton.Left);
    Assert.False(AutoScroll.IsActive);
    Assert.Null(viewer.Cursor);
    window.Close();
  });

  [Fact]
  public async Task Autoscroll_moves_the_offset_while_the_pointer_is_below_the_origin() => await session.RunAsync(() =>
  {
    var (window, viewer) = Open(5000);

    window.MouseDown(Inside, MouseButton.Middle);
    window.MouseMove(Inside + new Point(0, 120));
    try
    {
      AutoScroll.Frame(AutoScroll.Session);
      Thread.Sleep(30);
      AutoScroll.Frame(AutoScroll.Session);
      Assert.True(viewer.Offset.Y > 0);
    }
    finally
    {
      window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
    }

    Assert.False(AutoScroll.IsActive);
    window.Close();
  });

  [Fact]
  public async Task Releasing_after_a_drag_stops_autoscroll() => await session.RunAsync(() =>
  {
    var (window, _) = Open(1000);

    window.MouseDown(Inside, MouseButton.Middle);
    window.MouseMove(Inside + new Point(0, 60));
    window.MouseUp(Inside + new Point(0, 60), MouseButton.Middle);
    Assert.False(AutoScroll.IsActive);
    window.Close();
  });

  [Fact]
  public async Task Wheel_stops_autoscroll() => await session.RunAsync(() =>
  {
    var (window, _) = Open(1000);

    window.MouseDown(Inside, MouseButton.Middle);
    window.MouseUp(Inside, MouseButton.Middle);
    window.MouseWheel(Inside, new Vector(0, -1));
    Assert.False(AutoScroll.IsActive);
    window.Close();
  });
}
