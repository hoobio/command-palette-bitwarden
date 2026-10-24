using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.ViewModels;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Tests;

public class VaultWindowTests(AvaloniaSession session)
{
  private static readonly DateTime Revision = new(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);

  private static ItemSummary Summary(string name, int type = 1, string? subtext = null, bool favorite = false, string? folder = null, string? org = null,
    string[]? collections = null, bool deleted = false, bool archived = false, string[]? hosts = null, string? id = null, DateTime? revision = null) =>
    new(id ?? Guid.NewGuid().ToString(), type, name, subtext, favorite, folder, org, collections ?? [], deleted, archived, revision ?? Revision, false, false, false, hosts ?? []);

  private static List<ItemSummary> SampleItems() =>
  [
    Summary("GitHub", subtext: "octocat", favorite: true, folder: "f1", org: "o1", collections: ["c1"], hosts: ["github.com"]),
    Summary("Google", subtext: "jane@gmail.com", folder: "f2", hosts: ["accounts.google.com"]),
    Summary("Amazon", subtext: "jane@example.com", hosts: ["amazon.co.uk"]),
    Summary("Visa Platinum", type: 3, subtext: "Visa ····4242", folder: "f2"),
    Summary("Jane Doe", type: 4, subtext: "jane@example.com"),
    Summary("Wi-Fi codes", type: 2, folder: "f1"),
    Summary("Build server", type: 5, subtext: "SHA256:abc", org: "o1", collections: ["c1"]),
    Summary("Old forum", subtext: "former", deleted: true),
    Summary("Retired VPN", subtext: "vpn-user", archived: true),
  ];

  private static FiltersResponse SampleFilters() => new(
    [new FilterRef("f2", "Personal"), new FilterRef("f1", "Work")],
    [new FilterRef("c1", "Engineering", "o1")],
    [new FilterRef("o1", "Acme")]);

  private sealed class FakeExtension
  {
    public List<ItemSummary> Items { get; set; } = SampleItems();
    public string? ErrorCode { get; set; }
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
      if (ErrorCode is { } code)
        throw new IpcException(code);
      return Task.FromResult<JsonElement?>(op switch
      {
        Ops.ListItems => IpcJson.ToElement(new ItemListResponse(Items), IpcJson.Default.ItemListResponse),
        Ops.ListFilters => IpcJson.ToElement(SampleFilters(), IpcJson.Default.FiltersResponse),
        Ops.GetItem => IpcJson.ToElement(Detail(IpcJson.Read(payload, IpcJson.Default.ItemRequest).Id), IpcJson.Default.ItemDto),
        _ => null,
      });
    }

