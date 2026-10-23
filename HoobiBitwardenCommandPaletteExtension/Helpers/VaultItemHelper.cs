using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;
using Windows.UI.ViewManagement;
using HoobiBitwardenCommandPaletteExtension.Commands;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Pages;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Helpers;

internal static partial class VaultItemHelper
{
  internal static IconInfo GetIcon(BitwardenItem item, bool showWebsiteIcons = true, int priority = int.MaxValue) => item.Type switch
  {
    BitwardenItemType.Login => showWebsiteIcons ? GetFaviconIcon(item, priority) : new IconInfo("\uE774"),
    BitwardenItemType.SecureNote => new IconInfo("\uE70B"),
    BitwardenItemType.Card => showWebsiteIcons && item.CardBrand != null
      ? GetCardBrandIcon(item.CardBrand, priority)
      : new IconInfo("\uE8C7"),
    BitwardenItemType.Identity => new IconInfo("\uE77B"),
    BitwardenItemType.SshKey => new IconInfo("\uE8D7"),
    _ => new IconInfo("\uE72E"),
  };

  internal static ICommand GetDefaultCommand(BitwardenItem item, BitwardenCliService? service = null)
  {
    if (item.Reprompt == 1 && service != null && !RepromptPage.IsWithinGracePeriod(item.Id))
      return new RepromptPage(service, item.Id, BuildDefaultAction(item), "Open");

    return Track(item.Id, item.Type switch
    {
      BitwardenItemType.Login when ToSafeWebUrl(item.FirstUri) is { } url => new OpenUrlCommand(url),
      BitwardenItemType.SshKey when HasSshHost(item) => BuildSshCommand(item),
      _ => BuildOpenInWebVaultCommand(item.Id),
    });
  }

  internal static string? ToSafeWebUrl(string? uri)
  {
    if (string.IsNullOrWhiteSpace(uri))
      return null;
    var trimmed = uri.Trim();
    var candidate = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
    if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
      return null;
    return parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps ? parsed.AbsoluteUri : null;
  }

