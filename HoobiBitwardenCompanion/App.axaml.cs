using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HoobiBitwardenCompanion.Services;

namespace HoobiBitwardenCompanion;

internal sealed partial class App : Application
{
  internal static WindowCoordinator? Coordinator { get; set; }

  public override void Initialize() => AvaloniaXamlLoader.Load(this);

  public override void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Coordinator is { } coordinator)
    {
      desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
      _ = coordinator.RunAsync(() => desktop.Shutdown());
    }

    base.OnFrameworkInitializationCompleted();
  }
}