    private ItemDto Detail(string id)
    {
      var item = Items.Single(i => i.Id == id);
      return new ItemDto(id, item.Name, item.Type, "Work", null, item.Favorite, false, false, Revision, item.RevisionDate, null,
      [
        new FieldDto(ItemFields.Username, "Username", "Login credentials", item.Subtext, false),
        new FieldDto(ItemFields.Password, "Password", "Login credentials", null, true),
        new FieldDto(ItemFields.UriPrefix + "0", "Website", "Autofill options", "https://github.com", false, CanOpen: true),
      ], IconPath: item.IconPath);
    }
  }

  private static PipeChannel CompanionChannel(ChannelPair pair)
  {
    var channel = new PipeChannel(pair.CompanionInput, pair.CompanionOutput, (_, _, _) => Task.FromResult<JsonElement?>(null));
    _ = Task.Run(channel.RunAsync);
    return channel;
  }

  private static FilterViewModel FilterOf(VaultViewModel vm, FilterKind kind, string? label = null) =>
    vm.Filters.First(f => f.Kind == kind && (label is null || f.Label == label));

  [Fact]
  public void Filters_MatchEveryKind()
  {
    var items = SampleItems();
    var filters = VaultViewModel.BuildFilters(SampleFilters(), items);
    var rows = items.Select(i => new ItemRowViewModel(i)).ToList();
    string[] Names(FilterKind kind, string? label = null) =>
      [.. VaultViewModel.Filter(rows, filters.First(f => f.Kind == kind && (label is null || f.Label == label)), "").Select(r => r.Name)];

    Assert.Equal(["All items", "Favourites", "Types", "Login", "Card", "Identity", "Secure note", "SSH key", "Folders", "Personal", "Work", "No folder", "Collections", "Engineering", "Organisations", "Acme", "Archive", "Trash"], filters.Select(f => f.Label));
    Assert.Equal(7, Names(FilterKind.All).Length);
    Assert.Equal(["GitHub"], Names(FilterKind.Favorites));
    Assert.Equal(["Visa Platinum"], Names(FilterKind.Type, "Card"));
    Assert.Equal(["GitHub", "Wi-Fi codes"], Names(FilterKind.Folder, "Work"));
    Assert.Equal(["Amazon", "Jane Doe", "Build server"], Names(FilterKind.Folder, "No folder"));
    Assert.Equal(["GitHub", "Build server"], Names(FilterKind.Collection));
    Assert.Equal(["GitHub", "Build server"], Names(FilterKind.Organization));
    Assert.Equal(["Old forum"], Names(FilterKind.Trash));
    Assert.Equal(["Retired VPN"], Names(FilterKind.Archive));
    Assert.DoesNotContain(VaultViewModel.BuildFilters(SampleFilters(), [.. items.Where(i => !i.Archived)]), f => f.Kind == FilterKind.Archive);
    Assert.DoesNotContain(VaultViewModel.BuildFilters(new FiltersResponse([], [], []), items), f => f.Kind is FilterKind.Collection or FilterKind.Organization);
  }

  [Fact]
  public void Search_IsCaseInsensitiveAcrossNameSubtextAndHost()
  {
    var rows = SampleItems().Select(i => new ItemRowViewModel(i)).ToList();
    var all = new FilterViewModel(FilterKind.All, "All items");
    string[] Search(string query) => [.. VaultViewModel.Filter(rows, all, query).Select(r => r.Name)];

    Assert.Equal(["GitHub"], Search("GITHUB"));
    Assert.Equal(["GitHub"], Search("octo"));
    Assert.Equal(["Google"], Search("accounts.google"));
    Assert.Equal(["Amazon", "Jane Doe"], Search("example.com"));
    Assert.Equal(["Visa Platinum"], Search("4242"));
    Assert.Empty(Search("vpn-user"));
    Assert.Equal(7, Search("").Length);
  }

  [Fact]
  public void Filter_TwoThousandItems_StaysUnderOneFrame()
  {
    var rows = Enumerable.Range(0, 2000)
      .Select(i => new ItemRowViewModel(Summary($"Site {i:D4}", type: 1 + (i % 5), subtext: $"user{i}@example.com", favorite: i % 10 == 0, folder: i % 3 == 0 ? "f1" : null, hosts: [$"site{i}.example.org"])))
      .ToList();
    var all = new FilterViewModel(FilterKind.All, "All items");
    var favorites = new FilterViewModel(FilterKind.Favorites, "Favourites");
    string[] queries = ["s", "si", "sit", "site", "site 1", "site 19", "USER1999", "site1999.example", "nothing-matches"];

    Assert.Equal(2000, VaultViewModel.Filter(rows, all, "").Count);
    Assert.Equal(200, VaultViewModel.Filter(rows, favorites, "").Count);
    Assert.Equal(1000, VaultViewModel.Filter(rows, all, "site 1").Count);
    Assert.Equal(100, VaultViewModel.Filter(rows, all, "site 19").Count);
    Assert.Single(VaultViewModel.Filter(rows, all, "USER1999"));
    Assert.Empty(VaultViewModel.Filter(rows, all, "nothing-matches"));

    var timings = new List<double>();
    for (var run = 0; run < 20; run++)
    {
      foreach (var query in queries)
      {
        var watch = Stopwatch.StartNew();
        VaultViewModel.Filter(rows, all, query);
        timings.Add(watch.Elapsed.TotalMilliseconds);
      }
    }

    timings.Sort();
    var median = timings[timings.Count / 2];
    var p99 = timings[(int)(timings.Count * 0.99)];
    TestContext.Current.TestOutputHelper?.WriteLine($"Filter 2000 items: median {median:F3} ms, p99 {p99:F3} ms, max {timings[^1]:F3} ms");
    Assert.True(median < 16, $"median {median} ms");
  }

  [Fact]
  public Task VaultWindow_TwoThousandItems_LoadAndKeystrokeTimings() => session.RunAsync(() =>
  {
    using var pair = new ChannelPair((_, _, _) => Task.FromResult<JsonElement?>(null));
    using var channel = CompanionChannel(pair);
    var vm = new VaultViewModel(channel, _ => { });
    var window = new MainWindow(vm);
    window.Show();
    var items = Enumerable.Range(0, 2000).Select(i => Summary($"Site {i:D4}", subtext: $"user{i}@example.com", hosts: [$"site{i}.example.org"])).ToList();

    var load = Stopwatch.StartNew();
    vm.Apply(items, new FiltersResponse([], [], []));
    window.UpdateLayout();
    load.Stop();
    Assert.Equal(2000, vm.Rows.Count);

    string[] queries = ["s", "si", "sit", "site", "site 1", "site 19", "site 199", "site 1999", "site 199", "site 19", "site 1", "site", ""];
    var timings = new List<double>();
    for (var run = 0; run < 3; run++)
    {
      foreach (var query in queries)
      {
        var watch = Stopwatch.StartNew();
        vm.SearchText = query;
        vm.ApplySearch();
        window.UpdateLayout();
        timings.Add(watch.Elapsed.TotalMilliseconds);
      }
    }

    timings.Sort();
    var median = timings[timings.Count / 2];
    TestContext.Current.TestOutputHelper?.WriteLine($"Vault window 2000 items: first load {load.Elapsed.TotalMilliseconds:F1} ms, keystroke median {median:F2} ms, max {timings[^1]:F2} ms");
    Assert.Equal(2000, vm.Rows.Count);
    // ponytail: generous cap for slower CI agents; the measured median on a dev machine is in the test output.
    Assert.True(median < 50, $"median {median} ms");
    window.Close();
  });

  [Fact]
  public Task VaultChanged_ReloadKeepsRowInstancesSelectionAndDetail() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var opened = new List<string>();
    using var vm = new VaultViewModel(channel, opened.Add);

    await vm.ReloadAsync();
    Assert.Equal(7, vm.Rows.Count);
    Assert.Equal("Amazon", vm.Rows[0].Name);
    var github = vm.Rows.Single(r => r.Name == "GitHub");
    var google = vm.Rows.Single(r => r.Name == "Google");
    vm.SelectedRow = github;
    var detail = vm.Detail!;
    AvaloniaSession.WaitFor(() => detail.Title == "GitHub");

    var googleIndex = extension.Items.FindIndex(i => i.Name == "Google");
    extension.Items[googleIndex] = extension.Items[googleIndex] with { Subtext = "renamed", RevisionDate = Revision.AddDays(1) };
    extension.Items.Add(Summary("Zoom", subtext: "jane"));
    await vm.ReloadAsync();

    Assert.Equal(8, vm.Rows.Count);
    Assert.Same(github, vm.Rows.Single(r => r.Name == "GitHub"));
    Assert.NotSame(google, vm.Rows.Single(r => r.Name == "Google"));
    Assert.Equal("renamed", vm.Rows.Single(r => r.Name == "Google").Subtext);
    Assert.Same(github, vm.SelectedRow);
    Assert.Same(detail, vm.Detail);
    Assert.Equal(2, extension.Count(Ops.GetItem));

    vm.OpenSelectedCommand.Execute(null);
    Assert.Equal([github.Id], opened);

    extension.Items.RemoveAll(i => i.Name == "GitHub");
    await vm.ReloadAsync();
    Assert.Null(vm.SelectedRow);
    Assert.Null(vm.Detail);
    Assert.False(vm.OpenSelectedCommand.CanExecute(null));
  });

  [Fact]
  public Task SearchFilterAndSelection_Work() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new VaultViewModel(channel, _ => { });
    await vm.ReloadAsync();

    vm.SearchText = "jane";
    Assert.True(vm.IsSearchPending);
    Assert.Equal(7, vm.Rows.Count);
    vm.ApplySearch();
    Assert.False(vm.IsSearchPending);
    Assert.Equal(3, vm.Rows.Count);
    vm.MoveSelection(1);
    Assert.Equal("Amazon", vm.SelectedRow!.Name);
    vm.MoveSelection(1);
    vm.MoveSelection(1);
    vm.MoveSelection(1);
    Assert.Equal("Jane Doe", vm.SelectedRow!.Name);
    vm.MoveSelection(-1);
    Assert.Equal("Google", vm.SelectedRow!.Name);
    var detail = vm.Detail;

    vm.SelectedFilter = FilterOf(vm, FilterKind.Folder, "Personal");
    Assert.Equal(["Google"], vm.Rows.Select(r => r.Name));
    Assert.Same(detail, vm.Detail);

    vm.SelectedFilter = FilterOf(vm, FilterKind.Trash);
    Assert.True(vm.IsEmpty);
    Assert.Null(vm.SelectedRow);
    Assert.Null(vm.Detail);

    vm.ClearSearch();
    Assert.Equal(["Old forum"], vm.Rows.Select(r => r.Name));
    vm.MoveSelection(-1);
    Assert.Equal("Old forum", vm.SelectedRow!.Name);
  });

  [Fact]
  public Task Locked_BlanksTheListAndDetail_AndUnlockReloads() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new VaultViewModel(channel, _ => { });
    await vm.ReloadAsync();
    vm.SelectedRow = vm.Rows[0];
    Assert.NotNull(vm.Detail);

    vm.OnLocked();
    Assert.True(vm.IsLocked);
    Assert.Empty(vm.Rows);
    Assert.Empty(vm.Filters);
    Assert.Null(vm.Detail);

    extension.ErrorCode = IpcErrors.Locked;
    await vm.ReloadAsync();
    Assert.True(vm.IsLocked);

    extension.ErrorCode = IpcErrors.Failed;
    await vm.ReloadAsync();
    Assert.Equal("Something went wrong.", vm.ErrorMessage);

    extension.ErrorCode = null;
    await vm.ReloadAsync();
    Assert.False(vm.IsLocked);
    Assert.Null(vm.ErrorMessage);
    Assert.Equal(7, vm.Rows.Count);
  });

  [Fact]
  public Task VaultWindow_RendersEveryFilter_AndHandlesKeyboard() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var opened = new List<string>();
    var vm = new VaultViewModel(channel, opened.Add);
    var window = new MainWindow(vm);
    window.Show();
    await vm.ReloadAsync();

    Assert.True(window.CanMinimize);
    Assert.True(window.CanMaximize);
    foreach (var filter in vm.Filters.Where(f => !f.IsHeader).ToList())
    {
      vm.SelectedFilter = filter;
      Assert.NotNull(window.CaptureRenderedFrame());
      var list = window.FindControl<ListBox>("ItemList")!;
      Assert.Equal(vm.Rows.Count, list.ItemCount);
    }

    var rail = window.FindControl<ListBox>("FilterList")!;
    var foot = window.FindControl<ListBox>("FootList")!;
    foot.SelectedItem = vm.FootFilters.Single(f => f.Kind == FilterKind.Trash);
    Assert.Equal(FilterKind.Trash, vm.SelectedFilter!.Kind);
    Assert.Null(rail.SelectedItem);
    rail.SelectedItem = vm.RailFilters[1];
    Assert.Equal(FilterKind.Favorites, vm.SelectedFilter!.Kind);
    Assert.Null(foot.SelectedItem);
    foot.SelectedItem = vm.FootFilters.Single(f => f.Kind == FilterKind.Trash);
    await vm.ReloadAsync();
    Assert.Equal(FilterKind.Trash, ((FilterViewModel)foot.SelectedItem!).Kind);
    Assert.Null(rail.SelectedItem);
    vm.SelectedFilter = vm.Filters[0];
    Assert.Equal(vm.Filters[0], rail.SelectedItem);
    await vm.ReloadAsync();
    Assert.Equal(vm.Filters[0], rail.SelectedItem);
    Assert.Null(foot.SelectedItem);
    window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
    var search = window.FindControl<TextBox>("SearchBox")!;
    Assert.True(search.IsFocused);
    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
    Assert.Equal("Amazon", vm.SelectedRow!.Name);
    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
    Assert.Equal([vm.SelectedRow.Id], opened);

    window.KeyTextInput("goo");
    Assert.Equal("goo", vm.SearchText);
    vm.ApplySearch();
    Assert.Single(vm.Rows);
    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
    Assert.Equal(string.Empty, vm.SearchText);
    Assert.Equal(7, vm.Rows.Count);

    vm.SelectedRow = vm.Rows.Single(r => r.Name == "GitHub");
    AvaloniaSession.WaitFor(() => vm.Detail?.Title == "GitHub");
    var frame = window.CaptureRenderedFrame();
    Assert.NotNull(frame);
    SaveScreenshot(frame, "vault-window.png");

    vm.OnLocked();
    Assert.True(window.FindControl<StackPanel>("LockedPanel")!.IsVisible);
    Assert.NotNull(window.CaptureRenderedFrame());
    window.Close();
  });

  [Fact]
  public Task ItemWindow_RendersStackedGroups() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var vm = new ItemViewModel(extension.Items[0].Id, channel);
    var window = new ItemWindow(vm);
    window.Show();
    await vm.LoadAsync();

    var frame = window.CaptureRenderedFrame();
    Assert.NotNull(frame);
    SaveScreenshot(frame, "item-window.png");
    window.Close();
  });

  [Fact]
  public Task Coordinator_ShowVault_ReloadsOnVaultChanged_AndBlanksOnLocked() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    _ = coordinator.RunAsync(() => { });

    var vm = coordinator.ShowVault().ViewModel!;
    AvaloniaSession.WaitFor(() => vm.Rows.Count == 7);
    var item = coordinator.ShowItem(extension.Items[0].Id).ViewModel!;
    AvaloniaSession.WaitFor(() => item.Title == "GitHub");

    extension.Items.Add(Summary("Zoom"));
    await pair.Extension.SendEventAsync(Ops.VaultChanged);
    AvaloniaSession.WaitFor(() => vm.Rows.Count == 8);
    AvaloniaSession.WaitFor(() => extension.Count(Ops.GetItem) >= 2);

    await pair.Extension.SendEventAsync(Ops.Locked);
    AvaloniaSession.WaitFor(() => vm.IsLocked);
    Assert.Empty(vm.Rows);

    foreach (var window in coordinator.ItemWindows.ToList())
      window.Close();
    coordinator.ShowVault().Close();
  });

  [Fact]
  public Task Row_LoadsACachedFavicon_AndFallsBackToTheGlyph() => session.RunAsync(() =>
  {
    var dir = Directory.CreateTempSubdirectory().FullName;
    try
    {
      var png = Path.Combine(dir, "ok.png");
      var bad = Path.Combine(dir, "bad.png");
      using (var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(4, 4), new Avalonia.Vector(96, 96)))
        bitmap.Save(png, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
      File.WriteAllText(bad, "not an image");

      var ok = new ItemRowViewModel(Summary("GitHub") with { IconPath = png });
      Assert.True(ok.HasIcon);
      Assert.Same(ok.Icon, new ItemRowViewModel(Summary("Other") with { IconPath = png }).Icon);
      Assert.False(new ItemRowViewModel(Summary("Broken") with { IconPath = bad }).HasIcon);
      Assert.False(new ItemRowViewModel(Summary("Missing") with { IconPath = Path.Combine(dir, "missing.png") }).HasIcon);
      Assert.False(new ItemRowViewModel(Summary("None")).HasIcon);
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  });

  [Fact]
  public Task IconsChanged_ShowsALateIcoIcon_InTheRowAndHeader_WithoutReloadingTheDetail() => session.RunAsync(async () =>
  {
    var dir = Directory.CreateTempSubdirectory().FullName;
    try
    {
      var png = Path.Combine(dir, "frame.png");
      using (var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(16, 16), new Avalonia.Vector(96, 96)))
        bitmap.Save(png, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
      var pngBytes = await File.ReadAllBytesAsync(png, TestContext.Current.CancellationToken);
      var ico = Path.Combine(dir, "github.com.png");
      byte[] header = [0, 0, 1, 0, 1, 0, 16, 16, 0, 0, 1, 0, 32, 0, .. BitConverter.GetBytes(pngBytes.Length), 22, 0, 0, 0];
      await File.WriteAllBytesAsync(ico, [.. header, .. pngBytes], TestContext.Current.CancellationToken);

      var extension = new FakeExtension();
      using var pair = new ChannelPair(extension.HandleAsync);
      using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
      _ = coordinator.RunAsync(() => { });
      var window = coordinator.ShowVault();
      var vm = window.ViewModel!;
      AvaloniaSession.WaitFor(() => vm.Rows.Count == 7);
      vm.SelectedRow = vm.Rows.Single(r => r.Name == "GitHub");
      AvaloniaSession.WaitFor(() => vm.Detail?.Title == "GitHub");
      Assert.False(vm.SelectedRow.HasIcon);
      Assert.False(vm.Detail!.HasIcon);
      var loads = extension.Count(Ops.GetItem);

      extension.Items[0] = extension.Items[0] with { IconPath = ico };
      await pair.Extension.SendEventAsync(Ops.IconsChanged);
      AvaloniaSession.WaitFor(() => vm.Rows.Single(r => r.Name == "GitHub").HasIcon);
      AvaloniaSession.WaitFor(() => vm.Detail.HasIcon);
      Assert.Equal(loads, extension.Count(Ops.GetItem));
      Assert.NotNull(window.CaptureRenderedFrame());

      var itemWindow = coordinator.ShowItem(extension.Items[0].Id);
      AvaloniaSession.WaitFor(() => itemWindow.ViewModel!.HasIcon);
      itemWindow.Close();
      window.Close();
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
