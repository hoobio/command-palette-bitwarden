using System.IO.Pipes;
using Avalonia;
using Hoobi.Design.Avalonia;
using HoobiBitwardenCompanion.Services;

namespace HoobiBitwardenCompanion;

internal static class Program
{
  [STAThread]
  public static int Main(string[] args)
  {
    if (args.Length < 2)
      return 2;

    using var coordinator = new WindowCoordinator(
      new AnonymousPipeClientStream(PipeDirection.In, args[0]),
      new AnonymousPipeClientStream(PipeDirection.Out, args[1]));
    App.Coordinator = coordinator;
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    return 0;
  }

  public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .UseHoobiDesign();
}
