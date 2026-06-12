using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using HoobiBitwardenCompanion.Services;

namespace HoobiBitwardenCompanion.Views;

// Standalone generator window (COMPANION_WINUI_PHASE1 section 3.7). Generates an un-persisted value;
// no vault write. Wires the reusable GeneratorControl to the extension over IPC.
public sealed partial class GeneratePage : Page
{
    public GeneratePage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = InitializeAsync(e.Parameter);
    }

    private async System.Threading.Tasks.Task InitializeAsync(object? parameter)
    {
        if (parameter is not CompanionContext { Client: { } client })
        {
            ErrorText.Text = "Not connected to the Bitwarden extension; cannot generate.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Generator.Generator = client.GenerateAsync;
        await Generator.InitializeAsync();
    }
}
