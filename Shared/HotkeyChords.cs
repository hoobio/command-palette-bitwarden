using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HoobiBitwarden.Ipc;

internal static class HotkeyModifiers
{
  public const int Ctrl = 1;
  public const int Alt = 2;
  public const int Shift = 4;
  public const int All = Ctrl | Alt | Shift;
}

[StructLayout(LayoutKind.Auto)]
internal readonly record struct HotkeyChord(int Key, int Modifiers)
{
  public string Display => HotkeyKeys.Display(Key, Modifiers);

  public ChordDto ToDto() => new(Key, Modifiers, Display);
}

internal static class HotkeyKeys
{
  public const int Enter = 0x0D;

  private static readonly Dictionary<int, string> Names = new()
  {
    [0x08] = "Backspace",
    [0x09] = "Tab",
    [0x0D] = "Enter",
    [0x1B] = "Esc",
    [0x20] = "Space",
    [0x21] = "Page Up",
    [0x22] = "Page Down",
    [0x23] = "End",
    [0x24] = "Home",
    [0x25] = "Left",
    [0x26] = "Up",
    [0x27] = "Right",
    [0x28] = "Down",
    [0x2D] = "Insert",
    [0x2E] = "Delete",
    [0xBA] = ";",
    [0xBB] = "=",
    [0xBC] = ",",
    [0xBD] = "-",
    [0xBE] = ".",
    [0xBF] = "/",
    [0xC0] = "`",
    [0xDB] = "[",
    [0xDC] = "\\",
    [0xDD] = "]",
    [0xDE] = "'",
  };

  public static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

  public static bool IsKey(int key) => key is > 0 and < 255 && !IsModifier(key);

  public static string Name(int key) => key switch
  {
    >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
    >= 0x60 and <= 0x69 => $"Num {key - 0x60}",
    >= 0x70 and <= 0x87 => $"F{key - 0x6F}",
    _ => Names.TryGetValue(key, out var name) ? name : $"Key {key}",
  };

  public static IReadOnlyList<string> Caps(int key, int modifiers)
  {
    var caps = new List<string>(4);
    if ((modifiers & HotkeyModifiers.Ctrl) != 0)
      caps.Add("Ctrl");
    if ((modifiers & HotkeyModifiers.Alt) != 0)
      caps.Add("Alt");
    if ((modifiers & HotkeyModifiers.Shift) != 0)
      caps.Add("Shift");
    if (IsKey(key))
      caps.Add(Name(key));
    return caps;
  }

  public static string Display(int key, int modifiers) => string.Join('+', Caps(key, modifiers));
}
