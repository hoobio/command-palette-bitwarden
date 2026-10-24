using System.IO;
using System.Runtime.CompilerServices;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

internal static class TestEnvironment
{
    // Live Sync is on by default, so an unlocked test service would otherwise read the developer's real CLI data.json and connect to their server.
    [ModuleInitializer]
    internal static void IsolateCliData() =>
        Environment.SetEnvironmentVariable("BITWARDENCLI_APPDATA_DIR", Path.Combine(Path.GetTempPath(), $"bw_cli_{Guid.NewGuid():N}"));
}
