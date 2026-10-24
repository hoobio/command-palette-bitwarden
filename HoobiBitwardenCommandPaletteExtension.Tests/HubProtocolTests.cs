using System.Buffers.Text;
using System.IO;
using System.Text;
using System.Text.Json;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class HubProtocolTests : IDisposable
{
  private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"bw_hub_{Guid.NewGuid():N}")).FullName;

  public void Dispose()
  {
    Directory.Delete(_dir, true);
    GC.SuppressFinalize(this);
  }

  private static byte[] Str(string s) => [(byte)(0xa0 | s.Length), .. Encoding.UTF8.GetBytes(s)];

  private static byte[] Cat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

  private static byte[] Bytes(params byte[] b) => b;

  private static byte[] Varint(int value)
  {
    var result = new List<byte>();
    do
    {
      var b = (byte)(value & 0x7f);
      value >>= 7;
      result.Add(value > 0 ? (byte)(b | 0x80) : b);
    } while (value > 0);
    return [.. result];
  }

  private static byte[] Framed(byte[] body) => [.. Varint(body.Length), .. body];

  private static byte[] ComplexPayload() => Cat(
    Bytes(0x83),
    Str("Ts"), Bytes(0xd6, 0xff, 0x01, 0x02, 0x03, 0x04),
    Str("Blob"), Bytes(0xc7, 0x03, 0x01, 0xaa, 0xbb, 0xcc),
    Str("List"), Bytes(0x93, 0x01, 0xa1, (byte)'x', 0x91, 0xc0));

  private static byte[] Notification(byte[] contextId, int type, byte[]? payload, bool typeLast = false)
  {
    var context = Cat(Str("ContextId"), contextId);
    var typeField = Cat(Str("Type"), Bytes((byte)type));
    var payloadField = Cat(Str("Payload"), payload ?? Bytes(0x80));
    var fields = typeLast ? Cat(context, payloadField, typeField) : Cat(context, typeField, payloadField);
    return Cat(Bytes(0x83), fields);
  }

  private static byte[] Invocation(string target, byte[] notification) => Cat(
    Bytes(0x95, 0x01, 0x80, 0xc0), Str(target), Bytes(0x91), notification);

  private static byte[] ReceiveMessage(int type, byte[]? payload = null, bool typeLast = false) =>
    Invocation("ReceiveMessage", Notification(Bytes(0xc0), type, payload, typeLast));

  [Theory]
  [InlineData(0)]
  [InlineData(5)]
  [InlineData(9)]
  [InlineData(17)]
  public void Classify_VaultChangeTypes_ReturnVaultChanged(int type) =>
    Assert.Equal(HubFrameKind.VaultChanged, HubProtocol.Classify(ReceiveMessage(type)));

  [Theory]
  [InlineData(11)]
  [InlineData(12)]
  [InlineData(15)]
  public void Classify_OtherTypes_ReturnOther(int type) =>
    Assert.Equal(HubFrameKind.Other, HubProtocol.Classify(ReceiveMessage(type)));

  [Fact]
  public void Classify_ComplexPayloadBeforeType_SkipsPayload() =>
    Assert.Equal(HubFrameKind.VaultChanged, HubProtocol.Classify(
      Invocation("ReceiveMessage", Notification(Str("ctx"), 5, ComplexPayload()))));

  [Fact]
  public void Classify_TypeAfterComplexPayload_SkipsPayload() =>
    Assert.Equal(HubFrameKind.VaultChanged, HubProtocol.Classify(ReceiveMessage(5, ComplexPayload(), typeLast: true)));

  [Fact]
  public void Classify_Ping_ReturnsOther() =>
    Assert.Equal(HubFrameKind.Other, HubProtocol.Classify([0x91, 0x06]));

  [Fact]
  public void Classify_Close_ReturnsClose() =>
    Assert.Equal(HubFrameKind.Close, HubProtocol.Classify(Cat(Bytes(0x92, 0x07), Str("reason"))));

  [Fact]
  public void Classify_OtherTarget_ReturnsOther() =>
    Assert.Equal(HubFrameKind.Other, HubProtocol.Classify(Invocation("Other", Notification(Bytes(0xc0), 5, null))));

  [Fact]
  public void Classify_Truncated_ReturnsOtherWithoutThrowing()
  {
    var message = ReceiveMessage(5, ComplexPayload(), typeLast: true);
    message = message[..^2];
    for (var length = 0; length < message.Length; length++)
      Assert.Equal(HubFrameKind.Other, HubProtocol.Classify(message.AsSpan(0, length)));
  }

  [Fact]
  public void Classify_ReservedFormatByte_ReturnsOther() =>
    Assert.Equal(HubFrameKind.Other, HubProtocol.Classify([0xc1]));

  [Fact]
  public void TryReadVarInt_OneByte()
  {
    Assert.True(HubProtocol.TryReadVarInt([0x05, 0xff], out var value, out var size));
    Assert.Equal(5, value);
    Assert.Equal(1, size);
  }

  [Fact]
  public void TryReadVarInt_TwoBytes()
  {
    Assert.True(HubProtocol.TryReadVarInt([0xac, 0x02], out var value, out var size));
    Assert.Equal(300, value);
    Assert.Equal(2, size);
  }

  [Theory]
  [InlineData(new byte[0])]
  [InlineData(new byte[] { 0x80 })]
  [InlineData(new byte[] { 0xac })]
  public void TryReadVarInt_Incomplete_ReturnsFalse(byte[] data) =>
    Assert.False(HubProtocol.TryReadVarInt(data, out _, out _));

  private static byte[] HandshakeReply(string json = "{}") => Encoding.UTF8.GetBytes(json + "\u001e");

  [Fact]
  public void Append_HandshakeThenTwoFramesInOneChunk_ReturnsBothFrames()
  {
    var reader = new HubFrameReader();
    var chunk = Cat(HandshakeReply(), Framed(ReceiveMessage(5)), Framed(ReceiveMessage(11)));
    Assert.Equal([HubFrameKind.VaultChanged, HubFrameKind.Other], reader.Append(chunk));
    Assert.True(reader.HandshakeCompleted);
  }

  [Fact]
  public void Append_HandshakeSplitAcrossCalls_CompletesOnSeparator()
  {
    var reader = new HubFrameReader();
    Assert.Empty(reader.Append(Encoding.UTF8.GetBytes("{")));
    Assert.False(reader.HandshakeCompleted);
    Assert.Empty(reader.Append(Cat(Encoding.UTF8.GetBytes("}"), Bytes(0x1e))));
    Assert.True(reader.HandshakeCompleted);
  }

  [Fact]
  public void Append_FrameSplitAcrossCalls_HeldUntilComplete()
  {
    var reader = new HubFrameReader();
    var frame = Framed(ReceiveMessage(5));
    Assert.Empty(reader.Append(Cat(HandshakeReply(), frame[..5])));
    Assert.Equal([HubFrameKind.VaultChanged], reader.Append(frame.AsSpan(5..)));
  }

  [Fact]
  public void Append_FramePrefixOnly_HeldUntilBodyArrives()
  {
    var reader = new HubFrameReader();
    var frame = Framed(ReceiveMessage(5));
    reader.Append(HandshakeReply());
    Assert.Empty(reader.Append(frame.AsSpan(..1)));
    Assert.Equal([HubFrameKind.VaultChanged], reader.Append(frame.AsSpan(1..)));
  }

  [Fact]
  public void Append_HandshakeError_Throws()
  {
    var reader = new HubFrameReader();
    Assert.Throws<InvalidDataException>(() => reader.Append(HandshakeReply("{\"error\":\"x\"}")));
    Assert.False(reader.HandshakeCompleted);
  }

  [Fact]
  public void Append_ClosedFrame_ReturnsClose()
  {
    var reader = new HubFrameReader();
    var close = Framed(Cat(Bytes(0x92, 0x07), Str("bye")));
    Assert.Equal([HubFrameKind.Close], reader.Append(Cat(HandshakeReply(), close)));
  }

  private static Uri Hub(string environmentJson)
  {
    using var document = JsonDocument.Parse(environmentJson);
    return HubProtocol.ResolveHubUri(document.RootElement);
  }

  [Theory]
  [InlineData("{\"region\":\"US\",\"urls\":null}", "wss://notifications.bitwarden.com/hub")]
  [InlineData("{\"region\":\"EU\",\"urls\":null}", "wss://notifications.bitwarden.eu/hub")]
  [InlineData("{\"region\":\"Self-hosted\",\"urls\":{\"base\":\"https://vault.example.com/\"}}", "wss://vault.example.com/notifications/hub")]
  [InlineData("{\"region\":\"Self-hosted\",\"urls\":{\"base\":\"https://vault.example.com/\",\"notifications\":\"https://n.example.com:8443\"}}", "wss://n.example.com:8443/hub")]
  [InlineData("{\"region\":\"Self-hosted\",\"urls\":{\"base\":\"http://vault.local\"}}", "ws://vault.local/notifications/hub")]
  public void ResolveHubUri_ReturnsExpectedHub(string environmentJson, string expected) =>
    Assert.Equal(expected, Hub(environmentJson).AbsoluteUri);

  [Fact]
  public void ResolveHubUri_DefaultElement_UsesBitwardenCom() =>
    Assert.Equal("wss://notifications.bitwarden.com/hub", HubProtocol.ResolveHubUri(default).AbsoluteUri);

  private string WriteDataFile(string json)
  {
    var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.json");
    File.WriteAllText(path, json);
    return path;
  }

  [Fact]
  public void ReadConnectionInfo_PrefersUserEnvironment()
  {
    var path = WriteDataFile("""
      {
        "global_account_activeAccountId": "u1",
        "user_u1_token_accessToken": "fake-token",
        "user_u1_environment_environment": {"region":"EU","urls":null},
        "global_environment_environment": {"region":"US","urls":null}
      }
      """);
    var info = HubProtocol.ReadConnectionInfo(path);
    Assert.Equal("fake-token", info!.Value.AccessToken);
    Assert.Equal("wss://notifications.bitwarden.eu/hub", info.Value.HubUri.AbsoluteUri);
  }

  [Fact]
  public void ReadConnectionInfo_FallsBackToGlobalEnvironment()
  {
    var path = WriteDataFile("""
      {
        "global_account_activeAccountId": "u1",
        "user_u1_token_accessToken": "fake-token",
        "global_environment_environment": {"region":"Self-hosted","urls":{"base":"https://vault.example.com"}}
      }
      """);
    Assert.Equal("wss://vault.example.com/notifications/hub", HubProtocol.ReadConnectionInfo(path)!.Value.HubUri.AbsoluteUri);
  }

  [Fact]
  public void ReadConnectionInfo_NoEnvironment_UsesBitwardenCom()
  {
    var path = WriteDataFile("{\"global_account_activeAccountId\":\"u1\",\"user_u1_token_accessToken\":\"fake-token\"}");
    Assert.Equal("wss://notifications.bitwarden.com/hub", HubProtocol.ReadConnectionInfo(path)!.Value.HubUri.AbsoluteUri);
  }

  [Fact]
  public void ReadConnectionInfo_MissingActiveAccount_ReturnsNull() =>
    Assert.Null(HubProtocol.ReadConnectionInfo(WriteDataFile("{\"user_u1_token_accessToken\":\"fake-token\"}")));

  [Fact]
  public void ReadConnectionInfo_MissingToken_ReturnsNull() =>
    Assert.Null(HubProtocol.ReadConnectionInfo(WriteDataFile("{\"global_account_activeAccountId\":\"u1\"}")));

  private static string Jwt(string payloadJson) =>
    $"{Base64Url.EncodeToString("{\"alg\":\"none\"}"u8)}.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson))}.sig";

  [Fact]
  public void TokenExpiresWithin_FarFuture_ReturnsFalse() =>
    Assert.False(HubProtocol.TokenExpiresWithin(
      Jwt($"{{\"exp\":{DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()}}}"), TimeSpan.FromMinutes(5)));

  [Fact]
  public void TokenExpiresWithin_Past_ReturnsTrue() =>
    Assert.True(HubProtocol.TokenExpiresWithin(
      Jwt($"{{\"exp\":{DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()}}}"), TimeSpan.FromMinutes(5)));

  [Fact]
  public void TokenExpiresWithin_ExpiresInsideMargin_ReturnsTrue() =>
    Assert.True(HubProtocol.TokenExpiresWithin(
      Jwt($"{{\"exp\":{DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds()}}}"), TimeSpan.FromMinutes(5)));

  [Fact]
  public void TokenExpiresWithin_MissingExp_ReturnsTrue() =>
    Assert.True(HubProtocol.TokenExpiresWithin(Jwt("{\"sub\":\"x\"}"), TimeSpan.FromMinutes(5)));

  [Theory]
  [InlineData("not-a-jwt")]
  [InlineData("a.!!!.c")]
  [InlineData("a.bm90LWpzb24.c")]
  public void TokenExpiresWithin_Garbage_ReturnsTrue(string token) =>
    Assert.True(HubProtocol.TokenExpiresWithin(token, TimeSpan.FromMinutes(5)));

  [Fact]
  public void CliDataFile_ExplicitDirectory_AppendsDataJson() =>
    Assert.Equal(Path.Combine(_dir, "data.json"), HubProtocol.CliDataFile(_dir));

  [Fact]
  public void TryReadVarInt_LengthOverMaxFrameSize_Throws() =>
    Assert.Throws<InvalidDataException>(() => HubProtocol.TryReadVarInt(Varint(HubProtocol.MaxFrameSize + 1), out _, out _));

  [Fact]
  public void TryReadVarInt_MaxFrameSize_IsAccepted()
  {
    Assert.True(HubProtocol.TryReadVarInt(Varint(HubProtocol.MaxFrameSize), out var value, out _));
    Assert.Equal(HubProtocol.MaxFrameSize, value);
  }

  [Fact]
  public void TryReadVarInt_FiveContinuationBytes_Throws() =>
    Assert.Throws<InvalidDataException>(() => HubProtocol.TryReadVarInt([0x80, 0x80, 0x80, 0x80, 0x80], out _, out _));

  [Fact]
  public void TryReadVarInt_FourContinuationBytes_WaitsForMore() =>
    Assert.False(HubProtocol.TryReadVarInt([0x80, 0x80, 0x80, 0x80], out _, out _));

  [Fact]
  public void Append_OversizedFrameLength_Throws()
  {
    var reader = new HubFrameReader();
    Assert.Throws<InvalidDataException>(() => reader.Append(Cat(HandshakeReply(), Varint(HubProtocol.MaxFrameSize + 1))));
  }

  private static byte[] Nested(int depth) => Cat(Enumerable.Repeat(Bytes(0x91), depth).Append(Bytes(0xc0)).ToArray());

  [Fact]
  public void Classify_PayloadNestedPastDepthLimit_ReturnsOther() =>
    Assert.Equal(HubFrameKind.Other, HubProtocol.Classify(ReceiveMessage(5, Nested(40), typeLast: true)));

  [Fact]
  public void Classify_PayloadNestedWithinDepthLimit_StillClassifies() =>
    Assert.Equal(HubFrameKind.VaultChanged, HubProtocol.Classify(ReceiveMessage(5, Nested(20), typeLast: true)));
}

