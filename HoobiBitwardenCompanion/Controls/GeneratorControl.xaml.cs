using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanionIpc;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HoobiBitwardenCompanion.Controls;

// Reusable password/passphrase generator (COMPANION_WINUI_PHASE1 section 3.5). Generates locally with
// a CSPRNG (PasswordGenerator) - instant, no IPC or `bw generate` round-trip - and owns the options UI
// and copy. Used by the standalone generate window, item detail regenerate, and Quick Rotate.
public sealed partial class GeneratorControl : UserControl
{
    private bool _ready;

    public GeneratorControl() => InitializeComponent();

    public string? Value { get; private set; }

    // Produces the first value; call once the control is loaded.
    public void Initialize()
    {
        _ready = true;
        Regenerate();
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

    public void Regenerate()
    {
        if (!_ready) return;
        Value = PasswordGenerator.Generate(BuildOptions());
        ValueText.Text = Value;
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PasswordOptions == null || PassphraseOptions == null) return;
        var passphrase = ModeSelector.SelectedIndex == 1;
        PasswordOptions.Visibility = passphrase ? Visibility.Collapsed : Visibility.Visible;
        PassphraseOptions.Visibility = passphrase ? Visibility.Visible : Visibility.Collapsed;
        Regenerate();
    }

    private void OnOptionChanged(object sender, object e)
    {
        if (LengthValueText != null && LengthSlider != null)
            LengthValueText.Text = ((int)LengthSlider.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Regenerate();
    }

    private void OnOptionClicked(object sender, RoutedEventArgs e) => Regenerate();

    private void OnSeparatorChanged(object sender, TextChangedEventArgs e) => Regenerate();

    private void OnRegenerateClick(object sender, RoutedEventArgs e) => Regenerate();

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Value))
            ClipboardHelper.Copy(Value, "generated value");
    }
}
