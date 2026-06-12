using System;
using System.Threading.Tasks;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanionIpc;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HoobiBitwardenCompanion.Controls;

// Reusable password/passphrase generator (COMPANION_WINUI_PHASE1 section 3.5). Decoupled from IPC:
// the host sets Generator (typically VaultClient.GenerateAsync) and the control owns the options UI,
// coalesced regeneration, and copy. Used by the standalone generate window, item detail regenerate,
// and Quick Rotate.
public sealed partial class GeneratorControl : UserControl
{
    private bool _ready;
    private bool _pending;
    private bool _running;

    public GeneratorControl() => InitializeComponent();

    // Host-provided generator. Returns the generated value (or null on failure).
    internal Func<GeneratorOptions, Task<string?>>? Generator { get; set; }

    public string? Value { get; private set; }

    // Call after Generator is set to produce the first value.
    public async Task InitializeAsync()
    {
        _ready = true;
        await RegenerateAsync();
    }

    internal GeneratorOptions BuildOptions()
    {
        var passphrase = ModeSelector.SelectedIndex == 1;
        if (passphrase)
        {
            return new GeneratorOptions
            {
                Mode = GeneratorMode.Passphrase,
                Words = (int)WordsBox.Value,
                Separator = string.IsNullOrEmpty(SeparatorBox.Text) ? "-" : SeparatorBox.Text,
                Capitalize = CapitalizeToggle.IsChecked == true,
                IncludeNumber = IncludeNumberToggle.IsChecked == true,
            };
        }

        return new GeneratorOptions
        {
            Mode = GeneratorMode.Password,
            Length = (int)LengthSlider.Value,
            Uppercase = UppercaseToggle.IsChecked == true,
            Lowercase = LowercaseToggle.IsChecked == true,
            Numbers = NumbersToggle.IsChecked == true,
            Symbols = SymbolsToggle.IsChecked == true,
            MinNumber = (int)MinNumberBox.Value,
            MinSpecial = (int)MinSpecialBox.Value,
            AvoidAmbiguous = AvoidAmbiguousToggle.IsChecked == true,
        };
    }

    // Coalesces bursts of option changes into a single in-flight generation so a slider drag doesn't
    // spawn a `bw generate` per tick.
    public async Task RegenerateAsync()
    {
        if (!_ready || Generator == null) return;

        _pending = true;
        if (_running) return;
        _running = true;
        try
        {
            while (_pending)
            {
                _pending = false;
                var options = BuildOptions();
                string? value;
                try
                {
                    value = await Generator(options);
                }
                catch (Exception ex)
                {
                    ValueText.Text = $"Generator error: {ex.Message}";
                    return;
                }
                Value = value;
                ValueText.Text = value ?? "(failed to generate)";
            }
        }
        finally
        {
            _running = false;
        }
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PasswordOptions == null || PassphraseOptions == null) return;
        var passphrase = ModeSelector.SelectedIndex == 1;
        PasswordOptions.Visibility = passphrase ? Visibility.Collapsed : Visibility.Visible;
        PassphraseOptions.Visibility = passphrase ? Visibility.Visible : Visibility.Collapsed;
        _ = RegenerateAsync();
    }

    private void OnOptionChanged(object sender, object e)
    {
        if (LengthValueText != null && LengthSlider != null)
            LengthValueText.Text = ((int)LengthSlider.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _ = RegenerateAsync();
    }

    private void OnOptionClicked(object sender, RoutedEventArgs e) => _ = RegenerateAsync();

    private void OnSeparatorChanged(object sender, TextChangedEventArgs e) => _ = RegenerateAsync();

    private void OnRegenerateClick(object sender, RoutedEventArgs e) => _ = RegenerateAsync();

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Value))
            ClipboardHelper.Copy(Value);
    }
}