[Collection("SessionStore")]
public class CliDataFileEnvironmentTests
{
  private const string Variable = "BITWARDENCLI_APPDATA_DIR";

  private static string? WithEnvironment(string? value)
  {
    var previous = Environment.GetEnvironmentVariable(Variable);
    Environment.SetEnvironmentVariable(Variable, value);
    return previous;
  }

  [Fact]
  public void CliDataFile_NullDirectory_UsesRoamingAppData()
  {
    var previous = WithEnvironment(null);
    try
    {
      Assert.Equal(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bitwarden CLI", "data.json"),
        HubProtocol.CliDataFile(null));
    }
    finally
    {
      Environment.SetEnvironmentVariable(Variable, previous);
    }
  }

  [Fact]
  public void CliDataFile_NullDirectory_HonoursEnvironmentVariable()
  {
    var previous = WithEnvironment(Path.Combine(Path.GetTempPath(), "bw_env_dir"));
    try
    {
      Assert.Equal(Path.Combine(Path.GetTempPath(), "bw_env_dir", "data.json"), HubProtocol.CliDataFile(null));
    }
    finally
    {
      Environment.SetEnvironmentVariable(Variable, previous);
    }
  }

  [Fact]
  public void CliDataFile_ExplicitDirectory_BeatsEnvironmentVariable()
  {
    var previous = WithEnvironment(Path.Combine(Path.GetTempPath(), "bw_env_dir"));
    try
    {
      Assert.Equal(Path.Combine("explicit", "data.json"), HubProtocol.CliDataFile("explicit"));
    }
    finally
    {
      Environment.SetEnvironmentVariable(Variable, previous);
    }
  }
}

