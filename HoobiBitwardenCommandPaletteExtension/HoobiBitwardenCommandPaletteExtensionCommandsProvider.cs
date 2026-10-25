using System;
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
            new CommandItem(new CompanionCommand("New item", _service, companion.ShowNewItem))
            {
                Title = "New Bitwarden item",
                Subtitle = "Create a new login, new password, note, card, identity or SSH key",
                Icon = new IconInfo(""),
            },
            new CommandItem(new CompanionCommand("Generate password", _service, companion.ShowGenerator))
            {
                Title = "Generate password",
                Subtitle = "Open the generator for a new password or passphrase",
                Icon = new IconInfo(""),
            },
        ];

        Settings = _settingsManager.Settings;
        _ = _service.WarmCacheAsync();
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => [_fallbackItem];

    private sealed partial class CompanionCommand : InvokableCommand
    {
        private readonly BitwardenCliService _service;
        private readonly Action _show;

        public CompanionCommand(string name, BitwardenCliService service, Action show)
        {
            _service = service;
            _show = show;
            Name = name;
        }

        public override ICommandResult Invoke()
        {
            if (!_service.IsUnlocked)
                return CommandResult.ShowToast("Unlock your vault in Bitwarden first.");
            _show();
            return CommandResult.Dismiss();
        }
    }
}
