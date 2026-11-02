using System;
using System.Collections.Generic;
using System.Diagnostics;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Commands;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Pages;
using HoobiBitwardenCommandPaletteExtension.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;

namespace HoobiBitwardenCommandPaletteExtension.Helpers;

internal static partial class VaultItemHelper
{
  private sealed record BuiltAction(string Title, string Glyph, ICommand Command, Action? Run = null);

  private static readonly Dictionary<string, string> ActionGlyphs = new()
  {
    [HotkeyIds.OpenWindow] = "\uE8A7",
    [HotkeyIds.OpenWebVault] = "\uE774",
    [HotkeyIds.LoginCopyUsername] = "\uE77B",
    [HotkeyIds.LoginCopyPassword] = "\uE72E",
    [HotkeyIds.LoginCopyOtp] = "\uE916",
    [HotkeyIds.LoginOpenBrowser] = "\uE774",
    [HotkeyIds.LoginRotatePassword] = "\uE72C",
    [HotkeyIds.NoteCopyNotes] = "\uE70B",
    [HotkeyIds.CardCopyNumber] = "\uE8C7",
    [HotkeyIds.CardCopyCode] = "\uE72E",
    [HotkeyIds.CardCopyCardholder] = "\uE77B",
    [HotkeyIds.CardCopyExpiration] = "\uE787",
    [HotkeyIds.IdentityCopyEmail] = "\uE715",
    [HotkeyIds.IdentityCopyName] = "\uE77B",
    [HotkeyIds.IdentityCopyPhone] = "\uE717",
    [HotkeyIds.IdentityCopyUsername] = "\uE77B",
    [HotkeyIds.IdentityCopyAddress] = "\uE80F",
    [HotkeyIds.SshCopyPublicKey] = "\uE8D7",
    [HotkeyIds.SshCopyFingerprint] = "\uE928",
    [HotkeyIds.SshOpenSession] = "\uE756",
  };

  private static BuiltAction? BuildAction(BitwardenItem item, string actionId, BitwardenCliService? reprompt)
  {
    if (HotkeyCatalog.Find(actionId) is not { } action || !ActionGlyphs.TryGetValue(actionId, out var glyph))
      return null;

    var id = item.Id;
    var companion = CompanionHost.Current;
    var label = action.Title.StartsWith("Copy ", StringComparison.Ordinal) ? action.Title["Copy ".Length..] : action.Title;

    BuiltAction? Made(ICommand command, Action? run = null) => new(action.Title, glyph, command, run);
    BuiltAction? CopyText(string? text) => string.IsNullOrEmpty(text) ? null : Made(CopyFieldCommand(id, text, label, reprompt));
    BuiltAction? CopySecret(ProtectedSecret? secret) => secret is null ? null : Made(CopySecretCommand(id, secret, label, reprompt));

    return actionId switch
    {
      HotkeyIds.OpenWindow => companion is null ? null : Made(Track(id, BuildOpenInWindowCommand(companion, id)), () => companion.ShowItem(id)),
      HotkeyIds.OpenWebVault => Made(Track(id, BuildOpenInWebVaultCommand(id)), () => OpenWebVault(id)),
      HotkeyIds.LoginCopyUsername => CopyText(item.Username),
      HotkeyIds.LoginCopyPassword => CopySecret(item.Password),
      HotkeyIds.LoginCopyOtp => item.HasTotp ? Made(CopyCommand(id, label, reprompt, () => CopyOtpCommand.CopyToClipboard(item.TotpSecret!))) : null,
      HotkeyIds.LoginOpenBrowser => ToSafeWebUrl(item.FirstUri) is { } url
        ? Made(Track(id, new OpenUrlCommand(url)), () => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }))
        : null,
      HotkeyIds.LoginRotatePassword => companion is null || item.Password is null
        ? null
        : Made(Track(id, new AnonymousCommand(() => companion.RotateItem(id)) { Name = label, Result = CommandResult.Dismiss() }), () => companion.RotateItem(id)),
      HotkeyIds.NoteCopyNotes => CopySecret(item.Notes),
      HotkeyIds.CardCopyNumber => CopySecret(item.CardNumber),
      HotkeyIds.CardCopyCode => CopySecret(item.CardCode),
      HotkeyIds.CardCopyCardholder => CopyText(item.CardholderName),
      HotkeyIds.CardCopyExpiration => CopyText(item.CardExpiration),
      HotkeyIds.IdentityCopyEmail => CopyText(item.IdentityEmail),
      HotkeyIds.IdentityCopyName => CopyText(item.IdentityFullName),
      HotkeyIds.IdentityCopyPhone => CopyText(item.IdentityPhone),
      HotkeyIds.IdentityCopyUsername => CopyText(item.IdentityUsername),
      HotkeyIds.IdentityCopyAddress => CopyText(item.IdentityAddress),
      HotkeyIds.SshCopyPublicKey => CopyText(item.SshPublicKey),
      HotkeyIds.SshCopyFingerprint => CopyText(item.SshFingerprint),
      HotkeyIds.SshOpenSession => HasSshHost(item) ? Made(Track(id, BuildSshCommand(item)), () => StartSsh(item)) : null,
      _ => null,
    };
  }

  private static void OpenWebVault(string itemId) =>
    Process.Start(new ProcessStartInfo($"{GetVaultBaseUrl()}/#/vault?itemId={Uri.EscapeDataString(itemId)}") { UseShellExecute = true });

  private static (string Id, BuiltAction Built) ResolveEnterAction(BitwardenItem item, BitwardenCliService? reprompt)
  {
    var itemType = HotkeyItemTypes.For(item.Type);
    BuiltAction? built = null;
    var chosen = HotkeyCatalog.ResolveEnter(itemType, HotkeyStore.Current.EnterFor(itemType), actionId =>
    {
      var candidate = actionId == HotkeyIds.OpenWebVault && string.IsNullOrEmpty(BitwardenCliService.ServerUrl) ? null : BuildAction(item, actionId, reprompt);
      if (candidate is not null)
        built = candidate;
      return candidate is not null;
    });
    return (chosen, built ?? BuildAction(item, chosen, reprompt)!);
  }

  private static ICommand ResolveEnterCommand(BitwardenItem item, BitwardenCliService? service)
  {
    var reprompt = item.Reprompt == 1 ? service : null;
    var built = ResolveEnterAction(item, reprompt).Built;
    return built.Run is { } run && reprompt is not null && !RepromptGrace.IsWithinGracePeriod(item.Id)
      ? BuildRepromptCommand(item.Id, run)
      : built.Command;
  }

  private static KeyChord ToKeyChord(HotkeyChord chord) => KeyChordHelpers.FromModifiers(
    ctrl: (chord.Modifiers & HotkeyModifiers.Ctrl) != 0,
    shift: (chord.Modifiers & HotkeyModifiers.Shift) != 0,
    alt: (chord.Modifiers & HotkeyModifiers.Alt) != 0,
    vkey: (VirtualKey)chord.Key);

  private static CommandContextItem? BuildContextItem(BitwardenItem item, string actionId, BitwardenCliService? reprompt)
  {
    if (BuildAction(item, actionId, reprompt) is not { } built)
      return null;

    var contextItem = new CommandContextItem(built.Command) { Title = built.Title, Icon = new IconInfo(built.Glyph) };
    if (HotkeyStore.Current.ChordFor(actionId) is { } chord)
      contextItem.RequestedShortcut = ToKeyChord(chord);
    return contextItem;
  }
}
