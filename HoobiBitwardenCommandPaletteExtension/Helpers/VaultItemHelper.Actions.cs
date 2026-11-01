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
    [HotkeyIds.OpenWindow] = "",
    [HotkeyIds.OpenWebVault] = "",
    [HotkeyIds.LoginCopyUsername] = "",
    [HotkeyIds.LoginCopyPassword] = "",
    [HotkeyIds.LoginCopyOtp] = "",
    [HotkeyIds.LoginOpenBrowser] = "",
    [HotkeyIds.LoginRotatePassword] = "",
    [HotkeyIds.NoteCopyNotes] = "",
    [HotkeyIds.CardCopyNumber] = "",
    [HotkeyIds.CardCopyCode] = "",
    [HotkeyIds.CardCopyCardholder] = "",
    [HotkeyIds.CardCopyExpiration] = "",
    [HotkeyIds.IdentityCopyEmail] = "",
    [HotkeyIds.IdentityCopyName] = "",
    [HotkeyIds.IdentityCopyPhone] = "",
    [HotkeyIds.IdentityCopyUsername] = "",
    [HotkeyIds.IdentityCopyAddress] = "",
    [HotkeyIds.SshCopyPublicKey] = "",
    [HotkeyIds.SshCopyFingerprint] = "",
    [HotkeyIds.SshOpenSession] = "",
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

  private static ICommand ResolveEnterCommand(BitwardenItem item, BitwardenCliService? service)
  {
    var reprompt = item.Reprompt == 1 ? service : null;
    var itemType = HotkeyItemTypes.For(item.Type);
    BuiltAction? built = null;
    var chosen = HotkeyCatalog.ResolveEnter(itemType, HotkeyStore.Current.EnterFor(itemType), actionId =>
    {
      var candidate = BuildAction(item, actionId, reprompt);
      if (candidate is not null)
        built = candidate;
      return candidate is not null;
    });
    built ??= BuildAction(item, chosen, reprompt)!;
    return built.Run is { } run && reprompt is not null && !RepromptPage.IsWithinGracePeriod(item.Id)
      ? BuildRepromptCommand(reprompt, item.Id, run, "Open")
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
