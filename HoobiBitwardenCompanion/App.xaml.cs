using System;
using Microsoft.UI.Xaml;

namespace HoobiBitwardenCompanion;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var options = LaunchOptions.Parse(Environment.GetCommandLineArgs());
        _window = new MainWindow(options);
        _window.Activate();
    }
}
