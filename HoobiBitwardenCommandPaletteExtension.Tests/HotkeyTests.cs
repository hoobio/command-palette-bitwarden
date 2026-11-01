using System.IO;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Helpers;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class HotkeyTests : IDisposable
{
  private const int Ctrl = HotkeyModifiers.Ctrl;
  private const int Alt = HotkeyModifiers.Alt;
  private const int Shift = HotkeyModifiers.Shift;
  private const int C = 0x43;
  private const int O = 0x4F;

  private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bw_hotkeys_{Guid.NewGuid():N}");
  private readonly HotkeyStore _previous = HotkeyStore.Current;
  private readonly HotkeyStore _store;

  public HotkeyTests()
  {
    _store = new HotkeyStore(Path.Combine(_dir, "hotkeys.json"));
    HotkeyStore.Current = _store;
  }

  public void Dispose()
  {
    HotkeyStore.Current = _previous;
    if (Directory.Exists(_dir))
      Directory.Delete(_dir, recursive: true);
    GC.SuppressFinalize(this);
  }

  private string FilePath => Path.Combine(_dir, "hotkeys.json");

  [Theory]
  [InlineData(HotkeyIds.OpenWindow, "Ctrl+O")]
  [InlineData(HotkeyIds.OpenWebVault, "Ctrl+Shift+O")]
  [InlineData(HotkeyIds.LoginCopyUsername, "Ctrl+C")]
  [InlineData(HotkeyIds.LoginCopyPassword, "Ctrl+Shift+C")]
  [InlineData(HotkeyIds.LoginCopyOtp, "Ctrl+Alt+C")]
  [InlineData(HotkeyIds.LoginOpenBrowser, "Alt+Enter")]
  [InlineData(HotkeyIds.LoginRotatePassword, null)]
  [InlineData(HotkeyIds.NoteCopyNotes, "Ctrl+C")]
  [InlineData(HotkeyIds.CardCopyNumber, "Ctrl+C")]
  [InlineData(HotkeyIds.CardCopyCode, "Ctrl+Shift+C")]
  [InlineData(HotkeyIds.CardCopyCardholder, "Ctrl+Alt+C")]
  [InlineData(HotkeyIds.CardCopyExpiration, "Ctrl+E")]
  [InlineData(HotkeyIds.IdentityCopyEmail, "Ctrl+C")]
  [InlineData(HotkeyIds.IdentityCopyName, "Ctrl+Shift+C")]
  [InlineData(HotkeyIds.IdentityCopyPhone, "Ctrl+Alt+C")]
  [InlineData(HotkeyIds.IdentityCopyUsername, "Ctrl+U")]
  [InlineData(HotkeyIds.IdentityCopyAddress, "Ctrl+Shift+A")]
  [InlineData(HotkeyIds.SshCopyPublicKey, "Ctrl+C")]
  [InlineData(HotkeyIds.SshCopyFingerprint, "Ctrl+Shift+C")]
  [InlineData(HotkeyIds.SshOpenSession, "Alt+Enter")]
  public void Catalog_DefaultChords_MatchTheShippedShortcuts(string id, string? expected)
  {
    Assert.Equal(expected, HotkeyCatalog.Find(id)!.Default?.Display);
    Assert.Equal(expected, _store.ChordFor(id)?.Display);
  }

  [Fact]
  public void Catalog_HasEveryActionOnceAndNoDefaultConflictsPerItemType()
  {
    Assert.Equal(HotkeyCatalog.Actions.Length, HotkeyCatalog.Actions.Select(a => a.Id).Distinct().Count());
    Assert.Equal(20, HotkeyCatalog.Actions.Length);
    foreach (var (type, _) in HotkeyCatalog.Groups)
    {
      var chords = HotkeyCatalog.AvailableOn(type).Select(a => a.Default).OfType<HotkeyChord>().ToList();
      Assert.Equal(chords.Count, chords.Distinct().Count());
      Assert.True(HotkeyCatalog.IsItemType(type));
    }
  }

  [Theory]
  [InlineData(HotkeyItemTypes.All, HotkeyIds.OpenWindow)]
  [InlineData(HotkeyItemTypes.Login, HotkeyIds.LoginOpenBrowser)]
  [InlineData(HotkeyItemTypes.Card, HotkeyIds.OpenWindow)]
  [InlineData(HotkeyItemTypes.Identity, HotkeyIds.OpenWindow)]
  [InlineData(HotkeyItemTypes.Note, HotkeyIds.OpenWindow)]
  [InlineData(HotkeyItemTypes.Ssh, HotkeyIds.SshOpenSession)]
  public void EnterAction_DefaultsToTodaysBehaviour(string itemType, string expected)
  {
    Assert.Equal(expected, HotkeyCatalog.DefaultEnter(itemType));
    Assert.Equal(expected, _store.EnterFor(itemType));
  }

  [Theory]
  [InlineData(HotkeyItemTypes.Login, null, true, true, true, HotkeyIds.LoginOpenBrowser)]
  [InlineData(HotkeyItemTypes.Login, null, false, true, true, HotkeyIds.OpenWindow)]
  [InlineData(HotkeyItemTypes.Login, null, false, false, true, HotkeyIds.OpenWebVault)]
  [InlineData(HotkeyItemTypes.Ssh, null, true, true, true, HotkeyIds.SshOpenSession)]
  [InlineData(HotkeyItemTypes.Ssh, null, false, true, true, HotkeyIds.OpenWindow)]
  [InlineData(HotkeyItemTypes.Note, null, true, true, true, HotkeyIds.OpenWindow)]
  [InlineData(HotkeyItemTypes.Note, null, true, false, true, HotkeyIds.OpenWebVault)]
  [InlineData(HotkeyItemTypes.Login, HotkeyIds.LoginCopyPassword, true, true, true, HotkeyIds.LoginCopyPassword)]
  [InlineData(HotkeyItemTypes.Login, HotkeyIds.LoginCopyPassword, true, true, false, HotkeyIds.LoginOpenBrowser)]
  [InlineData(HotkeyItemTypes.Login, HotkeyIds.LoginCopyPassword, false, false, false, HotkeyIds.OpenWebVault)]
  public void ResolveEnter_FallsBackToTodaysDefaultWhenTheChoiceIsUnavailable(string itemType, string? chosen, bool hasTarget, bool companion, bool choiceAvailable, string expected)
  {
    bool Available(string id) => id switch
    {
      HotkeyIds.LoginOpenBrowser or HotkeyIds.SshOpenSession => hasTarget,
      HotkeyIds.OpenWindow => companion,
      HotkeyIds.OpenWebVault => true,
      _ => choiceAvailable,
    };

    Assert.Equal(expected, HotkeyCatalog.ResolveEnter(itemType, chosen, Available));
  }

  [Fact]
  public void EnterChoices_AreTheTypeActionsPlusTheAllItemActions()
  {
    Assert.Equal([HotkeyIds.OpenWindow, HotkeyIds.OpenWebVault], HotkeyCatalog.EnterChoices(HotkeyItemTypes.All).Select(a => a.Id));
    var login = HotkeyCatalog.EnterChoices(HotkeyItemTypes.Login).Select(a => a.Id).ToList();
    Assert.Contains(HotkeyIds.LoginCopyPassword, login);
    Assert.Contains(HotkeyIds.OpenWebVault, login);
    Assert.DoesNotContain(HotkeyIds.CardCopyNumber, login);
  }

  [Fact]
  public void SetChord_PersistsAcrossReloads_AndOnlyStoresOverrides()
  {
    Assert.Null(_store.SetChord(HotkeyIds.LoginCopyPassword, new HotkeyChord(0x50, Ctrl | Alt)));

    var reloaded = new HotkeyStore(FilePath);
    Assert.Equal("Ctrl+Alt+P", reloaded.ChordFor(HotkeyIds.LoginCopyPassword)?.Display);
    Assert.Equal("Ctrl+C", reloaded.ChordFor(HotkeyIds.LoginCopyUsername)?.Display);
    var json = File.ReadAllText(FilePath);
    Assert.Contains(HotkeyIds.LoginCopyPassword, json);
    Assert.DoesNotContain(HotkeyIds.LoginCopyUsername, json);
  }

  [Fact]
  public void SetChord_ToTheDefault_RemovesTheOverride()
  {
    _store.SetChord(HotkeyIds.LoginCopyPassword, new HotkeyChord(0x50, Ctrl));
    Assert.Null(_store.SetChord(HotkeyIds.LoginCopyPassword, HotkeyCatalog.Find(HotkeyIds.LoginCopyPassword)!.Default));

    Assert.DoesNotContain(HotkeyIds.LoginCopyPassword, File.ReadAllText(FilePath));
  }

  [Fact]
  public void SetChord_Null_UnbindsAndSurvivesReload()
  {
    Assert.Null(_store.SetChord(HotkeyIds.LoginCopyUsername, null));

    Assert.Null(_store.ChordFor(HotkeyIds.LoginCopyUsername));
    Assert.Null(new HotkeyStore(FilePath).ChordFor(HotkeyIds.LoginCopyUsername));
    Assert.Null(_store.SetChord(HotkeyIds.LoginRotatePassword, null));

    Assert.Null(_store.SetChord(HotkeyIds.LoginCopyUsername, HotkeyCatalog.Find(HotkeyIds.LoginCopyUsername)!.Default));
    Assert.Equal("Ctrl+C", _store.ChordFor(HotkeyIds.LoginCopyUsername)?.Display);
  }

  [Theory]
  [InlineData(C, 0, "Shortcuts must include Ctrl or Alt")]
  [InlineData(C, Shift, "Shortcuts must include Ctrl or Alt")]
  [InlineData(HotkeyKeys.Enter, 0, "Enter on its own is reserved for the Enter action")]
  [InlineData(HotkeyKeys.Enter, Shift, "Enter on its own is reserved for the Enter action")]
  [InlineData(0x11, Ctrl, "Choose a key as well as the modifiers")]
  [InlineData(0, Ctrl, "Choose a key as well as the modifiers")]
  [InlineData(C, 8 | Ctrl, "Choose a key as well as the modifiers")]
  public void Validate_RejectsChordsThatWouldFireWhileTyping(int key, int modifiers, string expected)
  {
    Assert.Equal(expected, _store.Validate(HotkeyIds.LoginCopyUsername, new HotkeyChord(key, modifiers)));
    Assert.Equal(expected, _store.SetChord(HotkeyIds.LoginCopyUsername, new HotkeyChord(key, modifiers)));
    Assert.Equal("Ctrl+C", _store.ChordFor(HotkeyIds.LoginCopyUsername)?.Display);
  }

  [Fact]
  public void Validate_AcceptsEnterWithAltOrCtrl()
  {
    Assert.Null(_store.Validate(HotkeyIds.LoginCopyUsername, new HotkeyChord(HotkeyKeys.Enter, Ctrl)));
    Assert.Null(_store.Validate(HotkeyIds.LoginCopyUsername, new HotkeyChord(HotkeyKeys.Enter, Alt | Shift)));
  }

  [Fact]
  public void Validate_UnknownAction_IsRejected()
  {
    Assert.Equal("Unknown action", _store.Validate("login.nope", null));
    Assert.Equal("Unknown action", _store.SetChord("login.nope", new HotkeyChord(C, Ctrl)));
    Assert.Null(_store.ChordFor("login.nope"));
  }

  [Fact]
  public void Validate_ConflictWithinTheSameItemType_NamesTheOtherAction()
  {
    var error = _store.SetChord(HotkeyIds.LoginCopyUsername, new HotkeyChord(C, Ctrl | Shift));

    Assert.Equal("Ctrl+Shift+C is already used by Copy Password (Login)", error);
    Assert.Equal("Ctrl+C", _store.ChordFor(HotkeyIds.LoginCopyUsername)?.Display);
  }

  [Fact]
  public void Validate_SameChordOnAnotherItemType_IsAllowed()
  {
    var chord = new HotkeyChord(0x4B, Ctrl);
    Assert.Null(_store.SetChord(HotkeyIds.CardCopyNumber, chord));
    Assert.Null(_store.SetChord(HotkeyIds.SshCopyPublicKey, chord));
    Assert.Null(_store.Validate(HotkeyIds.LoginCopyPassword, chord));
    Assert.Equal("Ctrl+K is already used by Copy Card Number (Card)", _store.Validate(HotkeyIds.CardCopyCode, chord));
  }

  [Fact]
  public void Validate_AllItemActionsConflictWithEveryItemType()
  {
    Assert.Equal("Ctrl+O is already used by Open in window", _store.Validate(HotkeyIds.SshCopyPublicKey, new HotkeyChord(O, Ctrl)));
    Assert.Equal("Ctrl+E is already used by Copy Expiration (Card)", _store.Validate(HotkeyIds.OpenWebVault, new HotkeyChord(0x45, Ctrl)));
    Assert.Null(_store.Validate(HotkeyIds.OpenWebVault, new HotkeyChord(O, Ctrl | Shift)));
  }

  [Fact]
  public void Validate_AFreedChordCanBeReused()
  {
    _store.SetChord(HotkeyIds.LoginCopyPassword, null);

    Assert.Null(_store.SetChord(HotkeyIds.LoginCopyUsername, new HotkeyChord(C, Ctrl | Shift)));
  }

  [Fact]
  public void SetEnter_PersistsAndFollowsAllItemsOnlyForTypesWithoutTheirOwnDefault()
  {
    Assert.Null(_store.SetEnter(HotkeyItemTypes.Login, HotkeyIds.LoginCopyPassword));
    Assert.Equal(HotkeyIds.LoginCopyPassword, new HotkeyStore(FilePath).EnterFor(HotkeyItemTypes.Login));

    Assert.Null(_store.SetEnter(HotkeyItemTypes.All, HotkeyIds.OpenWebVault));
    Assert.Equal(HotkeyIds.OpenWebVault, _store.EnterFor(HotkeyItemTypes.Card));
    Assert.Equal(HotkeyIds.LoginCopyPassword, _store.EnterFor(HotkeyItemTypes.Login));
    Assert.Equal(HotkeyIds.SshOpenSession, _store.EnterFor(HotkeyItemTypes.Ssh));

    Assert.Null(_store.SetEnter(HotkeyItemTypes.Card, HotkeyIds.OpenWindow));
    Assert.Equal(HotkeyIds.OpenWindow, _store.EnterFor(HotkeyItemTypes.Card));

    Assert.Null(_store.SetEnter(HotkeyItemTypes.Login, HotkeyIds.LoginOpenBrowser));
    Assert.Equal(HotkeyIds.LoginOpenBrowser, _store.EnterFor(HotkeyItemTypes.Login));
    Assert.DoesNotContain("\"login\"", File.ReadAllText(FilePath));
  }

  [Theory]
  [InlineData("login", "card.copyNumber")]
  [InlineData("all", "login.copyPassword")]
  [InlineData("nope", "all.openWindow")]
  [InlineData("login", "nope")]
  public void SetEnter_RejectsActionsOutsideTheItemType(string itemType, string actionId)
  {
    Assert.Equal("That action cannot be used for Enter", _store.SetEnter(itemType, actionId));
    if (HotkeyCatalog.IsItemType(itemType))
      Assert.Equal(HotkeyCatalog.DefaultEnter(itemType), _store.EnterFor(itemType));
  }

  [Fact]
  public void ResetAll_ClearsEveryOverride_AndRaisesChanged()
  {
    var changes = 0;
    _store.Changed += () => changes++;
    _store.SetChord(HotkeyIds.LoginCopyPassword, null);
    _store.SetEnter(HotkeyItemTypes.Login, HotkeyIds.LoginCopyPassword);

    Assert.True(_store.ResetAll());

    Assert.Equal(3, changes);
    Assert.Equal("Ctrl+Shift+C", _store.ChordFor(HotkeyIds.LoginCopyPassword)?.Display);
    Assert.Equal(HotkeyIds.LoginOpenBrowser, _store.EnterFor(HotkeyItemTypes.Login));
    Assert.Equal(HotkeyIds.LoginOpenBrowser, new HotkeyStore(FilePath).EnterFor(HotkeyItemTypes.Login));
  }

  [Fact]
  public void Changed_IsNotRaisedForRejectedChanges()
  {
    var changes = 0;
    _store.Changed += () => changes++;

    _store.SetChord(HotkeyIds.LoginCopyUsername, new HotkeyChord(C, 0));
    _store.SetEnter(HotkeyItemTypes.Login, "nope");

    Assert.Equal(0, changes);
  }

  [Theory]
  [InlineData("{ not json")]
  [InlineData("")]
  [InlineData("[]")]
  public void Load_CorruptFile_FallsBackToDefaults(string content)
  {
    Directory.CreateDirectory(_dir);
    File.WriteAllText(FilePath, content);

    var store = new HotkeyStore(FilePath);

    Assert.Equal("Ctrl+Shift+C", store.ChordFor(HotkeyIds.LoginCopyPassword)?.Display);
    Assert.Equal(HotkeyIds.LoginOpenBrowser, store.EnterFor(HotkeyItemTypes.Login));
    Assert.Null(store.SetChord(HotkeyIds.LoginCopyPassword, null));
  }

  [Fact]
  public void Load_MissingFile_UsesDefaults()
  {
    Assert.False(File.Exists(FilePath));
    Assert.Equal("Ctrl+O", _store.ChordFor(HotkeyIds.OpenWindow)?.Display);
  }

  [Fact]
  public void Load_DropsUnknownActionsAndInvalidChords()
  {
    Directory.CreateDirectory(_dir);
    File.WriteAllText(FilePath, """
      {
        "chords": {
          "login.nope": { "key": 67, "modifiers": 1 },
          "login.copyPassword": { "key": 67, "modifiers": 0 },
          "login.copyUsername": { "key": 80, "modifiers": 1 }
        },
        "enter": { "login": "card.copyNumber", "card": "card.copyNumber", "nope": "all.openWindow" }
      }
      """);

    var store = new HotkeyStore(FilePath);

    Assert.Equal("Ctrl+Shift+C", store.ChordFor(HotkeyIds.LoginCopyPassword)?.Display);
    Assert.Equal("Ctrl+P", store.ChordFor(HotkeyIds.LoginCopyUsername)?.Display);
    Assert.Equal(HotkeyIds.LoginOpenBrowser, store.EnterFor(HotkeyItemTypes.Login));
    Assert.Equal(HotkeyIds.CardCopyNumber, store.EnterFor(HotkeyItemTypes.Card));
  }

  [Fact]
  public void SetChord_UnwritableLocation_ReportsAnErrorWithoutRaisingChanged()
  {
    Directory.CreateDirectory(_dir);
    var blocked = Path.Combine(_dir, "blocked");
    File.WriteAllText(blocked, "file");
    var store = new HotkeyStore(Path.Combine(blocked, "hotkeys.json"));
    var changes = 0;
    store.Changed += () => changes++;

    Assert.Equal("Could not save the shortcuts", store.SetChord(HotkeyIds.LoginCopyPassword, null));
    Assert.Equal("Could not save the shortcuts", store.SetEnter(HotkeyItemTypes.Login, HotkeyIds.LoginCopyPassword));
    Assert.False(store.ResetAll());
    Assert.Equal(0, changes);
  }

  [Theory]
  [InlineData(0x41, "A")]
  [InlineData(0x35, "5")]
  [InlineData(0x60, "Num 0")]
  [InlineData(0x70, "F1")]
  [InlineData(0x87, "F24")]
  [InlineData(0x0D, "Enter")]
  [InlineData(0x21, "Page Up")]
  [InlineData(0xBC, ",")]
  [InlineData(0xE7, "Key 231")]
  public void HotkeyKeys_Name(int key, string expected) => Assert.Equal(expected, HotkeyKeys.Name(key));

  [Fact]
  public void HotkeyKeys_CapsOrderModifiersBeforeTheKey_AndDropModifierOnlyKeys()
  {
    Assert.Equal(["Ctrl", "Alt", "Shift", "C"], HotkeyKeys.Caps(C, Ctrl | Alt | Shift));
    Assert.Equal(["Ctrl"], HotkeyKeys.Caps(0x11, Ctrl));
    Assert.Empty(HotkeyKeys.Caps(0, 0));
    Assert.True(HotkeyKeys.IsKey(C));
    Assert.False(HotkeyKeys.IsKey(0x5B));
    Assert.False(HotkeyKeys.IsKey(255));
  }

  [Fact]
  public void BuildContextItems_UseTheStoredChords()
  {
    var item = new BitwardenItem
    {
      Id = "test-login",
      Type = BitwardenItemType.Login,
      Username = "user@test.com",
      Password = ProtectedSecret.From("secret"),
      Uris = [new ItemUri("https://example.com", UriMatchType.Default)],
    };

    var defaults = VaultItemHelper.BuildContextItems(item);
    Assert.Equal(["Copy Username", "Copy Password", "Open in Browser"], defaults.Select(ci => ci.Title));
    Assert.Equal((0x43, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift), (defaults[1].RequestedShortcut.Vkey, defaults[1].RequestedShortcut.Modifiers));

    _store.SetChord(HotkeyIds.LoginCopyPassword, new HotkeyChord(0x50, Ctrl | Alt));
    _store.SetChord(HotkeyIds.LoginOpenBrowser, null);
    var changed = VaultItemHelper.BuildContextItems(item);

    Assert.Equal((0x50, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Menu), (changed[1].RequestedShortcut.Vkey, changed[1].RequestedShortcut.Modifiers));
    Assert.Equal(0, changed[2].RequestedShortcut.Vkey);
  }

  [Fact]
  public void GetDefaultCommand_UsesTheChosenEnterAction_AndFallsBackWhenUnavailable()
  {
    BitwardenCliService.ResetStaticState();
    var login = new BitwardenItem
    {
      Id = "test-login",
      Type = BitwardenItemType.Login,
      Password = ProtectedSecret.From("secret"),
      Uris = [new ItemUri("https://example.com", UriMatchType.Default)],
    };
    var noPassword = new BitwardenItem
    {
      Id = "test-login-2",
      Type = BitwardenItemType.Login,
      Uris = [new ItemUri("https://example.com", UriMatchType.Default)],
    };
    Assert.NotEqual("Copy Password", VaultItemHelper.GetDefaultCommand(login).Name);

    _store.SetEnter(HotkeyItemTypes.Login, HotkeyIds.LoginCopyPassword);

    Assert.Equal("Copy Password", VaultItemHelper.GetDefaultCommand(login).Name);
    Assert.NotEqual("Copy Password", VaultItemHelper.GetDefaultCommand(noPassword).Name);
    Assert.Equal(VaultItemHelper.GetDefaultCommand(noPassword).Name, VaultItemHelper.BuildContextItems(noPassword).Single(ci => ci.Title == "Open in Browser").Command!.Name);
  }

  [Fact]
  public void GetDefaultCommand_ChosenCopyWithReprompt_UsesTheCopyRepromptFlow()
  {
    Pages.RepromptPage.ClearGracePeriod();
    var item = new BitwardenItem
    {
      Id = "test-protected",
      Type = BitwardenItemType.Login,
      Reprompt = 1,
      Password = ProtectedSecret.From("secret"),
    };
    _store.SetEnter(HotkeyItemTypes.Login, HotkeyIds.LoginCopyPassword);

    Assert.IsType<Pages.RepromptPage>(VaultItemHelper.GetDefaultCommand(item, new BitwardenCliService()));
  }
}

