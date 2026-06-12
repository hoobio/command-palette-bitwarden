using System;
using System.Collections.Generic;
using HoobiBitwardenCompanionIpc;

namespace HoobiBitwardenCompanion;

internal enum CompanionMode
{
    Login = 0,
    ItemDetail = 1,
    Generate = 2,
    QuickRotate = 3,
}

// What the companion was asked to do, parsed from the launch command line the extension passes
// (e.g. `--mode item --id <guid> --pipe <name>`). The extension is the vault authority; the id tells
// the companion which item to drive over IPC and the pipe name is the channel back to the extension.
internal sealed record LaunchOptions(CompanionMode Mode, string? ItemId, string? PipeName)
{
    public static LaunchOptions Parse(string[] argv)
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < argv.Length - 1; i++)
        {
            if (argv[i].StartsWith("--", StringComparison.Ordinal))
                args[argv[i]] = argv[i + 1];
        }

        var mode = args.TryGetValue(IpcLaunchArgs.Mode, out var m) ? m.ToLowerInvariant() : "login";
        var itemId = args.GetValueOrDefault(IpcLaunchArgs.ItemId);
        var pipe = args.GetValueOrDefault(IpcLaunchArgs.Pipe);

        var parsedMode = mode switch
        {
            "item" or "detail" => CompanionMode.ItemDetail,
            "generate" => CompanionMode.Generate,
            "rotate" => CompanionMode.QuickRotate,
            _ => CompanionMode.Login,
        };

        return new LaunchOptions(parsedMode, itemId, pipe);
    }
}
