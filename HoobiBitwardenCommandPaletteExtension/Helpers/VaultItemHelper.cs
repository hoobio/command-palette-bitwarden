using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;
using Windows.UI.ViewManagement;
using HoobiBitwardenCommandPaletteExtension.Commands;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Pages;
using HoobiBitwardenCommandPaletteExtension.Services;
using OtpNet;

namespace HoobiBitwardenCommandPaletteExtension.Helpers;

internal static partial class VaultItemHelper
{
  internal static IconInfo GetIcon(BitwardenItem item, bool showWebsiteIcons = true) => item.Type switch
  {
    BitwardenItemType.Login => showWebsiteIcons ? GetFaviconIcon(item.FirstUri) : new IconInfo("\uE774"),
    BitwardenItemType.SecureNote => new IconInfo("\uE70B"),
    BitwardenItemType.Card => showWebsiteIcons && item.CardBrand != null
      ? GetCardBrandIcon(item.CardBrand)
      : new IconInfo("\uE8C7"),
    BitwardenItemType.Identity => new IconInfo("\uE77B"),
    BitwardenItemType.SshKey => new IconInfo("\uE8D7"),
    _ => new IconInfo("\uE72E"),
  };

  internal static ICommand GetDefaultCommand(BitwardenItem item, BitwardenCliService? service = null)
  {
    if (item.Reprompt == 1 && service != null && !RepromptPage.IsWithinGracePeriod())
      return new RepromptPage(service, BuildDefaultAction(item), "Open");

    return Track(item.Id, item.Type switch
    {
      BitwardenItemType.Login when !string.IsNullOrEmpty(item.FirstUri) => new OpenUrlCommand(item.FirstUri),
      BitwardenItemType.SshKey when IsValidSshHost(item.SshHost) => BuildSshCommand(item.SshHost!),
      _ => BuildOpenInWebVaultCommand(item.Id),
    });
  }

  private static Action BuildDefaultAction(BitwardenItem item) => item.Type switch
  {
    BitwardenItemType.Login when !string.IsNullOrEmpty(item.FirstUri) =>
      () => Process.Start(new ProcessStartInfo(item.FirstUri) { UseShellExecute = true }),
    BitwardenItemType.SshKey when IsValidSshHost(item.SshHost) =>
      () => { try { Process.Start(new ProcessStartInfo("ssh", item.SshHost!) { UseShellExecute = false }); } catch { } },
    _ => () => Process.Start(new ProcessStartInfo(
      $"{BitwardenCliService.ServerUrl}/#/vault?itemId={Uri.EscapeDataString(item.Id)}")
    { UseShellExecute = true }),
  };

  internal static CommandContextItem[] BuildContextItems(BitwardenItem item, BitwardenCliService? service = null)
  {
    var items = new List<CommandContextItem>();
    var id = item.Id;
    var reprompt = item.Reprompt == 1 ? service : null;

    switch (item.Type)
    {
      case BitwardenItemType.Login:
        AddLoginContextItems(items, item, id, reprompt);
        break;
      case BitwardenItemType.SecureNote:
        AddNoteContextItems(items, item, id, reprompt);
        break;
      case BitwardenItemType.Card:
        AddCardContextItems(items, item, id, reprompt);
        break;
      case BitwardenItemType.Identity:
        AddIdentityContextItems(items, item, id, reprompt);
        break;
      case BitwardenItemType.SshKey:
        AddSshKeyContextItems(items, item, id, reprompt);
        break;
    }

    AddCustomFieldContextItems(items, item, id, reprompt);

    var serverUrl = BitwardenCliService.ServerUrl;
    if (!string.IsNullOrEmpty(serverUrl))
    {
      items.Add(new CommandContextItem(Track(id, BuildOpenInWebVaultCommand(id)))
      {
        Title = "View in Web Vault",
        Icon = new IconInfo("\uE774"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.O),
      });
    }

    return items.ToArray();
  }

