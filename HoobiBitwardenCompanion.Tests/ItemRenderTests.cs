using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.ViewModels;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Tests;

public class ItemRenderTests(AvaloniaSession session)
{
  private static readonly DateTime Revision = new(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);

  private sealed class FakeExtension
  {
    public string Id { get; } = Guid.NewGuid().ToString();
    public string? IconPath { get; set; }
    public List<string> Requests { get; } = [];

    public int Count(string op)
    {
      lock (Requests)
        return Requests.Count(r => r == op);
    }

    public Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
    {
      lock (Requests)
        Requests.Add(op);
      return Task.FromResult<JsonElement?>(op == Ops.GetItem ? IpcJson.ToElement(Detail(), IpcJson.Default.ItemDto) : null);
    }

    private ItemDto Detail() => new(Id, "GitHub", 1, "Work", null, true, false, false, Revision, Revision, null,
    [
      new FieldDto(ItemFields.Username, "Username", "Login credentials", "octocat", false),
      new FieldDto(ItemFields.Password, "Password", "Login credentials", null, true),
      new FieldDto(ItemFields.UriPrefix + "0", "Website", "Autofill options", "https://github.com", false, CanOpen: true),
    ], IconPath: IconPath);
  }

  private static PipeChannel CompanionChannel(ChannelPair pair)
  {
    var channel = new PipeChannel(pair.CompanionInput, pair.CompanionOutput, (_, _, _) => Task.FromResult<JsonElement?>(null));
    _ = Task.Run(channel.RunAsync);
    return channel;
  }

  [Fact]
  public Task ItemWindow_RendersStackedGroups() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var vm = new ItemViewModel(extension.Id, channel);
    var window = new ItemWindow(vm);
    window.Show();
    await vm.LoadAsync();

    var frame = window.CaptureRenderedFrame();
    Assert.NotNull(frame);
    SaveScreenshot(frame, "item-window.png");
    window.Close();
  });

  [Fact]
  public Task ItemWindow_ScrollBarHugsTheRightEdge_AndNoValueClips() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var vm = new ItemViewModel(extension.Id, channel);
    var window = new ItemWindow(vm) { Width = 440, Height = 340 };
    window.Show();
    await vm.LoadAsync();
    var frame = window.CaptureRenderedFrame();
    Assert.NotNull(frame);
    SaveScreenshot(frame, "item-window-scroll.png");

    var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.IsVisible);
    Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
    var bar = scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>().First(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
    Assert.True(bar.IsVisible);
    var barLeft = bar.TranslatePoint(default, window)!.Value.X;
    Assert.InRange(window.Bounds.Width - (barLeft + bar.Bounds.Width), -0.5, 0.5);

    var values = scroll.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text) && t.Text.Contains("2025", StringComparison.Ordinal)).ToList();
    Assert.NotEmpty(values);
    foreach (var value in values)
    {
      Assert.DoesNotContain(value.TextLayout.TextLines, l => l.HasCollapsed);
      Assert.True(value.TranslatePoint(new Point(value.Bounds.Width, 0), window)!.Value.X <= barLeft, $"'{value.Text}' runs under the scroll bar");
    }

    scroll.ScrollToEnd();
    var bottom = window.CaptureRenderedFrame();
    Assert.NotNull(bottom);
    SaveScreenshot(bottom, "item-window-scroll-end.png");
    window.Close();
  });

  [Fact]
  public Task Coordinator_VaultChanged_ReloadsEveryItemWindow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });

    var item = coordinator.ShowItem(extension.Id).ViewModel!;
    AvaloniaSession.WaitFor(() => item.Title == "GitHub");
    var loads = extension.Count(Ops.GetItem);

    await pair.Extension.SendEventAsync(Ops.VaultChanged);
    AvaloniaSession.WaitFor(() => extension.Count(Ops.GetItem) > loads);

    foreach (var window in coordinator.ItemWindows.ToList())
      window.Close();
  });

  [Fact]
  public Task Header_LoadsACachedIcon_AndFallsBackToTheGlyph() => session.RunAsync(() =>
  {
    var dir = Directory.CreateTempSubdirectory().FullName;
    try
    {
      var png = Path.Combine(dir, "ok.png");
      var bad = Path.Combine(dir, "bad.png");
      using (var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96)))
        bitmap.Save(png, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
      File.WriteAllText(bad, "not an image");

      Assert.NotNull(ItemViewModel.LoadIcon(png));
      Assert.Same(ItemViewModel.LoadIcon(png), ItemViewModel.LoadIcon(png));
      Assert.Null(ItemViewModel.LoadIcon(bad));
      Assert.Null(ItemViewModel.LoadIcon(Path.Combine(dir, "missing.png")));
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  });

  [Fact]
  public Task Header_ShowsAnIcoIcon_FromTheItem() => session.RunAsync(async () =>
  {
    var dir = Directory.CreateTempSubdirectory().FullName;
    try
    {
      var png = Path.Combine(dir, "frame.png");
      using (var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96)))
        bitmap.Save(png, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
      var pngBytes = await File.ReadAllBytesAsync(png, TestContext.Current.CancellationToken);
      var ico = Path.Combine(dir, "github.com.png");
      byte[] header = [0, 0, 1, 0, 1, 0, 16, 16, 0, 0, 1, 0, 32, 0, .. BitConverter.GetBytes(pngBytes.Length), 22, 0, 0, 0];
      await File.WriteAllBytesAsync(ico, [.. header, .. pngBytes], TestContext.Current.CancellationToken);

      var extension = new FakeExtension();
      using var pair = new ChannelPair(extension.HandleAsync);
      using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
      _ = coordinator.RunAsync(() => { });
      var plain = coordinator.ShowItem(extension.Id);
      AvaloniaSession.WaitFor(() => plain.ViewModel!.Title == "GitHub");
      Assert.False(plain.ViewModel!.HasIcon);

      extension.IconPath = ico;
      await pair.Extension.SendEventAsync(Ops.VaultChanged);
      AvaloniaSession.WaitFor(() => plain.ViewModel.HasIcon);
      Assert.NotNull(plain.CaptureRenderedFrame());
      plain.Close();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  });

  private static void SaveScreenshot(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
  {
    if (Environment.GetEnvironmentVariable("COMPANION_SCREENSHOT_DIR") is { Length: > 0 } dir)
      frame.Save(Path.Combine(dir, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
  }
}
