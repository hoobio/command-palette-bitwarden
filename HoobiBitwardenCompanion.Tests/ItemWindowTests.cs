using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.ViewModels;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Tests;

public class ItemWindowTests(AvaloniaSession session)
{
  private static readonly string ItemId = Guid.NewGuid().ToString();

  private sealed class FakeExtension
  {
    public bool RepromptRequired { get; set; }
    public string? ErrorCode { get; set; }
    public int TotpRemaining { get; set; } = 3;
    public int TotpCalls { get; private set; }
    public List<string> Requests { get; } = [];

    public Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
    {
      lock (Requests)
        Requests.Add(op);
      if (ErrorCode is { } code)
        throw new IpcException(code);

      return Task.FromResult<JsonElement?>(op switch
      {
        Ops.GetItem => IpcJson.ToElement(Item(), IpcJson.Default.ItemDto),
        Ops.Reveal => IpcJson.ToElement(new SecretResponse("hunter2"), IpcJson.Default.SecretResponse),
        Ops.GetTotp => IpcJson.ToElement(new TotpResponse($"00000{++TotpCalls}", TotpRemaining, 30), IpcJson.Default.TotpResponse),
        Ops.VerifyReprompt => VerifyReprompt(IpcJson.Read(payload, IpcJson.Default.RepromptRequest)),
        _ => null,
      });
    }

    private JsonElement VerifyReprompt(RepromptRequest request)
    {
      var ok = request.Password == "correct";
      if (ok)
        RepromptRequired = false;
      return IpcJson.ToElement(new RepromptResponse(ok, 0), IpcJson.Default.RepromptResponse);
    }

