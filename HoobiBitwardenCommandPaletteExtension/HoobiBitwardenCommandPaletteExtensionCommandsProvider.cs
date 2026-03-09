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

    public HoobiBitwardenCommandPaletteExtensionCommandsProvider()
    {
#if DEBUG
        DisplayName = "Bitwarden (Dev)";
#else
        DisplayName = "Bitwarden";
#endif
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _settingsManager = new BitwardenSettingsManager();
        var service = new BitwardenCliService(_settingsManager);
        _fallbackItem = new BitwardenFallbackItem(service);
        _commands = [
            new CommandItem(new HoobiBitwardenCommandPaletteExtensionPage(service, _settingsManager))
            {
                Title = "Bitwarden",
                Subtitle = "Search your vault",
            },
        ];

        Settings = _settingsManager.Settings;

        _ = Task.Run(service.WarmCacheAsync);
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => [_fallbackItem];
}
