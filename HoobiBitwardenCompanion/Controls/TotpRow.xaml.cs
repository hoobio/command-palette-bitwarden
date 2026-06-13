using System;
using System.Globalization;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanionIpc;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace HoobiBitwardenCompanion.Controls;

// Live TOTP row (COMPANION_WINUI_PHASE1 §3.4): shows the current code with a countdown ring that
// shrinks smoothly over the period and snaps back at rollover, refreshing ~30x/sec. The raw seed is
// only ever shown in edit mode (a plain FieldRow elsewhere); here the user sees the usable code,
// computed locally via TotpCalculator.
public sealed partial class TotpRow : UserControl
{
    private const double Radius = 14;
    private const double Centre = 17; // half of the 34px box

    private static readonly Brush Healthy = new SolidColorBrush(Colors.SeaGreen);
    private static readonly Brush Warning = new SolidColorBrush(Colors.OrangeRed);

    private readonly string _seed;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly ArcSegment _arc = new() { Size = new Size(Radius, Radius), SweepDirection = SweepDirection.Clockwise };
    private readonly PathFigure _figure;

    private int _period = 30;
    private long _lastBucket = -1;
    private bool _valid = true;
    private string _currentCode = string.Empty;

    public TotpRow(string seed)
    {
        _seed = seed;
        InitializeComponent();

        _figure = new PathFigure { StartPoint = new Point(Centre, Centre - Radius), IsClosed = false };
        _figure.Segments.Add(_arc);
        var geometry = new PathGeometry();
        geometry.Figures.Add(_figure);
        CountdownArc.Data = geometry;

        try
        {
            _period = TotpCalculator.IsSteamSecret(_seed) ? 30 : TotpCalculator.ParseTotpSecret(_seed).Period;
        }
        catch
        {
            _valid = false;
        }

        _timer.Tick += (_, _) => Tick();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_valid)
        {
            CodeText.Text = "(invalid key)";
            CopyButton.IsEnabled = false;
            return;
        }
        Tick();
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    private void Tick()
    {
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        var elapsed = nowSeconds % _period;
        var fraction = 1.0 - elapsed / _period;
        var remaining = Math.Clamp((int)Math.Ceiling(_period - elapsed), 1, _period);

        UpdateArc(fraction);
        SecondsText.Text = remaining.ToString(CultureInfo.InvariantCulture);
        CountdownArc.Stroke = remaining <= 5 ? Warning : Healthy;

        var bucket = (long)(nowSeconds / _period);
        if (bucket != _lastBucket)
        {
            _lastBucket = bucket;
            RecomputeCode();
        }
    }

    private void RecomputeCode()
    {
        try
        {
            (_currentCode, _, _) = TotpCalculator.ComputeCode(_seed);
            CodeText.Text = FormatCode(_currentCode);
        }
        catch
        {
            CodeText.Text = "(invalid key)";
            CopyButton.IsEnabled = false;
            _timer.Stop();
        }
    }

    private void UpdateArc(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var angle = fraction * 360.0;
        if (angle <= 0.01)
        {
            CountdownArc.Visibility = Visibility.Collapsed;
            return;
        }
        CountdownArc.Visibility = Visibility.Visible;
        if (angle >= 360.0) angle = 359.99; // a full circle can't be drawn as a single arc

        var radians = angle * Math.PI / 180.0;
        _arc.Point = new Point(Centre + Radius * Math.Sin(radians), Centre - Radius * Math.Cos(radians));
        _arc.IsLargeArc = angle > 180.0;
    }

    // Group into halves for legibility, e.g. "123 456" or "1234 5678".
    private static string FormatCode(string code) =>
        code.Length is 6 or 8 ? $"{code[..(code.Length / 2)]} {code[(code.Length / 2)..]}" : code;

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentCode))
            ClipboardHelper.Copy(_currentCode, "TOTP");
    }
}