    private ItemDto Item() => new(
      ItemId, "GitHub", 1, "Work", "Acme", false, true, RepromptRequired,
      new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc), null,
      RepromptRequired ? [] :
      [
        new FieldDto(ItemFields.Username, "Username", "Login credentials", "octocat", false),
        new FieldDto(ItemFields.Password, "Password", "Login credentials", null, true),
        new FieldDto(ItemFields.Totp, "Verification code (TOTP)", "Login credentials", null, true),
        new FieldDto(ItemFields.UriPrefix + "0", "Website", "Autofill options", "https://github.com", false, CanOpen: true),
      ]);
  }

  private static FieldViewModel Field(ItemViewModel vm, string key) => vm.Groups.SelectMany(g => g.Fields).Single(f => f.Key == key);

  [Fact]
  public Task ItemWindow_HasCloseOnlyChrome_AndMenuTogglesAlwaysOnTop() => session.RunAsync(() =>
  {
    ItemWindow.AlwaysOnTop = false;
    var window = new ItemWindow();
    window.Show();

    Assert.False(window.CanMinimize);
    Assert.False(window.CanMaximize);
    Assert.True(window.CanResize);
    Assert.Same(window.FindResource("HoobiCloseOnlyDecorations"), window.WindowDecorationsTheme);
    Assert.NotNull(window.CaptureRenderedFrame());

    var root = window.FindControl<Grid>("Root")!;
    root.ContextMenu!.Open(root);
    var item = window.FindControl<MenuItem>("AlwaysOnTopItem")!;
    Assert.False(item.IsChecked);
    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Assert.True(window.Topmost);
    Assert.True(item.IsChecked);
    Assert.True(ItemWindow.AlwaysOnTop);

    var second = new ItemWindow();
    Assert.True(second.Topmost);

    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Assert.False(window.Topmost);
    Assert.False(ItemWindow.AlwaysOnTop);
    window.Close();
  });

  [Fact]
  public Task MainWindow_KeepsMinimiseAndMaximise() => session.RunAsync(() =>
  {
    var window = new MainWindow();
    Assert.True(window.CanMinimize);
    Assert.True(window.CanMaximize);
    Assert.Same(window.FindResource("HoobiWindowDecorations"), window.WindowDecorationsTheme);
  });

  [Fact]
  public Task ItemViewModel_LoadsRevealsCopiesOpensAndTicksTotp() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new ItemViewModel(ItemId, channel);

    await vm.LoadAsync();

    Assert.Equal("GitHub", vm.Title);
    Assert.Equal("Work  ·  Acme", vm.Subtitle);
    Assert.Equal(["Login credentials", "Autofill options", "Item history"], vm.Groups.Select(g => g.Name));
    var password = Field(vm, ItemFields.Password);
    Assert.Equal(FieldViewModel.Mask, password.DisplayValue);
    Assert.Equal("octocat", Field(vm, ItemFields.Username).DisplayValue);

    await password.ToggleRevealCommand.ExecuteAsync(null);
    Assert.Equal("hunter2", password.DisplayValue);
    await password.ToggleRevealCommand.ExecuteAsync(null);
    Assert.Equal(FieldViewModel.Mask, password.DisplayValue);
    Assert.Null(password.RevealedValue);

    await Field(vm, ItemFields.Username).CopyCommand.ExecuteAsync(null);
    Assert.Equal("Copied Username", vm.StatusMessage);
    await Field(vm, ItemFields.UriPrefix + "0").OpenCommand.ExecuteAsync(null);
    Assert.Contains(Ops.OpenUri, extension.Requests);

    var totp = Field(vm, ItemFields.Totp);
    Assert.Equal("000001", totp.DisplayValue);
    Assert.Equal(3, totp.TotpRemaining);
    Assert.Equal(36, totp.TotpSweep);
    await vm.TickAsync();
    await vm.TickAsync();
    Assert.Equal(1, totp.TotpRemaining);
    Assert.Equal(1, extension.TotpCalls);
    await vm.TickAsync();
    Assert.Equal(2, extension.TotpCalls);
    Assert.Equal("000002", totp.DisplayValue);
  });

  [Fact]
  public Task ItemWindow_RevealedSecretsAndTotp_AreNotSelectable() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    var vm = new ItemViewModel(ItemId, channel);
    var window = new ItemWindow(vm);
    window.Show();
    await vm.LoadAsync();
    await Field(vm, ItemFields.Password).ToggleRevealCommand.ExecuteAsync(null);
    Assert.NotNull(window.CaptureRenderedFrame());

    var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
    Assert.Contains(texts, t => t is not SelectableTextBlock && t.Text == "hunter2");
    Assert.Contains(texts, t => t is not SelectableTextBlock && t.Text == "000001");
    Assert.DoesNotContain(texts, t => t is SelectableTextBlock && t.Text is "hunter2" or "000001");
    Assert.Contains(texts, t => t is SelectableTextBlock && t.Text == "octocat");
    window.Close();
  });

  [Fact]
  public Task ItemViewModel_RepromptFlow_UnlocksAfterVerification() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { RepromptRequired = true };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new ItemViewModel(ItemId, channel);

    await vm.LoadAsync();
    Assert.True(vm.RepromptRequired);
    Assert.Empty(vm.Groups);

    Assert.False(await vm.VerifyRepromptAsync("wrong"));
    Assert.Equal("Incorrect master password.", vm.ErrorMessage);

    Assert.True(await vm.VerifyRepromptAsync("correct"));
    Assert.False(vm.RepromptRequired);
    Assert.Null(vm.ErrorMessage);
    Assert.NotEmpty(vm.Groups);
  });

  [Fact]
  public Task ItemViewModel_MapsErrorsToMessages() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var channel = CompanionChannel(pair);
    using var vm = new ItemViewModel(ItemId, channel);
    await vm.LoadAsync();

    extension.ErrorCode = IpcErrors.Reprompt;
    await Field(vm, ItemFields.Password).ToggleRevealCommand.ExecuteAsync(null);
    Assert.True(vm.RepromptRequired);

    extension.ErrorCode = IpcErrors.Locked;
    await vm.LoadAsync();
    Assert.Equal(ItemViewModel.Describe(new IpcException(IpcErrors.Locked)), vm.ErrorMessage);
    Assert.Equal("This item is no longer in the vault.", ItemViewModel.Describe(new IpcException(IpcErrors.NotFound)));
    Assert.Equal("The connection to Command Palette was lost.", ItemViewModel.Describe(new IOException()));
    Assert.Equal("Something went wrong.", ItemViewModel.Describe(new IpcException(IpcErrors.Failed)));
    Assert.Equal("", ItemViewModel.GlyphFor(5));
  });

  [Fact]
  public Task Coordinator_ShowItem_ReusesTheOpenWindow_AndValidatesIds() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    var closed = false;
    var run = coordinator.RunAsync(() => closed = true);

    await pair.Extension.RequestAsync(Ops.ShowItem, IpcJson.ToElement(new ItemRequest(ItemId), IpcJson.Default.ItemRequest), TestContext.Current.CancellationToken);
    await pair.Extension.RequestAsync(Ops.ShowItem, IpcJson.ToElement(new ItemRequest(ItemId.ToUpperInvariant()), IpcJson.Default.ItemRequest), TestContext.Current.CancellationToken);
    var window = Assert.Single(coordinator.ItemWindows);
    Assert.Same(window, coordinator.ShowItem(ItemId));

    var invalid = await Assert.ThrowsAsync<IpcException>(() => pair.Extension.RequestAsync(Ops.ShowItem, IpcJson.ToElement(new ItemRequest("nope"), IpcJson.Default.ItemRequest), TestContext.Current.CancellationToken));
    Assert.Equal(IpcErrors.Invalid, invalid.Code);
    var unknown = await Assert.ThrowsAsync<IpcException>(() => pair.Extension.RequestAsync("getItem", null, TestContext.Current.CancellationToken));
    Assert.Equal(IpcErrors.Invalid, unknown.Code);

    await pair.Extension.RequestAsync(Ops.ShowVault, null, TestContext.Current.CancellationToken);
    var vault = coordinator.ShowVault();
    Assert.True(vault.IsVisible);
    vault.Close();

    window.Close();
    Assert.Empty(coordinator.ItemWindows);

    pair.Extension.Dispose();
    await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    AvaloniaSession.WaitFor(() => closed);
  });

  private static PipeChannel CompanionChannel(ChannelPair pair)
  {
    var channel = new PipeChannel(pair.CompanionInput, pair.CompanionOutput, (_, _, _) => Task.FromResult<JsonElement?>(null));
    _ = Task.Run(channel.RunAsync);
    return channel;
  }
}
