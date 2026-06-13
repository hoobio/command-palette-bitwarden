using System;
using System.Globalization;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanionIpc;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HoobiBitwardenCompanion.Controls;

// Live TOTP row (COMPANION_WINUI_PHASE1 §3.4): shows the current 6-digit code with a depleting
// countdown ring, refreshing every second. The raw seed is only ever shown in edit mode (a plain
// FieldRow elsewhere) - here the user sees the usable code, computed locally via TotpCalculator.
public sealed partial class TotpRow : UserControl
{
    private static readonly Brush Healthy = new SolidColorBrush(Colors.SeaGreen);
    private static readonly Brush Warning = new SolidColorBrush(Colors.OrangeRed);

    private readonly string _seed;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _currentCode = string.Empty;

    public TotpRow(string seed)
    {
        _seed = seed;
        InitializeComponent();
        _timer.Tick += (_, _) => Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Refresh();
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    private void Refresh()
    {
        string code;
        int remaining, period;
        try
        {
            (code, remaining, period) = TotpCalculator.ComputeCode(_seed);
        }
        catch
        {
            CodeText.Text = "(invalid key)";
            CopyButton.IsEnabled = false;
            _timer.Stop();
            return;
        }

        _currentCode = code;
        CodeText.Text = FormatCode(code);
        SecondsText.Text = remaining.ToString(CultureInfo.InvariantCulture);
        CountdownRing.Maximum = period;
        CountdownRing.Value = remaining;
        CountdownRing.Foreground = remaining <= 5 ? Warning : Healthy;
    }

    // Group into halves for legibility, e.g. "123 456" or "1234 5678".
    private static string FormatCode(string code) =>
        code.Length is 6 or 8 ? $"{code[..(code.Length / 2)]} {code[(code.Length / 2)..]}" : code;

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentCode))
            ClipboardHelper.Copy(_currentCode);
    }
}
