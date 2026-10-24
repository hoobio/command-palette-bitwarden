using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Pages;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class CompanionWriteOpsTests
{
  private const string Revision = "2025-01-02T00:00:00.000Z";
  private static readonly DateTime RevisionDate = new(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
  private static readonly string OrgId = Guid.NewGuid().ToString();
  private static readonly string CollectionId = Guid.NewGuid().ToString();

  private sealed class FakeVault
  {
    private readonly List<(string Args, FakeCliProcess Process)> _pending = [];
    private int _tick;

    public Dictionary<string, JsonObject> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ProcessStartInfo> Started { get; } = [];
    public List<string> Stdin { get; } = [];
    public Func<string, JsonObject, JsonObject>? OnWrite { get; set; }
    public string? WriteOutput { get; set; }
    public string? WriteError { get; set; }
    public string? EchoOutput { get; set; }
    public Action? OnSync { get; set; }
    public Func<string, FakeCliProcess?>? Override { get; set; }

    public FakeCliProcess Create(ProcessStartInfo psi)
    {
      lock (this)
      {
        Flush();
        Started.Add(psi);
        var args = psi.Arguments;
        if (Override?.Invoke(args) is { } overridden)
          return overridden;
        if (args == "sync")
        {
          OnSync?.Invoke();
          return new FakeCliProcess("Syncing complete.\n");
        }
        if (args.StartsWith("get item ", StringComparison.Ordinal))
          return new FakeCliProcess(Items.TryGetValue(args[9..], out var item) ? item.ToJsonString() + "\n" : "Not found.\n");

        var process = new FakeCliProcess(WriteOutput ?? EchoOutput ?? "", WriteError ?? "");
        _pending.Add((args, process));
        return process;
      }
    }

    private void Flush()
    {
      foreach (var (args, process) in _pending)
      {
        var stdin = process.GetStdinContent();
        if (stdin.Length > 0)
          Stdin.Add(stdin);
        var parts = args.Split(' ');
        var body = stdin.Length > 0 ? JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(stdin))) : null;
        var stamp = RevisionDate.AddMinutes(++_tick).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        if (WriteOutput is not null || WriteError is not null)
          continue;
        switch (parts[0])
        {
          case "edit" when parts[1] == "item":
            var edited = (JsonObject)body!;
            edited["revisionDate"] = stamp;
            Items[parts[2]] = OnWrite?.Invoke(args, edited) ?? edited;
            break;
          case "edit" when parts[1] == "item-collections":
            Items[parts[2]]["collectionIds"] = body!.DeepClone();
            break;
          case "move":
            Items[parts[1]]["organizationId"] = parts[2];
            Items[parts[1]]["collectionIds"] = body!.DeepClone();
            break;
          case "delete" when parts.Length > 3:
            Items.Remove(parts[2]);
            break;
          case "delete":
            Items[parts[2]]["deletedDate"] = stamp;
            break;
          case "archive":
            Items[parts[2]]["archivedDate"] = stamp;
            break;
        }

        if (parts.Length > 2 && Items.TryGetValue(parts[0] == "move" ? parts[1] : parts[2], out var touched))
          touched["revisionDate"] = stamp;
      }

      _pending.Clear();
    }

    public int Writes => Started.Count(p => BitwardenCliService.IsDataWrite(p.Arguments));
  }

  private sealed class Harness
  {
    public FakeVault Vault { get; } = new();
    public BitwardenCliService Service { get; }
    public CompanionOps Ops { get; }
    public List<(string Text, bool Sensitive)> TextCopies { get; } = [];
    public int CacheUpdates { get; private set; }

    public Harness(params JsonObject[] items)
    {
      foreach (var item in items)
        Vault.Items[item["id"]!.GetValue<string>()] = item;
      Service = new BitwardenCliService(processFactory: Vault.Create);
      Service.LoadTestData(BitwardenCliService.ParseItems(new JsonArray([.. items.Select(i => (JsonNode?)i.DeepClone())]).ToJsonString()), []);
      Service.SetSession("session-key");
      Service.CacheUpdated += () => CacheUpdates++;
      Ops = new CompanionOps(Service, verifyPassword: p => Task.FromResult(p == "correct"), copyText: (t, s) => TextCopies.Add((t, s)),
        loadCollections: () => Task.FromResult<List<VaultCollection>?>([]));
    }

    public Task<JsonElement?> Handle<T>(string op, T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
      Ops.HandleAsync(op, IpcJson.ToElement(payload, info), TestContext.Current.CancellationToken);
  }

  private static JsonObject Login(string id, int reprompt = 0) => JsonNode.Parse($$"""
    {
      "object": "item", "id": "{{id}}", "organizationId": null, "folderId": null, "type": 1, "reprompt": {{reprompt}},
      "name": "GitHub", "notes": "old note", "favorite": false,
      "fields": [{ "name": "pin", "value": "1234", "type": 1, "linkedId": null }, { "name": "user", "value": null, "type": 3, "linkedId": 100 }],
      "login": {
        "uris": [{ "match": null, "uri": "https://github.com" }], "username": "octocat", "password": "hunter2", "totp": null, "passwordRevisionDate": null,
        "fido2Credentials": [{ "credentialId": "cred-1", "keyType": "public-key", "keyAlgorithm": "ECDSA", "keyCurve": "P-256", "keyValue": "PASSKEY-SECRET",
          "rpId": "github.com", "userHandle": "dXNlcg", "userName": "octocat", "counter": "0", "rpName": "GitHub", "userDisplayName": "Octo",
          "discoverable": "true", "creationDate": "2025-01-01T00:00:00.000Z" }]
      },
      "collectionIds": [], "revisionDate": "{{Revision}}", "creationDate": "2025-01-01T00:00:00.000Z", "deletedDate": null,
      "passwordHistory": [{ "lastUsedDate": "2024-12-01T00:00:00.000Z", "password": "older-pass" }, { "lastUsedDate": "2024-11-01T00:00:00.000Z", "password": "oldest-pass" }],
      "attachments": [{ "id": "att1", "fileName": "a.txt" }]
    }
    """)!.AsObject();

  private static EditableItem Edited(EditableItem item) => item with
  {
    Name = "GitHub work",
    Notes = "new note",
    Favorite = true,
    Login = item.Login! with { Password = "rotated-secret", Uris = [new UriEdit("https://github.com/login", 3), new UriEdit("gist.github.com")] },
    Fields = [.. item.Fields, new CustomFieldEdit("enabled", "true", 2)],
  };

  private static string NewId() => Guid.NewGuid().ToString();

  [Fact]
  public async Task Edit_SendsBase64JsonOnStdin_AndNeverInTheArguments()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var editable = await h.Ops.GetEditableItemAsync(id);
    var item = editable with
    {
      Name = "New login",
      Fields = [new CustomFieldEdit("api", "field-secret", 1)],
      Login = editable.Login! with { Password = "brand-new-secret", Totp = "JBSWY3DPEHPK3PXP" },
    };

    IpcJson.Read(await h.Handle(Ops.UpdateItem, item, IpcJson.Default.EditableItem), IpcJson.Default.SavedResponse);

    var edit = Assert.Single(h.Vault.Started, p => p.Arguments.StartsWith("edit", StringComparison.Ordinal));
    Assert.Equal($"edit item {id}", edit.Arguments);
    Assert.Empty(edit.ArgumentList);
    Assert.True(edit.RedirectStandardInput);
    foreach (var psi in h.Vault.Started)
    {
      foreach (var secret in new[] { "brand-new-secret", "field-secret", "JBSWY3DPEHPK3PXP", "{", "New login" })
        Assert.DoesNotContain(secret, psi.Arguments, StringComparison.Ordinal);
    }

    var sent = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Assert.Single(h.Vault.Stdin))))!;
    Assert.Equal("New login", sent["name"]!.GetValue<string>());
    Assert.Equal("brand-new-secret", sent["login"]!["password"]!.GetValue<string>());
    Assert.Equal("New login", h.Service.GetCachedItem(id)!.Name);
  }

  [Fact]
  public async Task Edit_KeepsPasskeysHistoryAndAttachments_AndUpdatesTheCache()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var original = Login(id);

    var editable = await h.Ops.GetEditableItemAsync(id);
    Assert.Equal(RevisionDate, editable.RevisionDate);
    Assert.Equal("hunter2", editable.Login!.Password);
    Assert.Equal(100, editable.Fields[1].LinkedId);
    var saved = await h.Ops.UpdateItemAsync(Edited(editable));

    var sent = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Assert.Single(h.Vault.Stdin))))!;
    Assert.True(JsonNode.DeepEquals(original["login"]!["fido2Credentials"], sent["login"]!["fido2Credentials"]));
    Assert.True(JsonNode.DeepEquals(original["passwordHistory"], sent["passwordHistory"]));
    Assert.True(JsonNode.DeepEquals(original["attachments"], sent["attachments"]));
    Assert.Equal("rotated-secret", sent["login"]!["password"]!.GetValue<string>());
    Assert.Equal(3, sent["login"]!["uris"]![0]!["match"]!.GetValue<int>());

    var cached = h.Service.GetCachedItem(id)!;
    Assert.Equal("GitHub work", cached.Name);
    Assert.True(cached.HasPasskey);
    Assert.Equal(saved.RevisionDate, cached.RevisionDate);
    Assert.Equal(1, h.CacheUpdates);
  }

  private static JsonObject CacheComplete(string id)
  {
    var item = Login(id);
    item["fields"] = JsonNode.Parse("""[{ "name": "pin", "value": "1234", "type": 1, "linkedId": null }, { "name": "env", "value": "prod", "type": 0, "linkedId": null }]""");
    item["login"]!["totp"] = "JBSWY3DPEHPK3PXP";
    item["login"]!["uris"]!.AsArray().Add(new JsonObject { ["match"] = 3, ["uri"] = "https://gist.github.com" });
    return item;
  }

  private static string Json(EditableItem item) => JsonSerializer.Serialize(item, IpcJson.Default.EditableItem);

  [Fact]
  public async Task Edit_FromACompleteCacheEntry_StartsNoBw_AndSavesOntoTheLiveItem()
  {
    var id = NewId();
    var h = new Harness(CacheComplete(id));

    var editable = await h.Ops.GetEditableItemAsync(id);

    Assert.Empty(h.Vault.Started);
    Assert.Equal(Json(CompanionOps.ToEditable(CacheComplete(id))), Json(editable));

    await h.Ops.UpdateItemAsync(Edited(editable));
    var sent = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Assert.Single(h.Vault.Stdin))))!;
    Assert.True(JsonNode.DeepEquals(Login(id)["login"]!["fido2Credentials"], sent["login"]!["fido2Credentials"]));
    Assert.True(JsonNode.DeepEquals(Login(id)["passwordHistory"], sent["passwordHistory"]));
    Assert.Equal("rotated-secret", sent["login"]!["password"]!.GetValue<string>());
  }

  [Fact]
  public async Task Edit_FromACacheEntryOlderThanTheVault_IsAConflict_AndWritesNothing()
  {
    var id = NewId();
    var h = new Harness(CacheComplete(id));
    var editable = await h.Ops.GetEditableItemAsync(id);
    h.Vault.Items[id]["revisionDate"] = "2025-02-01T00:00:00.000Z";

    Assert.Equal(IpcErrors.Conflict, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.UpdateItem, Edited(editable), IpcJson.Default.EditableItem))).Code);
    Assert.Equal(0, h.Vault.Writes);
    Assert.Equal(new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), h.Service.GetCachedItem(id)!.RevisionDate);

    await h.Ops.UpdateItemAsync(Edited(await h.Ops.GetEditableItemAsync(id)));
    Assert.Equal("GitHub work", h.Service.GetCachedItem(id)!.Name);
  }

  [Fact]
  public void EditableFromCache_OnlyWhenTheCacheKeepsEveryEditedValue()
  {
    static bool Check(Models.BitwardenItemType type, string fields, string uris = "[]") =>
      BitwardenCliService.IsEditableFromCache(type, JsonNode.Parse(fields), JsonNode.Parse($$"""{ "uris": {{uris}} }"""));

    Assert.True(Check(Models.BitwardenItemType.Login, """[{ "name": "a", "value": "1", "type": 0 }, { "name": "b", "value": "2", "type": 1 }]""", """[{ "uri": "x.com" }]"""));
    Assert.True(BitwardenCliService.IsEditableFromCache(Models.BitwardenItemType.SecureNote, null, null));
    Assert.False(Check(Models.BitwardenItemType.Identity, "[]"));
    Assert.False(Check(Models.BitwardenItemType.Login, """[{ "name": "a", "value": "true", "type": 2 }]"""));
    Assert.False(Check(Models.BitwardenItemType.Login, """[{ "name": "a", "value": null, "type": 3, "linkedId": 100 }]"""));
    Assert.False(Check(Models.BitwardenItemType.Login, """[{ "name": "a", "value": "", "type": 1 }]"""));
    Assert.False(Check(Models.BitwardenItemType.Login, """[{ "name": "", "value": "1", "type": 0 }]"""));
    Assert.False(Check(Models.BitwardenItemType.Login, """[{ "name": "a", "value": "1", "type": 0 }, { "name": "A", "value": "2", "type": 0 }]"""));
    Assert.False(Check(Models.BitwardenItemType.Login, "[]", """[{ "uri": "" }]"""));
    Assert.False(Check(Models.BitwardenItemType.Login, "[]", """[{ "uri": null }]"""));
  }

  [Fact]
  public async Task Edit_OfACardOrSshKeyFromTheCache_RevealsItsSecrets()
  {
    var card = NewId();
    var ssh = NewId();
    var h = new Harness(
      JsonNode.Parse($$"""{ "id": "{{card}}", "type": 3, "name": "Visa", "revisionDate": "{{Revision}}", "card": { "cardholderName": "Jane", "brand": "Visa", "number": "4111", "expMonth": "4", "expYear": "2030", "code": "123" } }""")!.AsObject(),
      JsonNode.Parse($$"""{ "id": "{{ssh}}", "type": 5, "name": "Key", "revisionDate": "{{Revision}}", "sshKey": { "privateKey": "PRIVATE", "publicKey": "ssh-ed25519 AAAA", "keyFingerprint": "SHA256:x" } }""")!.AsObject());

    var cardEdit = await h.Ops.GetEditableItemAsync(card);
    var sshEdit = await h.Ops.GetEditableItemAsync(ssh);

    Assert.Empty(h.Vault.Started);
    Assert.Equal(new CardEdit("Jane", "Visa", "4111", "4", "2030", "123"), cardEdit.Card);
    Assert.Equal(new SshKeyEdit("PRIVATE", "ssh-ed25519 AAAA", "SHA256:x"), sshEdit.SshKey);
  }

  [Fact]
  public async Task Edit_WithAStaleRevision_IsAConflict_AndWritesNothing()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var editable = await h.Ops.GetEditableItemAsync(id);
    h.Vault.Items[id]["revisionDate"] = "2025-02-01T00:00:00.000Z";

    var error = await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.UpdateItem, Edited(editable), IpcJson.Default.EditableItem));

    Assert.Equal(IpcErrors.Conflict, error.Code);
    Assert.Equal(0, h.Vault.Writes);
    Assert.Equal(1, h.CacheUpdates);
    Assert.Equal(new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), h.Service.GetCachedItem(id)!.RevisionDate);
  }

  [Fact]
  public async Task Edit_ServerOutOfDate_IsAConflict_AndSyncsBeforeReturning()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var editable = await h.Ops.GetEditableItemAsync(id);
    h.Vault.WriteError = "The cipher you are updating is out of date. Please save your work, sync your vault, and try again.";
    h.Vault.OnSync = () => h.Vault.Items[id]["name"] = "Renamed elsewhere";

    Assert.Equal(IpcErrors.Conflict, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.UpdateItem, Edited(editable), IpcJson.Default.EditableItem))).Code);
    Assert.Contains(h.Vault.Started, p => p.Arguments == "sync");
    Assert.Equal("Renamed elsewhere", h.Service.GetCachedItem(id)!.Name);
  }

  [Fact]
  public async Task Edit_WhoseSavedItemMentionsOutOfDate_StillSucceeds()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var editable = await h.Ops.GetEditableItemAsync(id);
    var echoed = Login(id);
    echoed["notes"] = "this password is out of date";
    h.Vault.EchoOutput = echoed.ToJsonString() + "\n";

    await h.Handle(Ops.UpdateItem, Edited(editable) with { Notes = "this password is out of date" }, IpcJson.Default.EditableItem);

    Assert.Equal(1, h.CacheUpdates);
  }

  [Fact]
  public async Task Edit_WhoseSavedFieldsDiffer_ReportsFailure()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var editable = await h.Ops.GetEditableItemAsync(id);
    h.Vault.OnWrite = (_, item) =>
    {
      item["login"]!["password"] = "hunter2";
      return item;
    };

    Assert.Equal(IpcErrors.Failed, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.UpdateItem, Edited(editable), IpcJson.Default.EditableItem))).Code);
    Assert.Equal(0, h.CacheUpdates);

    h.Vault.OnWrite = (_, item) =>
    {
      ((JsonObject)item["login"]!).Remove("fido2Credentials");
      return item;
    };
    editable = await h.Ops.GetEditableItemAsync(id);
    Assert.Equal(IpcErrors.Failed, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.UpdateItem, Edited(editable), IpcJson.Default.EditableItem))).Code);
  }

  [Fact]
  public async Task EveryOp_ValidatesItsIds_BeforeRunningBw()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var editable = await h.Ops.GetEditableItemAsync(id);
    h.Vault.Started.Clear();

    async Task Invalid(Func<Task> call) => Assert.Equal(IpcErrors.Invalid, (await Assert.ThrowsAsync<IpcException>(call)).Code);

    await Invalid(() => h.Handle(Ops.UpdateItem, editable with { Id = "not-a-guid" }, IpcJson.Default.EditableItem));
    await Invalid(() => h.Handle(Ops.UpdateItem, editable with { FolderId = "../x" }, IpcJson.Default.EditableItem));
    await Invalid(() => h.Handle(Ops.UpdateItem, editable with { RevisionDate = null }, IpcJson.Default.EditableItem));
    await Invalid(() => h.Handle(Ops.UpdateItem, editable with { Name = " " }, IpcJson.Default.EditableItem));
    await Invalid(() => h.Handle(Ops.GetEditableItem, new ItemRequest("x"), IpcJson.Default.ItemRequest));
    await Invalid(() => h.Handle(Ops.GetPasswordHistory, new ItemRequest("x; rm"), IpcJson.Default.ItemRequest));
    foreach (var op in new[] { Ops.DeleteItem, Ops.ArchiveItem, Ops.MoveItem, Ops.SetCollections })
      await Invalid(() => h.Handle(op, new WriteRequest("--permanent", RevisionDate, OrganizationId: OrgId, CollectionIds: [CollectionId]), IpcJson.Default.WriteRequest));
    await Invalid(() => h.Handle(Ops.MoveItem, new WriteRequest(id, RevisionDate, OrganizationId: "org", CollectionIds: [CollectionId]), IpcJson.Default.WriteRequest));
    await Invalid(() => h.Handle(Ops.MoveItem, new WriteRequest(id, RevisionDate, OrganizationId: OrgId, CollectionIds: ["c1"]), IpcJson.Default.WriteRequest));
    await Invalid(() => h.Handle(Ops.MoveItem, new WriteRequest(id, RevisionDate, OrganizationId: OrgId, CollectionIds: []), IpcJson.Default.WriteRequest));
    await Invalid(() => h.Handle(Ops.SetCollections, new WriteRequest(id, RevisionDate), IpcJson.Default.WriteRequest));
    await Invalid(() => h.Handle(Ops.SetCollections, new WriteRequest(id, RevisionDate, CollectionIds: ["bad"]), IpcJson.Default.WriteRequest));
    await Invalid(() => h.Handle("runBw", new WriteRequest(id, RevisionDate), IpcJson.Default.WriteRequest));
    Assert.Throws<ArgumentException>(() => BitwardenCliService.RequireId("item; whoami"));

    Assert.Empty(h.Vault.Started);
  }

  [Fact]
  public void Validate_RejectsBadUrisMonthsLinksAndIncompleteSshKeys()
  {
    var login = new EditableItem(null, 1, "x", null, null, [], false, false, null, null, [], Login: new LoginEdit(null, null, null, [new UriEdit("https://ok.example")]));
    CompanionOps.Validate(login);
    CompanionOps.Validate(login with { Login = login.Login! with { Uris = [new UriEdit("^https://.*$", 4)] } });

    void Invalid(EditableItem item) => Assert.Equal(IpcErrors.Invalid, Assert.Throws<IpcException>(() => CompanionOps.Validate(item)).Code);
    CompanionOps.Validate(login with { Login = login.Login! with { Uris = [new UriEdit("My router"), new UriEdit(null), new UriEdit("http://[bad")] } });
    CompanionOps.Validate(login with { Fields = [new CustomFieldEdit("", "v", 0)] });
    Invalid(login with { Login = login.Login! with { Uris = [new UriEdit("ok.example", 9)] } });
    Invalid(login with { Type = 3, Login = null, Card = new CardEdit(null, null, null, "13", null, null) });
    Invalid(login with { Fields = [new CustomFieldEdit("linked", null, 3, 300)] });
    Invalid(login with { Fields = [new CustomFieldEdit("text", "v", 0, 100)] });
    Invalid(login with { Type = 5, Login = null, SshKey = new SshKeyEdit("key", null, "fp") });
    Invalid(login with { Type = 9 });
    CompanionOps.Validate(login with { Type = 3, Login = null, Card = new CardEdit(null, null, null, "12", "2030", null), Fields = [new CustomFieldEdit("l", null, 3, 305)] });
  }

  [Fact]
  public async Task Reprompt_GatesEditableItemAndPasswordHistory()
  {
    RepromptPage.ClearGracePeriod();
    var id = NewId();
    var h = new Harness(Login(id, reprompt: 1));
    var payload = IpcJson.ToElement(new ItemRequest(id), IpcJson.Default.ItemRequest);

    Assert.Equal(IpcErrors.Reprompt, (await Assert.ThrowsAsync<IpcException>(() => h.Ops.HandleAsync(Ops.GetEditableItem, payload, TestContext.Current.CancellationToken))).Code);
    Assert.Equal(IpcErrors.Reprompt, (await Assert.ThrowsAsync<IpcException>(() => h.Ops.HandleAsync(Ops.GetPasswordHistory, payload, TestContext.Current.CancellationToken))).Code);
    Assert.Equal(IpcErrors.Reprompt, (await Assert.ThrowsAsync<IpcException>(() => h.Ops.CopyPasswordHistoryAsync(id, ItemFields.HistoryPrefix + "0", RevisionDate))).Code);
    Assert.DoesNotContain(h.Vault.Started, p => p.Arguments.StartsWith("get", StringComparison.Ordinal));

    RepromptPage.GracePeriodSeconds = 0;
    try
    {
      var verified = await h.Ops.VerifyRepromptAsync(id, "correct", Ops.GetEditableItem, payload);
      Assert.Equal("hunter2", IpcJson.Read(verified.Result, IpcJson.Default.EditableItem).Login!.Password);
      var history = await h.Ops.VerifyRepromptAsync(id, "correct", Ops.GetPasswordHistory, payload);
      Assert.Equal([new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 11, 1, 0, 0, 0, DateTimeKind.Utc)],
        IpcJson.Read(history.Result, IpcJson.Default.PasswordHistoryResponse).LastUsed);
      Assert.Equal(IpcErrors.Reprompt, (await Assert.ThrowsAsync<IpcException>(() => h.Ops.GetEditableItemAsync(id))).Code);

      await h.Ops.VerifyRepromptAsync(id, "correct", Ops.Copy, IpcJson.ToElement(new FieldRequest(id, ItemFields.HistoryPrefix + "1", RevisionDate), IpcJson.Default.FieldRequest));
      Assert.Equal(("oldest-pass", true), Assert.Single(h.TextCopies));
      await Assert.ThrowsAsync<IpcException>(() => h.Ops.CopyPasswordHistoryAsync(id, ItemFields.HistoryPrefix + "5", RevisionDate, verified: true));
    }
    finally
    {
      RepromptPage.GracePeriodSeconds = 60;
    }
  }

  [Fact]
  public async Task DeleteArchiveMoveAndCollections_VerifyStateAndUpdateTheCache()
  {
    var id = NewId();
    var trashed = NewId();
    var h = new Harness(Login(id), Login(trashed));
    async Task<SavedResponse?> Write(string op, WriteRequest request) =>
      await h.Handle(op, request, IpcJson.Default.WriteRequest) is { } reply ? IpcJson.Read(reply, IpcJson.Default.SavedResponse) : null;

    await Write(Ops.DeleteItem, new WriteRequest(trashed, RevisionDate));
    Assert.Null(h.Service.GetCachedItem(trashed));
    Assert.NotNull(h.Vault.Items[trashed]["deletedDate"]);

    var moved = await Write(Ops.MoveItem, new WriteRequest(id, RevisionDate, OrganizationId: OrgId, CollectionIds: [CollectionId]));
    Assert.Equal(OrgId, h.Service.GetCachedItem(id)!.OrganizationId);
    var other = NewId();
    var collected = await Write(Ops.SetCollections, new WriteRequest(id, moved!.RevisionDate, CollectionIds: [CollectionId, other]));
    Assert.Equal([CollectionId, other], h.Service.GetCachedItem(id)!.CollectionIds);

    Assert.Equal(IpcErrors.Conflict, (await Assert.ThrowsAsync<IpcException>(() => Write(Ops.ArchiveItem, new WriteRequest(id, moved.RevisionDate)))).Code);
    var archived = await Write(Ops.ArchiveItem, new WriteRequest(id, collected!.RevisionDate));
    Assert.Null(h.Service.GetCachedItem(id));
    Assert.NotNull(h.Vault.Items[id]["archivedDate"]);

    await Write(Ops.DeleteItem, new WriteRequest(id, archived!.RevisionDate, Permanent: true));
    Assert.False(h.Vault.Items.ContainsKey(id));
    Assert.Contains(h.Vault.Started, p => p.Arguments == $"delete item {id} --permanent");
    Assert.Equal(IpcErrors.NotFound, (await Assert.ThrowsAsync<IpcException>(() => Write(Ops.DeleteItem, new WriteRequest(id, RevisionDate)))).Code);
    Assert.True(h.Service.WriteVersion >= 5);
  }

  [Fact]
  public async Task ArchiveIgnoredByBw_ReportsFailure()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    h.Vault.WriteOutput = "Premium required.";

    Assert.Equal(IpcErrors.Failed, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.ArchiveItem, new WriteRequest(id, RevisionDate), IpcJson.Default.WriteRequest))).Code);
    Assert.Equal(IpcErrors.Failed, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.UpdateItem,
      Edited(CompanionOps.ToEditable(Login(id))), IpcJson.Default.EditableItem))).Code);
  }

  [Fact]
  public async Task Writes_WhenLocked_AreRejected()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    h.Service.ClearSession();

    Assert.Equal(IpcErrors.Locked, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.DeleteItem, new WriteRequest(id, RevisionDate), IpcJson.Default.WriteRequest))).Code);
    Assert.Equal(IpcErrors.Locked, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.UpdateItem,
      CompanionOps.ToEditable(Login(id)), IpcJson.Default.EditableItem))).Code);
  }

  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  [InlineData(4)]
  [InlineData(5)]
  public void EveryType_RoundTripsThroughBwJson(int type)
  {
    var item = new EditableItem(null, type, "Item", Guid.NewGuid().ToString(), null, [], true, true, "notes",
      null, [new CustomFieldEdit("t", "v", 0), new CustomFieldEdit("b", "true", 2), new CustomFieldEdit("", "unnamed", 0)],
      Login: type == 1 ? new LoginEdit("u", "p", "t", [new UriEdit("a.example", 0), new UriEdit("b.example"), new UriEdit("My router"), new UriEdit(null)]) : null,
      Card: type == 3 ? new CardEdit("Jane", "Visa", "4111", "04", "2030", "123") : null,
      Identity: type == 4 ? new IdentityEdit("Ms", "Jane", "Q", "Doe", "1 Road", "Flat 2", "Floor 3", "Leeds", "Yorks", "LS1", "GB", "Acme", "j@x", "01", "AB123", "jdoe", "P1", "L1") : null,
      SshKey: type == 5 ? new SshKeyEdit("PRIVATE", "ssh-ed25519 AAAA", "SHA256:x") : null);
    var node = new JsonObject { ["type"] = type };
    CompanionOps.Apply(node, item);
    node["revisionDate"] = Revision;

    var back = CompanionOps.ToEditable(node);

    Assert.Equal(JsonSerializer.Serialize(item with { RevisionDate = RevisionDate }, IpcJson.Default.EditableItem), JsonSerializer.Serialize(back, IpcJson.Default.EditableItem));
    Assert.True(BitwardenCliService.ContentMatches(node, (JsonObject)node.DeepClone()));
  }

  [Fact]
  public async Task Write_ThatTimesOut_IsNotStopped_ThenSyncs_AndReportsAnUnknownOutcome()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    h.Service.WriteTimeoutMs = 50;
    var hung = new FakeCliProcess(0) { ExitSignal = new TaskCompletionSource() };
    h.Vault.Override = args => args == $"edit item {id}" ? hung : null;

    var save = h.Handle(Ops.UpdateItem, Edited(CompanionOps.ToEditable(Login(id))), IpcJson.Default.EditableItem);
    await Task.Delay(300, TestContext.Current.CancellationToken);
    Assert.False(save.IsCompleted);
    Assert.False(hung.Killed);

    hung.StdoutStream.Complete();
    hung.StderrStream.Complete();
    hung.ExitSignal.SetResult();

    string? code = null;
    try
    {
      await save;
    }
    catch (IpcException ex)
    {
      code = ex.Code;
    }

    Assert.Equal(IpcErrors.Unknown, code);
    Assert.False(hung.Killed);
    Assert.Single(h.Vault.Started, p => p.Arguments == $"edit item {id}");
    Assert.Contains(h.Vault.Started, p => p.Arguments == "sync");
  }

  [Fact]
  public async Task Reprompt_GatesEveryWriteOp_EvenWithNoGracePeriod()
  {
    RepromptPage.ClearGracePeriod();
    RepromptPage.GracePeriodSeconds = 0;
    try
    {
      var id = NewId();
      var h = new Harness(Login(id, reprompt: 1));
      var editable = CompanionOps.ToEditable(Login(id, reprompt: 1));

      async Task Gated(string op, JsonElement payload) =>
        Assert.Equal(IpcErrors.Reprompt, (await Assert.ThrowsAsync<IpcException>(() => h.Ops.HandleAsync(op, payload, TestContext.Current.CancellationToken))).Code);
      JsonElement Write(WriteRequest request) => IpcJson.ToElement(request, IpcJson.Default.WriteRequest);

      await Gated(Ops.UpdateItem, IpcJson.ToElement(Edited(editable), IpcJson.Default.EditableItem));
      await Gated(Ops.DeleteItem, Write(new WriteRequest(id, RevisionDate)));
      await Gated(Ops.DeleteItem, Write(new WriteRequest(id, RevisionDate, Permanent: true)));
      await Gated(Ops.ArchiveItem, Write(new WriteRequest(id, RevisionDate)));
      await Gated(Ops.MoveItem, Write(new WriteRequest(id, RevisionDate, OrganizationId: OrgId, CollectionIds: [CollectionId])));
      await Gated(Ops.SetCollections, Write(new WriteRequest(id, RevisionDate, CollectionIds: [CollectionId])));
      Assert.Equal(0, h.Vault.Writes);

      Assert.False((await h.Ops.VerifyRepromptAsync(id, "wrong", Ops.ArchiveItem, Write(new WriteRequest(id, RevisionDate)))).Verified);
      Assert.Equal(0, h.Vault.Writes);
      RepromptPage.ClearGracePeriod();

      var verified = await h.Ops.VerifyRepromptAsync(id, "correct", Ops.UpdateItem, IpcJson.ToElement(Edited(editable), IpcJson.Default.EditableItem));
      Assert.True(verified.Verified);
      var saved = IpcJson.Read(verified.Result, IpcJson.Default.SavedResponse);
      Assert.Equal("GitHub work", h.Service.GetCachedItem(id)!.Name);
      await Gated(Ops.ArchiveItem, Write(new WriteRequest(id, saved.RevisionDate)));
    }
    finally
    {
      RepromptPage.GracePeriodSeconds = 60;
      RepromptPage.ClearGracePeriod();
    }
  }

  [Fact]
  public async Task PermanentDelete_CountsAsDoneOnlyOnNotFound()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var gets = 0;
    h.Vault.Override = args => args == $"get item {id}" && ++gets == 2 ? new FakeCliProcess("", "Request failed: socket hang up\n") : null;

    Assert.Equal(IpcErrors.Failed, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.DeleteItem, new WriteRequest(id, RevisionDate, Permanent: true), IpcJson.Default.WriteRequest))).Code);
    Assert.NotNull(h.Service.GetCachedItem(id));
  }

  [Fact]
  public async Task Refresh_ThatStartedBeforeAWrite_DoesNotBringBackTheOldItem()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var stale = Login(id).ToJsonString();
    var held = new FakeCliProcess(0);
    var lists = 0;
    h.Vault.Override = args => args switch
    {
      "list items" => ++lists == 1 ? held : new FakeCliProcess("[" + h.Vault.Items[id].ToJsonString() + "]\n"),
      "list folders" or "list organizations" => new FakeCliProcess("[]\n"),
      _ => null,
    };

    var refresh = h.Service.RefreshCacheAsync();
    await h.Ops.UpdateItemAsync(Edited(await h.Ops.GetEditableItemAsync(id)));
    held.StdoutStream.Enqueue("[" + stale + "]\n");
    held.StdoutStream.Complete();
    held.StderrStream.Complete();
    await refresh;

    Assert.Equal("GitHub work", h.Service.GetCachedItem(id)!.Name);
    Assert.Equal(2, lists);
  }

  [Fact]
  public async Task CollectionWrites_RequireAtLeastOneCollection()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    h.Vault.Started.Clear();

    Assert.Equal(IpcErrors.Invalid, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.SetCollections, new WriteRequest(id, RevisionDate, CollectionIds: []), IpcJson.Default.WriteRequest))).Code);
    Assert.Empty(h.Vault.Started);
  }

  [Fact]
  public void IsDataWrite_CoversTheMutatingVerbsOnly()
  {
    foreach (var args in new[] { "sync", "create item", "edit item x", "edit item-collections x", "delete item x", "restore item x", "move x y", "archive item x" })
      Assert.True(BitwardenCliService.IsDataWrite(args), args);
    foreach (var args in new[] { "get item x", "list items", "status", "lock", "unlock --raw" })
      Assert.False(BitwardenCliService.IsDataWrite(args), args);
  }
}
