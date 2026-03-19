using System;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class SetServerPage : DynamicListPage
{
  private readonly Action<ServerConfig>? _onSubmit;
  private string _searchText = string.Empty;

  public SetServerPage(BitwardenCliService service, Action<ServerConfig>? onSubmit = null)
  {
    _ = service;
    _onSubmit = onSubmit;
    Name = "Set Server";
    Title = "Set Bitwarden Server";
    Icon = new IconInfo("\uE774");
    PlaceholderText = "Choose a preset or type a self-hosted URL...";
    IsLoading = false;
  }

  public override void UpdateSearchText(string oldSearch, string newSearch)
  {
    _searchText = newSearch;
    RaiseItemsChanged();
  }

  private static string DetectPreset(string? serverUrl)
  {
    if (string.IsNullOrEmpty(serverUrl) || serverUrl.Contains("bitwarden.com", StringComparison.OrdinalIgnoreCase))
      return "bitwarden.com";
    if (serverUrl.Contains("bitwarden.eu", StringComparison.OrdinalIgnoreCase))
      return "bitwarden.eu";
    return "self-hosted";
  }

  public override IListItem[] GetItems()
  {
    var currentUrl = BitwardenCliService.ServerUrl ?? "";
    var preset = DetectPreset(currentUrl);
    var typed = _searchText.Trim();
    var items = new System.Collections.Generic.List<IListItem>();

    // If the user typed something that looks like a URL, show "use this URL" + "advanced" items.
    if (typed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        typed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
      var isValid = Uri.TryCreate(typed, UriKind.Absolute, out _);
      var useItem = new ListItem(new AnonymousCommand(() => _onSubmit?.Invoke(new ServerConfig(typed)))
      {
        Name = "Use this URL",
        Result = isValid ? CommandResult.GoBack() : CommandResult.KeepOpen(),
      })
      {
        Title = typed,
        Subtitle = isValid ? "Use as self-hosted server URL" : "Invalid URL",
        Icon = new IconInfo("\uE774"),
      };
      if (!isValid)
        useItem.Tags = [new Tag("Invalid") { Foreground = ColorHelpers.FromRgb(0xED, 0x82, 0x74) }];
      items.Add(useItem);

      if (isValid)
      {
        items.Add(new ListItem(new SelfHostedAdvancedPage(typed, _onSubmit))
        {
          Title = "Advanced URL overrides...",
          Subtitle = "Configure Web Vault, API, Identity, Icons and other URLs",
          Icon = new IconInfo("\uE713"),
        });
      }
    }

    // Cloud preset
    var cloudItem = new ListItem(new AnonymousCommand(() => _onSubmit?.Invoke(new ServerConfig("bitwarden.com")))
    { Name = "Select", Result = CommandResult.GoBack() })
    {
      Title = "Bitwarden Cloud",
      Subtitle = "bitwarden.com",
      Icon = new IconInfo("\uE774"),
    };
    if (preset == "bitwarden.com")
      cloudItem.Tags = [new Tag("Current") { Background = ColorHelpers.FromRgb(0x3E, 0x7C, 0xCF) }];
    items.Add(cloudItem);

    // EU preset
    var euItem = new ListItem(new AnonymousCommand(() => _onSubmit?.Invoke(new ServerConfig("bitwarden.eu")))
    { Name = "Select", Result = CommandResult.GoBack() })
    {
      Title = "Bitwarden EU",
      Subtitle = "bitwarden.eu",
      Icon = new IconInfo("\uE774"),
    };
    if (preset == "bitwarden.eu")
      euItem.Tags = [new Tag("Current") { Background = ColorHelpers.FromRgb(0x3E, 0x7C, 0xCF) }];
    items.Add(euItem);

    // Self-hosted hint (shown when not typing a URL)
    if (!typed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
    {
      var hintSubtitle = preset == "self-hosted"
          ? currentUrl
          : "Type your server URL above (e.g. https://vault.example.com)";
      var hintItem = new ListItem(new NoOpCommand())
      {
        Title = "Self-hosted",
        Subtitle = hintSubtitle,
        Icon = new IconInfo("\uE774"),
      };
      if (preset == "self-hosted")
        hintItem.Tags = [new Tag("Current") { Background = ColorHelpers.FromRgb(0x3E, 0x7C, 0xCF) }];
      items.Add(hintItem);
    }

    return [.. items];
  }
}

internal sealed partial class SelfHostedAdvancedPage : ContentPage
{
  public SelfHostedAdvancedPage(string baseUrl, Action<ServerConfig>? onSubmit)
  {
    Name = "Advanced";
    Title = "Advanced URL Overrides";
    Icon = new IconInfo("\uE713");
    var form = new SelfHostedAdvancedForm(baseUrl, onSubmit);
    _ = form; // assigned via GetContent
    _form = form;
  }

  private readonly SelfHostedAdvancedForm _form;
  public override IContent[] GetContent() => [_form];
}

internal sealed partial class SelfHostedAdvancedForm : FormContent
{
  private readonly string _baseUrl;
  private readonly Action<ServerConfig>? _onSubmit;

  public SelfHostedAdvancedForm(string baseUrl, Action<ServerConfig>? onSubmit)
  {
    _baseUrl = baseUrl;
    _onSubmit = onSubmit;
    TemplateJson = $$"""
    {
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "type": "AdaptiveCard",
        "version": "1.6",
        "body": [
            {
                "type": "TextBlock",
                "text": "Leave blank to derive from {{baseUrl}}",
                "isSubtle": true,
                "size": "small",
                "wrap": true
            },
            {
                "type": "Input.Text",
                "id": "WebVaultUrl",
                "label": "Web Vault URL",
                "placeholder": "{base URL}/"
            },
            {
                "type": "Input.Text",
                "id": "ApiUrl",
                "label": "API URL",
                "placeholder": "{base URL}/api"
            },
            {
                "type": "Input.Text",
                "id": "IdentityUrl",
                "label": "Identity URL",
                "placeholder": "{base URL}/identity"
            },
            {
                "type": "Input.Text",
                "id": "IconsUrl",
                "label": "Icons URL",
                "placeholder": "{base URL}/icons"
            },
            {
                "type": "Input.Text",
                "id": "NotificationsUrl",
                "label": "Notifications URL",
                "placeholder": "{base URL}/notifications"
            },
            {
                "type": "Input.Text",
                "id": "EventsUrl",
                "label": "Events URL",
                "placeholder": "{base URL}/events"
            },
            {
                "type": "Input.Text",
                "id": "KeyConnectorUrl",
                "label": "Key Connector URL",
                "placeholder": "{base URL}/key-connector"
            }
        ],
        "actions": [
            {
                "type": "Action.Submit",
                "title": "Save"
            }
        ]
    }
    """;
  }

  public override ICommandResult SubmitForm(string inputs, string data)
  {
    var formInput = JsonNode.Parse(inputs)?.AsObject();

    var webVaultUrl = ReadOptionalUrl(formInput, "WebVaultUrl", out var e1); if (e1 != null) return CommandResult.ShowToast(e1);
    var apiUrl = ReadOptionalUrl(formInput, "ApiUrl", out var e2); if (e2 != null) return CommandResult.ShowToast(e2);
    var identityUrl = ReadOptionalUrl(formInput, "IdentityUrl", out var e3); if (e3 != null) return CommandResult.ShowToast(e3);
    var iconsUrl = ReadOptionalUrl(formInput, "IconsUrl", out var e4); if (e4 != null) return CommandResult.ShowToast(e4);
    var notificationsUrl = ReadOptionalUrl(formInput, "NotificationsUrl", out var e5); if (e5 != null) return CommandResult.ShowToast(e5);
    var eventsUrl = ReadOptionalUrl(formInput, "EventsUrl", out var e6); if (e6 != null) return CommandResult.ShowToast(e6);
    var keyConnectorUrl = ReadOptionalUrl(formInput, "KeyConnectorUrl", out var e7); if (e7 != null) return CommandResult.ShowToast(e7);

    _onSubmit?.Invoke(new ServerConfig(_baseUrl,
      WebVaultUrl: webVaultUrl, ApiUrl: apiUrl, IdentityUrl: identityUrl,
      IconsUrl: iconsUrl, NotificationsUrl: notificationsUrl,
      EventsUrl: eventsUrl, KeyConnectorUrl: keyConnectorUrl));
    return CommandResult.GoBack();
  }

  private static string? ReadOptionalUrl(JsonObject? formInput, string key, out string? error)
  {
    error = null;
    var val = formInput?[key]?.GetValue<string>()?.Trim();
    if (string.IsNullOrEmpty(val)) return null;
    if (!Uri.TryCreate(val, UriKind.Absolute, out var uri) || uri.Scheme != "https")
    {
      error = $"Invalid {key}: must start with https://";
      return null;
    }
    return val;
  }
}

