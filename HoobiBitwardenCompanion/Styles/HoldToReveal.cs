using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HoobiBitwardenCompanion.Styles;

internal static class HoldToReveal
{
  public static readonly AttachedProperty<bool> IsEnabledProperty =
    AvaloniaProperty.RegisterAttached<TextBox, bool>("IsEnabled", typeof(HoldToReveal));

  static HoldToReveal()
  {
    IsEnabledProperty.Changed.AddClassHandler<TextBox>((box, e) =>
    {
      box.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
      box.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
      box.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
      if (!e.GetNewValue<bool>())
        return;
      box.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
      box.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
      box.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
    });
  }

  public static bool GetIsEnabled(TextBox box) => box.GetValue(IsEnabledProperty);

  public static void SetIsEnabled(TextBox box, bool value) => box.SetValue(IsEnabledProperty, value);

  // Handled on tunnel so the template's reveal ToggleButton never sees the press and cannot latch on click.
  private static void OnPressed(object? sender, PointerPressedEventArgs e)
  {
    if (sender is not TextBox box
      || (e.Source as Visual)?.FindAncestorOfType<ToggleButton>(includeSelf: true) is not { } button
      || !ReferenceEquals(button.TemplatedParent, box))
      return;
    e.Handled = true;
    e.Pointer.Capture(box);
    box.RevealPassword = true;
  }

  private static void OnReleased(object? sender, PointerReleasedEventArgs e)
  {
    if (sender is not TextBox { RevealPassword: true } box)
      return;
    box.RevealPassword = false;
    e.Pointer.Capture(null);
  }

  private static void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
  {
    if (sender is TextBox box)
      box.RevealPassword = false;
  }
}
