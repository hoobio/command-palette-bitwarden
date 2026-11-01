using System;
using System.Collections.Generic;
using System.Linq;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Models;

namespace HoobiBitwardenCommandPaletteExtension.Helpers;

internal static class HotkeyItemTypes
{
  public const string All = "all";
  public const string Login = "login";
  public const string Card = "card";
  public const string Identity = "identity";
  public const string Note = "note";
  public const string Ssh = "ssh";

  public static string For(BitwardenItemType type) => type switch
  {
    BitwardenItemType.Login => Login,
    BitwardenItemType.Card => Card,
    BitwardenItemType.Identity => Identity,
    BitwardenItemType.SshKey => Ssh,
    _ => Note,
  };
}

internal static class HotkeyIds
{
  public const string OpenWindow = "all.openWindow";
  public const string OpenWebVault = "all.openWebVault";
  public const string LoginCopyUsername = "login.copyUsername";
  public const string LoginCopyPassword = "login.copyPassword";
  public const string LoginCopyOtp = "login.copyOtp";
  public const string LoginOpenBrowser = "login.openBrowser";
  public const string LoginRotatePassword = "login.rotatePassword";
  public const string NoteCopyNotes = "note.copyNotes";
  public const string CardCopyNumber = "card.copyNumber";
  public const string CardCopyCode = "card.copyCode";
  public const string CardCopyCardholder = "card.copyCardholder";
  public const string CardCopyExpiration = "card.copyExpiration";
  public const string IdentityCopyEmail = "identity.copyEmail";
  public const string IdentityCopyName = "identity.copyName";
  public const string IdentityCopyPhone = "identity.copyPhone";
  public const string IdentityCopyUsername = "identity.copyUsername";
  public const string IdentityCopyAddress = "identity.copyAddress";
  public const string SshCopyPublicKey = "ssh.copyPublicKey";
  public const string SshCopyFingerprint = "ssh.copyFingerprint";
  public const string SshOpenSession = "ssh.openSession";
}

internal sealed record HotkeyAction(string Id, string ItemType, string Title, HotkeyChord? Default);

internal static class HotkeyCatalog
{
  private const int KeyA = 0x41;
  private const int KeyC = 0x43;
  private const int KeyE = 0x45;
  private const int KeyO = 0x4F;
  private const int KeyU = 0x55;

  internal static readonly (string ItemType, string Title)[] Groups =
  [
    (HotkeyItemTypes.All, "All items"),
    (HotkeyItemTypes.Login, "Login"),
    (HotkeyItemTypes.Card, "Card"),
    (HotkeyItemTypes.Identity, "Identity"),
    (HotkeyItemTypes.Note, "Secure note"),
    (HotkeyItemTypes.Ssh, "SSH key"),
  ];

