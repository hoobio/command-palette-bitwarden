using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HoobiBitwardenCompanion.Views;

// Standalone generator window (COMPANION_WINUI_PHASE1 section 3.7). Generates an un-persisted value
// locally (CSPRNG) - no vault write, no extension/IPC needed.
public sealed partial class GeneratePage : Page
{
    public GeneratePage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Generator.Initialize();
    }
}
