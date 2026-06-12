using System.Text.Json.Nodes;
using HoobiBitwardenCompanionIpc;

namespace HoobiBitwardenCompanion.Services;

// Companion-only projection of the shared GeneratorOptions: the IPC payload sent to the extension,
// which runs `bw generate`. The companion never generates locally.
internal static class GeneratorOptionsIpcExtensions
{
    public static JsonObject ToIpcArgs(this GeneratorOptions o) => new()
    {
        [IpcFields.Mode] = o.Mode == GeneratorMode.Passphrase ? "passphrase" : "password",
        [IpcFields.Length] = o.Length,
        [IpcFields.Uppercase] = o.Uppercase,
        [IpcFields.Lowercase] = o.Lowercase,
        [IpcFields.Numbers] = o.Numbers,
        [IpcFields.Symbols] = o.Symbols,
        [IpcFields.MinNumber] = o.MinNumber,
        [IpcFields.MinSpecial] = o.MinSpecial,
        [IpcFields.AvoidAmbiguous] = o.AvoidAmbiguous,
        [IpcFields.Words] = o.Words,
        [IpcFields.Separator] = o.Separator,
        [IpcFields.Capitalize] = o.Capitalize,
        [IpcFields.IncludeNumber] = o.IncludeNumber,
    };
}
