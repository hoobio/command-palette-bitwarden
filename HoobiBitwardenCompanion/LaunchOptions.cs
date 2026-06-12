using System;
using System.Collections.Generic;

namespace HoobiBitwardenCompanion;

internal enum CompanionMode
{
    Login = 0,
    ItemDetail = 1,
    Generate = 2,
    QuickRotate = 3,
}

// What the companion was asked to do, parsed from the launch command line the extension passes
// (e.g. `--mode item --id <guid>`). The extension is the vault authority; the id just tells the
// companion which item to drive over IPC.
internal sealed record LaunchOptions(CompanionMode Mode, string? ItemId)
{
    public static LaunchOptions Parse(string[] argv)
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < argv.Length - 1; i++)
        {
            if (argv[i].StartsWith("--", StringComparison.Ordinal))
                args[argv[i][2..]] = argv[i + 1];
        }

        var mode = args.TryGetValue("mode", out var m) ? m.ToLowerInvariant() : "login";
        var itemId = args.GetValueOrDefault("id");

        return mode switch
        {
            "item" or "detail" => new LaunchOptions(CompanionMode.ItemDetail, itemId),
            "generate" => new LaunchOptions(CompanionMode.Generate, null),
            "rotate" => new LaunchOptions(CompanionMode.QuickRotate, itemId),
            _ => new LaunchOptions(CompanionMode.Login, null),
        };
    }
}
