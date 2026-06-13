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
        // The extension launches us via Process.Start. Pin the AppUserModelID to our own "Companion"
        // app id (the app this exe belongs to) so the taskbar button groups under it and shows its
        // Square44x44 logo, rather than an ambiguous/blank association.
        try
        {
            var aumid = Windows.ApplicationModel.Package.Current.Id.FamilyName + "!Companion";
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