  internal static Tag[] BuildTags(BitwardenItem item, bool showWatchtowerTags = true, ForegroundContext? context = null, bool showContextTag = true, string totpTagStyle = "off", bool showPasskeyTag = true, bool showProtectedTag = true)
  {
    var tags = new List<Tag>();

    if (AccessTracker.IsLastCopied(item.Id))
      tags.Add(new Tag("Recent") { Foreground = ColorHelpers.FromRgb(0x87, 0xD9, 0x6C) });

    if (showProtectedTag && item.Reprompt == 1)
      tags.Add(new Tag("\uD83D\uDD12") { Foreground = ColorHelpers.FromRgb(0xFF, 0xD1, 0x73) });

    if (item.Favorite)
      tags.Add(new Tag("\u2605") { Foreground = ColorHelpers.FromRgb(0xFA, 0xCC, 0x6E) });

    if (showContextTag && context != null && ContextAwarenessService.ContextScore(context, item) > 0)
    {
      tags.Add(new Tag("Context") { Foreground = ColorHelpers.FromRgb(0x40, 0x9F, 0xFF) });
    }

    if (totpTagStyle != "off" && item.HasTotp)
    {
      var totpTag = totpTagStyle == "live" ? GetLiveTotpTag(item.TotpSecret!) : GetStaticTotpTag();
      if (totpTag != null)
        tags.Add(totpTag);
    }

    if (showPasskeyTag && item.HasPasskey)
      tags.Add(new Tag("Passkey") { Foreground = ColorHelpers.FromRgb(0xA0, 0xC4, 0xFF) });

    if (showWatchtowerTags)
      AddWatchtowerTags(tags, item);

    return tags.Count > 0 ? [.. tags] : [];
  }

  private static Tag GetStaticTotpTag() =>
    new Tag("2FA") { Foreground = ColorHelpers.FromRgb(0x90, 0xE1, 0xC6) };

  private static Tag? GetLiveTotpTag(string totpSecret)
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

    if (item.Uris.Count > 0 && item.Uris.Any(u => u.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
      tags.Add(new Tag("Insecure URL") { Foreground = ColorHelpers.FromRgb(0xF2, 0x87, 0x79) });
  }

  private static void AddLoginContextItems(List<CommandContextItem> items, BitwardenItem item, string id, BitwardenCliService? reprompt = null)
  {
    if (!string.IsNullOrEmpty(item.Username))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.Username!, "Username", reprompt))
      {
        Title = "Copy Username",
        Icon = new IconInfo("\uE77B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.Password))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.Password!, "Password", reprompt, isSensitive: true))
      {
        Title = "Copy Password",
        Icon = new IconInfo("\uE72E"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.C),
      });
    }

