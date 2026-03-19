namespace HoobiBitwardenCommandPaletteExtension.Models;

internal sealed record ServerConfig(
    string BaseUrl,
    string? WebVaultUrl = null,
    string? ApiUrl = null,
    string? IdentityUrl = null,
    string? IconsUrl = null,
    string? NotificationsUrl = null,
    string? EventsUrl = null,
    string? KeyConnectorUrl = null
);
