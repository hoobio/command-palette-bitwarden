using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HoobiBitwardenCompanion.Tests;

internal static class HoverFlashProbe
{
  private static readonly Color Ground = Color.Parse("#FF181C26");

  public static void ProbePopups(TopLevel top, SortedSet<string> flashes)
  {
    foreach (var popup in top.OpenedPopups)
    {
      if (popup.Child is { } child)
        Probe(child, flashes);
    }
  }

  public static void Probe(Visual root, SortedSet<string> flashes)
  {
    foreach (var control in root.GetSelfAndVisualDescendants().OfType<TemplatedControl>().Where(control => control.IsEffectivelyVisible).ToList())
    {
      var targets = Subtree(control).SelectMany(element => (element.Transitions ?? []).OfType<BrushTransition>().Select(transition => (element, transition.Property!))).ToList();
      if (targets.Count == 0)
        continue;

      var pseudo = (IPseudoClasses)control.Classes;
      var rest = Read(targets);
      pseudo.Add(":pointerover");
      var over = Read(targets);
      pseudo.Add(":pressed");
      var pressed = Read(targets);
      pseudo.Remove(":pressed");
      pseudo.Remove(":pointerover");

      for (var i = 0; i < targets.Count; i++)
      {
        var (element, property) = targets[i];
        var where = $"{control.GetType().Name}[{string.Join(' ', control.Classes)}] {element.GetType().Name}#{(element as StyledElement)?.Name}.{property.Name}";
        Check(flashes, where, "rest", rest[i], "pointerover", over[i]);
        Check(flashes, where, "pointerover", over[i], "pressed", pressed[i]);
        Check(flashes, where, "rest", rest[i], "pressed", pressed[i]);
      }
    }
  }

  private static IEnumerable<Animatable> Subtree(TemplatedControl control)
  {
    var pending = new Stack<Visual>([control]);
    while (pending.TryPop(out var visual))
    {
      if (visual is Animatable animatable)
        yield return animatable;
      foreach (var child in visual.GetVisualChildren())
      {
        if (child is not TemplatedControl)
          pending.Push(child);
      }
    }
  }

  private static Color?[] Read(List<(Animatable Element, AvaloniaProperty Property)> targets) =>
    targets.Select(target => (target.Element.GetBaseValue(target.Property) as ISolidColorBrush)?.Color).ToArray();

  private static void Check(SortedSet<string> flashes, string where, string fromState, Color? from, string toState, Color? to)
  {
    if (from is not { } a || to is not { } b || a == b)
      return;

    var ends = Math.Max(Luma(a), Luma(b));
    for (var step = 1; step < 20; step++)
    {
      if (Luma(Interpolate(step / 20d, a, b)) > ends + 2)
      {
        flashes.Add($"{where}: {fromState} {a} to {toState} {b}");
        return;
      }
    }
  }

  private static double Luma(Color color)
  {
    var alpha = color.A / 255d;
    double Over(byte channel, byte ground) => channel * alpha + ground * (1 - alpha);
    return 0.2126 * Over(color.R, Ground.R) + 0.7152 * Over(color.G, Ground.G) + 0.0722 * Over(color.B, Ground.B);
  }

  private static Color Interpolate(double progress, Color from, Color to)
  {
    static double Linear(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
    static byte Srgb(double linear) => (byte)Math.Round((linear <= 0.0031308 ? linear * 12.92 : Math.Pow(linear, 5d / 12d) * 1.055 - 0.055) * 255);
    byte Channel(byte a, byte b) => Srgb(Linear(a) + progress * (Linear(b) - Linear(a)));
    return new Color((byte)Math.Round(from.A + progress * (to.A - from.A)), Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
  }
}
