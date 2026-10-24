using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace HoobiBitwardenCompanion.Styles;

internal static class AutoScroll
{
  private const double DeadZone = 12;
  private const double MarkerSize = 24;

  public static readonly AttachedProperty<bool> IsEnabledProperty =
    AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(AutoScroll));

  private static readonly long Origin = Stopwatch.GetTimestamp();

  private static Cursor? _scrollCursor;
  private static ScrollViewer? _viewer;
  private static TopLevel? _topLevel;
  private static AdornerLayer? _layer;
  private static Control? _marker;
  private static Cursor? _previousCursor;
  private static Point _origin;
  private static Point _pointer;
  private static double _y;
  private static bool _held;
  private static bool _dragged;
  private static TimeSpan? _lastFrame;
  private static int _session;

  static AutoScroll()
  {
    IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((viewer, e) =>
    {
      viewer.RemoveHandler(InputElement.PointerPressedEvent, OnViewerPressed);
      if (e.GetNewValue<bool>())
        viewer.AddHandler(InputElement.PointerPressedEvent, OnViewerPressed, RoutingStrategies.Tunnel);
    });
  }

  public static bool IsActive => _viewer is not null;

  internal static int Session => _session;

  public static bool GetIsEnabled(ScrollViewer viewer) => viewer.GetValue(IsEnabledProperty);

  public static void SetIsEnabled(ScrollViewer viewer, bool value) => viewer.SetValue(IsEnabledProperty, value);

  // Firefox's autoscroll curve: pixels per 20 ms grow with the distance in dead-zone units to the power 1.5.
  internal static double Speed(double distance)
  {
    var steps = distance / DeadZone;
    return steps > 1 ? steps * Math.Sqrt(steps) - 1 : steps < -1 ? steps * Math.Sqrt(-steps) + 1 : 0;
  }

  internal static double Advance(double y, double distance, double elapsedMs, double max) =>
    Math.Clamp(y + Speed(distance) * elapsedMs / 20, 0, max);

  private static double Max(ScrollViewer viewer) => Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

  private static void OnViewerPressed(object? sender, PointerPressedEventArgs e)
  {
    if (_viewer is not null
      || sender is not ScrollViewer viewer
      || e.GetCurrentPoint(viewer).Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonPressed
      || Max(viewer) <= 0
      || TopLevel.GetTopLevel(viewer) is not { } topLevel)
      return;

    e.Handled = true;
    e.Pointer.Capture(viewer);
    Enter(viewer, topLevel, e.GetPosition(viewer));
  }

  private static void Enter(ScrollViewer viewer, TopLevel topLevel, Point point)
  {
    _viewer = viewer;
    _topLevel = topLevel;
    _origin = _pointer = point;
    _y = viewer.Offset.Y;
    _held = true;
    _dragged = false;
    _lastFrame = null;
    var session = ++_session;

    topLevel.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
    topLevel.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    topLevel.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    topLevel.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
    topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    if (topLevel is WindowBase window)
      window.Deactivated += OnDeactivated;

    viewer.DetachedFromVisualTree += OnDetached;
    _previousCursor = viewer.Cursor;
    viewer.Cursor = _scrollCursor ??= new Cursor(StandardCursorType.SizeNorthSouth);
    ShowMarker(viewer, point);
    topLevel.RequestAnimationFrame(_ => Frame(session));
  }

  private static void Exit()
  {
    if (_viewer is not { } viewer || _topLevel is not { } topLevel)
      return;

    _viewer = null;
    _topLevel = null;
    topLevel.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
    topLevel.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
    topLevel.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
    topLevel.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
    topLevel.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
    if (topLevel is WindowBase window)
      window.Deactivated -= OnDeactivated;

    viewer.DetachedFromVisualTree -= OnDetached;
    viewer.Cursor = _previousCursor;
    _previousCursor = null;
    if (_marker is not null)
      _layer?.Children.Remove(_marker);

    _marker = null;
    _layer = null;
  }

  internal static void Frame(int session)
  {
    if (session != _session || _viewer is not { } viewer || _topLevel is not { } topLevel)
      return;

    if (Math.Abs(viewer.Offset.Y - _y) > 1)
      _y = viewer.Offset.Y;

    var now = Stopwatch.GetElapsedTime(Origin);
    var elapsed = _lastFrame is { } last ? Math.Min((now - last).TotalMilliseconds, 100) : 0;
    _lastFrame = now;
    _y = Advance(_y, _pointer.Y - _origin.Y, elapsed, Max(viewer));
    viewer.SetCurrentValue(ScrollViewer.OffsetProperty, viewer.Offset.WithY(_y));
    topLevel.RequestAnimationFrame(_ => Frame(session));
  }

  private static void ShowMarker(ScrollViewer viewer, Point point)
  {
    if (AdornerLayer.GetAdornerLayer(viewer) is not { } layer)
      return;

    var circle = new Border
    {
      Width = MarkerSize,
      Height = MarkerSize,
      CornerRadius = new CornerRadius(MarkerSize / 2),
      Background = viewer.FindResource("LayerFillColorAltBrush") as IBrush,
      Child = new Avalonia.Controls.Shapes.Path
      {
        Data = Geometry.Parse("M8,10 L12,6 L16,10 M8,14 L12,18 L16,14"),
        Stroke = viewer.FindResource("TextFillColorPrimaryBrush") as IBrush,
        StrokeThickness = 1.5,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
      },
    };
    Canvas.SetLeft(circle, point.X - MarkerSize / 2);
    Canvas.SetTop(circle, point.Y - MarkerSize / 2);
    var marker = new Canvas { IsHitTestVisible = false, Children = { circle } };
    AdornerLayer.SetAdornedElement(marker, viewer);
    layer.Children.Add(marker);
    _layer = layer;
    _marker = marker;
  }

  private static void OnMoved(object? sender, PointerEventArgs e)
  {
    if (_viewer is null)
      return;

    _pointer = e.GetPosition(_viewer);
    if (_held && Point.Distance(_pointer, _origin) > DeadZone)
      _dragged = true;
  }

  private static void OnPressed(object? sender, PointerPressedEventArgs e)
  {
    e.Handled = true;
    Exit();
  }

  private static void OnReleased(object? sender, PointerReleasedEventArgs e)
  {
    if (!_held || e.InitialPressMouseButton != MouseButton.Middle)
      return;

    e.Handled = true;
    _held = false;
    if (_dragged)
      Exit();
  }

  private static void OnWheel(object? sender, PointerWheelEventArgs e) => Exit();

  private static void OnKeyDown(object? sender, KeyEventArgs e)
  {
    if (e.Key == Key.Escape)
    {
      e.Handled = true;
      Exit();
    }
  }

  private static void OnDeactivated(object? sender, EventArgs e) => Exit();

  private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Exit();
}
