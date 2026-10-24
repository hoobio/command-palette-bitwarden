using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class CompanionOpsTests
{
  private const string TotpSecret = "JBSWY3DPEHPK3PXP";

  private sealed class Harness
  {
    public List<ProtectedSecret> SecretCopies { get; } = [];
    public List<(string Text, bool Sensitive)> TextCopies { get; } = [];
    public List<string> Opened { get; } = [];
    public BitwardenCliService Service { get; } = new();
    public string ExpectedPassword { get; set; } = "correct";
    public CompanionOps Ops { get; }

    public Harness(params BitwardenItem[] items)
    {
      Service.LoadTestData([.. items], new() { ["f1"] = "Work" }, new() { ["o1"] = "Acme" });
      Service.SetSession("session-key");
      Ops = new CompanionOps(
        Service,
        password => Task.FromResult(password == ExpectedPassword),
        SecretCopies.Add,
        (text, sensitive) => TextCopies.Add((text, sensitive)),
        Opened.Add);
    }
  }

  private static string NewId() => Guid.NewGuid().ToString();

  private static BitwardenItem Login(string id, int reprompt = 0) => new()
  {
    Id = id,
    Name = "GitHub",
    Type = BitwardenItemType.Login,
    Reprompt = reprompt,
    FolderId = "f1",
    OrganizationId = "o1",
    Username = "octocat",
    Password = ProtectedSecret.From("hunter2"),
    HasTotp = true,
    TotpSecret = ProtectedSecret.From(TotpSecret),
    Notes = ProtectedSecret.From("secret note"),
    CreationDate = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
    RevisionDate = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc),
    Uris =
    [
      new ItemUri("https://github.com/login", UriMatchType.Default),
      new ItemUri("javascript:alert(1)", UriMatchType.Default),
      new ItemUri("ftp://files.example.com", UriMatchType.Default),
    ],
    CustomFields = new Dictionary<string, CustomField>
    {
      ["password"] = new("visible-custom"),
      ["apiKey"] = new(null, ProtectedSecret.From("hidden-custom")),
    },
  };

  private static string Code(IpcException ex) => ex.Code;

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("not-a-guid")]
  [InlineData("../../etc")]
  public void InvalidId_IsRejected(string? id)
  {
    var h = new Harness();
    Assert.Equal(IpcErrors.Invalid, Code(Assert.Throws<IpcException>(() => h.Ops.GetItem(id))));
    Assert.Equal(IpcErrors.Invalid, Code(Assert.Throws<IpcException>(() => h.Ops.Reveal(id, ItemFields.Password))));
    Assert.Equal(IpcErrors.Invalid, Code(Assert.Throws<IpcException>(() => h.Ops.Copy(id, ItemFields.Password))));
  }

  [Fact]
  public void UnknownItem_IsNotFound()
  {
    var h = new Harness(Login(NewId()));
    Assert.Equal(IpcErrors.NotFound, Code(Assert.Throws<IpcException>(() => h.Ops.GetItem(NewId()))));
  }

  [Fact]
  public void LockedVault_IsReportedAsLocked()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    h.Service.ClearSession();
    Assert.Equal(IpcErrors.Locked, Code(Assert.Throws<IpcException>(() => h.Ops.GetItem(id))));
  }

  [Fact]
  public void GetItem_Login_IsSecretFree()
  {
    var id = NewId();
    var h = new Harness(Login(id));

    var dto = h.Ops.GetItem(id);
    var json = JsonSerializer.Serialize(dto, IpcJson.Default.ItemDto);

    Assert.DoesNotContain("hunter2", json, StringComparison.Ordinal);
    Assert.DoesNotContain(TotpSecret, json, StringComparison.Ordinal);
    Assert.DoesNotContain("secret note", json, StringComparison.Ordinal);
    Assert.DoesNotContain("hidden-custom", json, StringComparison.Ordinal);
    Assert.Equal("Work", dto.FolderName);
    Assert.Equal("Acme", dto.OrganizationName);
    Assert.False(dto.RepromptRequired);
    Assert.Equal(new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), dto.CreationDate);
    var password = Assert.Single(dto.Fields, f => f.Key == ItemFields.Password);
    Assert.True(password.IsSecret);
    Assert.Null(password.Value);
    Assert.Equal("octocat", Assert.Single(dto.Fields, f => f.Key == ItemFields.Username).Value);
    Assert.True(Assert.Single(dto.Fields, f => f.Key == ItemFields.Totp).IsSecret);
    Assert.True(Assert.Single(dto.Fields, f => f.Key == ItemFields.Notes).IsSecret);
    Assert.Equal([true, false, false], dto.Fields.Where(f => f.Key.StartsWith(ItemFields.UriPrefix, StringComparison.Ordinal)).Select(f => f.CanOpen));
    var custom = dto.Fields.Where(f => f.Key.StartsWith(ItemFields.CustomPrefix, StringComparison.Ordinal)).ToList();
    Assert.Equal(["password", "apiKey"], custom.Select(f => f.Label));
    Assert.Equal("visible-custom", custom[0].Value);
    Assert.True(custom[1].IsSecret);
    Assert.Null(custom[1].Value);
  }

  [Fact]
  public void MapFields_CoversEveryItemType()
  {
    var card = new BitwardenItem { Type = BitwardenItemType.Card, CardholderName = "Jane", CardBrand = "Visa", CardNumber = ProtectedSecret.From("4111111111111111"), CardExpMonth = "12", CardExpYear = "2030", CardCode = ProtectedSecret.From("123") };
    var identity = new BitwardenItem { Type = BitwardenItemType.Identity, IdentityFullName = "Jane Doe", IdentityUsername = "jd", IdentityCompany = "Acme", IdentityEmail = "j@x.io", IdentityPhone = "555", IdentityAddress = "1 Road\nTown" };
    var ssh = new BitwardenItem { Type = BitwardenItemType.SshKey, SshPrivateKey = ProtectedSecret.From("-----BEGIN KEY-----"), SshPublicKey = "ssh-ed25519 AAA", SshFingerprint = "SHA256:abc" };
    var note = new BitwardenItem { Type = BitwardenItemType.SecureNote, Notes = ProtectedSecret.From("note body") };

    var cardFields = CompanionOps.MapFields(card);
    Assert.Equal([ItemFields.CardholderName, ItemFields.CardBrand, ItemFields.CardNumber, ItemFields.CardExpiration, ItemFields.CardCode], cardFields.Select(f => f.Key));
    Assert.All(cardFields.Where(f => f.Key is ItemFields.CardNumber or ItemFields.CardCode), f => Assert.Null(f.Value));
    Assert.Equal("12 / 2030", cardFields.Single(f => f.Key == ItemFields.CardExpiration).Value);

    var identityFields = CompanionOps.MapFields(identity);
    Assert.Equal(6, identityFields.Count);
    Assert.True(identityFields.Single(f => f.Key == ItemFields.IdentityAddress).IsMultiline);

    var sshFields = CompanionOps.MapFields(ssh);
    var privateKey = sshFields.Single(f => f.Key == ItemFields.SshPrivateKey);
    Assert.True(privateKey.IsSecret);
    Assert.Null(privateKey.Value);
    Assert.Equal("SHA256:abc", sshFields.Single(f => f.Key == ItemFields.SshFingerprint).Value);

    var noteField = Assert.Single(CompanionOps.MapFields(note));
    Assert.Equal(ItemFields.Notes, noteField.Key);
    Assert.Null(noteField.Value);
  }

  [Fact]
  public void Reveal_ReturnsOnlyTheRequestedSecret()
  {
    var id = NewId();
    var h = new Harness(Login(id));

    Assert.Equal("hunter2", h.Ops.Reveal(id, ItemFields.Password));
    Assert.Equal("hidden-custom", h.Ops.Reveal(id, ItemFields.CustomPrefix + "1"));
    Assert.Equal(IpcErrors.Invalid, Code(Assert.Throws<IpcException>(() => h.Ops.Reveal(id, ItemFields.Username))));
    Assert.Equal(IpcErrors.Invalid, Code(Assert.Throws<IpcException>(() => h.Ops.Reveal(id, ItemFields.Totp))));
    Assert.Equal(IpcErrors.Invalid, Code(Assert.Throws<IpcException>(() => h.Ops.Reveal(id, "custom:9"))));
    Assert.Equal(IpcErrors.Invalid, Code(Assert.Throws<IpcException>(() => h.Ops.Reveal(id, "bogus"))));
  }

  [Fact]
  public void Copy_RoutesSecretsAndPlainTextToTheRightClipboardPath()
  {
    var id = NewId();
    var item = Login(id);
    var h = new Harness(item);

    h.Ops.Copy(id, ItemFields.Password);
    h.Ops.Copy(id, ItemFields.Username);
    h.Ops.Copy(id, ItemFields.UriPrefix + "0");
    h.Ops.Copy(id, ItemFields.CustomPrefix + "0");
    h.Ops.Copy(id, ItemFields.Totp);

    Assert.Same(item.Password, Assert.Single(h.SecretCopies));
    Assert.Equal(("octocat", false), h.TextCopies[0]);
    Assert.Equal(("https://github.com/login", false), h.TextCopies[1]);
    Assert.Equal(("visible-custom", false), h.TextCopies[2]);
    Assert.Matches("^[0-9]{6}$", h.TextCopies[3].Text);
    Assert.True(h.TextCopies[3].Sensitive);
  }

  [Fact]
  public void Copy_MissingValue_IsNotFound()
  {
    var id = NewId();
    var h = new Harness(new BitwardenItem { Id = id, Type = BitwardenItemType.Login });
    Assert.Equal(IpcErrors.NotFound, Code(Assert.Throws<IpcException>(() => h.Ops.Copy(id, ItemFields.Password))));
    Assert.Equal(IpcErrors.NotFound, Code(Assert.Throws<IpcException>(() => h.Ops.GetTotp(id))));
  }

  [Fact]
  public void GetTotp_ReturnsCodeRemainingAndPeriod()
  {
    var id = NewId();
    var h = new Harness(new BitwardenItem { Id = id, Type = BitwardenItemType.Login, TotpSecret = ProtectedSecret.From($"otpauth://totp/x?secret={TotpSecret}&period=60") });

    var totp = h.Ops.GetTotp(id);

    Assert.Matches("^[0-9]{6}$", totp.Code);
    Assert.Equal(60, totp.Period);
    Assert.InRange(totp.Remaining, 1, 60);
  }

  [Fact]
  public void OpenUri_AllowsOnlyHttpAndHttps()
  {
    var id = NewId();
    var h = new Harness(Login(id));

    h.Ops.OpenUri(id, 0);
    Assert.Equal(IpcErrors.Invalid, Code(Assert.Throws<IpcException>(() => h.Ops.OpenUri(id, 1))));
    Assert.Equal(IpcErrors.Invalid, Code(Assert.Throws<IpcException>(() => h.Ops.OpenUri(id, 2))));
    Assert.Equal(IpcErrors.Invalid, Code(Assert.Throws<IpcException>(() => h.Ops.OpenUri(id, 3))));
    Assert.Equal(IpcErrors.Invalid, Code(Assert.Throws<IpcException>(() => h.Ops.OpenUri(id, -1))));
    Assert.Equal(["https://github.com/login"], h.Opened);
  }

  [Fact]
  public async Task Reprompt_GatesEverySecretAndPlainOpUntilVerified()
  {
    var id = NewId();
    var h = new Harness(Login(id, reprompt: 1));

    var locked = h.Ops.GetItem(id);
    Assert.True(locked.RepromptRequired);
    Assert.Empty(locked.Fields);
    Assert.Equal(IpcErrors.Reprompt, Code(Assert.Throws<IpcException>(() => h.Ops.Reveal(id, ItemFields.Password))));
    Assert.Equal(IpcErrors.Reprompt, Code(Assert.Throws<IpcException>(() => h.Ops.Copy(id, ItemFields.Username))));
    Assert.Equal(IpcErrors.Reprompt, Code(Assert.Throws<IpcException>(() => h.Ops.GetTotp(id))));
    Assert.Equal(IpcErrors.Reprompt, Code(Assert.Throws<IpcException>(() => h.Ops.OpenUri(id, 0))));

    Assert.False((await h.Ops.VerifyRepromptAsync(id, "wrong")).Verified);
    Assert.False((await h.Ops.VerifyRepromptAsync(id, "")).Verified);
    Assert.False(h.Ops.HasGrant(id));

    Assert.True((await h.Ops.VerifyRepromptAsync(id, "correct")).Verified);
    Assert.True(h.Ops.HasGrant(id));
    Assert.False(h.Ops.GetItem(id).RepromptRequired);
    Assert.Equal("hunter2", h.Ops.Reveal(id, ItemFields.Password));
    h.Ops.Copy(id, ItemFields.Username);
    Assert.Equal(("octocat", true), Assert.Single(h.TextCopies));
  }

  [Fact]
  public async Task VerifyReprompt_ItemWithoutReprompt_IsAlwaysVerified()
  {
    var id = NewId();
    var h = new Harness(Login(id)) { ExpectedPassword = "never" };
    Assert.True((await h.Ops.VerifyRepromptAsync(id, "anything")).Verified);
    Assert.False(h.Ops.HasGrant(id));
  }

  [Fact]
  public async Task Grants_AreWipedWhenTheVaultLocks()
  {
    var id = NewId();
    var h = new Harness(Login(id, reprompt: 1));
    await h.Ops.VerifyRepromptAsync(id, "correct");

    h.Ops.ClearGrantsIfLocked();
    Assert.True(h.Ops.HasGrant(id));

    h.Service.ClearSession();
    h.Ops.ClearGrantsIfLocked();
    Assert.False(h.Ops.HasGrant(id));
  }

  [Fact]
  public async Task DisposedSecret_IsReportedAsLocked()
  {
    var id = NewId();
    var item = Login(id);
    var h = new Harness(item);
    item.Password!.Dispose();

    var ex = await Assert.ThrowsAsync<IpcException>(() => h.Ops.HandleAsync(Ops.Reveal, IpcJson.ToElement(new FieldRequest(id, ItemFields.Password), IpcJson.Default.FieldRequest), TestContext.Current.CancellationToken));
    Assert.Equal(IpcErrors.Locked, ex.Code);
  }

  [Fact]
  public async Task HandleAsync_OverPipeChannel_RoundTripsEveryOp()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    using var pair = new ChannelPair(h.Ops.HandleAsync);
    var ct = TestContext.Current.CancellationToken;

    var item = IpcJson.Read(await pair.Client.RequestAsync(Ops.GetItem, IpcJson.ToElement(new ItemRequest(id), IpcJson.Default.ItemRequest), ct), IpcJson.Default.ItemDto);
    var secret = IpcJson.Read(await pair.Client.RequestAsync(Ops.Reveal, IpcJson.ToElement(new FieldRequest(id, ItemFields.Password), IpcJson.Default.FieldRequest), ct), IpcJson.Default.SecretResponse);
    await pair.Client.RequestAsync(Ops.Copy, IpcJson.ToElement(new FieldRequest(id, ItemFields.Username), IpcJson.Default.FieldRequest), ct);
    var totp = IpcJson.Read(await pair.Client.RequestAsync(Ops.GetTotp, IpcJson.ToElement(new ItemRequest(id), IpcJson.Default.ItemRequest), ct), IpcJson.Default.TotpResponse);
    await pair.Client.RequestAsync(Ops.OpenUri, IpcJson.ToElement(new UriRequest(id, 0), IpcJson.Default.UriRequest), ct);
    var verify = IpcJson.Read(await pair.Client.RequestAsync(Ops.VerifyReprompt, IpcJson.ToElement(new RepromptRequest(id, "x"), IpcJson.Default.RepromptRequest), ct), IpcJson.Default.RepromptResponse);
    var unknown = await Assert.ThrowsAsync<IpcException>(() => pair.Client.RequestAsync("runBw", null, ct));
    var badPayload = await Assert.ThrowsAsync<IpcException>(() => pair.Client.RequestAsync(Ops.GetItem, null, ct));

    Assert.Equal("GitHub", item.Name);
    Assert.Equal("hunter2", secret.Value);
    Assert.Single(h.TextCopies);
    Assert.Equal(30, totp.Period);
    Assert.Single(h.Opened);
    Assert.True(verify.Verified);
    Assert.Equal(IpcErrors.Invalid, unknown.Code);
    Assert.Equal(IpcErrors.Invalid, badPayload.Code);
  }

  [Fact]
  public async Task PipeChannel_ConcurrentRequestsAndEventsAreCorrelated()
  {
    var events = new List<string>();
    using var pair = new ChannelPair(
      async (op, payload, ct) =>
      {
        await Task.Delay(Random.Shared.Next(1, 20), ct);
        return payload;
      },
      (op, _) => { lock (events) events.Add(op); });
    var ct = TestContext.Current.CancellationToken;

    var replies = await Task.WhenAll(Enumerable.Range(0, 50).Select(i =>
      pair.Client.RequestAsync("echo", IpcJson.ToElement(new ItemRequest(i.ToString(System.Globalization.CultureInfo.InvariantCulture)), IpcJson.Default.ItemRequest), ct)));
    await pair.Server.SendEventAsync("ping");
    await WaitForAsync(() => { lock (events) return events.Count == 1; });

    Assert.Equal(Enumerable.Range(0, 50).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)), replies.Select(r => IpcJson.Read(r, IpcJson.Default.ItemRequest).Id));
  }

  [Fact]
  public async Task PipeChannel_Eof_FailsPendingRequestsAndEndsTheLoop()
  {
    var pair = new ChannelPair(NeverAnswers);
    var pending = pair.Client.RequestAsync("slow", null, TestContext.Current.CancellationToken);

    pair.Server.Dispose();

    await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    await pair.ClientLoop.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    Assert.True(pair.Client.IsClosed);
    await Assert.ThrowsAsync<IOException>(() => pair.Client.RequestAsync("after", null, TestContext.Current.CancellationToken));
    pair.Dispose();
  }

  private static async Task<JsonElement?> NeverAnswers(string op, JsonElement? payload, CancellationToken cancellationToken)
  {
    await Task.Delay(Timeout.Infinite, cancellationToken);
    return null;
  }

  [Fact]
  public async Task PipeChannel_CancelledRequest_Throws()
  {
    using var pair = new ChannelPair(NeverAnswers);
    using var cts = new CancellationTokenSource();
    var pending = pair.Client.RequestAsync("never", null, cts.Token);
    await cts.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
  }

  private static async Task WaitForAsync(Func<bool> condition)
  {
    for (var i = 0; i < 200 && !condition(); i++)
      await Task.Delay(25);
    Assert.True(condition());
  }

  private sealed class ChannelPair : IDisposable
  {
    public ChannelPair(IpcRequestHandler serverHandler, Action<string, JsonElement?>? clientEvents = null)
    {
      var toServer = new AnonymousPipeServerStream(PipeDirection.Out);
      var fromServer = new AnonymousPipeServerStream(PipeDirection.In);
      var serverIn = new AnonymousPipeClientStream(PipeDirection.In, toServer.ClientSafePipeHandle);
      var serverOut = new AnonymousPipeClientStream(PipeDirection.Out, fromServer.ClientSafePipeHandle);
      Client = new PipeChannel(fromServer, toServer, (_, _, _) => throw new IpcException(IpcErrors.Invalid), clientEvents);
      Server = new PipeChannel(serverIn, serverOut, serverHandler);
      ClientLoop = Task.Run(Client.RunAsync);
      _ = Task.Run(Server.RunAsync);
    }

    public PipeChannel Client { get; }
    public PipeChannel Server { get; }
    public Task ClientLoop { get; }

    public void Dispose()
    {
      Server.Dispose();
      Client.Dispose();
    }
  }
}
