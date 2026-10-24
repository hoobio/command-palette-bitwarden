# Architecture

## Technology Stack

- **.NET 10** / C# targeting `net10.0-windows10.0.26100.0`
- **Microsoft.CommandPalette.Extensions SDK** - WinRT-based extension API for PowerToys Command Palette
- **Win32 P/Invoke** via `LibraryImport` - clipboard APIs, window management
- **UI Automation COM Interop** - browser address bar URL extraction
- **System.Security.Cryptography** - TOTP code generation (RFC 6238 HMAC)
- **Svg.Skia / SkiaSharp** - SVG rasterization (converts SVG favicons to PNG for display)
- **Bitwarden CLI** (`bw`) - vault operations (login, unlock, list, sync)
- **Windows Credential Manager** - session key persistence
- **MSIX** - packaging and distribution

## Project Structure

```
HoobiBitwardenCommandPaletteExtension/
├── Program.cs                          # COM server entry point
├── HoobiBitwardenCommandPaletteExtension.cs  # IExtension - returns providers to host
├── HoobiBitwardenCommandPaletteExtensionCommandsProvider.cs  # Instantiates pages, settings, fallback
├── BitwardenFallbackItem.cs            # Fallback search for short queries
├── Commands/
│   └── CopyOtpCommand.cs              # TOTP computation and secure copy
├── Companion/
│   ├── CompanionHost.cs               # Starts and reuses the companion process, owns the pipes
│   └── CompanionOps.cs                # Companion request handlers (validation, re-prompt, secrets)
├── Helpers/
│   └── VaultItemHelper.cs             # Icons, tags, default commands, context menus
├── Models/
│   └── BitwardenItem.cs               # Vault item data model (JSON deserialization)
├── Pages/
│   ├── HoobiBitwardenCommandPaletteExtensionPage.cs  # Main vault list (DynamicListPage)
│   ├── LoginPage.cs                    # Email/password login form
│   ├── SetServerPage.cs               # Server URL configuration form
│   └── UnlockVaultPage.cs             # Master password unlock form
├── Services/
│   ├── AccessTracker.cs               # Access history + recent item tracking
│   ├── BitwardenCliService.cs         # CLI interaction, caching, search, sorting
│   ├── BitwardenCliService.LiveSync.cs  # Starts/stops the notifications listener, debounced sync
│   ├── BitwardenSettingsManager.cs    # Extension settings (toggles + choices)
│   ├── ContextAwarenessService.cs     # Window detection, URL extraction, context scoring
│   ├── FaviconService.cs              # Favicon download, disk/memory cache, icon refresh events
│   ├── HubProtocol.cs                 # SignalR MessagePack framing, CLI state and hub URL parsing
│   ├── ICliProcess.cs                 # Process abstraction for unit testing
│   ├── NotificationsService.cs        # WebSocket connection to the notifications hub
│   ├── ProtectedSecret.cs             # In-memory encryption for secrets (CryptProtectMemory)
│   ├── SecureClipboardService.cs      # Clipboard security + auto-clear
│   ├── SessionStore.cs               # Windows Credential Manager wrapper
│   └── SvgRasterizer.cs              # SVG to PNG conversion via SkiaSharp
└── Assets/                            # Icons and images
HoobiBitwardenCompanion/               # Avalonia companion window app
├── Program.cs                         # Reads the pipe handles, starts Avalonia
├── Services/WindowCoordinator.cs      # Pipe requests from the extension, window registry
├── ViewModels/                        # Item and field view models
├── Views/                             # Main window and item window
└── Styles/                            # Hoobi design tokens, control styles, glyphs, fonts
Shared/                                # IPC contracts and PipeChannel, linked into both projects
```

## Data Flow

```
User opens Command Palette
    → HoobiBitwardenCommandPaletteExtensionPage.GetItems()
        → ContextAwarenessService.CaptureContext() (if enabled)
        → BitwardenCliService.SearchCached(query, context)
            → Filter by search text + prefix filters
            → Sort by: Recent > Favorite > Context > Last Accessed > Revision > Alpha
        → VaultItemHelper.BuildTags(item, watchtower, context)
        → Return ListItem[] to Command Palette
    
User selects an action
    → SecureClipboardService.CopySensitive() / .CopyPlain()
    → AccessTracker.Record(itemId)
    → CommandResult (dismiss, toast, etc.)
```

## Key Services

| Service | Lifecycle | Purpose |
|---------|-----------|---------|
| `BitwardenCliService` | Singleton | Wraps `bw` CLI, manages session state, caches vault items, handles search/filter/sort |
| `ContextAwarenessService` | Static | Captures foreground window context (process names, titles, browser URLs) via Win32 + UI Automation |
| `SecureClipboardService` | Static | Clipboard operations with history exclusion and configurable auto-clear timer |
| `SessionStore` | Static | Reads/writes session keys to Windows Credential Manager |
| `AccessTracker` | Static | Tracks item access times (persisted to JSON) and recent-copy state (5-min in-memory timer) |
| `BitwardenSettingsManager` | Instance | Manages user-configurable settings via Command Palette settings UI |
| `FaviconService` | Static | Downloads favicons (PNG/SVG) with 7-day disk cache and 5-min negative cache; fires `IconCached` to refresh the list UI |
| `NotificationsService` | Instance (while unlocked, Live Sync on) | Listens to the server's notifications hub and requests a sync when the vault changes |

