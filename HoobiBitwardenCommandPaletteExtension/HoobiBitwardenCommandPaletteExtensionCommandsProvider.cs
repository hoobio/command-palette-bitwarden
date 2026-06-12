using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension;

public partial class HoobiBitwardenCommandPaletteExtensionCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly BitwardenFallbackItem _fallbackItem;
    private readonly BitwardenSettingsManager _settingsManager;
    private readonly BitwardenCliService _service;

    public HoobiBitwardenCommandPaletteExtensionCommandsProvider()
    {
#if CHANNEL_DEV
        DisplayName = "Bitwarden (Dev)";
#elif CHANNEL_PRERELEASE
        DisplayName = "Bitwarden (Prerelease)";
#else
        DisplayName = "Bitwarden";
#endif
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _settingsManager = new BitwardenSettingsManager();
        _service = new BitwardenCliService(_settingsManager);
        _fallbackItem = new BitwardenFallbackItem(_service, _settingsManager);
        _commands = [
            new CommandItem(new HoobiBitwardenCommandPaletteExtensionPage(_service, _settingsManager))
            {
#if CHANNEL_DEV
                Title = "Bitwarden (Dev)",
#elif CHANNEL_PRERELEASE
                Title = "Bitwarden (Prerelease)",
#else
                Title = "Bitwarden",
#endif
                Subtitle = "Search your vault",
            },
        ];

        Settings = _settingsManager.Settings;
        _ = _service.WarmCacheAsync();
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => [_fallbackItem];
}