internal sealed class ScriptedCliFactory
{
  private readonly Lock _gate = new();
  private readonly List<string> _args = [];
  private readonly List<FakeCliProcess> _blockedSyncs = [];
  private int _alive;

  public bool BlockSync { get; set; }
  public bool FailSync { get; set; }
  public int MaxAliveSyncs { get; private set; }
  public string[] Args { get { lock (_gate) return [.. _args]; } }
  public int SyncCount => Args.Count(a => a == "sync");

  public ICliProcess Create(System.Diagnostics.ProcessStartInfo psi)
  {
    var args = psi.Arguments;
    lock (_gate)
    {
      _args.Add(args);
      if (args == "sync" && BlockSync)
      {
        var blocked = new FakeCliProcess(0);
        _blockedSyncs.Add(blocked);
        MaxAliveSyncs = Math.Max(MaxAliveSyncs, ++_alive);
        return blocked;
      }
    }

    return new FakeCliProcess(stdout: args switch
    {
      "sync" => FailSync ? "" : "Syncing complete.\n",
      "status" => "{\"status\":\"unlocked\"}\n",
      _ when args.StartsWith("unlock", StringComparison.Ordinal) => "session_key_abc\n",
      _ => "[]\n",
    });
  }

  public void ReleaseSync(int index)
  {
    FakeCliProcess blocked;
    lock (_gate)
    {
      blocked = _blockedSyncs[index];
      _alive--;
    }
    blocked.StdoutStream.Enqueue("Syncing complete.\n");
    blocked.StdoutStream.Complete();
  }

