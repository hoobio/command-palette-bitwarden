using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Pages;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public sealed class CompanionVaultToolsOpsTests : IDisposable
{
  private const string Revision = "2025-01-02T00:00:00.000Z";
  private const string FileBytes = "attachment-bytes-sentinel-qz";
  private static readonly DateTime RevisionDate = new(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);
  private readonly string _dir = Directory.CreateTempSubdirectory("bw-companion-").FullName;

  public void Dispose() => Directory.Delete(_dir, recursive: true);

  private sealed class FakeBw
  {
    private readonly List<(string Args, FakeCliProcess Process)> _pending = [];
    private int _tick;

    public Dictionary<string, JsonObject> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Args { get; } = [];
    public List<string> Stdin { get; } = [];
    public string? AttachmentError { get; set; }
    public Func<string, FakeCliProcess?>? Override { get; set; }
    public Action<JsonObject>? OnEdit { get; set; }

    public FakeCliProcess Create(ProcessStartInfo psi)
    {
      lock (this)
      {
        Flush();
        var args = psi.Arguments;
        Args.Add(args);
        if (Override?.Invoke(args) is { } overridden)
          return overridden;
        var parts = args.Split(' ');
        switch (parts[0])
        {
          case "sync":
            return new FakeCliProcess("Syncing complete.\n");
          case "list":
            return new FakeCliProcess((parts[1] switch
            {
              "items" => new JsonArray([.. Items.Values.Select(i => (JsonNode?)i.DeepClone())]),
              "folders" => new JsonArray([.. Folders.Select(f => (JsonNode?)new JsonObject { ["id"] = f.Key, ["name"] = f.Value })]),
              _ => new JsonArray(),
            }).ToJsonString() + "\n");
          case "get" when parts[1] == "item":
            return new FakeCliProcess(Items.TryGetValue(parts[2], out var item) ? item.ToJsonString() + "\n" : "Not found.\n");
          case "get" when parts[1] == "attachment":
            if (AttachmentError is { } downloadError)
              return new FakeCliProcess(stderr: downloadError);
            var output = Unquote(args[(args.IndexOf("--output ", StringComparison.Ordinal) + 9)..]);
            File.WriteAllText(output, FileBytes);
            return new FakeCliProcess($"Saved {output}\n");
          case "create" when parts[1] == "attachment":
            if (AttachmentError is { } uploadError)
              return new FakeCliProcess(stderr: uploadError);
            var file = Unquote(args[(args.IndexOf("--file ", StringComparison.Ordinal) + 7)..args.IndexOf(" --itemid", StringComparison.Ordinal)]);
            var target = Items[parts[^1]];
            ((JsonArray)target["attachments"]!).Add(new JsonObject { ["id"] = "att" + ++_tick, ["fileName"] = Path.GetFileName(file), ["size"] = "100", ["sizeName"] = "100 Bytes", ["url"] = "https://files.example/secret-url" });
            target["revisionDate"] = Stamp();
            return new FakeCliProcess(target.ToJsonString() + "\n");
          case "delete" when parts[1] == "attachment":
            var owner = Items[parts[^1]];
            var list = (JsonArray)owner["attachments"]!;
            list.Remove(list.First(a => a!["id"]!.GetValue<string>() == parts[2]));
            owner["revisionDate"] = Stamp();
            return new FakeCliProcess(string.Empty);
        }

        var process = new FakeCliProcess("{}\n");
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
        if ((parts[0], parts[1]) == ("edit", "item"))
        {
          var body = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(stdin)))!.AsObject();
          body["revisionDate"] = Stamp();
          OnEdit?.Invoke(body);
          Items[parts[2]] = body;
        }
      }

      _pending.Clear();
    }

    private string Stamp() => RevisionDate.AddMinutes(++_tick).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string Unquote(string value) => value.Trim().Trim('"');
  }

  private sealed class Harness
  {
    public FakeBw Bw { get; } = new();
    public BitwardenCliService Service { get; }
    public CompanionOps Ops { get; }
    public List<(string Text, bool Sensitive)> TextCopies { get; } = [];
    public List<string> SecretCopies { get; } = [];

    public Harness(params JsonObject[] items)
    {
      foreach (var item in items)
        Bw.Items[item["id"]!.GetValue<string>()] = item;
      Service = new BitwardenCliService(processFactory: Bw.Create);
      Load();
      Service.SetSession("session-key");
      Ops = new CompanionOps(Service, verifyPassword: p => Task.FromResult(p == "correct"), copySecret: s => SecretCopies.Add(s.Reveal()),
        copyText: (t, s) => TextCopies.Add((t, s)), loadCollections: () => Task.FromResult<List<VaultCollection>?>([]));
    }

    public void Load() => Service.LoadTestData(
      BitwardenCliService.ParseItems(new JsonArray([.. Bw.Items.Values.Select(i => (JsonNode?)i.DeepClone())]).ToJsonString()),
      new Dictionary<string, string>(Bw.Folders));

    public Task<JsonElement?> Handle<T>(string op, T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
      Ops.HandleAsync(op, IpcJson.ToElement(payload, info), TestContext.Current.CancellationToken);

    public async Task<string> Fails<T>(string op, T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
      (await Assert.ThrowsAsync<IpcException>(() => Handle(op, payload, info))).Code;
  }

  private static string NewId() => Guid.NewGuid().ToString();

  private static JsonObject Login(string id, int reprompt = 0, string? folderId = null) => JsonNode.Parse($$"""
    {
      "object": "item", "id": "{{id}}", "organizationId": null, "folderId": {{(folderId is null ? "null" : $"\"{folderId}\"")}}, "type": 1, "reprompt": {{reprompt}},
      "name": "GitHub", "notes": null, "favorite": false, "fields": [],
      "login": { "uris": [], "username": "octocat", "password": "hunter2", "totp": null },
      "collectionIds": [], "revisionDate": "{{Revision}}", "deletedDate": null, "passwordHistory": [],
      "attachments": [{ "id": "att1", "fileName": "report.pdf", "size": "4096", "sizeName": "4 KB", "url": "https://files.example/secret-url" }]
    }
    """)!.AsObject();

  [Theory]
  [InlineData(@"C:\Users\jane\report.pdf", true)]
  [InlineData(@"D:\a b\c [1].txt", true)]
  [InlineData(@"\\server\share\a.txt", false)]
  [InlineData(@"\\?\C:\a.txt", false)]
  [InlineData(@"\\.\COM1", false)]
  [InlineData(@"//server/share/a.txt", false)]
  [InlineData(@"relative\a.txt", false)]
  [InlineData(@"-rf", false)]
  [InlineData(@"--output=C:\a.txt", false)]
  [InlineData(@"C:\dir\", false)]
  [InlineData(@"C:\a\..\b.txt", false)]
  [InlineData(@"C:\a.txt:stream", false)]
  [InlineData(@"C:\x\NUL.txt", false)]
  [InlineData(@"C:\x\a"" --itemid x.txt", false)]
  [InlineData(@"C:/a.txt", false)]
  [InlineData(@"C:\a.txt ", false)]
  [InlineData("", false)]
  [InlineData(null, false)]
  public void RequireLocalPath_AcceptsOnlyPlainLocalPaths(string? path, bool valid)
  {
    if (valid)
      Assert.Equal(path, CompanionOps.RequireLocalPath(path));
    else
      Assert.Equal(IpcErrors.Invalid, Assert.Throws<IpcException>(() => CompanionOps.RequireLocalPath(path)).Code);
  }

  [Fact]
  public async Task GetItem_ListsAttachmentsWithoutTheirDownloadUrl()
  {
    var id = NewId();
    var h = new Harness(Login(id));

    var json = JsonSerializer.Serialize(h.Ops.GetItem(id), IpcJson.Default.ItemDto);

    Assert.Equal(new AttachmentDto("att1", "report.pdf", "4 KB"), Assert.Single(h.Ops.GetItem(id).Attachments!));
    Assert.DoesNotContain("secret-url", json, StringComparison.Ordinal);
    await Task.CompletedTask;
  }

  [Fact]
  public async Task Download_HasBwWriteTheFile_AndNoBytesCrossThePipe()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var path = Path.Combine(_dir, "report [1].pdf");

    var reply = await h.Handle(Ops.DownloadAttachment, new AttachmentRequest(id, RevisionDate, "att1", path), IpcJson.Default.AttachmentRequest);

    Assert.Null(reply);
    Assert.Equal(FileBytes, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    Assert.Equal($"get attachment att1 --itemid {id} --output \"{path}\"", Assert.Single(h.Bw.Args));
  }

  [Fact]
  public async Task Upload_ArgvHoldsOnlyTheVerbTheGuidAndTheQuotedPath()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var path = Path.Combine(_dir, "notes.txt");
    await File.WriteAllTextAsync(path, FileBytes, TestContext.Current.CancellationToken);

    var reply = await h.Handle(Ops.UploadAttachment, new AttachmentRequest(id, RevisionDate, Path: path), IpcJson.Default.AttachmentRequest);

    var saved = IpcJson.Read(reply, IpcJson.Default.SavedResponse);
    Assert.Equal(id, saved.Id);
    Assert.DoesNotContain(FileBytes, reply!.Value.GetRawText(), StringComparison.Ordinal);
    Assert.Contains($"create attachment --file \"{path}\" --itemid {id}", h.Bw.Args);
    Assert.Empty(h.Bw.Stdin);
    Assert.Equal(["report.pdf", "notes.txt"], h.Service.GetCachedItem(id)!.Attachments.Select(a => a.FileName));
  }

  [Fact]
  public async Task DeleteAttachment_RemovesIt_AndChecksTheIdAgainstTheItem()
  {
    var id = NewId();
    var h = new Harness(Login(id));

    Assert.Equal(IpcErrors.Invalid, await h.Fails(Ops.DeleteAttachment, new AttachmentRequest(id, RevisionDate, "../att1"), IpcJson.Default.AttachmentRequest));
    Assert.Equal(IpcErrors.Invalid, await h.Fails(Ops.DeleteAttachment, new AttachmentRequest(id, RevisionDate, "att1 --itemid x"), IpcJson.Default.AttachmentRequest));
    Assert.Equal(IpcErrors.NotFound, await h.Fails(Ops.DeleteAttachment, new AttachmentRequest(id, RevisionDate, "att9"), IpcJson.Default.AttachmentRequest));
    Assert.Empty(h.Bw.Args);

    await h.Handle(Ops.DeleteAttachment, new AttachmentRequest(id, RevisionDate, "att1"), IpcJson.Default.AttachmentRequest);

    Assert.Contains($"delete attachment att1 --itemid {id}", h.Bw.Args);
    Assert.Empty(h.Service.GetCachedItem(id)!.Attachments);
  }

  [Fact]
  public async Task AttachmentOps_AreRepromptGated()
  {
    RepromptPage.ClearGracePeriod();
    var id = NewId();
    var h = new Harness(Login(id, reprompt: 1));
    var path = Path.Combine(_dir, "up.txt");
    await File.WriteAllTextAsync(path, "x", TestContext.Current.CancellationToken);
    var download = new AttachmentRequest(id, RevisionDate, "att1", Path.Combine(_dir, "down.pdf"));

    Assert.Equal(IpcErrors.Reprompt, await h.Fails(Ops.DownloadAttachment, download, IpcJson.Default.AttachmentRequest));
    Assert.Equal(IpcErrors.Reprompt, await h.Fails(Ops.UploadAttachment, new AttachmentRequest(id, RevisionDate, Path: path), IpcJson.Default.AttachmentRequest));
    Assert.Equal(IpcErrors.Reprompt, await h.Fails(Ops.DeleteAttachment, new AttachmentRequest(id, RevisionDate, "att1"), IpcJson.Default.AttachmentRequest));
    Assert.DoesNotContain(h.Bw.Args, a => a.Contains("attachment", StringComparison.Ordinal));
    Assert.Null(h.Ops.GetItem(id).Attachments);

    RepromptPage.GracePeriodSeconds = 0;
    try
    {
      Assert.False((await h.Ops.VerifyRepromptAsync(id, "wrong", Ops.DownloadAttachment, IpcJson.ToElement(download, IpcJson.Default.AttachmentRequest))).Verified);
      Assert.True((await h.Ops.VerifyRepromptAsync(id, "correct", Ops.DownloadAttachment, IpcJson.ToElement(download, IpcJson.Default.AttachmentRequest))).Verified);
      Assert.True(File.Exists(download.Path));
    }
    finally
    {
      RepromptPage.GracePeriodSeconds = 60;
      RepromptPage.ClearGracePeriod();
    }
  }

  [Theory]
  [InlineData("Premium status is required to use this feature.", IpcErrors.Premium)]
  [InlineData("You must have premium status to use attachments.", IpcErrors.Premium)]
  [InlineData("Max file size is 500 MB.", IpcErrors.TooLarge)]
  [InlineData("Not enough storage available.", IpcErrors.NoStorage)]
  [InlineData("Something else broke.", IpcErrors.Failed)]
  public async Task BwAttachmentErrors_BecomeTypedErrors(string stderr, string code)
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var path = Path.Combine(_dir, "up.txt");
    await File.WriteAllTextAsync(path, "x", TestContext.Current.CancellationToken);
    h.Bw.AttachmentError = stderr;

    Assert.Equal(code, await h.Fails(Ops.UploadAttachment, new AttachmentRequest(id, RevisionDate, Path: path), IpcJson.Default.AttachmentRequest));
    Assert.Equal(code, await h.Fails(Ops.DownloadAttachment, new AttachmentRequest(id, RevisionDate, "att1", Path.Combine(_dir, "d.pdf")), IpcJson.Default.AttachmentRequest));
  }

  [Fact]
  public async Task Upload_RejectsMissingAndOversizedFiles_Locally()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var big = Path.Combine(_dir, "big.bin");
    await using (var stream = File.Create(big))
      stream.SetLength(BitwardenCliService.MaxAttachmentBytes + 1);

    Assert.Equal(IpcErrors.NotFound, await h.Fails(Ops.UploadAttachment, new AttachmentRequest(id, RevisionDate, Path: Path.Combine(_dir, "missing.txt")), IpcJson.Default.AttachmentRequest));
    Assert.Equal(IpcErrors.TooLarge, await h.Fails(Ops.UploadAttachment, new AttachmentRequest(id, RevisionDate, Path: big), IpcJson.Default.AttachmentRequest));
    Assert.Equal(IpcErrors.Invalid, await h.Fails(Ops.DownloadAttachment, new AttachmentRequest(id, RevisionDate, "att1", Path.Combine(_dir, "nope", "a.pdf")), IpcJson.Default.AttachmentRequest));
    Assert.Empty(h.Bw.Args);
  }

  [Fact]
  public void RequireGenerated_RejectsValuesThatDoNotMatchTheOptions()
  {
    Assert.Equal("Abcdefghij0123456789", CompanionOps.RequireGenerated(new GeneratorOptions(), "Abcdefghij0123456789"));
    Assert.Equal(IpcErrors.Failed, Assert.Throws<IpcException>(() => CompanionOps.RequireGenerated(new GeneratorOptions(), "short")).Code);
    Assert.Equal(IpcErrors.Failed, Assert.Throws<IpcException>(() => CompanionOps.RequireGenerated(new GeneratorOptions(GeneratorKinds.Passphrase), "two words")).Code);
    Assert.Equal("two words", CompanionOps.RequireGenerated(new GeneratorOptions(GeneratorKinds.Passphrase, Separator: " "), "two words"));
  }

  [Fact]
  public async Task Generate_RejectsOptionsItCannotMeet()
  {
    var h = new Harness();

    Assert.Equal(IpcErrors.Invalid, await h.Fails(Ops.Generate, new GeneratorOptions(Length: 4), IpcJson.Default.GeneratorOptions));
    Assert.Equal(IpcErrors.Failed, await h.Fails(Ops.Generate, new GeneratorOptions(Length: 5, Special: true, MinNumber: 9), IpcJson.Default.GeneratorOptions));
    Assert.Empty(h.Ops.ListGeneratorHistory().Entries);
  }

  [Fact]
  public async Task Generate_ReturnsTheValue_AndKeepsItInMaskedHistory()
  {
    var h = new Harness();

    var generated = IpcJson.Read(await h.Handle(Ops.Generate, new GeneratorOptions(), IpcJson.Default.GeneratorOptions), IpcJson.Default.GeneratedResponse);

    Assert.Equal(20, generated.Value.Length);
    Assert.Empty(h.Bw.Args);
    var history = IpcJson.Read(await h.Ops.HandleAsync(Ops.ListGeneratorHistory, null, TestContext.Current.CancellationToken), IpcJson.Default.GeneratorHistoryResponse);
    var entry = Assert.Single(history.Entries);
    Assert.Equal((generated.EntryId, GeneratorKinds.Password), (entry.Id, entry.Kind));
    Assert.DoesNotContain(generated.Value, JsonSerializer.Serialize(history, IpcJson.Default.GeneratorHistoryResponse), StringComparison.Ordinal);

    await h.Handle(Ops.CopyGenerated, new HistoryEntryRequest(entry.Id), IpcJson.Default.HistoryEntryRequest);
    Assert.Equal(generated.Value, Assert.Single(h.SecretCopies));
  }

  [Fact]
  public async Task History_KeepsTheLatestFifty_AndIsWipedOnLock()
  {
    var h = new Harness();
    var ids = new List<long>();
    for (var i = 0; i < CompanionOps.HistoryLimit + 5; i++)
      ids.Add(IpcJson.Read(await h.Handle(Ops.Generate, new GeneratorOptions(), IpcJson.Default.GeneratorOptions), IpcJson.Default.GeneratedResponse).EntryId);

    var entries = h.Ops.ListGeneratorHistory().Entries;
    Assert.Equal(CompanionOps.HistoryLimit, entries.Count);
    Assert.Equal(ids[^1], entries[0].Id);
    Assert.DoesNotContain(entries, e => e.Id == ids[0]);
    Assert.Equal(IpcErrors.NotFound, await h.Fails(Ops.CopyGenerated, new HistoryEntryRequest(ids[0]), IpcJson.Default.HistoryEntryRequest));

    h.Service.ClearSession();
    h.Ops.RaiseIfLocked();
    Assert.Equal(IpcErrors.Locked, Assert.Throws<IpcException>(() => h.Ops.ListGeneratorHistory()).Code);
    h.Service.SetSession("session-key");
    Assert.Empty(h.Ops.ListGeneratorHistory().Entries);
    Assert.Equal(IpcErrors.NotFound, await h.Fails(Ops.CopyGenerated, new HistoryEntryRequest(ids[^1]), IpcJson.Default.HistoryEntryRequest));
  }

  private static RotateRequest Rotate(string id) => new(id, RevisionDate, new GeneratorOptions());

  [Fact]
  public async Task Rotate_SavesThroughTheEditPath_ThenCopiesWithTheClearTimer()
  {
    var id = NewId();
    var h = new Harness(Login(id));

    var saved = IpcJson.Read(await h.Handle(Ops.RotatePassword, Rotate(id), IpcJson.Default.RotateRequest), IpcJson.Default.SavedResponse);

    Assert.Equal(id, saved.Id);
    Assert.Equal([$"get item {id}", $"edit item {id}", $"get item {id}"], h.Bw.Args);
    var sent = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Assert.Single(h.Bw.Stdin))))!;
    var password = sent["login"]!["password"]!.GetValue<string>();
    Assert.Equal(20, password.Length);
    Assert.Equal((password, true), Assert.Single(h.TextCopies));
    Assert.Single(h.Ops.ListGeneratorHistory().Entries);
    Assert.DoesNotContain(h.Bw.Args, a => a.Contains(password, StringComparison.Ordinal));
  }

  [Fact]
  public async Task Rotate_WithAGeneratedEntry_SavesThatExactValue_AndKeepsOneHistoryRow()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    var generated = IpcJson.Read(await h.Handle(Ops.Generate, new GeneratorOptions(), IpcJson.Default.GeneratorOptions), IpcJson.Default.GeneratedResponse);

    await h.Handle(Ops.RotatePassword, new RotateRequest(id, RevisionDate, EntryId: generated.EntryId), IpcJson.Default.RotateRequest);

    var sent = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Assert.Single(h.Bw.Stdin))))!;
    Assert.Equal(generated.Value, sent["login"]!["password"]!.GetValue<string>());
    Assert.Equal((generated.Value, true), Assert.Single(h.TextCopies));
    Assert.Single(h.Ops.ListGeneratorHistory().Entries);

    Assert.Equal(IpcErrors.NotFound, await h.Fails(Ops.RotatePassword, new RotateRequest(id, h.Ops.GetItem(id).RevisionDate, EntryId: generated.EntryId + 1), IpcJson.Default.RotateRequest));
    Assert.Equal(IpcErrors.Invalid, await h.Fails(Ops.RotatePassword, new RotateRequest(id, RevisionDate), IpcJson.Default.RotateRequest));
    Assert.Single(h.Bw.Stdin);
  }

  [Fact]
  public async Task Rotate_ThatBwDoesNotConfirm_CopiesNothing()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    h.Service.WriteTimeoutMs = 50;
    var hung = new FakeCliProcess(0) { ExitSignal = new TaskCompletionSource() };
    h.Bw.Override = args => args.StartsWith("edit item", StringComparison.Ordinal) ? hung : null;

    var rotate = h.Handle(Ops.RotatePassword, Rotate(id), IpcJson.Default.RotateRequest);
    await Task.Delay(300, TestContext.Current.CancellationToken);
    hung.StdoutStream.Complete();
    hung.StderrStream.Complete();
    hung.ExitSignal.SetResult();

    string? code = null;
    try
    {
      await rotate;
    }
    catch (IpcException ex)
    {
      code = ex.Code;
    }

    Assert.Equal(IpcErrors.Unknown, code);
    Assert.False(hung.Killed);
    Assert.Empty(h.TextCopies);
  }

  [Fact]
  public async Task Rotate_OnConflictOrMismatchedSave_CopiesNothing()
  {
    var id = NewId();
    var h = new Harness(Login(id));
    h.Bw.Items[id]["revisionDate"] = "2025-03-01T00:00:00.000Z";

    Assert.Equal(IpcErrors.Conflict, await h.Fails(Ops.RotatePassword, Rotate(id), IpcJson.Default.RotateRequest));
    Assert.DoesNotContain(h.Bw.Args, a => a.StartsWith("edit", StringComparison.Ordinal));

    h.Bw.Items[id]["revisionDate"] = Revision;
    h.Bw.OnEdit = body => body["login"]!["password"] = "something-else";
    Assert.Equal(IpcErrors.Failed, await h.Fails(Ops.RotatePassword, Rotate(id), IpcJson.Default.RotateRequest));
    Assert.Empty(h.TextCopies);
  }

  [Fact]
  public async Task Rotate_IsRepromptGated_AndLoginOnly()
  {
    RepromptPage.ClearGracePeriod();
    var id = NewId();
    var h = new Harness(Login(id, reprompt: 1));

    Assert.Equal(IpcErrors.Reprompt, await h.Fails(Ops.RotatePassword, Rotate(id), IpcJson.Default.RotateRequest));
    Assert.Empty(h.Bw.Args);
    Assert.Equal(IpcErrors.Invalid, await h.Fails(Ops.RotatePassword, Rotate(id) with { Options = new GeneratorOptions("username") }, IpcJson.Default.RotateRequest));

    RepromptPage.GracePeriodSeconds = 0;
    try
    {
      var result = await h.Ops.VerifyRepromptAsync(id, "correct", Ops.RotatePassword, IpcJson.ToElement(Rotate(id), IpcJson.Default.RotateRequest));
      Assert.Equal(id, IpcJson.Read(result.Result, IpcJson.Default.SavedResponse).Id);
      Assert.Single(h.TextCopies);
    }
    finally
    {
      RepromptPage.GracePeriodSeconds = 60;
      RepromptPage.ClearGracePeriod();
    }

    var note = NewId();
    var noteJson = Login(note);
    noteJson["type"] = 2;
    noteJson.Remove("login");
    var h2 = new Harness(noteJson);
    Assert.Equal(IpcErrors.Invalid, await h2.Fails(Ops.RotatePassword, Rotate(note), IpcJson.Default.RotateRequest));
  }
}