    if (item.HasTotp)
    {
      items.Add(new CommandContextItem(SensitiveCommand(id, () => CopyOtpCommand.CopyToClipboard(item.TotpSecret!), "OTP", reprompt))
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

  private static void AddNoteContextItems(List<CommandContextItem> items, BitwardenItem item, string id, BitwardenCliService? reprompt = null)
  {
    if (!string.IsNullOrEmpty(item.Notes))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.Notes!, "Notes", reprompt, isSensitive: true))
      {
        Title = "Copy Notes",
        Icon = new IconInfo("\uE70B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }
  }

  private static void AddCardContextItems(List<CommandContextItem> items, BitwardenItem item, string id, BitwardenCliService? reprompt = null)
  {
    if (!string.IsNullOrEmpty(item.CardNumber))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.CardNumber!, "Card Number", reprompt, isSensitive: true))
      {
        Title = "Copy Card Number",
        Icon = new IconInfo("\uE8C7"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.CardCode))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.CardCode!, "Security Code", reprompt, isSensitive: true))
      {
        Title = "Copy Security Code",
        Icon = new IconInfo("\uE72E"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.CardholderName))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.CardholderName!, "Cardholder Name", reprompt))
      {
        Title = "Copy Cardholder Name",
        Icon = new IconInfo("\uE77B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, alt: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.CardExpiration))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.CardExpiration!, "Expiration", reprompt))
      {
        Title = "Copy Expiration",
        Icon = new IconInfo("\uE787"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.E),
      });
    }
  }

  private static void AddIdentityContextItems(List<CommandContextItem> items, BitwardenItem item, string id, BitwardenCliService? reprompt = null)
  {
    if (!string.IsNullOrEmpty(item.IdentityEmail))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.IdentityEmail!, "Email", reprompt))
      {
        Title = "Copy Email",
        Icon = new IconInfo("\uE715"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.IdentityFullName))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.IdentityFullName!, "Name", reprompt))
      {
        Title = "Copy Name",
        Icon = new IconInfo("\uE77B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.IdentityPhone))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.IdentityPhone!, "Phone", reprompt))
      {
        Title = "Copy Phone",
        Icon = new IconInfo("\uE717"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, alt: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.IdentityUsername))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.IdentityUsername!, "Username", reprompt))
      {
        Title = "Copy Username",
        Icon = new IconInfo("\uE77B"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.U),
      });
    }

    if (!string.IsNullOrEmpty(item.IdentityAddress))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.IdentityAddress!, "Address", reprompt))
      {
        Title = "Copy Address",
        Icon = new IconInfo("\uE80F"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.A),
      });
    }
  }

  private static void AddSshKeyContextItems(List<CommandContextItem> items, BitwardenItem item, string id, BitwardenCliService? reprompt = null)
  {
    if (!string.IsNullOrEmpty(item.SshPublicKey))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.SshPublicKey!, "Public Key", reprompt))
      {
        Title = "Copy Public Key",
        Icon = new IconInfo("\uE8D7"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, vkey: VirtualKey.C),
      });
    }

    if (!string.IsNullOrEmpty(item.SshFingerprint))
    {
      items.Add(new CommandContextItem(CopyFieldCommand(id, item.SshFingerprint!, "Fingerprint", reprompt))
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

  private static void AddCustomFieldContextItems(List<CommandContextItem> items, BitwardenItem item, string id, BitwardenCliService? reprompt = null)
  {
    if (item.CustomFields.Count == 0)
      return;

    foreach (var (fieldName, field) in item.CustomFields)
    {
      if (string.IsNullOrEmpty(field.Value))
        continue;

      // Skip the 'host' custom field as it's handled by SSH context items
      if (item.Type == BitwardenItemType.SshKey && fieldName.Equals("host", StringComparison.OrdinalIgnoreCase))
        continue;

      ICommand copyCmd = CopyFieldCommand(id, field.Value, fieldName, reprompt, isSensitive: field.IsHidden);

      items.Add(new CommandContextItem(copyCmd)
      {
        Title = $"Copy \"{fieldName}\"",
        Icon = new IconInfo(field.IsHidden ? "\uE72E" : "\uE8C8"),
      });
    }
  }

  private static readonly UISettings _uiSettings = new();

  private static bool IsDarkTheme() =>
    _uiSettings.GetColorValue(UIColorType.Background).R < 128;

  private static string GetVaultBaseUrl()
  {
    var serverUrl = BitwardenCliService.ServerUrl;
    if (string.IsNullOrEmpty(serverUrl) || serverUrl.Contains("bitwarden.com", StringComparison.OrdinalIgnoreCase))
      return "https://vault.bitwarden.com";
    if (serverUrl.Contains("bitwarden.eu", StringComparison.OrdinalIgnoreCase))
      return "https://vault.bitwarden.eu";
    return serverUrl;
  }

  internal static string SanitizeBrandSlug(string brand) =>
    UnsafeSlugChars().Replace(brand.ToLowerInvariant().Replace(" ", "_"), "");

  internal static string GetCardBrandImageUrl(string brand, bool isDark)
  {
    var slug = SanitizeBrandSlug(brand);
    var theme = isDark ? "dark" : "light";
    return $"{GetVaultBaseUrl()}/images/{slug}-{theme}.png";
  }

  internal static IconInfo GetCardBrandIcon(string brand)
  {
    var isDark = IsDarkTheme();
    var slug = SanitizeBrandSlug(brand);
    var theme = isDark ? "dark" : "light";
    return FaviconService.GetOrQueue(
      $"card-brand:{slug}:{theme}",
      GetCardBrandImageUrl(brand, isDark),
      new IconInfo("\uE8C7"));
  }

  internal static IconInfo GetFaviconIcon(string? uri)
  {
    if (string.IsNullOrEmpty(uri))
      return new IconInfo("\uE774");

    try
    {
      var host = new Uri(uri).Host;
      if (string.IsNullOrEmpty(host))
        return new IconInfo("\uE774");

      // Match Bitwarden region CDN behaviour:
      //   - US cloud (serverUrl null):  https://icons.bitwarden.net/{domain}/icon.png
      //   - EU cloud:                   https://icons.bitwarden.eu/{domain}/icon.png
      //   - Self-hosted (override set): {iconsUrl}/{domain}/icon.png
      //   - Self-hosted (no override):  {serverUrl}/icons/{domain}/icon.png
      var serverUrl = BitwardenCliService.ServerUrl;
      var iconsUrl = BitwardenCliService.IconsUrl;
      string iconBase;
      if (!string.IsNullOrEmpty(iconsUrl))
        iconBase = iconsUrl;
      else if (string.IsNullOrEmpty(serverUrl) || serverUrl.Contains("bitwarden.com", StringComparison.OrdinalIgnoreCase))
        iconBase = "https://icons.bitwarden.net";
      else if (serverUrl.Contains("bitwarden.eu", StringComparison.OrdinalIgnoreCase))
        iconBase = "https://vault.bitwarden.eu/icons";
      else
        iconBase = serverUrl + "/icons";
      var iconUrl = $"{iconBase}/{host}/icon.png";

      return FaviconService.GetOrQueue(host, iconUrl);
    }
    catch
    {
      return new IconInfo("\uE774");
    }
  }

  internal static OpenUrlCommand BuildOpenInWebVaultCommand(string itemId) =>
    new($"{BitwardenCliService.ServerUrl}/#/vault?itemId={Uri.EscapeDataString(itemId)}")
    {
      Name = "View in Web Vault",
    };

  private static AnonymousCommand BuildSshCommand(string host) => new(() =>
  {
    try { Process.Start(new ProcessStartInfo("ssh", host) { UseShellExecute = false }); }
    catch { }
  })
  {
    Name = $"SSH to {host}",
    Result = CommandResult.Dismiss(),
  };

  internal static bool IsValidSshHost(string? host) =>
      !string.IsNullOrEmpty(host) && SshHostPattern().IsMatch(host);

  [GeneratedRegex(@"^[\w.+-]+@[\w.-]+$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
  private static partial Regex SshHostPattern();

  [GeneratedRegex(@"[^a-z0-9_]", RegexOptions.None, matchTimeoutMilliseconds: 100)]
  private static partial Regex UnsafeSlugChars();

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

  private static ICommand CopyFieldCommand(string itemId, string text, string label, BitwardenCliService? reprompt, bool isSensitive = false)
  {
    if (reprompt != null && !RepromptPage.IsWithinGracePeriod())
      return new RepromptPage(reprompt, () => SecureClipboardService.CopySensitive(text), label);
    return Track(itemId, isSensitive
      ? CopySensitive(text, label)
      : CopyNonSensitive(text, label));
  }

  private static ICommand SensitiveCommand(string itemId, Action action, string label, BitwardenCliService? reprompt)
  {
    if (reprompt != null && !RepromptPage.IsWithinGracePeriod())
      return new RepromptPage(reprompt, action, label);
    return Track(itemId, new AnonymousCommand(action) { Name = $"Copy {label}", Result = CommandResult.ShowToast($"Copied {label} to clipboard") });
  }

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
