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
        // The extension launches us via Process.Start, so we inherit the package identity but no
        // explicit AppUserModelID. Packaged apps take their taskbar icon from the app's AUMID tile
        // logo (not the window icon), so without this the taskbar button is blank. Point it at the
        // "App" id: its AppListEntry is visible so the shell resolves its Square44x44 logo (the
        // Companion id is AppListEntry="none", which the taskbar can't resolve a logo for).
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
