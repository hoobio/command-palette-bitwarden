using System.Text.Json;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.Tests;

public class PipeChannelTests
{
  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  private static Task<JsonElement?> Echo(string op, JsonElement? payload, CancellationToken cancellationToken) =>
    op == "fail" ? throw new IpcException(IpcErrors.NotFound)
    : op == "crash" ? throw new InvalidOperationException("boom")
    : Task.FromResult(payload);

  [Fact]
  public async Task Request_RoundTripsPayload()
  {
    using var pair = new ChannelPair(Echo, Echo);
    var reply = await pair.Companion!.RequestAsync("echo", IpcJson.ToElement(new ItemRequest("abc"), IpcJson.Default.ItemRequest), Ct);
    Assert.Equal("abc", IpcJson.Read(reply, IpcJson.Default.ItemRequest).Id);
  }

  [Fact]
  public async Task HandlerErrors_ComeBackAsCodes()
  {
    using var pair = new ChannelPair(Echo, Echo);
    Assert.Equal(IpcErrors.NotFound, (await Assert.ThrowsAsync<IpcException>(() => pair.Companion!.RequestAsync("fail", null, Ct))).Code);
    Assert.Equal(IpcErrors.Failed, (await Assert.ThrowsAsync<IpcException>(() => pair.Companion!.RequestAsync("crash", null, Ct))).Code);
    Assert.Equal(IpcErrors.Invalid, Assert.Throws<IpcException>(() => IpcJson.Read(null, IpcJson.Default.ItemRequest)).Code);
  }

  [Fact]
  public async Task ConcurrentRequests_BothDirections_AreCorrelated()
  {
    using var pair = new ChannelPair(Echo, Echo);
    var fromCompanion = Enumerable.Range(0, 40).Select(i => pair.Companion!.RequestAsync("echo", IpcJson.ToElement(new ItemRequest($"c{i}"), IpcJson.Default.ItemRequest), Ct));
    var fromExtension = Enumerable.Range(0, 40).Select(i => pair.Extension.RequestAsync("echo", IpcJson.ToElement(new ItemRequest($"e{i}"), IpcJson.Default.ItemRequest), Ct));

    var replies = await Task.WhenAll(fromCompanion.Concat(fromExtension));

    Assert.Equal(
      Enumerable.Range(0, 40).Select(i => $"c{i}").Concat(Enumerable.Range(0, 40).Select(i => $"e{i}")),
      replies.Select(r => IpcJson.Read(r, IpcJson.Default.ItemRequest).Id));
  }

  [Fact]
  public async Task ThrowingEventHandler_IsLogged_AndTheLoopKeepsReading()
  {
    var logs = new List<string>();
    using var pair = new ChannelPair(Echo);
    using var companion = new PipeChannel(pair.CompanionInput, pair.CompanionOutput, Echo, (_, _) => throw new InvalidOperationException("boom"), logs.Add);
    _ = Task.Run(companion.RunAsync, Ct);

    await pair.Extension.SendEventAsync("evt");
    var reply = await pair.Extension.RequestAsync("echo", IpcJson.ToElement(new ItemRequest("still"), IpcJson.Default.ItemRequest), Ct);

    Assert.Equal("still", IpcJson.Read(reply, IpcJson.Default.ItemRequest).Id);
    Assert.False(companion.IsClosed);
    Assert.Equal(["Event evt handler failed: InvalidOperationException"], logs);
  }

  [Fact]
  public async Task Eof_EndsTheLoopAndFailsRequests()
  {
    var pair = new ChannelPair(Echo, Echo);
    pair.Extension.Dispose();

    await pair.CompanionLoop.WaitAsync(TimeSpan.FromSeconds(10), Ct);

    Assert.True(pair.Companion!.IsClosed);
    await Assert.ThrowsAsync<IOException>(() => pair.Companion.RequestAsync("echo", null, Ct));
    await Assert.ThrowsAsync<IOException>(() => pair.Companion.SendEventAsync("evt"));
    pair.Dispose();
  }
}