  public int BlockedSyncCount { get { lock (_gate) return _blockedSyncs.Count; } }
}

[Collection("SessionStore")]
public class LiveSyncSyncTests : IDisposable
{
  private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"bw_livesync_{Guid.NewGuid():N}")).FullName;
  private readonly BitwardenSettingsManager _settings;
  private readonly BitwardenCliService _svc;
  private readonly ScriptedCliFactory _factory = new();

  public LiveSyncSyncTests()
  {
    _settings = new BitwardenSettingsManager(Path.Combine(_dir, "settings.json"));
    _settings.CliDataDirectoryOverride.Value = Path.Combine(_dir, "data");
    _svc = new BitwardenCliService(_settings, _factory.Create);
  }

  public void Dispose()
  {
    _settings.LiveSync.Value = false;
    RaiseSettingsChanged();
    Directory.Delete(_dir, true);
    GC.SuppressFinalize(this);
  }

  private void RaiseSettingsChanged()
  {
    var raise = _settings.Settings.GetType().GetMethod(
      "RaiseSettingsChanged",
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
    raise.Invoke(_settings.Settings, null);
  }

  private async Task UnlockWithLiveSync()
  {
    _settings.LiveSync.Value = true;
    var (success, _) = await _svc.UnlockAsync("masterpass");
    Assert.True(success);
    Assert.True(_svc.IsLiveSyncActive);
  }

  private static async Task WaitFor(Func<bool> condition)
  {
    for (var i = 0; i < 500 && !condition(); i++)
      await Task.Delay(10, TestContext.Current.CancellationToken);
    Assert.True(condition());
  }

  [Fact]
  public void QueueLiveSync_NotActive_IsNoOp()
  {
    _svc.QueueLiveSync();
    Assert.False(_svc.IsLiveSyncQueued);
  }

  [Fact]
  public async Task QueueLiveSync_Active_SetsQueued()
  {
    await UnlockWithLiveSync();
    _svc.QueueLiveSync();
    Assert.True(_svc.IsLiveSyncQueued);
  }

  [Fact]
  public async Task QueueLiveSync_AfterLiveSyncDisabled_IsNoOpAndClearsTimer()
  {
    await UnlockWithLiveSync();
    _svc.QueueLiveSync();
    _settings.LiveSync.Value = false;
    RaiseSettingsChanged();
    Assert.False(_svc.IsLiveSyncQueued);
    _svc.QueueLiveSync();
    Assert.False(_svc.IsLiveSyncQueued);
  }

  [Fact]
  public async Task LiveSyncAsync_Locked_RunsNoProcess()
  {
    await _svc.LiveSyncAsync();
    Assert.Empty(_factory.Args);
  }

  [Fact]
  public async Task LiveSyncAsync_Unlocked_RunsBwSync()
  {
    await UnlockWithLiveSync();
    await _svc.LiveSyncAsync();
    Assert.Equal(1, _factory.SyncCount);
  }

  [Fact]
  public async Task LiveSyncAsync_WhileOneInFlight_QueuesInsteadOfSecondSync()
  {
    await UnlockWithLiveSync();
    _factory.BlockSync = true;

    var first = _svc.LiveSyncAsync();
    await WaitFor(() => _factory.BlockedSyncCount == 1);

    await _svc.LiveSyncAsync();
    Assert.Equal(1, _factory.SyncCount);
    Assert.True(_svc.IsLiveSyncQueued);

    _factory.ReleaseSync(0);
    await first;
    Assert.Equal(1, _factory.SyncCount);
  }

  private void ClearQueuedTimer()
  {
    var field = typeof(BitwardenCliService).GetField("_liveSyncTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    (field.GetValue(_svc) as IDisposable)?.Dispose();
    field.SetValue(_svc, null);
  }

  [Fact]
  public async Task SyncVaultAsync_SyncReportsComplete_ReturnsTrue()
  {
    await UnlockWithLiveSync();
    Assert.True(await _svc.SyncVaultAsync());
  }

  [Fact]
  public async Task SyncVaultAsync_EmptySyncOutput_ReturnsFalse()
  {
    await UnlockWithLiveSync();
    _factory.FailSync = true;
    Assert.False(await _svc.SyncVaultAsync());
  }

  [Fact]
  public async Task LiveSyncAsync_SyncFails_QueuesRetry()
  {
    await UnlockWithLiveSync();
    _factory.FailSync = true;
    await _svc.LiveSyncAsync();
    Assert.True(_svc.IsLiveSyncQueued);
  }

  [Fact]
  public async Task LiveSyncAsync_SyncSucceeds_DoesNotQueue()
  {
    await UnlockWithLiveSync();
    await _svc.LiveSyncAsync();
    Assert.False(_svc.IsLiveSyncQueued);
  }

  [Fact]
  public async Task LiveSyncAsync_FourConsecutiveFailures_FourthDoesNotQueue()
  {
    await UnlockWithLiveSync();
    _factory.FailSync = true;
    for (var attempt = 1; attempt <= 3; attempt++)
    {
      await _svc.LiveSyncAsync();
      Assert.True(_svc.IsLiveSyncQueued, $"attempt {attempt} should queue a retry");
      ClearQueuedTimer();
    }

    await _svc.LiveSyncAsync();
    Assert.False(_svc.IsLiveSyncQueued);
    Assert.Equal(4, _factory.SyncCount);
  }

  [Fact]
  public async Task LiveSyncAsync_AfterRetriesExhausted_NextFailureQueuesAgain()
  {
    await UnlockWithLiveSync();
    _factory.FailSync = true;
    for (var attempt = 0; attempt < 4; attempt++)
    {
      await _svc.LiveSyncAsync();
      ClearQueuedTimer();
    }

    await _svc.LiveSyncAsync();
    Assert.True(_svc.IsLiveSyncQueued);
  }

  [Fact]
  public async Task SyncVaultAsync_Concurrent_NeverRunsTwoSyncProcessesAtOnce()
  {
    _factory.BlockSync = true;
    var first = _svc.SyncVaultAsync();
    var second = _svc.SyncVaultAsync();

    await WaitFor(() => _factory.BlockedSyncCount == 1);
    await Task.Delay(200, TestContext.Current.CancellationToken);
    Assert.Equal(1, _factory.BlockedSyncCount);

    _factory.ReleaseSync(0);
    await WaitFor(() => _factory.BlockedSyncCount == 2);
    _factory.ReleaseSync(1);
    await Task.WhenAll(first, second);

    Assert.Equal(1, _factory.MaxAliveSyncs);
  }
}

[Collection("SessionStore")]
public class LiveSyncLifecycleTests : IDisposable
{
  private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"bw_live_{Guid.NewGuid():N}")).FullName;
  private readonly BitwardenSettingsManager _settings;
  private readonly BitwardenCliService _svc;
  private readonly FakeProcessFactory _factory = new();

  public LiveSyncLifecycleTests()
  {
    _settings = new BitwardenSettingsManager(Path.Combine(_dir, "settings.json"));
    _settings.CliDataDirectoryOverride.Value = Path.Combine(_dir, "data");
    _svc = new BitwardenCliService(_settings, _factory.Create);
  }

  public void Dispose()
  {
    _settings.LiveSync.Value = false;
    RaiseSettingsChanged();
    Directory.Delete(_dir, true);
    GC.SuppressFinalize(this);
  }

  private void RaiseSettingsChanged()
  {
    var raise = _settings.Settings.GetType().GetMethod(
      "RaiseSettingsChanged",
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
    raise.Invoke(_settings.Settings, null);
  }

  private async Task Unlock()
  {
    _factory.Enqueue(new FakeCliProcess(stdout: "session_key_abc\n", exitCode: 0));
    var (success, _) = await _svc.UnlockAsync("masterpass");
    Assert.True(success);
  }

  [Fact]
  public async Task Unlock_LiveSyncOff_StaysInactive()
  {
    _settings.LiveSync.Value = false;
    await Unlock();
    Assert.False(_svc.IsLiveSyncActive);
  }

  [Fact]
  public async Task Unlock_LiveSyncOn_BecomesActive()
  {
    _settings.LiveSync.Value = true;
    await Unlock();
    Assert.True(_svc.IsLiveSyncActive);
  }

  [Fact]
  public void LiveSyncOn_WhileLocked_StaysInactive()
  {
    _settings.LiveSync.Value = true;
    RaiseSettingsChanged();
    Assert.False(_svc.IsLiveSyncActive);
  }

  [Fact]
  public async Task LockAsync_StopsLiveSync()
  {
    _settings.LiveSync.Value = true;
    await Unlock();
    _factory.Enqueue(new FakeCliProcess(stdout: "Your vault is locked.\n", exitCode: 0));
    await _svc.LockAsync();
    Assert.False(_svc.IsLiveSyncActive);
  }

  [Fact]
  public async Task ClearSession_StopsLiveSync()
  {
    _settings.LiveSync.Value = true;
    await Unlock();
    _svc.ClearSession();
    Assert.False(_svc.IsLiveSyncActive);
  }

  [Fact]
  public async Task SettingToggled_StartsAndStopsLiveSync()
  {
    await Unlock();
    _settings.LiveSync.Value = true;
    RaiseSettingsChanged();
    Assert.True(_svc.IsLiveSyncActive);

    _settings.LiveSync.Value = false;
    RaiseSettingsChanged();
    Assert.False(_svc.IsLiveSyncActive);
  }
}
