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
        // We're the package's single app ("App"). Pin the AUMID to it so the taskbar button groups
        // under it and shows the branded Square44x44 logo.
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

        // The extension always launches us with a --pipe; its absence means we were launched from the
        // Start tile / app list (we're the package's entry point). There's no standalone companion
        // experience, so open Command Palette - where this extension lives - and exit.
        if (string.IsNullOrEmpty(options.PipeName))
        {
            LaunchCommandPalette();
            Exit();
            return;
        }

        _window = new MainWindow(options);
        _window.Activate();
    }

    private static void LaunchCommandPalette()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = @"shell:AppsFolder\Microsoft.CommandPalette_8wekyb3d8bbwe!App",
                UseShellExecute = true,
            });
        }
        catch { /* best-effort */ }
    }
}
