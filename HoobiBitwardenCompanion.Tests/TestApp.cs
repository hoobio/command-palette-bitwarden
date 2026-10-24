using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using HoobiBitwardenCompanion.Styles;

[assembly: AssemblyFixture(typeof(HoobiBitwardenCompanion.Tests.AvaloniaSession))]
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace HoobiBitwardenCompanion.Tests;

public sealed class AvaloniaSession : IDisposable
{
  private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaSession));

  public AvaloniaSession() =>
    HoobiBitwardenCompanion.Services.CompanionSettings.FilePath = Path.Combine(Path.GetTempPath(), $"companion-generator-{Guid.NewGuid():N}.json");

  public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
    .UseSkia()
    .UseHoobiFonts()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

  public Task RunAsync(Action action) => _session.Dispatch(action, TestContext.Current.CancellationToken);

  // The session thread only runs dispatched actions, so awaited continuations need the dispatcher pumped here.
  public Task RunAsync(Func<Task> action) => RunAsync(() =>
  {
    var task = action();
    WaitFor(() => task.IsCompleted);
    if (task.Exception is { } ex)
      System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException ?? ex);
  });

  public static void WaitFor(Func<bool> condition)
  {
    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (!condition() && DateTime.UtcNow < deadline)
    {
      Dispatcher.UIThread.RunJobs();
      Thread.Yield();
    }

    Assert.True(condition());
  }

  // xunit can run fixture disposal on the session's own thread, where a blocking Dispose waits on itself.
  public void Dispose() => _ = Task.Run(_session.Dispose);
}
