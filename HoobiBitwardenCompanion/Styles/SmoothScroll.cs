using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HoobiBitwardenCompanion.Styles;

internal sealed class WheelAnimation
{
  public const double NotchPixels = 48;
  public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(250);

  private double _from;
  private TimeSpan _start;

  public bool IsActive { get; private set; }

  public double Target { get; private set; }

  public double LastWritten { get; private set; }

  public static bool IsPrecise(double delta) => Math.Abs(delta - Math.Round(delta)) > 1e-6;

  public double Aim(double offset) => IsActive ? Target : offset;

  public bool CanMove(double offset, double pixels, double max) => pixels < 0 ? Aim(offset) > 0 : pixels > 0 && Aim(offset) < max;

  public void Wheel(double offset, double pixels, double max, TimeSpan now)
  {
    Target = Math.Clamp(Aim(offset) + pixels, 0, max);
    _from = offset;
    _start = now;
    LastWritten = offset;
    IsActive = true;
  }

  public double Step(TimeSpan now, double max)
  {
    Target = Math.Clamp(Target, 0, max);
    var progress = Math.Clamp((now - _start) / Duration, 0, 1);
    var eased = 1 - Math.Pow(1 - progress, 3);
    LastWritten = Math.Clamp(_from + (Target - _from) * eased, 0, max);
    IsActive = progress < 1;
    return LastWritten;
  }

  public void Cancel() => IsActive = false;
}

internal static class SmoothScroll
{
  public static readonly AttachedProperty<bool> IsEnabledProperty =
    AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(SmoothScroll));

  private static readonly AttachedProperty<WheelAnimation?> AnimationProperty =
    AvaloniaProperty.RegisterAttached<ScrollViewer, WheelAnimation?>("Animation", typeof(SmoothScroll));

  private static readonly long Origin = Stopwatch.GetTimestamp();

  static SmoothScroll()
  {
    IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((viewer, e) =>
    {
      viewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
      if (e.GetNewValue<bool>())
        viewer.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    });
  }

  public static bool GetIsEnabled(ScrollViewer viewer) => viewer.GetValue(IsEnabledProperty);

  public static void SetIsEnabled(ScrollViewer viewer, bool value) => viewer.SetValue(IsEnabledProperty, value);

  private static TimeSpan Now => Stopwatch.GetElapsedTime(Origin);

  private static double Max(ScrollViewer viewer) => Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

  private static WheelAnimation Animation(ScrollViewer viewer)
  {
    if (viewer.GetValue(AnimationProperty) is not { } animation)
    {
      animation = new WheelAnimation();
      viewer.SetValue(AnimationProperty, animation);
    }

    return animation;
  }

  private static bool CanMove(ScrollViewer viewer, double pixels) => Animation(viewer).CanMove(viewer.Offset.Y, pixels, Max(viewer));

  // The outer viewer's tunnel handler runs first, so it leaves the notch to the innermost viewer that can still move that way.
  private static bool InnerViewerTakes(ScrollViewer viewer, Visual? source, double pixels)
  {
    for (var visual = source; visual is not null && visual != viewer; visual = visual.GetVisualParent())
    {
      if (visual is ScrollViewer inner && GetIsEnabled(inner) && CanMove(inner, pixels))
        return true;
    }

    return false;
  }

  private static void OnWheel(object? sender, PointerWheelEventArgs e)
  {
    if (sender is not ScrollViewer viewer || e.KeyModifiers != KeyModifiers.None || e.Delta.X != 0 || e.Delta.Y == 0)
      return;

    var pixels = -e.Delta.Y * WheelAnimation.NotchPixels;
    if (InnerViewerTakes(viewer, e.Source as Visual, pixels) || !CanMove(viewer, pixels))
      return;

    e.Handled = true;
    var animation = Animation(viewer);
    if (WheelAnimation.IsPrecise(e.Delta.Y))
    {
      animation.Cancel();
      Write(viewer, Math.Clamp(viewer.Offset.Y + pixels, 0, Max(viewer)));
      return;
    }

    var running = animation.IsActive;
    animation.Wheel(viewer.Offset.Y, pixels, Max(viewer), Now);
    if (!running)
      RequestFrame(viewer, animation);
  }

  private static void RequestFrame(ScrollViewer viewer, WheelAnimation animation)
  {
    if (TopLevel.GetTopLevel(viewer) is { } topLevel)
      topLevel.RequestAnimationFrame(_ => Frame(viewer, animation));
    else
      animation.Cancel();
  }

  private static void Frame(ScrollViewer viewer, WheelAnimation animation)
  {
    if (!animation.IsActive)
      return;

    // Keyboard, the scroll bar and bring-into-view write the offset too; any of them ends the animation.
    if (Math.Abs(viewer.Offset.Y - animation.LastWritten) > 1)
    {
      animation.Cancel();
      return;
    }

    Write(viewer, animation.Step(Now, Max(viewer)));
    if (animation.IsActive)
      RequestFrame(viewer, animation);
  }

  private static void Write(ScrollViewer viewer, double y) => viewer.SetCurrentValue(ScrollViewer.OffsetProperty, viewer.Offset.WithY(y));
}