## Live Sync

The Bitwarden apps get vault changes from the server's notifications hub, a SignalR endpoint at `{notificationsUrl}/hub`. The `bw` CLI does not listen to it. With the **Live Sync** setting on, `NotificationsService` connects to the hub while the vault is unlocked:

- The hub URL comes from the CLI environment in `data.json`: an explicit notifications URL, else `{base}/notifications` for a self-hosted server, else `notifications.bitwarden.com` or `notifications.bitwarden.eu` for the cloud region.
- The access token is read from `user_<id>_token_accessToken` for the active account and sent in the `Authorization` header, so it does not appear in proxy access logs. The token stays in memory as plaintext for the life of the connection, unlike the session key, and is not logged. The refresh token is not read.
- The hub protocol is SignalR MessagePack, because Vaultwarden does not support the JSON protocol. The client sends a SignalR ping every 15 seconds.
- Vault change types (cipher, folder, vault, settings and organization syncs) start a `bw sync` after a 2-second debounce. Sends, auth requests and logout notifications are ignored. All `bw sync` runs in the extension are serialised, because two concurrent runs can interleave their `data.json` writes.
- When the token expires within 5 minutes, after a 401, or after a connection that stayed up for at least a minute drops, the client runs `bw sync` before it reconnects. `bw sync` refreshes the token in `data.json` and gets changes missed while offline. Reconnects use a jittered backoff up to 10 minutes.
- Locking, logging out, changing the CLI configuration or turning the setting off stops the listener.

## In-Memory Secret Protection

Passwords, TOTP secrets, notes, card numbers, card security codes, SSH private keys, hidden custom fields, the session key and the pending master password during a 2FA login are kept in `ProtectedSecret` instances. Each instance encrypts its value with `CryptProtectMemory` and a key that is bound to this process. A value is decrypted into a pinned buffer for a copy, a search, a TOTP calculation or a list subtitle, and the buffer is zeroed after use. The cached secrets are erased when the vault locks or logs out. This protection applies to the long-lived copies that the extension keeps. Plaintext also passes through the `bw` CLI output, the Command Palette form input, the clipboard, the secure-note subtitle and card last-4 digits in the list, and the `BW_SESSION`/`BW_MP` environment of `bw` child processes.

## Extension Registration

The extension is a COM out-of-process server. `Program.cs` registers it with the PowerToys host. The host calls `IExtension.GetProvider()` to get the `CommandsProvider`, which supplies the main vault page and the fallback search item.

## Companion Window Process

The companion window (see [Companion Window](Companion-Window)) is a separate Avalonia 12 app, `HoobiBitwardenCompanion`, with FluentAvaloniaUI and CommunityToolkit.Mvvm. It is a thin UI. The extension stays the package owner and the vault backend: the `bw` CLI, the session key, the item cache and the clipboard stay in the extension.

- **Packaging.** The extension csproj references the companion project with `Targets="Build;Publish"` and adds the self-contained publish output under `Companion\` as content. The loose layout that `Add-AppxPackage -Register` reads and the MSIX payload both contain `Companion\HoobiBitwardenCompanion.exe`. The manifest is unchanged. The companion is a child process of the packaged extension, so it has the package identity.
- **Process lifetime.** `Companion/CompanionHost.cs` starts one companion on first use and reuses it. If the companion has exited, the next request starts a new one. Closing a window does not stop the companion. The companion stops when its input pipe reaches end of file, which occurs when the extension process ends.
- **Transport.** Two anonymous pipes, one for each direction. The client handles are passed as command-line arguments and inherited by the child. Each message is one line of JSON: `{ id, type: req|res|evt, op, payload, error }`, serialized with a source-generated `JsonSerializerContext`. `Shared/PipeChannel.cs` is the one channel class for both ends: it correlates responses by id, sends events, cancels pending requests and fails them on end of file. The contracts and the channel are in `Shared/` and compiled into both projects.
- **Operations.** Only typed operations are accepted. Companion to extension: `getItem`, `reveal`, `copy`, `getTotp`, `openUri`, `verifyReprompt`. Extension to companion: `showItem`, `showVault`. `Companion/CompanionOps.cs` validates every item id with `Guid.TryParse`, reads the cached item, and decrypts a `ProtectedSecret` only for the response.
- **Secrets.** `getItem` returns no secret values, only flags for the secret fields that exist. A secret crosses the pipe only for `reveal` (one field) and `getTotp` (the current code). `copy` runs in the extension with `SecureClipboardService`, so the secret does not go to the companion and the clipboard clear timer continues after a window closes. `openUri` opens only `http` and `https` addresses.
- **Master password re-prompt.** For an item with re-prompt, the extension refuses `getItem` field data, `reveal`, `copy`, `getTotp` and `openUri` until `verifyReprompt` succeeds. Verification uses `BitwardenCliService.VerifyMasterPasswordAsync` and the same failure cooldown as the Command Palette re-prompt page. The grant is kept for each item for the companion session and is erased when the vault locks, auto-locks, logs out or the CLI configuration changes.
