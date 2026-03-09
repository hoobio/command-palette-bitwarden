using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;
using HoobiBitwardenCommandPaletteExtension.Commands;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;
using OtpNet;

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

    AddCustomFieldContextItems(items, item, id);

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

  internal static Tag[] BuildTags(BitwardenItem item, bool showWatchtowerTags = true, ForegroundContext? context = null)
  {
    var tags = new List<Tag>();

    if (AccessTracker.IsLastCopied(item.Id))
      tags.Add(new Tag("Recent") { Foreground = ColorHelpers.FromRgb(0x87, 0xD9, 0x6C) });

    if (item.Favorite)
      tags.Add(new Tag("\u2605") { Foreground = ColorHelpers.FromRgb(0xFA, 0xCC, 0x6E) });

    if (context != null && ContextAwarenessService.ContextScore(context, item) > 0)
    {
      tags.Add(new Tag("Context") { Foreground = ColorHelpers.FromRgb(0x40, 0x9F, 0xFF) });
    }

    if (item.HasTotp)
    {
      var totpTag = GetTotpTag(item.TotpSecret!);
      if (totpTag != null)
        tags.Add(totpTag);
    }

    if (showWatchtowerTags)
      AddWatchtowerTags(tags, item);

    return tags.Count > 0 ? [.. tags] : [];
  }

  private static Tag? GetTotpTag(string totpSecret)
  {
    try
    {
      var (key, digits, period) = CopyOtpCommand.ParseTotpSecret(totpSecret);
      var totp = new Totp(key, step: period, totpSize: digits);
      var code = totp.ComputeTotp();
      var remaining = totp.RemainingSeconds();

      return new Tag($"{code} ({remaining}s)") { Foreground = ColorHelpers.FromRgb(0x90, 0xE1, 0xC6) };
    }
    catch
    {
      return new Tag("TOTP") { Foreground = ColorHelpers.FromRgb(0x68, 0x68, 0x68) };
    }
  }

  private static void AddWatchtowerTags(List<Tag> tags, BitwardenItem item)
  {
    if (item.Type != BitwardenItemType.Login)
      return;

    if (!string.IsNullOrEmpty(item.Password))
    {
      if (item.Password.Length < 8)
        tags.Add(new Tag("Weak") { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) });

      var lastChanged = item.PasswordRevisionDate ?? item.RevisionDate;
      if (DateTime.UtcNow - lastChanged > TimeSpan.FromDays(365))
        tags.Add(new Tag("Old") { Foreground = ColorHelpers.FromRgb(0xFF, 0xD1, 0x73) });
    }

    if (item.Uris.Count > 0 && item.Uris.Any(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
      tags.Add(new Tag("Insecure URL") { Foreground = ColorHelpers.FromRgb(0xF2, 0x87, 0x79) });
  }

  private static void AddLoginContextItems(List<CommandContextItem> items, BitwardenItem item, string id)
  {
    if (!string.IsNullOrEmpty(item.Username))
    {
      items.Add(new CommandContextItem(Track(id, CopyNonSensitive(item.Username!, "Username")))
      {
        Title = "Copy Username",
        Icon = new IconInfo("\uE77B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.Password))
    {
      items.Add(new CommandContextItem(Track(id, CopySensitive(item.Password!, "Password")))
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
      items.Add(new CommandContextItem(Track(id, CopySensitive(item.Notes!, "Notes")))
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
      items.Add(new CommandContextItem(Track(id, CopySensitive(item.CardNumber!, "Card Number")))
      {
        Title = "Copy Card Number",
        Icon = new IconInfo("\uE8C7"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.CardCode))
    {
      items.Add(new CommandContextItem(Track(id, CopySensitive(item.CardCode!, "Security Code")))
      {
        Title = "Copy Security Code",
        Icon = new IconInfo("\uE72E"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.CardholderName))
    {
      items.Add(new CommandContextItem(Track(id, CopyNonSensitive(item.CardholderName!, "Cardholder Name")))
      {
        Title = "Copy Cardholder Name",
        Icon = new IconInfo("\uE77B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, alt: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.CardExpiration))
    {
      items.Add(new CommandContextItem(Track(id, CopyNonSensitive(item.CardExpiration!, "Expiration")))
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
      items.Add(new CommandContextItem(Track(id, CopyNonSensitive(item.IdentityEmail!, "Email")))
      {
        Title = "Copy Email",
        Icon = new IconInfo("\uE715"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.IdentityFullName))
    {
      items.Add(new CommandContextItem(Track(id, CopyNonSensitive(item.IdentityFullName!, "Name")))
      {
        Title = "Copy Name",
        Icon = new IconInfo("\uE77B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.IdentityPhone))
    {
      items.Add(new CommandContextItem(Track(id, CopyNonSensitive(item.IdentityPhone!, "Phone")))
      {
        Title = "Copy Phone",
        Icon = new IconInfo("\uE717"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, alt: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.IdentityUsername))
    {
      items.Add(new CommandContextItem(Track(id, CopyNonSensitive(item.IdentityUsername!, "Username")))
      {
        Title = "Copy Username",
        Icon = new IconInfo("\uE77B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.U),
      });
    }

    if (!string.IsNullOrEmpty(item.IdentityAddress))
    {
      items.Add(new CommandContextItem(Track(id, CopyNonSensitive(item.IdentityAddress!, "Address")))
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
      items.Add(new CommandContextItem(Track(id, CopyNonSensitive(item.SshPublicKey!, "Public Key")))
      {
        Title = "Copy Public Key",
        Icon = new IconInfo("\uE8D7"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.SshFingerprint))
    {
      items.Add(new CommandContextItem(Track(id, CopyNonSensitive(item.SshFingerprint!, "Fingerprint")))
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

  private static void AddCustomFieldContextItems(List<CommandContextItem> items, BitwardenItem item, string id)
  {
    if (item.CustomFields.Count == 0)
      return;

    foreach (var (fieldName, fieldValue) in item.CustomFields)
    {
      if (string.IsNullOrEmpty(fieldValue))
        continue;

      // Skip the 'host' custom field as it's handled by SSH context items
      if (item.Type == BitwardenItemType.SshKey && fieldName.Equals("host", StringComparison.OrdinalIgnoreCase))
        continue;

      items.Add(new CommandContextItem(Track(id, CopyNonSensitive(fieldValue, fieldName)))
      {
        Title = $"Copy \"{fieldName}\"",
        Icon = new IconInfo("\uE8C8"),
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

  private static AnonymousCommand CopySensitive(string text, string label) => new(() =>
      SecureClipboardService.CopySensitive(text))
  {
    Name = $"Copy {label}",
    Result = CommandResult.ShowToast($"Copied {label} to clipboard"),
  };

  private static AnonymousCommand CopyNonSensitive(string text, string label) => new(() =>
      SecureClipboardService.CopyNonSensitive(text))
  {
    Name = $"Copy {label}",
    Result = CommandResult.ShowToast($"Copied {label} to clipboard"),
  };

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