  private static Action BuildDefaultAction(BitwardenItem item) => item.Type switch
  {
    BitwardenItemType.Login when ToSafeWebUrl(item.FirstUri) is { } url =>
      () => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }),
    BitwardenItemType.SshKey when HasSshHost(item) => () => StartSsh(item),
    _ => () => Process.Start(new ProcessStartInfo(
      $"{GetVaultBaseUrl()}/#/vault?itemId={Uri.EscapeDataString(item.Id)}")
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

  internal static Tag[] BuildTags(BitwardenItem item, bool showWatchtowerTags = true, ForegroundContext? context = null, bool showContextTag = true, string totpTagStyle = "off", bool showPasskeyTag = true, bool showProtectedTag = true, string? organizationName = null, string organizationTagStyle = "off")
  {
    var baseTags = BuildBaseTags(item, showWatchtowerTags, context, showContextTag, showPasskeyTag, showProtectedTag, organizationName, organizationTagStyle);

    if (totpTagStyle != "off" && item.HasTotp)
    {
      var totpTag = totpTagStyle == "live" ? BuildTotpTag(item) : GetStaticTotpTag();
      if (totpTag != null)
        return [.. baseTags, totpTag];
    }

    return baseTags;
  }

  internal static Tag[] BuildBaseTags(BitwardenItem item, bool showWatchtowerTags = true, ForegroundContext? context = null, bool showContextTag = true, bool showPasskeyTag = true, bool showProtectedTag = true, string? organizationName = null, string organizationTagStyle = "off")
  {
    var tags = new List<Tag>();

    if (AccessTracker.IsLastCopied(item.Id))
      tags.Add(new Tag("Recent") { Foreground = ColorHelpers.FromRgb(0x87, 0xD9, 0x6C) });

    if (showProtectedTag && item.Reprompt == 1)
      tags.Add(new Tag("\uD83D\uDD12") { Foreground = ColorHelpers.FromRgb(0xFF, 0xD1, 0x73) });

    if (item.Favorite)
      tags.Add(new Tag("\u2605") { Foreground = ColorHelpers.FromRgb(0xFA, 0xCC, 0x6E) });

    if (showContextTag)
    {
      var isContextMatch = context != null && ContextAwarenessService.ContextScore(context, item) > 0;
      if (isContextMatch)
        tags.Add(new Tag("Context") { Foreground = ColorHelpers.FromRgb(0x40, 0x9F, 0xFF) });
    }

    if (showPasskeyTag && item.HasPasskey)
      tags.Add(new Tag("Passkey") { Foreground = ColorHelpers.FromRgb(0xA0, 0xC4, 0xFF) });

    var orgTag = BuildOrganizationTag(item, organizationName, organizationTagStyle);
    if (orgTag != null)
      tags.Add(orgTag);

    if (showWatchtowerTags)
      AddWatchtowerTags(tags, item);

    return tags.Count > 0 ? [.. tags] : [];
  }

  private static readonly OptionalColor[] OrganizationColors =
  [
    ColorHelpers.FromRgb(0xB7, 0x9C, 0xE6),
    ColorHelpers.FromRgb(0xE2, 0xA8, 0xE3),
    ColorHelpers.FromRgb(0xF0, 0xA0, 0xB8),
    ColorHelpers.FromRgb(0xFF, 0xC0, 0x9E),
    ColorHelpers.FromRgb(0xD0, 0xE0, 0x7A),
    ColorHelpers.FromRgb(0x87, 0xE0, 0xB5),
    ColorHelpers.FromRgb(0x7B, 0xD0, 0xE5),
    ColorHelpers.FromRgb(0xA0, 0xA8, 0xE8),
  ];

  internal static OptionalColor GetOrganizationColor(string organizationId)
  {
    unchecked
    {
      uint hash = 2166136261;
      foreach (var c in organizationId)
        hash = (hash ^ c) * 16777619;
      return OrganizationColors[hash % (uint)OrganizationColors.Length];
    }
  }

  internal static Tag? BuildOrganizationTag(BitwardenItem item, string? organizationName, string organizationTagStyle)
  {
    if (organizationTagStyle == "off") return null;
    if (string.IsNullOrEmpty(item.OrganizationId)) return null;
    if (string.IsNullOrEmpty(organizationName))
    {
      DebugLogService.Log("OrgTag", $"Skipped org tag for item {item.Id}: orgId={item.OrganizationId} has no resolved name");
      return null;
    }

    var color = GetOrganizationColor(item.OrganizationId);
    var text = organizationTagStyle == "initials"
      ? GetOrganizationInitials(organizationName)
      : (organizationName.Length > 20 ? organizationName[..20] + "\u2026" : organizationName);
    return new Tag(text)
    {
      ToolTip = organizationName,
      Foreground = color,
    };
  }

  internal static string GetOrganizationInitials(string name)
  {
    var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length <= 1)
      return char.ToUpperInvariant(name.Trim()[0]).ToString();
    var sb = new System.Text.StringBuilder(3);
    foreach (var part in parts)
    {
      if (sb.Length >= 3) break;
      sb.Append(char.ToUpperInvariant(part[0]));
    }
    return sb.ToString();
  }

  internal static Tag? BuildTotpTag(BitwardenItem item) =>
    item.Reprompt == 1 && !RepromptPage.IsWithinGracePeriod(item.Id)
      ? GetStaticTotpTag()
      : BuildTotpTag(item.TotpSecret!);

  private static Tag GetStaticTotpTag() =>
    new Tag("2FA") { Foreground = ColorHelpers.FromRgb(0x90, 0xE1, 0xC6) };

  internal static Tag? BuildTotpTag(ProtectedSecret totpSecret)
  {
    try
    {
      var (code, remaining) = CopyOtpCommand.ComputeCode(totpSecret);
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

    if (item.Password != null)
    {
      if (item.HasWeakPassword)
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
    AddCopy(items, id, reprompt, item.Username, "Username", "\uE77B", Chord(VirtualKey.C));
    AddCopy(items, id, reprompt, item.Password, "Password", "\uE72E", Chord(VirtualKey.C, shift: true));

    if (item.HasTotp)
      AddCopyItem(items, CopyCommand(id, "OTP", reprompt, () => CopyOtpCommand.CopyToClipboard(item.TotpSecret!)), "OTP", "\uE916", Chord(VirtualKey.C, alt: true));

    if (ToSafeWebUrl(item.FirstUri) is { } url)
    {
      items.Add(new CommandContextItem(Track(id, new OpenUrlCommand(url)))
      {
        Title = "Open in Browser",
        Icon = new IconInfo("\uE774"),
        RequestedShortcut = KeyChordHelpers.FromModifiers(alt: true, vkey: VirtualKey.Enter),
      });
    }
  }

  private static void AddNoteContextItems(List<CommandContextItem> items, BitwardenItem item, string id, BitwardenCliService? reprompt = null)
  {
    AddCopy(items, id, reprompt, item.Notes, "Notes", "\uE70B", Chord(VirtualKey.C));
  }

  private static void AddCardContextItems(List<CommandContextItem> items, BitwardenItem item, string id, BitwardenCliService? reprompt = null)
  {
    AddCopy(items, id, reprompt, item.CardNumber, "Card Number", "\uE8C7", Chord(VirtualKey.C));
    AddCopy(items, id, reprompt, item.CardCode, "Security Code", "\uE72E", Chord(VirtualKey.C, shift: true));
    AddCopy(items, id, reprompt, item.CardholderName, "Cardholder Name", "\uE77B", Chord(VirtualKey.C, alt: true));
    AddCopy(items, id, reprompt, item.CardExpiration, "Expiration", "\uE787", Chord(VirtualKey.E));
  }

  private static void AddIdentityContextItems(List<CommandContextItem> items, BitwardenItem item, string id, BitwardenCliService? reprompt = null)
  {
    AddCopy(items, id, reprompt, item.IdentityEmail, "Email", "\uE715", Chord(VirtualKey.C));
    AddCopy(items, id, reprompt, item.IdentityFullName, "Name", "\uE77B", Chord(VirtualKey.C, shift: true));
    AddCopy(items, id, reprompt, item.IdentityPhone, "Phone", "\uE717", Chord(VirtualKey.C, alt: true));
    AddCopy(items, id, reprompt, item.IdentityUsername, "Username", "\uE77B", Chord(VirtualKey.U));
    AddCopy(items, id, reprompt, item.IdentityAddress, "Address", "\uE80F", Chord(VirtualKey.A, shift: true));
  }

  private static void AddSshKeyContextItems(List<CommandContextItem> items, BitwardenItem item, string id, BitwardenCliService? reprompt = null)
  {
    AddCopy(items, id, reprompt, item.SshPublicKey, "Public Key", "\uE8D7", Chord(VirtualKey.C));
    AddCopy(items, id, reprompt, item.SshFingerprint, "Fingerprint", "\uE928", Chord(VirtualKey.C, shift: true));

    if (HasSshHost(item))
    {
      items.Add(new CommandContextItem(Track(id, BuildSshCommand(item)))
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
      if (string.IsNullOrEmpty(field.Value) && field.Secret == null)
        continue;

      if (item.Type == BitwardenItemType.SshKey && fieldName.Equals("host", StringComparison.OrdinalIgnoreCase))
        continue;

      var copyCmd = field.Secret is { } secret
        ? CopySecretCommand(id, secret, fieldName, reprompt)
        : CopyFieldCommand(id, field.Value!, fieldName, reprompt);

      items.Add(new CommandContextItem(copyCmd)
      {
        Title = $"Copy \"{fieldName}\"",
        Icon = new IconInfo(field.IsHidden ? "\uE72E" : "\uE8C8"),
      });
    }
  }

  private static readonly UISettings _uiSettings = new();
  private static bool _isDarkThemeCached = ComputeIsDarkTheme();

  static VaultItemHelper()
  {
    _uiSettings.ColorValuesChanged += (_, _) => _isDarkThemeCached = ComputeIsDarkTheme();
  }

  private static bool ComputeIsDarkTheme() =>
    _uiSettings.GetColorValue(UIColorType.Background).R < 128;

  private static bool IsDarkTheme() => _isDarkThemeCached;

  private static string GetVaultBaseUrl()
  {
    var serverUrl = BitwardenCliService.ServerUrl;
    if (string.IsNullOrEmpty(serverUrl) || IsCloudHost(serverUrl, "bitwarden.com"))
      return "https://vault.bitwarden.com";
    if (IsCloudHost(serverUrl, "bitwarden.eu"))
      return "https://vault.bitwarden.eu";
    return serverUrl;
  }

  internal static bool IsCloudHost(string? serverUrl, string domain)
  {
    if (string.IsNullOrWhiteSpace(serverUrl))
      return false;
    var candidate = serverUrl.Contains("://", StringComparison.Ordinal) ? serverUrl.Trim() : "https://" + serverUrl.Trim();
    if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
      return false;
    var host = uri.IdnHost.TrimEnd('.');
    return host.Equals(domain, StringComparison.OrdinalIgnoreCase)
      || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
  }

  internal static string SanitizeBrandSlug(string brand) =>
    UnsafeSlugChars().Replace(brand.ToLowerInvariant().Replace(" ", "_"), "");

  internal static string GetCardBrandImageUrl(string brand, bool isDark)
  {
    var slug = SanitizeBrandSlug(brand);
    var theme = isDark ? "dark" : "light";
    return $"{GetVaultBaseUrl()}/images/{slug}-{theme}.png";
  }

  internal static IconInfo GetCardBrandIcon(string brand, int priority = int.MaxValue)
  {
    if (!BitwardenCliService.ServerUrlResolved)
      return new IconInfo("\uE8C7");

    var isDark = IsDarkTheme();
    var slug = SanitizeBrandSlug(brand);
    var theme = isDark ? "dark" : "light";
    return FaviconService.GetOrQueue(
      $"card-brand:{slug}:{theme}",
      GetCardBrandImageUrl(brand, isDark),
      new IconInfo("\uE8C7"),
      priority);
  }

  internal static IconInfo GetFaviconIcon(BitwardenItem item, int priority = int.MaxValue)
  {
    var host = item.FirstHost;
    if (string.IsNullOrEmpty(host))
      return new IconInfo("\uE774");

    var serverUrl = BitwardenCliService.ServerUrl;
    var iconsUrl = BitwardenCliService.IconsUrl;

    // Until the server resolves, an empty ServerUrl is unknown, not cloud: guessing leaks self-hosted hostnames to icons.bitwarden.net.
    if (string.IsNullOrEmpty(iconsUrl) && !BitwardenCliService.ServerUrlResolved)
      return new IconInfo("\uE774");

    string iconBase;
    if (!string.IsNullOrEmpty(iconsUrl))
      iconBase = iconsUrl;
    else if (string.IsNullOrEmpty(serverUrl) || IsCloudHost(serverUrl, "bitwarden.com"))
      iconBase = "https://icons.bitwarden.net";
    else if (IsCloudHost(serverUrl, "bitwarden.eu"))
      iconBase = "https://vault.bitwarden.eu/icons";
    else
      iconBase = serverUrl + "/icons";
    var iconUrl = $"{iconBase}/{host}/icon.png";

    return FaviconService.GetOrQueue(host, iconUrl, priority: priority);
  }

  internal static OpenUrlCommand BuildOpenInWebVaultCommand(string itemId) =>
    new($"{GetVaultBaseUrl()}/#/vault?itemId={Uri.EscapeDataString(itemId)}")
    {
      Name = "View in Web Vault",
    };

  private static bool HasSshHost(BitwardenItem item) => IsValidSshHost(item.RevealSshHost());

  private static void StartSsh(BitwardenItem item)
  {
    try
    {
      var host = item.RevealSshHost();
      if (IsValidSshHost(host))
        Process.Start(new ProcessStartInfo("ssh", host!) { UseShellExecute = false });
    }
    catch { }
  }

  private static AnonymousCommand BuildSshCommand(BitwardenItem item) => new(() => StartSsh(item))
  {
    Name = item.IsSshHostHidden ? "SSH Session" : $"SSH to {item.SshHost}",
    Result = CommandResult.Dismiss(),
  };

  internal static bool IsValidSshHost(string? host) =>
      !string.IsNullOrEmpty(host) && SshHostPattern().IsMatch(host);

  [GeneratedRegex(@"^[\w.+-]+@[\w.-]+$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
  private static partial Regex SshHostPattern();

  [GeneratedRegex(@"[^a-z0-9_]", RegexOptions.None, matchTimeoutMilliseconds: 100)]
  private static partial Regex UnsafeSlugChars();

  private static KeyChord Chord(VirtualKey key, bool shift = false, bool alt = false) =>
    KeyChordHelpers.FromModifiers(ctrl: true, shift: shift, alt: alt, vkey: key);

  private static void AddCopy(List<CommandContextItem> items, string id, BitwardenCliService? reprompt, string? text, string label, string glyph, KeyChord shortcut)
  {
    if (!string.IsNullOrEmpty(text))
      AddCopyItem(items, CopyFieldCommand(id, text, label, reprompt), label, glyph, shortcut);
  }

  private static void AddCopy(List<CommandContextItem> items, string id, BitwardenCliService? reprompt, ProtectedSecret? secret, string label, string glyph, KeyChord shortcut)
  {
    if (secret != null)
      AddCopyItem(items, CopySecretCommand(id, secret, label, reprompt), label, glyph, shortcut);
  }

  private static void AddCopyItem(List<CommandContextItem> items, ICommand command, string label, string glyph, KeyChord shortcut) =>
    items.Add(new CommandContextItem(command)
    {
      Title = "Copy " + label,
      Icon = new IconInfo(glyph),
      RequestedShortcut = shortcut,
    });

  private static ICommand CopyFieldCommand(string itemId, string text, string label, BitwardenCliService? reprompt) =>
    CopyCommand(itemId, label, reprompt, () => SecureClipboardService.CopySensitive(text), () => SecureClipboardService.CopyNonSensitive(text));

  private static ICommand CopySecretCommand(string itemId, ProtectedSecret secret, string label, BitwardenCliService? reprompt) =>
    CopyCommand(itemId, label, reprompt, () => SecureClipboardService.CopySensitive(secret));

  private static ICommand CopyCommand(string itemId, string label, BitwardenCliService? reprompt, Action sensitiveCopy, Action? plainCopy = null)
  {
    if (reprompt != null && !RepromptPage.IsWithinGracePeriod(itemId))
      return new RepromptPage(reprompt, itemId, sensitiveCopy, label);
    return Track(itemId, new AnonymousCommand(GuardLocked(plainCopy ?? sensitiveCopy)) { Name = $"Copy {label}", Result = CommandResult.ShowToast($"Copied {label} to clipboard") });
  }

  internal static Action GuardLocked(Action copy) => () =>
  {
    try { copy(); }
    catch (ObjectDisposedException)
    {
      DebugLogService.Log("Copy", "Secret disposed by a vault lock");
      ExtensionHost.ShowStatus(new StatusMessage { Message = "Vault locked. Unlock and try again.", State = MessageState.Warning }, StatusContext.Page);
    }
  };

  private static TrackedInvokable Track(string itemId, InvokableCommand inner) => new(inner, itemId);

  private sealed partial class TrackedInvokable : InvokableCommand
  {
    private readonly InvokableCommand _inner;
    private readonly string _itemId;

    public TrackedInvokable(InvokableCommand inner, string itemId)
    {
      _inner = inner;
      _itemId = itemId;
      _inner.PropChanged += OnInnerPropChanged;
    }

    public override string Name { get => _inner.Name; set => _inner.Name = value; }

    public override string Id { get => _inner.Id; set => _inner.Id = value; }

    public override IconInfo Icon { get => _inner.Icon; set => _inner.Icon = value; }

    public override ICommandResult Invoke()
    {
      // Tracking runs on a worker so the slow file write and UI rebuild do not delay the result returned to the host.
      var result = _inner.Invoke();
      var id = _itemId;
      _ = Task.Run(() => AccessTracker.Record(id));
      return result;
    }

    private void OnInnerPropChanged(object sender, IPropChangedEventArgs args)
    {
      if (args?.PropertyName is { } name)
        OnPropertyChanged(name);
    }
  }
}
