using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;
using HoobiBitwardenCommandPaletteExtension.Commands;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Helpers;

internal static partial class VaultItemHelper
{
  internal static IconInfo GetIcon(BitwardenItem item) => item.Type switch
  {
    BitwardenItemType.Login => GetFaviconIcon(item.FirstUri),
    BitwardenItemType.SecureNote => new IconInfo("\uE70B"),
    BitwardenItemType.Card => new IconInfo("\uE8C7"),
    BitwardenItemType.Identity => new IconInfo("\uE77B"),
    BitwardenItemType.SshKey => new IconInfo("\uE8D7"),
    _ => new IconInfo("\uE72E"),
  };

  internal static ICommand GetDefaultCommand(BitwardenItem item) => Track(item.Id, item.Type switch
  {
    BitwardenItemType.Login when !string.IsNullOrEmpty(item.FirstUri) => new OpenUrlCommand(item.FirstUri),
    BitwardenItemType.SshKey when IsValidSshHost(item.SshHost) => BuildSshCommand(item.SshHost!),
    _ => BuildOpenInBitwardenCommand(item.Id),
  });

  internal static CommandContextItem[] BuildContextItems(BitwardenItem item)
  {
    var items = new List<CommandContextItem>();
    var id = item.Id;

    switch (item.Type)
    {
      case BitwardenItemType.Login:
        AddLoginContextItems(items, item, id);
        break;
      case BitwardenItemType.SecureNote:
        AddNoteContextItems(items, item, id);
        break;
      case BitwardenItemType.Card:
        AddCardContextItems(items, item, id);
        break;
      case BitwardenItemType.Identity:
        AddIdentityContextItems(items, item, id);
        break;
      case BitwardenItemType.SshKey:
        AddSshKeyContextItems(items, item, id);
        break;
    }

    items.Add(new CommandContextItem(Track(id, BuildOpenInBitwardenCommand(id)))
    {
      Title = "Open in Bitwarden",
      Icon = new IconInfo("\uE8A7"),
      RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.O),
    });

    var serverUrl = BitwardenCliService.ServerUrl;
    if (!string.IsNullOrEmpty(serverUrl))
    {
      items.Add(new CommandContextItem(Track(id, new OpenUrlCommand($"{serverUrl}/#/vault?itemId={Uri.EscapeDataString(id)}")))
      {
        Title = "View in Web Vault",
        Icon = new IconInfo("\uE774"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.O),
      });
    }

