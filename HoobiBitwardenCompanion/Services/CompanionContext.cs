using System;

namespace HoobiBitwardenCompanion.Services;

// Passed to pages as the navigation parameter: the connected vault client, the launch options, a
// callback to close the host window (e.g. after a successful unlock), and the host window itself so
// pages can drive the title bar (item icon + name).
internal sealed record CompanionContext(VaultClient? Client, LaunchOptions Options, Action? RequestClose = null, MainWindow? Host = null);
