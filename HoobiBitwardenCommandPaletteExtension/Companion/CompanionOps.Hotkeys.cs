using System.Linq;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Helpers;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps
{
  internal static HotkeysResponse GetHotkeys()
  {
    var store = HotkeyStore.Current;
    return new(HotkeyCatalog.Groups
      .Select(group => new HotkeyGroupDto(
        group.ItemType,
        group.Title,
        store.InheritedEnterFor(group.ItemType),
        store.EnterFor(group.ItemType),
        HotkeyCatalog.EnterChoices(group.ItemType).Select(a => new ChoiceDto(a.Title, a.Id)).ToArray(),
        HotkeyCatalog.ActionsFor(group.ItemType).Select(a => new HotkeyActionDto(a.Id, a.Title, a.Default?.ToDto(), store.ChordFor(a.Id)?.ToDto())).ToArray()))
      .ToArray());
  }

  internal static HotkeyResponse SetHotkey(SetHotkeyRequest request) =>
    Respond(HotkeyStore.Current.SetChord(request.ActionId, request.Chord is { } chord ? new HotkeyChord(chord.Key, chord.Modifiers) : null));

  internal static HotkeyResponse SetEnterAction(SetEnterActionRequest request) =>
    Respond(HotkeyStore.Current.SetEnter(request.ItemType, request.ActionId));

  internal static HotkeyResponse ResetHotkeys() =>
    Respond(HotkeyStore.Current.ResetAll() ? null : "Could not save the shortcuts");

  private static HotkeyResponse Respond(string? error) => new(error is null, error);
}