[Collection("SessionStore")]
public class CompanionHotkeyOpsTests : IDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bw_hotkey_ops_{Guid.NewGuid():N}");
  private readonly HotkeyStore _previous = HotkeyStore.Current;
  private readonly BitwardenSettingsManager _settings;
  private readonly CompanionOps _ops;

  public CompanionHotkeyOpsTests()
  {
    Directory.CreateDirectory(_dir);
    HotkeyStore.Current = new HotkeyStore(Path.Combine(_dir, "hotkeys.json"));
    _settings = new BitwardenSettingsManager(Path.Combine(_dir, "settings.json"));
    var service = new BitwardenCliService(_settings, new FakeProcessFactory().Create) { DataFileOverride = Path.Combine(_dir, "missing.json") };
    _ops = new CompanionOps(service);
  }

  public void Dispose()
  {
    _ops.Dispose();
    HotkeyStore.Current = _previous;
    Directory.Delete(_dir, recursive: true);
    GC.SuppressFinalize(this);
  }

  private async Task<HotkeysResponse> GetAsync() =>
    IpcJson.Read(await _ops.HandleAsync(Ops.GetHotkeys, null, CancellationToken.None), IpcJson.Default.HotkeysResponse);

  private async Task<HotkeyResponse> SetAsync(string actionId, ChordDto? chord) =>
    IpcJson.Read(await _ops.HandleAsync(Ops.SetHotkey, IpcJson.ToElement(new SetHotkeyRequest(actionId, chord), IpcJson.Default.SetHotkeyRequest), CancellationToken.None), IpcJson.Default.HotkeyResponse);

  [Fact]
  public async Task GetHotkeys_GroupsTheCatalogueByItemType_WithDefaultsAndCurrentValues()
  {
    var response = await GetAsync();

    Assert.Equal(["all", "login", "card", "identity", "note", "ssh"], response.Groups.Select(g => g.ItemType));
    Assert.Equal(["All items", "Login", "Card", "Identity", "Secure note", "SSH key"], response.Groups.Select(g => g.Title));
    var login = response.Groups.Single(g => g.ItemType == "login");
    Assert.Equal(("login.openBrowser", "login.openBrowser"), (login.DefaultEnter, login.CurrentEnter));
    Assert.Equal(["login.copyUsername", "login.copyPassword", "login.copyOtp", "login.openBrowser", "login.rotatePassword"], login.Actions.Select(a => a.Id));
    var password = login.Actions.Single(a => a.Id == "login.copyPassword");
    Assert.Equal(new ChordDto(0x43, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "Ctrl+Shift+C"), password.Default);
    Assert.Equal(password.Default, password.Current);
    Assert.Null(login.Actions.Single(a => a.Id == "login.rotatePassword").Current);
    Assert.Contains(login.EnterChoices, c => c.Value == "all.openWebVault" && c.Title == "View in Web Vault");
  }

  [Fact]
  public async Task SetHotkey_AppliesValidChords_AndReportsErrors()
  {
    var chord = new ChordDto(0x50, HotkeyModifiers.Ctrl, "ignored");
    Assert.Equal(new HotkeyResponse(true, null), await SetAsync("login.copyPassword", chord));
    var changed = (await GetAsync()).Groups.Single(g => g.ItemType == "login").Actions.Single(a => a.Id == "login.copyPassword");
    Assert.Equal(new ChordDto(0x50, HotkeyModifiers.Ctrl, "Ctrl+P"), changed.Current);

    Assert.Equal(new HotkeyResponse(false, "Shortcuts must include Ctrl or Alt"), await SetAsync("login.copyUsername", new ChordDto(0x43, 0, "C")));
    Assert.Equal(new HotkeyResponse(false, "Ctrl+P is already used by Copy Password (Login)"), await SetAsync("login.copyUsername", chord));
    Assert.Equal(new HotkeyResponse(true, null), await SetAsync("login.copyPassword", null));
    Assert.Null((await GetAsync()).Groups.Single(g => g.ItemType == "login").Actions.Single(a => a.Id == "login.copyPassword").Current);
  }

  [Fact]
  public async Task SetEnterAction_AndResetHotkeys_RoundTrip()
  {
    var reply = IpcJson.Read(
      await _ops.HandleAsync(Ops.SetEnterAction, IpcJson.ToElement(new SetEnterActionRequest("login", "login.copyPassword"), IpcJson.Default.SetEnterActionRequest), CancellationToken.None),
      IpcJson.Default.HotkeyResponse);
    Assert.True(reply.Success);
    Assert.Equal("login.copyPassword", (await GetAsync()).Groups.Single(g => g.ItemType == "login").CurrentEnter);

    var rejected = IpcJson.Read(
      await _ops.HandleAsync(Ops.SetEnterAction, IpcJson.ToElement(new SetEnterActionRequest("login", "card.copyNumber"), IpcJson.Default.SetEnterActionRequest), CancellationToken.None),
      IpcJson.Default.HotkeyResponse);
    Assert.Equal(new HotkeyResponse(false, "That action cannot be used for Enter"), rejected);

    await SetAsync("login.copyUsername", null);
    Assert.Equal(new HotkeyResponse(true, null), IpcJson.Read(await _ops.HandleAsync(Ops.ResetHotkeys, null, CancellationToken.None), IpcJson.Default.HotkeyResponse));
    var login = (await GetAsync()).Groups.Single(g => g.ItemType == "login");
    Assert.Equal("login.openBrowser", login.CurrentEnter);
    Assert.Equal("Ctrl+C", login.Actions.Single(a => a.Id == "login.copyUsername").Current?.Display);
  }

  [Fact]
  public async Task SetHotkey_WithoutPayload_IsInvalid()
  {
    var ex = await Assert.ThrowsAsync<IpcException>(() => _ops.HandleAsync(Ops.SetHotkey, null, CancellationToken.None));
    Assert.Equal(IpcErrors.Invalid, ex.Code);
  }
}