    return items.ToArray();
  }

  private static void AddLoginContextItems(List<CommandContextItem> items, BitwardenItem item, string id)
  {
    if (!string.IsNullOrEmpty(item.Username))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.Username)))
      {
        Title = "Copy Username",
        Icon = new IconInfo("\uE77B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.Password))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.Password)))
      {
        Title = "Copy Password",
        Icon = new IconInfo("\uE72E"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.C),
      });
    }

    if (item.HasTotp)
    {
      items.Add(new CommandContextItem(Track(id, new CopyOtpCommand(item.TotpSecret!)))
      {
        Title = "Copy OTP",
        Icon = new IconInfo("\uE916"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, alt: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.FirstUri))
    {
      items.Add(new CommandContextItem(Track(id, new OpenUrlCommand(item.FirstUri)))
      {
        Title = "Open in Browser",
        Icon = new IconInfo("\uE774"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(alt: true, vkey: VirtualKey.Enter),
      });
    }
  }

  private static void AddNoteContextItems(List<CommandContextItem> items, BitwardenItem item, string id)
  {
    if (!string.IsNullOrEmpty(item.Notes))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.Notes)))
      {
        Title = "Copy Notes",
        Icon = new IconInfo("\uE70B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }
  }

  private static void AddCardContextItems(List<CommandContextItem> items, BitwardenItem item, string id)
  {
    if (!string.IsNullOrEmpty(item.CardNumber))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.CardNumber)))
      {
        Title = "Copy Card Number",
        Icon = new IconInfo("\uE8C7"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.CardCode))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.CardCode)))
      {
        Title = "Copy Security Code",
        Icon = new IconInfo("\uE72E"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.CardholderName))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.CardholderName)))
      {
        Title = "Copy Cardholder Name",
        Icon = new IconInfo("\uE77B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, alt: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.CardExpiration))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.CardExpiration)))
      {
        Title = "Copy Expiration",
        Icon = new IconInfo("\uE787"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.E),
      });
    }
  }

  private static void AddIdentityContextItems(List<CommandContextItem> items, BitwardenItem item, string id)
  {
    if (!string.IsNullOrEmpty(item.IdentityEmail))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.IdentityEmail)))
      {
        Title = "Copy Email",
        Icon = new IconInfo("\uE715"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.IdentityFullName))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.IdentityFullName)))
      {
        Title = "Copy Name",
        Icon = new IconInfo("\uE77B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.IdentityPhone))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.IdentityPhone)))
      {
        Title = "Copy Phone",
        Icon = new IconInfo("\uE717"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, alt: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.IdentityUsername))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.IdentityUsername)))
      {
        Title = "Copy Username",
        Icon = new IconInfo("\uE77B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.U),
      });
    }

    if (!string.IsNullOrEmpty(item.IdentityAddress))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.IdentityAddress)))
      {
        Title = "Copy Address",
        Icon = new IconInfo("\uE80F"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.A),
      });
    }
  }

  private static void AddSshKeyContextItems(List<CommandContextItem> items, BitwardenItem item, string id)
  {
    if (!string.IsNullOrEmpty(item.SshPublicKey))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.SshPublicKey)))
      {
        Title = "Copy Public Key",
        Icon = new IconInfo("\uE8D7"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.SshFingerprint))
    {
      items.Add(new CommandContextItem(Track(id, new CopyTextCommand(item.SshFingerprint)))
      {
        Title = "Copy Fingerprint",
        Icon = new IconInfo("\uE928"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.C),
      });
    }

    if (IsValidSshHost(item.SshHost))
    {
      items.Add(new CommandContextItem(Track(id, BuildSshCommand(item.SshHost!)))
      {
        Title = "Open SSH Session",
        Icon = new IconInfo("\uE756"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(alt: true, vkey: VirtualKey.Enter),
      });
    }
  }

  internal static IconInfo GetFaviconIcon(string? uri)
  {
    if (string.IsNullOrEmpty(uri))
      return new IconInfo("\uE72E");

    try
    {
      var host = new Uri(uri).Host;
      return new IconInfo($"https://icons.bitwarden.net/{host}/icon.png");
    }
    catch
    {
      return new IconInfo("\uE72E");
    }
  }

  internal static AnonymousCommand BuildOpenInBitwardenCommand(string itemId) => new(() =>
  {
    try
    {
      ClipboardHelper.SetText(itemId);
      Process.Start(new ProcessStartInfo("bitwarden://") { UseShellExecute = true });
    }
    catch { }
  })
  {
    Name = "Open in Bitwarden",
    Result = CommandResult.Dismiss(),
  };

  private static AnonymousCommand BuildSshCommand(string host) => new(() =>
  {
    try { Process.Start(new ProcessStartInfo("ssh", host) { UseShellExecute = true }); }
    catch { }
  })
  {
    Name = $"SSH to {host}",
    Result = CommandResult.Dismiss(),
  };

  private static bool IsValidSshHost(string? host) =>
      !string.IsNullOrEmpty(host) && SshHostPattern().IsMatch(host);

  [GeneratedRegex(@"^[\w.+-]+@[\w.-]+$")]
  private static partial Regex SshHostPattern();

  private static TrackedInvokable Track(string itemId, InvokableCommand inner) => new(inner, itemId);

  private sealed partial class TrackedInvokable(InvokableCommand inner, string itemId) : InvokableCommand
  {
    public override ICommandResult Invoke()
    {
      AccessTracker.Record(itemId);
      return inner.Invoke();
    }
  }
}
