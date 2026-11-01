using Avalonia;
using Avalonia.Headless;
using Hoobi.Design.Avalonia.Testing;

[assembly: AssemblyFixture(typeof(HoobiBitwardenCompanion.Tests.AvaloniaSession))]
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace HoobiBitwardenCompanion.Tests;

public sealed class AvaloniaSession : IDisposable
{
  private readonly HeadlessSession _session = new(typeof(AvaloniaSession));

  public AvaloniaSession() =>
    HoobiBitwardenCompanion.Services.CompanionSettings.FilePath = Path.Combine(Path.GetTempPath(), $"companion-generator-{Guid.NewGuid():N}.json");

  public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
    .UseSkia()
    .UseHoobiDesign()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

  public Task RunAsync(Action action)
  {
    _session.Run(action, TestContext.Current.CancellationToken);
    return Task.CompletedTask;
  }

  public Task RunAsync(Func<Task> action) => RunAsync(() =>
  {
    var task = action();
    WaitFor(() => task.IsCompleted);
    if (task.Exception is { } ex)
      System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException ?? ex);
  });

  public static void WaitFor(Func<bool> condition) => Assert.True(HeadlessSession.WaitFor(condition));

  public void Dispose() => _session.Dispose();
}
