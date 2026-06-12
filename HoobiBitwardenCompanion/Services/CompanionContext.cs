namespace HoobiBitwardenCompanion.Services;

// Passed to pages as the navigation parameter: the connected vault client plus the launch options.
internal sealed record CompanionContext(VaultClient? Client, LaunchOptions Options);
