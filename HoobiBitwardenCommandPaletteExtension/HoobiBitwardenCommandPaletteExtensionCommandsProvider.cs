using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCommandPaletteExtension.Companion;
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
        var companion = new CompanionHost(new CompanionOps(_service));
        CompanionHost.Current = companion;
        _commands = [
            new CommandItem(new HoobiBitwardenCommandPaletteExtensionPage(_service, _settingsManager))
            {
                Title = DisplayName,
                Subtitle = "Search your vault",
            },
            new CommandItem(new AnonymousCommand(companion.ShowVault) { Name = "Open vault", Result = CommandResult.Dismiss() })
            {
                Title = "Open vault",
                Subtitle = "Open the Bitwarden window",
                Icon = new IconInfo(""),
            },
        ];

        Settings = _settingsManager.Settings;
        _ = _service.WarmCacheAsync();
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => [_fallbackItem];
}