  internal static readonly HotkeyAction[] Actions =
  [
    new(HotkeyIds.OpenWindow, HotkeyItemTypes.All, "Open in companion", Ctrl(KeyO)),
    new(HotkeyIds.OpenWebVault, HotkeyItemTypes.All, "View in Web Vault", Ctrl(KeyO, HotkeyModifiers.Shift)),
    new(HotkeyIds.LoginCopyUsername, HotkeyItemTypes.Login, "Copy Username", Ctrl(KeyC)),
    new(HotkeyIds.LoginCopyPassword, HotkeyItemTypes.Login, "Copy Password", Ctrl(KeyC, HotkeyModifiers.Shift)),
    new(HotkeyIds.LoginCopyOtp, HotkeyItemTypes.Login, "Copy OTP", Ctrl(KeyC, HotkeyModifiers.Alt)),
    new(HotkeyIds.LoginOpenBrowser, HotkeyItemTypes.Login, "Open in Browser", new(HotkeyKeys.Enter, HotkeyModifiers.Alt)),
    new(HotkeyIds.LoginRotatePassword, HotkeyItemTypes.Login, "Rotate password", null),
    new(HotkeyIds.CardCopyNumber, HotkeyItemTypes.Card, "Copy Card Number", Ctrl(KeyC)),
    new(HotkeyIds.CardCopyCode, HotkeyItemTypes.Card, "Copy Code", Ctrl(KeyC, HotkeyModifiers.Shift)),
    new(HotkeyIds.CardCopyCardholder, HotkeyItemTypes.Card, "Copy Cardholder", Ctrl(KeyC, HotkeyModifiers.Alt)),
    new(HotkeyIds.CardCopyExpiration, HotkeyItemTypes.Card, "Copy Expiration", Ctrl(KeyE)),
    new(HotkeyIds.IdentityCopyEmail, HotkeyItemTypes.Identity, "Copy Email", Ctrl(KeyC)),
    new(HotkeyIds.IdentityCopyName, HotkeyItemTypes.Identity, "Copy Name", Ctrl(KeyC, HotkeyModifiers.Shift)),
    new(HotkeyIds.IdentityCopyPhone, HotkeyItemTypes.Identity, "Copy Phone", Ctrl(KeyC, HotkeyModifiers.Alt)),
    new(HotkeyIds.IdentityCopyUsername, HotkeyItemTypes.Identity, "Copy Username", Ctrl(KeyU)),
    new(HotkeyIds.IdentityCopyAddress, HotkeyItemTypes.Identity, "Copy Address", Ctrl(KeyA, HotkeyModifiers.Shift)),
    new(HotkeyIds.NoteCopyNotes, HotkeyItemTypes.Note, "Copy Notes", Ctrl(KeyC)),
    new(HotkeyIds.SshCopyPublicKey, HotkeyItemTypes.Ssh, "Copy Public Key", Ctrl(KeyC)),
    new(HotkeyIds.SshCopyFingerprint, HotkeyItemTypes.Ssh, "Copy Fingerprint", Ctrl(KeyC, HotkeyModifiers.Shift)),
    new(HotkeyIds.SshOpenSession, HotkeyItemTypes.Ssh, "Open SSH Session", new(HotkeyKeys.Enter, HotkeyModifiers.Alt)),
  ];

  private static HotkeyChord Ctrl(int key, int extra = 0) => new(key, HotkeyModifiers.Ctrl | extra);

  internal static HotkeyAction? Find(string? id) => Actions.FirstOrDefault(a => a.Id == id);

  internal static bool IsItemType(string? itemType) => Groups.Any(g => g.ItemType == itemType);

  internal static IEnumerable<HotkeyAction> ActionsFor(string itemType) =>
    Actions.Where(a => a.ItemType == itemType);

  internal static IEnumerable<HotkeyAction> AvailableOn(string itemType) =>
    Actions.Where(a => a.ItemType is HotkeyItemTypes.All || a.ItemType == itemType);

  internal static IEnumerable<string> TypesOf(HotkeyAction action) =>
    action.ItemType == HotkeyItemTypes.All
      ? Groups.Select(g => g.ItemType).Where(t => t != HotkeyItemTypes.All)
      : new[] { action.ItemType };

  internal static HotkeyAction[] EnterChoices(string itemType) =>
    (itemType == HotkeyItemTypes.All ? ActionsFor(HotkeyItemTypes.All) : AvailableOn(itemType)).ToArray();

  internal static string DefaultEnter(string itemType) => itemType switch
  {
    HotkeyItemTypes.Login => HotkeyIds.LoginOpenBrowser,
    HotkeyItemTypes.Ssh => HotkeyIds.SshOpenSession,
    _ => HotkeyIds.OpenWindow,
  };

  internal static string ResolveEnter(string itemType, string? chosen, Func<string, bool> isAvailable)
  {
    if (chosen is not null && isAvailable(chosen))
      return chosen;
    if (itemType == HotkeyItemTypes.Login && isAvailable(HotkeyIds.LoginOpenBrowser))
      return HotkeyIds.LoginOpenBrowser;
    if (itemType == HotkeyItemTypes.Ssh && isAvailable(HotkeyIds.SshOpenSession))
      return HotkeyIds.SshOpenSession;
    return isAvailable(HotkeyIds.OpenWindow) ? HotkeyIds.OpenWindow : HotkeyIds.OpenWebVault;
  }
}
