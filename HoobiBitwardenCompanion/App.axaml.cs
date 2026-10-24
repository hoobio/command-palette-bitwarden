using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.Views;

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
      // A hidden throwaway item window loads its XAML and styles once, which takes about 250 ms off the first real window.
      Dispatcher.UIThread.Post(() => new ItemWindow().Close(), DispatcherPriority.Background);
    }

    base.OnFrameworkInitializationCompleted();
  }
}
