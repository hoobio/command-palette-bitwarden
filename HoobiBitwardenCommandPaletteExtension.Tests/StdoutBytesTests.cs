using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class StdoutBytesTests
{
  private static (BitwardenCliService Service, FakeProcessFactory Factory) CreateService()
  {
    var factory = new FakeProcessFactory();
    var svc = new BitwardenCliService(processFactory: factory.Create) { DataFileOverride = Path.Combine(Path.GetTempPath(), $"bw_missing_{Guid.NewGuid():N}.json") };
    svc.SetSession("key");
    return (svc, factory);
  }

  private static JsonElement Value(string json) => JsonDocument.Parse(json).RootElement.GetProperty("v");

  [Theory]
  [InlineData("""{"v":"a\"b\\cé\nd"}""", "a\"b\\cé\nd")]
  [InlineData("""{"v":"pässwörd ✓ 密码 🔑"}""", "pässwörd ✓ 密码 🔑")]
  [InlineData("""{"v":"🔑 key"}""", "🔑 key")]
  public void FromJson_UnescapesIntoTheSecret(string json, string expected)
  {
    using var secret = ProtectedSecret.FromJson(Value(json))!;
    Assert.Equal(expected, secret.Reveal());
  }

  [Fact]
  public void FromJson_LongValueOverTheStackLimit_RoundTrips()
  {
    var plaintext = new string('x', 3000) + "é";
    using var secret = ProtectedSecret.FromJson(Value("{\"v\":\"" + plaintext + "\"}"))!;
    Assert.Equal(plaintext, secret.Reveal());
  }

  [Theory]
  [InlineData("""{"v":null}""")]
  [InlineData("""{"v":""}""")]
  public void FromJson_NullOrEmpty_ReturnsNull(string json)
  {
    Assert.Null(ProtectedSecret.FromJson(Value(json)));
  }

  [Fact]
  public void FromJson_NonString_Throws()
  {
    Assert.Throws<InvalidOperationException>(() => ProtectedSecret.FromJson(Value("""{"v":42}""")));
  }

  [Fact]
  public void SecretBuffer_Dispose_ZeroesTheWholeBuffer()
  {
    var bytes = "noise[\"secret\"]"u8.ToArray();
    var buffer = new SecretBuffer(bytes, 5, 10);
    Assert.Equal("[\"secret\"]", Encoding.UTF8.GetString(buffer.Json.Span));
    Assert.Null(buffer.Text);

    buffer.Dispose();

    Assert.All(bytes, b => Assert.Equal(0, b));
  }

  [Fact]
  public void SecretBuffer_Text_HasNoJson()
  {
    using var buffer = new SecretBuffer("Not found.");
    Assert.True(buffer.Json.IsEmpty);
    Assert.Null(BitwardenCliService.ParseObject(buffer));
    Assert.Same(buffer, BitwardenCliService.ThrowIfRejected(buffer));
  }

  [Fact]
  public void ThrowIfRejected_KnownText_Throws()
  {
    static VaultWriteError Rejection(string text)
    {
      using var buffer = new SecretBuffer(text);
      return Assert.Throws<VaultWriteException>(() => BitwardenCliService.ThrowIfRejected(buffer)).Error;
    }

    Assert.Equal(VaultWriteError.Premium, Rejection("Premium status is required to use this feature."));
    Assert.Equal(VaultWriteError.TooLarge, Rejection("Max file size is 500 MB."));
    Assert.Equal(VaultWriteError.NoStorage, Rejection("Not enough storage available."));
  }

  [Fact]
  public void ThrowIfRejected_Json_PassesThrough()
  {
    var bytes = """{"id":"x"}"""u8.ToArray();
    using var buffer = new SecretBuffer(bytes, 0, bytes.Length);
    Assert.Same(buffer, BitwardenCliService.ThrowIfRejected(buffer));
    Assert.Equal("x", BitwardenCliService.ParseObject(buffer)!["id"]!.GetValue<string>());
  }

  [Fact]
  public void ParseItems_EscapedSecrets_ReachProtectedSecretsUnescaped()
  {
    var items = BitwardenCliService.ParseItems("""
      [
        { "id": "l", "name": "Login", "type": 1, "notes": "line1\nline2 é",
          "login": { "password": "p\"w\\d", "totp": "JBSWY", "uris": [] },
          "fields": [{ "name": "api", "value": "töken 🔑", "type": 1 }, { "name": "plain", "value": "v", "type": 0 }],
          "passwordHistory": [{ "lastUsedDate": "2024-01-01T00:00:00Z", "password": "old\tone" }] },
        { "id": "c", "name": "Card", "type": 3, "card": { "number": "4111 1111", "code": "1\"2" } },
        { "id": "s", "name": "Ssh", "type": 5, "sshKey": { "privateKey": "-----BEGIN-----\nabc\n-----END-----", "publicKey": "ssh-ed25519" } }
      ]
      """u8.ToArray());

    Assert.Equal(3, items.Count);
    Assert.Equal("line1\nline2 é", items[0].Notes!.Reveal());
    Assert.Equal("p\"w\\d", items[0].Password!.Reveal());
    Assert.Equal("JBSWY", items[0].TotpSecret!.Reveal());
    Assert.Equal("töken 🔑", items[0].CustomFields["api"].Secret!.Reveal());
    Assert.Null(items[0].CustomFields["api"].Value);
    Assert.Equal("v", items[0].CustomFields["plain"].Value);
    Assert.Equal("old\tone", Assert.Single(items[0].PasswordHistory!).Password.Reveal());
    Assert.True(items[0].EditableFromCache);
    Assert.Equal("4111 1111", items[1].CardNumber!.Reveal());
    Assert.Equal("1\"2", items[1].CardCode!.Reveal());
    Assert.Equal("-----BEGIN-----\nabc\n-----END-----", items[2].SshPrivateKey!.Reveal());
    Assert.Equal("ssh-ed25519", items[2].SshPublicKey);
  }

  [Fact]
  public void ParseItems_EmptyAndNullSecrets_AreNull()
  {
    var items = BitwardenCliService.ParseItems("""
      [{ "id": "l", "name": "Login", "type": 1, "notes": "", "login": { "password": null, "totp": "" },
         "fields": [{ "name": "h", "value": "", "type": 1 }, { "name": "n", "value": null, "type": 1 }] }]
      """u8.ToArray());

    var item = Assert.Single(items);
    Assert.Null(item.Notes);
    Assert.Null(item.Password);
    Assert.Null(item.TotpSecret);
    Assert.False(item.HasTotp);
    Assert.Null(Assert.Single(item.CustomFields).Value.Secret);
    Assert.False(item.EditableFromCache);
  }

  private static void EnqueueRefresh(FakeProcessFactory factory, FakeCliProcess items)
  {
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n"));
    factory.Enqueue(items);
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n"));
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n"));
  }

  [Fact]
  public async Task Refresh_LeadingNoiseBeforeTheArray_IsSkipped()
  {
    var (svc, factory) = CreateService();
    EnqueueRefresh(factory, new FakeCliProcess(stdout: "Update available: 2099.1.0\r\n[{\"id\":\"1\",\"name\":\"Item\",\"type\":2,\"notes\":\"né\"}]\n"));

    await svc.RefreshCacheAsync();

    Assert.Equal("né", Assert.Single(svc.SearchCached()).Notes!.Reveal());
  }

  [Fact]
  public async Task Refresh_ArrayCompleteBeforeEof_ExitsEarly()
  {
    var (svc, factory) = CreateService();
    var items = new FakeCliProcess(exitCode: 0, id: 7);
    items.StdoutStream.Enqueue("[{\"id\":\"1\",\"name\":\"Early\",\"type\":2,\"notes\":\"x]\\\"}\"}]\n");
    EnqueueRefresh(factory, items);

    await svc.RefreshCacheAsync();

    var item = Assert.Single(svc.SearchCached());
    Assert.Equal("Early", item.Name);
    Assert.Equal("x]\"}", item.Notes!.Reveal());
    items.StdoutStream.Complete();
    items.StderrStream.Complete();
  }

  [Fact]
  public async Task Refresh_OutputLargerThanTheInitialBuffer_GrowsAndParsesEverything()
  {
    var (svc, factory) = CreateService();
    var note = string.Concat(Enumerable.Repeat("é漢🔑 ", 60));
    var json = "[" + string.Join(",", Enumerable.Range(0, 200).Select(i => $"{{\"id\":\"id-{i}\",\"name\":\"Item {i}\",\"type\":2,\"notes\":\"{note}\"}}")) + "]";
    Assert.True(Encoding.UTF8.GetByteCount(json) > BitwardenCliService.CliReadBufferBytes * 4);
    EnqueueRefresh(factory, new FakeCliProcess(stdout: json + "\n"));

    await svc.RefreshCacheAsync();

    var cached = svc.SearchCached();
    Assert.Equal(200, cached.Count);
    Assert.All(cached, i => Assert.Equal(note, i.Notes!.Reveal()));
  }

  [Fact]
  public async Task Refresh_TruncatedJson_KeepsTheCacheEmpty()
  {
    var (svc, factory) = CreateService();
    EnqueueRefresh(factory, new FakeCliProcess(stdout: "[{\"id\":\"1\",\"name\":\"Cut\",\"type\":2  \t"));

    await svc.RefreshCacheAsync();

    Assert.Empty(svc.SearchCached());
  }
}
