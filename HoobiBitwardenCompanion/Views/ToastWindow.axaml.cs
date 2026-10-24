using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace HoobiBitwardenCompanion.Views;

internal sealed partial class ToastWindow : Window
{
  internal static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(2);
  internal static readonly TimeSpan FadeFor = TimeSpan.FromMilliseconds(150);

  private readonly DispatcherTimer _hideTimer = new() { Interval = VisibleFor };
  private readonly DispatcherTimer _fadeTimer = new() { Interval = FadeFor };
  private Screen? _screen;

  public ToastWindow()
  {
    InitializeComponent();
    ToastWindowInterop.MakeNonActivating(this);
    _hideTimer.Tick += (_, _) => FadeOut();
    _fadeTimer.Tick += (_, _) =>
    {
      _fadeTimer.Stop();
      Hide();
    };
    SizeChanged += (_, _) => PlaceOnScreen();
    ScalingChanged += (_, _) => PlaceOnScreen();
  }

  internal static ToastWindow? Current { get; private set; }

  internal string? Text => Message.Text;

  internal bool IsSuccess => Glyph.IsVisible;

  internal bool IsHideScheduled => _hideTimer.IsEnabled;

  internal bool IsFading => _fadeTimer.IsEnabled;

  internal double CardOpacity => Card.GetBaseValue(OpacityProperty).GetValueOrDefault();

  internal static void Show(TopLevel? source, string text, bool success = true) => (Current ??= new ToastWindow()).Present(source, text, success);

  internal void Present(TopLevel? source, string text, bool success)
  {
    _hideTimer.Stop();
    _fadeTimer.Stop();
    Message.Text = text;
    Glyph.IsVisible = success;
    _screen = source?.Screens?.ScreenFromTopLevel(source) ?? Screens.Primary;
    Show();
    PlaceOnScreen();
    Card.Opacity = 1;
    _hideTimer.Start();
  }

  internal void FadeOut()
  {
    _hideTimer.Stop();
    Card.Opacity = 0;
    _fadeTimer.Start();
  }

  internal static PixelPoint Place(PixelRect workArea, Size size, double scaling)
  {
    var width = (int)Math.Ceiling(size.Width * scaling);
    var height = (int)Math.Ceiling(size.Height * scaling);
    return new PixelPoint(workArea.X + ((workArea.Width - width) / 2), workArea.Bottom - height);
  }

  private void PlaceOnScreen()
  {
    if (_screen is { } screen && IsVisible)
      Position = Place(screen.WorkingArea, ClientSize, screen.Scaling);
  }
}
