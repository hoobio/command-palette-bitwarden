using System;

namespace HoobiBitwardenCompanion.Services;

// Passed to pages as the navigation parameter: the connected vault client, the launch options, and a
// callback to close the host window (e.g. after a successful unlock).
internal sealed record CompanionContext(VaultClient? Client, LaunchOptions Options, Action? RequestClose = null);
