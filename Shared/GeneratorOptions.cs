using System;

// Shared generator options, linked into BOTH the extension and the companion (no duplicate model).
// The fields/defaults live here; each project adds its own projection as an extension method:
// the extension's ToCliArgs() (build `bw generate` args) and the companion's ToIpcArgs() (build the
// IPC payload). GeneratorOptionsProvider is the single shaped accessor for secure defaults that a
// future settings UI will source.
namespace HoobiBitwardenCompanionIpc;

internal enum GeneratorMode
{
    Password = 0,
    Passphrase = 1,
}

internal sealed record GeneratorOptions
{
    public const int MinLength = 5;
    public const int MaxLength = 128;
    public const int MinWords = 3;
    public const int MaxWords = 20;

    public GeneratorMode Mode { get; init; } = GeneratorMode.Password;

    // Password mode
    public int Length { get; init; } = 20;
    public bool Uppercase { get; init; } = true;
    public bool Lowercase { get; init; } = true;
    public bool Numbers { get; init; } = true;
    public bool Symbols { get; init; } = true;
    public int MinNumber { get; init; } = 1;
    public int MinSpecial { get; init; } = 1;
    public bool AvoidAmbiguous { get; init; } = true;

    // Passphrase mode
    public int Words { get; init; } = 6;
    public string Separator { get; init; } = "-";
    public bool Capitalize { get; init; }
    public bool IncludeNumber { get; init; } = true;
}

internal static class GeneratorOptionsProvider
{
    public static GeneratorOptions DefaultPassword() => new();

    public static GeneratorOptions DefaultPassphrase() => new() { Mode = GeneratorMode.Passphrase };
}
