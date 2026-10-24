using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.ViewModels;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Tests;

public class TextCentreTests(AvaloniaSession session)
{
  private const double Tolerance = 1;

  // OS/2 sCapHeight: Hanken 697/1000, Cascadia 1420/2048; gallery strings have no descenders, so the lowest ink row is the baseline and ascenders or i-dots do not skew the centre.
  private const double HankenCapHeight = 0.697;
  private const double CascadiaCapHeight = 1420d / 2048;

  private static object Resource(string key) => Application.Current!.FindResource(key)!;

  private static ControlTheme Theme(string key) => (ControlTheme)Resource(key);

  private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

  private static TextBlock Icon(string glyph, double size = 16, string classes = "Icon") => new() { Classes = { classes }, Text = (string)Resource(glyph), FontSize = size };

  private static StackPanel Pair(double spacing, params Control[] children)
  {
    var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
    panel.Children.AddRange(children);
    return panel;
  }

  private static Border Boxed(string classes, Control child)
  {
    var border = new Border { Child = child, HorizontalAlignment = HorizontalAlignment.Left };
    border.Classes.AddRange(classes.Split(' '));
    return border;
  }

  private static Button Button(object content, string? theme = null, double? height = null)
  {
    var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Left };
    if (theme is not null)
      button.Theme = Theme(theme);
    if (height is { } h)
      button.Height = h;
    return button;
  }

  private static List<(string Name, Control Box)> Gallery() =>
  [
    ("button", Button("Edit")),
    ("button accent", Button("Save", "AccentButtonStyle")),
    ("button danger", Button("Delete", "DangerButtonStyle")),
    ("button dialog", Button("Cancel", "DialogCloseButtonStyle", 36)),
    ("button icon+text", Button(Pair(10, Icon("Glyph.Add"), Label("Add field"), Icon("Glyph.ChevronDown")), "AddRowButtonStyle")),
    ("button add row", Button(Pair(10, Icon("Glyph.Add"), Label("Add attachment")), "AddRowButtonStyle")),
    ("button icon", Button(Resource("Glyph.Copy"), "IconButtonStyle")),
    ("badge", Boxed("Badge", Pair(4, Icon("Glyph.Folder", 14, "BadgeIcon"), Label("Work")))),
    ("badge accent", Boxed("Badge Accent", Pair(4, Icon("Glyph.StarFilled", 14, "BadgeIcon"), Label("Favourite")))),
    ("text box", new TextBox { Text = "Work", Width = 240, HorizontalAlignment = HorizontalAlignment.Left }),
    ("password box", new TextBox { Classes = { "revealPasswordButton" }, PasswordChar = '•', PlaceholderText = "Master", Width = 240, HorizontalAlignment = HorizontalAlignment.Left }),
    ("number box", new NumericUpDown { Value = 20, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Left }),
    ("combo box", new ComboBox { ItemsSource = new[] { "Work" }, SelectedIndex = 0, Width = 240 }),
    ("combo item", new ComboBoxItem { Content = "Work", Width = 240 }),
    ("tab", new TabStrip { ItemsSource = new[] { "Password", "Passphrase" }, SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Left }),
    ("key value row", Boxed("Row KeyValue", new TextBlock { Text = "Brand", FontSize = 13, VerticalAlignment = VerticalAlignment.Center })),
    ("mono text box", new TextBox { Text = "SHA256", FontFamily = (Avalonia.Media.FontFamily)Resource("MonoFontFamily"), Width = 240, HorizontalAlignment = HorizontalAlignment.Left }),
    ("mono value", Boxed("Row KeyValue", new TextBlock { Text = "4821 AB", FontFamily = (Avalonia.Media.FontFamily)Resource("MonoFontFamily"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center })),
    ("validation line", Pair(6, Icon("Glyph.Warning"), new TextBlock { Text = "Enter a name", Classes = { "Caption", "Warn" }, VerticalAlignment = VerticalAlignment.Center })),
    ("alert", Boxed("Alert", new TextBlock { Text = "Item edited in the vault", TextWrapping = Avalonia.Media.TextWrapping.Wrap })),
    ("reprompt title", Pair(10, Icon("Glyph.Lock", 20), new TextBlock { Text = "Master lock", FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center })),
    ("verify button", Button(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Unlock" } } }, "AccentButtonStyle")),
    ("menu item", new MenuFlyoutPresenter { ItemsSource = new[] { new MenuItem { Header = "Edit", Icon = new TextBlock { Classes = { "Icon" }, Text = (string)Resource("Glyph.Pin") } } } }),
  ];

  private sealed record Offset(string Name, string Text, double Pixels);

  private static Control Box(Control box) => box switch
  {
    TabStrip strip => strip.GetVisualDescendants().OfType<TabStripItem>().First(),
    MenuFlyoutPresenter menu => menu.GetVisualDescendants().OfType<MenuItem>().Single(),
    _ => box,
  };

  private static IEnumerable<Control> Inks(Control box) => box.GetVisualDescendants().OfType<Control>()
    .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && (c is TextBlock { Text.Length: > 0 } || c is TextPresenter { Text.Length: > 0 }))
    .Where(c => c is not TextBlock || c.FindAncestorOfType<TextPresenter>() is null);

  private static Rect Bounds(Visual visual) => new(visual.TranslatePoint(default, TopLevel.GetTopLevel(visual)!)!.Value, visual.Bounds.Size);

  private static IEnumerable<Offset> Measure(string name, Control outer, int width, byte[] pixels, int stride)
  {
    foreach (var ink in Inks(outer))
    {
      var box = Bounds(ink.GetVisualAncestors().TakeWhile(a => a != outer).OfType<Button>().FirstOrDefault() ?? outer);
      var inkBox = Bounds(ink);
      var left = Math.Max((int)Math.Ceiling(inkBox.Left), (int)Math.Ceiling(box.Left) + 2);
      var right = Math.Min((int)Math.Floor(inkBox.Right), Math.Min((int)Math.Floor(box.Right) - 2, width));
      if (InkRows(pixels, stride, left, right, (int)Math.Round(box.Top) + 2, (int)Math.Round(box.Bottom) - 2) is not var (first, last))
        continue;
      var (text, size, family) = ink is TextBlock block ? (block.Text!, block.FontSize, block.FontFamily) : (((TextPresenter)ink).Text!, ((TextPresenter)ink).FontSize, ((TextPresenter)ink).FontFamily);
      var glyph = text.All(c => c is >= (char)0xE000 and <= (char)0xF8FF);
      var cap = family.Name.Contains("Cascadia", StringComparison.Ordinal) ? CascadiaCapHeight : HankenCapHeight;
      var centre = glyph ? (first + last + 1) / 2.0 : last + 1 - (cap * size / 2);
      yield return new Offset(name, glyph ? "icon" : text, centre - box.Center.Y);
    }
  }

  private static (int First, int Last)? InkRows(byte[] pixels, int stride, int left, int right, int top, int bottom)
  {
    var counts = new Dictionary<uint, int>();
    for (var y = top; y < bottom; y++)
      for (var x = left; x < right; x++)
        counts[Pixel(pixels, stride, x, y)] = counts.GetValueOrDefault(Pixel(pixels, stride, x, y)) + 1;
    if (counts.Count == 0)
      return null;
    var background = counts.MaxBy(c => c.Value).Key;
    var rows = Enumerable.Range(top, bottom - top).Where(y => Enumerable.Range(left, right - left).Any(x => Distance(Pixel(pixels, stride, x, y), background) > 64)).ToList();
    return rows.Count > 0 ? (rows[0], rows[^1]) : null;
  }

  private static uint Pixel(byte[] pixels, int stride, int x, int y) => BitConverter.ToUInt32(pixels, (y * stride) + (x * 4));

  private static int Distance(uint a, uint b) => Enumerable.Range(0, 3).Max(i => Math.Abs((int)((a >> (i * 8)) & 0xFF) - (int)((b >> (i * 8)) & 0xFF)));

  private static (byte[] Pixels, int Stride) Pixels(Bitmap frame)
  {
    var stride = frame.PixelSize.Width * 4;
    var buffer = new byte[stride * frame.PixelSize.Height];
    var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
    try
    {
      frame.CopyPixels(new PixelRect(frame.PixelSize), handle.AddrOfPinnedObject(), buffer.Length, stride);
    }
    finally
    {
      handle.Free();
    }

    return (buffer, stride);
  }

  private static List<Offset> Render(Window window, IEnumerable<(string Name, Control Box)> boxes)
  {
    window.Show();
    for (var i = 0; i < 20; i++)
    {
      AvaloniaHeadlessPlatform.ForceRenderTimerTick();
      Dispatcher.UIThread.RunJobs();
    }

    var frame = window.CaptureRenderedFrame()!;
    var (pixels, stride) = Pixels(frame);
    var offsets = boxes.SelectMany(b => Measure(b.Name, Box(b.Box), frame.PixelSize.Width, pixels, stride)).ToList();
    window.Close();
    return offsets;
  }

  private static void AssertCentred(List<Offset> offsets)
  {
    TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, offsets.Select(o => $"{o.Name} '{o.Text}': {o.Pixels:+0.0;-0.0;0}")));
    var off = offsets.Where(o => Math.Abs(o.Pixels) > Tolerance).ToList();
    Assert.True(off.Count == 0, "Ink off centre: " + string.Join("; ", off.Select(o => $"{o.Name} '{o.Text}' {o.Pixels:+0.0;-0.0}px")));
  }

  [Fact]
  public Task TextInControls_IsCentredOnItsBox() => session.RunAsync(() =>
  {
    var gallery = Gallery();
    var panel = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
    panel.Children.AddRange(gallery.Select(g => g.Box));
    var offsets = Render(new Window { Width = 480, Height = 1200, Content = panel }, gallery);

    Assert.Equal(gallery.Count, offsets.Select(o => o.Name).Distinct().Count());
    AssertCentred(offsets);
  });

  [Fact]
  public Task TitleBar_TextIsCentredOnTheBar() => session.RunAsync(() =>
  {
    using var channel = new PipeChannel(new MemoryStream(), new MemoryStream(), (_, _, _) => Task.FromResult<System.Text.Json.JsonElement?>(null));
    var window = new ItemWindow(new ItemViewModel(Guid.NewGuid().ToString(), channel) { Title = "GitHub" }) { Width = 440, Height = 620 };
    var offsets = Render(window, [("title bar", window.FindControl<Grid>("AppTitleBar")!)]);

    Assert.Contains(offsets, o => o.Text == "GitHub");
    AssertCentred(offsets);
  });
}
