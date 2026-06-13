using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace HoobiBitwardenCompanion;

public partial class App : Application
{
    private Window? _window;

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appID);

    public App()
    {
        // The extension launches us via Process.Start. The packaged taskbar icon comes from the
        // AUMID's app tile logo; the "Companion" app is AppListEntry="none" so the shell won't resolve
        // a logo for it (blank button). Point at the visible "App" id, whose branded Square44x44 logo
        // does resolve, so the taskbar shows the Bitwarden shield.
        try
        {
            var aumid = Windows.ApplicationModel.Package.Current.Id.FamilyName + "!App";
            SetCurrentProcessExplicitAppUserModelID(aumid);
        }
        catch { /* unpackaged / no identity: nothing to do */ }

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var options = LaunchOptions.Parse(Environment.GetCommandLineArgs());
        _window = new MainWindow(options);
        _window.Activate();
    }
}
