using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Helpers;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed record StoredChord(int Key, int Modifiers);

internal sealed record HotkeyFile(Dictionary<string, StoredChord>? Chords, Dictionary<string, string>? Enter);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HotkeyFile))]
internal sealed partial class HotkeyJson : JsonSerializerContext;

internal sealed class HotkeyStore
{
  private static readonly string DefaultPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "HoobiBitwardenCommandPalette",
    "hotkeys.json");

  private const string SaveError = "Could not save the shortcuts";

  private readonly string _path;
  private readonly Lock _gate = new();
  private Dictionary<string, StoredChord> _chords = [];
  private Dictionary<string, string> _enter = [];

  internal HotkeyStore(string path)
  {
    _path = path;
    Load();
  }

  internal static HotkeyStore Current { get; set; } = new(DefaultPath);

  internal event Action? Changed;

  internal HotkeyChord? ChordFor(string actionId)
  {
    var action = HotkeyCatalog.Find(actionId);
    if (action is null)
      return null;
    lock (_gate)
      return _chords.TryGetValue(actionId, out var stored)
        ? (stored.Key == 0 ? null : new HotkeyChord(stored.Key, stored.Modifiers))
        : action.Default;
  }

  internal string EnterFor(string itemType)
  {
    lock (_gate)
      return _enter.TryGetValue(itemType, out var own) ? own : InheritedEnterFor(itemType);
  }

  internal string InheritedEnterFor(string itemType)
  {
    lock (_gate)
      return FollowsAll(itemType) && _enter.TryGetValue(HotkeyItemTypes.All, out var all) ? all : HotkeyCatalog.DefaultEnter(itemType);
  }

  private static bool FollowsAll(string itemType) => itemType is HotkeyItemTypes.Card or HotkeyItemTypes.Identity or HotkeyItemTypes.Note;

  internal string? Validate(string actionId, HotkeyChord? chord)
  {
    if (HotkeyCatalog.Find(actionId) is not { } action)
      return "Unknown action";
    if (chord is not { } value)
      return null;
    if (!HotkeyKeys.IsKey(value.Key) || (value.Modifiers & ~HotkeyModifiers.All) != 0)
      return "Choose a key as well as the modifiers";
    if ((value.Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt)) == 0)
      return value.Key == HotkeyKeys.Enter ? "Enter on its own is reserved for the Enter action" : "Shortcuts must include Ctrl or Alt";

    lock (_gate)
      return ConflictsWith(action, value).FirstOrDefault() is { } other
        ? $"{value.Display} is already used by {other.Title}{(other.ItemType == HotkeyItemTypes.All ? string.Empty : $" ({GroupTitle(other.ItemType)})")}"
        : null;
  }

  private IEnumerable<HotkeyAction> ConflictsWith(HotkeyAction action, HotkeyChord value) =>
    HotkeyCatalog.TypesOf(action).SelectMany(HotkeyCatalog.AvailableOn).Where(o => o.Id != action.Id && ChordFor(o.Id) == value);

  internal string? SetChord(string actionId, HotkeyChord? chord) => Apply(() =>
  {
    if (Validate(actionId, chord) is { } error)
      return error;

    if (chord == HotkeyCatalog.Find(actionId)!.Default)
      _chords.Remove(actionId);
    else
      _chords[actionId] = chord is { } value ? new StoredChord(value.Key, value.Modifiers) : new StoredChord(0, 0);
    return null;
  });

  internal string? SetEnter(string itemType, string? actionId) => Apply(() =>
  {
    if (!HotkeyCatalog.IsItemType(itemType) || (actionId is not null && HotkeyCatalog.EnterChoices(itemType).All(a => a.Id != actionId)))
      return "That action cannot be used for Enter";

    if (actionId is null || (actionId == HotkeyCatalog.DefaultEnter(itemType) && !(FollowsAll(itemType) && _enter.ContainsKey(HotkeyItemTypes.All))))
      _enter.Remove(itemType);
    else
      _enter[itemType] = actionId;
    return null;
  });

  internal bool ResetAll() => Apply(() =>
  {
    _chords.Clear();
    _enter.Clear();
    return null;
  }) is null;

  private string? Apply(Func<string?> change)
  {
    lock (_gate)
    {
      var chords = new Dictionary<string, StoredChord>(_chords);
      var enter = new Dictionary<string, string>(_enter);
      if (change() is { } error)
        return error;
      if (!TrySave())
      {
        _chords = chords;
        _enter = enter;
        return SaveError;
      }
    }

    Changed?.Invoke();
    return null;
  }

  private static string GroupTitle(string itemType) => HotkeyCatalog.Groups.First(g => g.ItemType == itemType).Title;

  private void Load()
  {
    try
    {
      if (!File.Exists(_path))
        return;
      var file = JsonSerializer.Deserialize(File.ReadAllText(_path), HotkeyJson.Default.HotkeyFile);
      _chords = (file?.Chords ?? []).Where(c => HotkeyCatalog.Find(c.Key) is not null && c.Value is not null && IsStoredChordValid(c.Value)).ToDictionary(c => c.Key, c => c.Value);
      _enter = (file?.Enter ?? []).Where(e => HotkeyCatalog.IsItemType(e.Key) && e.Value is not null && HotkeyCatalog.EnterChoices(e.Key).Any(a => a.Id == e.Value)).ToDictionary(e => e.Key, e => e.Value);
      DropConflicts();
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Hotkeys", $"Ignoring {_path}: {ex.GetType().Name}: {ex.Message}");
      _chords = [];
      _enter = [];
    }
  }

  private void DropConflicts()
  {
    bool dropped;
    do
    {
      dropped = false;
      for (var i = 0; i < HotkeyCatalog.Actions.Length && !dropped; i++)
      {
        var action = HotkeyCatalog.Actions[i];
        if (!_chords.TryGetValue(action.Id, out var stored) || stored.Key == 0)
          continue;
        var chord = new HotkeyChord(stored.Key, stored.Modifiers);
        if (ConflictsWith(action, chord).Any(o => !_chords.ContainsKey(o.Id) || Array.IndexOf(HotkeyCatalog.Actions, o) < i))
        {
          DebugLogService.Log("Hotkeys", $"Dropping {action.Id}: {chord.Display} conflicts with another action");
          _chords.Remove(action.Id);
          dropped = true;
        }
      }
    }
    while (dropped);
  }

  private static bool IsStoredChordValid(StoredChord chord) =>
    chord.Key == 0 || (HotkeyKeys.IsKey(chord.Key) && (chord.Modifiers & ~HotkeyModifiers.All) == 0 && (chord.Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt)) != 0);

  private bool TrySave()
  {
    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
      var temp = _path + ".tmp";
      File.WriteAllText(temp, JsonSerializer.Serialize(new HotkeyFile(_chords, _enter), HotkeyJson.Default.HotkeyFile));
      File.Move(temp, _path, overwrite: true);
      return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      DebugLogService.Log("Hotkeys", $"Could not save {_path}: {ex.GetType().Name}: {ex.Message}");
      return false;
    }
  }
}
