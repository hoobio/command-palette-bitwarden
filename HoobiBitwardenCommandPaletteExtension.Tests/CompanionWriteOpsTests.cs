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
    private readonly List<(string Args, FakeCliProcess Process, string? Id)> _pending = [];
    private int _tick;

    public Dictionary<string, JsonObject> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ProcessStartInfo> Started { get; } = [];
    public List<string> Stdin { get; } = [];
    public Func<string, JsonObject, JsonObject>? OnWrite { get; set; }
    public string? WriteOutput { get; set; }

    public FakeCliProcess Create(ProcessStartInfo psi)
    {
      lock (this)
      {
        Flush();
        Started.Add(psi);
        var args = psi.Arguments;
        if (args.StartsWith("get item ", StringComparison.Ordinal))
          return new FakeCliProcess(Items.TryGetValue(args[9..], out var item) ? item.ToJsonString() + "\n" : "Not found.\n");
        if (args == "create item")
        {
          var id = Guid.NewGuid().ToString();
          var created = new FakeCliProcess(WriteOutput ?? $"{{\"id\":\"{id}\"}}\n");
          _pending.Add((args, created, id));
          return created;
        }

        var process = new FakeCliProcess(WriteOutput ?? "");
        _pending.Add((args, process, null));
        return process;
      }
    }

    private void Flush()
    {
      foreach (var (args, process, createdId) in _pending)
      {
        var stdin = process.GetStdinContent();
        if (stdin.Length > 0)
          Stdin.Add(stdin);
        var parts = args.Split(' ');
        var body = stdin.Length > 0 ? JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(stdin))) : null;
        var stamp = RevisionDate.AddMinutes(++_tick).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        if (WriteOutput is not null)
          continue;
        switch (parts[0])
        {
          case "create":
            var created = (JsonObject)body!;
            created["id"] = createdId;
            created["revisionDate"] = stamp;
            Items[createdId!] = OnWrite?.Invoke(args, created) ?? created;
            break;
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
          case "restore":
            Items[parts[2]][Items[parts[2]]["deletedDate"] is null ? "archivedDate" : "deletedDate"] = null;
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
        loadExtras: () => Task.FromResult<VaultExtras?>(new VaultExtras([], [])));
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
  public async Task Create_SendsBase64JsonOnStdin_AndNeverInTheArguments()
  {
    var h = new Harness();
    var item = new EditableItem(null, 1, "New login", null, null, [], false, false, "a note", null, [new CustomFieldEdit("api", "field-secret", 1)],
      Login: new LoginEdit("jane", "brand-new-secret", "JBSWY3DPEHPK3PXP", [new UriEdit("example.com")]));

    var saved = IpcJson.Read(await h.Handle(Ops.CreateItem, item, IpcJson.Default.EditableItem), IpcJson.Default.SavedResponse);

    var create = Assert.Single(h.Vault.Started, p => p.Arguments.StartsWith("create", StringComparison.Ordinal));
    Assert.Equal("create item", create.Arguments);
    Assert.Empty(create.ArgumentList);
    Assert.True(create.RedirectStandardInput);
    foreach (var psi in h.Vault.Started)
    {
      foreach (var secret in new[] { "brand-new-secret", "field-secret", "JBSWY3DPEHPK3PXP", "{", "New login" })
        Assert.DoesNotContain(secret, psi.Arguments, StringComparison.Ordinal);
    }

    var sent = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Assert.Single(h.Vault.Stdin))))!;
    Assert.Equal("New login", sent["name"]!.GetValue<string>());
    Assert.Equal("brand-new-secret", sent["login"]!["password"]!.GetValue<string>());
    Assert.Equal(saved.Id, h.Service.GetCachedItem(saved.Id)!.Id);
    Assert.Equal(1, h.CacheUpdates);
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
    Assert.Equal(0, h.CacheUpdates);
  }

  [Fact]
  public async Task Edit_ServerOutOfDate_IsAConflict()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var editable = await h.Ops.GetEditableItemAsync(id);
    h.Vault.WriteOutput = "The cipher you are updating is out of date. Please save your work, sync your vault, and try again.";

    Assert.Equal(IpcErrors.Conflict, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.UpdateItem, Edited(editable), IpcJson.Default.EditableItem))).Code);
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
    await Invalid(() => h.Handle(Ops.CreateItem, editable with { Name = " " }, IpcJson.Default.EditableItem));
    await Invalid(() => h.Handle(Ops.GetEditableItem, new ItemRequest("x"), IpcJson.Default.ItemRequest));
    await Invalid(() => h.Handle(Ops.GetPasswordHistory, new ItemRequest("x; rm"), IpcJson.Default.ItemRequest));
    foreach (var op in new[] { Ops.DeleteItem, Ops.RestoreItem, Ops.ArchiveItem, Ops.MoveItem, Ops.SetCollections })
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
    CompanionOps.Validate(login with { Login = login.Login! with { Uris = [new UriEdit("^https://.*$", ItemRules.RegexMatch)] } });

    void Invalid(EditableItem item) => Assert.Equal(IpcErrors.Invalid, Assert.Throws<IpcException>(() => CompanionOps.Validate(item)).Code);
    Invalid(login with { Login = login.Login! with { Uris = [new UriEdit("http://[bad")] } });
    Invalid(login with { Login = login.Login! with { Uris = [new UriEdit("ok.example", 9)] } });
    Invalid(login with { Type = 3, Login = null, Card = new CardEdit(null, null, null, "13", null, null) });
    Invalid(login with { Fields = [new CustomFieldEdit("linked", null, 3, 300)] });
    Invalid(login with { Fields = [new CustomFieldEdit("text", "v", 0, 100)] });
    Invalid(login with { Fields = [new CustomFieldEdit("", "v", 0)] });
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
  public async Task DeleteRestoreArchiveMoveAndCollections_VerifyStateAndUpdateTheCache()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    async Task<SavedResponse?> Write(string op, WriteRequest request) =>
      await h.Handle(op, request, IpcJson.Default.WriteRequest) is { } reply ? IpcJson.Read(reply, IpcJson.Default.SavedResponse) : null;

    var deleted = await Write(Ops.DeleteItem, new WriteRequest(id, RevisionDate));
    Assert.Null(h.Service.GetCachedItem(id));
    Assert.NotNull(h.Vault.Items[id]["deletedDate"]);
    var restored = await Write(Ops.RestoreItem, new WriteRequest(id, deleted!.RevisionDate));
    Assert.Equal(restored!.RevisionDate, h.Ops.GetItem(id).RevisionDate);
    Assert.Equal(IpcErrors.Conflict, (await Assert.ThrowsAsync<IpcException>(() => Write(Ops.ArchiveItem, new WriteRequest(id, deleted.RevisionDate)))).Code);
    var archived = await Write(Ops.ArchiveItem, new WriteRequest(id, restored.RevisionDate));
    Assert.Null(h.Service.GetCachedItem(id));
    var unarchived = await Write(Ops.RestoreItem, new WriteRequest(id, archived!.RevisionDate));
    Assert.Null(h.Vault.Items[id]["archivedDate"]);

    var moved = await Write(Ops.MoveItem, new WriteRequest(id, unarchived!.RevisionDate, OrganizationId: OrgId, CollectionIds: [CollectionId]));
    Assert.Equal(OrgId, h.Service.GetCachedItem(id)!.OrganizationId);
    var other = NewId();
    await Write(Ops.SetCollections, new WriteRequest(id, moved!.RevisionDate, CollectionIds: [CollectionId, other]));
    Assert.Equal([CollectionId, other], h.Service.GetCachedItem(id)!.CollectionIds);

    await Write(Ops.DeleteItem, new WriteRequest(id, h.Ops.GetItem(id).RevisionDate, Permanent: true));
    Assert.False(h.Vault.Items.ContainsKey(id));
    Assert.Null(h.Service.GetCachedItem(id));
    Assert.Contains(h.Vault.Started, p => p.Arguments == $"delete item {id} --permanent");
    Assert.Equal(IpcErrors.NotFound, (await Assert.ThrowsAsync<IpcException>(() => Write(Ops.RestoreItem, new WriteRequest(id, RevisionDate)))).Code);
    Assert.True(h.Service.WriteVersion >= 7);
  }

  [Fact]
  public async Task ArchiveIgnoredByBw_ReportsFailure()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    h.Vault.WriteOutput = "Premium required.";

    Assert.Equal(IpcErrors.Failed, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.ArchiveItem, new WriteRequest(id, RevisionDate), IpcJson.Default.WriteRequest))).Code);
    Assert.Equal(IpcErrors.Failed, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.CreateItem,
      new EditableItem(null, 2, "Note", null, null, [], false, false, "n", null, []), IpcJson.Default.EditableItem))).Code);
  }

  [Fact]
  public async Task Writes_WhenLocked_AreRejected()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    h.Service.ClearSession();

    Assert.Equal(IpcErrors.Locked, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.DeleteItem, new WriteRequest(id, RevisionDate), IpcJson.Default.WriteRequest))).Code);
    Assert.Equal(IpcErrors.Locked, (await Assert.ThrowsAsync<IpcException>(() => h.Handle(Ops.CreateItem,
      new EditableItem(null, 2, "Note", null, null, [], false, false, null, null, []), IpcJson.Default.EditableItem))).Code);
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
      null, [new CustomFieldEdit("t", "v", 0), new CustomFieldEdit("b", "true", 2)],
      Login: type == 1 ? new LoginEdit("u", "p", "t", [new UriEdit("a.example", 0), new UriEdit("b.example")]) : null,
      Card: type == 3 ? new CardEdit("Jane", "Visa", "4111", "04", "2030", "123") : null,
      Identity: type == 4 ? new IdentityEdit("Ms", "Jane", "Q", "Doe", "1 Road", "Flat 2", "Floor 3", "Leeds", "Yorks", "LS1", "GB", "Acme", "j@x", "01", "AB123", "jdoe", "P1", "L1") : null,
      SshKey: type == 5 ? new SshKeyEdit("PRIVATE", "ssh-ed25519 AAAA", "SHA256:x") : null);
    var node = CompanionOps.NewItemJson(type);
    CompanionOps.Apply(node, item);
    node["revisionDate"] = Revision;

    var back = CompanionOps.ToEditable(node);

    Assert.Equal(JsonSerializer.Serialize(item with { RevisionDate = RevisionDate }, IpcJson.Default.EditableItem), JsonSerializer.Serialize(back, IpcJson.Default.EditableItem));
    Assert.True(BitwardenCliService.ContentMatches(node, (JsonObject)node.DeepClone()));
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
