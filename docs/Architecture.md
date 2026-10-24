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
│   ├── CompanionOps.cs                # Companion request handlers (validation, re-prompt, secrets)
│   └── CompanionOps.Writes.cs         # Typed edit DTOs to and from bw item JSON, write requests
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
│   ├── BitwardenCliService.VaultExtras.cs  # Trash, archive and collections for the companion vault window
│   ├── BitwardenCliService.Writes.cs  # Verified vault writes (create, edit, delete, restore, archive, move, collections)
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
├── ViewModels/                        # Vault, item row, filter, item and field view models
├── Views/                             # Vault window, item window and the shared item view
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
- **Transport.** Two anonymous pipes, one for each direction. The client handles are passed as command-line arguments and inherited by the child. Each message is one line of JSON: `{ id, type: req|res|evt, op, payload, error }`, serialised with a source-generated `JsonSerializerContext`. `Shared/PipeChannel.cs` is the one channel class for both ends: it correlates responses by id, sends events, cancels pending requests and fails them on end of file. The contracts and the channel are in `Shared/` and compiled into both projects.
- **Operations.** Only typed operations are accepted. Companion to extension: `listItems`, `listFilters`, `getItem`, `reveal`, `copy`, `getTotp`, `openUri`, `verifyReprompt`, `getEditableItem`, `getPasswordHistory`, `createItem`, `updateItem`, `deleteItem`, `restoreItem`, `archiveItem`, `moveItem`, `setCollections`. Extension to companion: `showItem`, `showVault`, the `vaultChanged` event when the item cache refreshes (`BitwardenCliService.CacheUpdated`, which also fires after a Live Sync), and the `locked` event when the vault locks. Requests time out after 60 seconds; a timed-out extension request ends the companion process tree, so the next request starts a new one. `reveal`, `copy` and `openUri` carry the item revision date and return the `stale` error when the cached item changed. `Companion/CompanionOps.cs` validates every item id with `Guid.TryParse`, reads the cached item, and decrypts a `ProtectedSecret` only for the response.
- **Vault list.** `listItems` returns one summary for each item: id, type, name, subtext, favourite, folder, organization, collections, trash and archive flags, revision date, TOTP, re-prompt and notes flags, URI hosts, and the path of a cached favicon. The subtext is the Command Palette subtitle (the username for a login, also with re-prompt), except for a secure note, whose subtitle is the first line of its notes. `listFilters` returns the folder, collection and organization names. The item cache holds only active items, because `bw list items` leaves out deleted and archived items. On the first list request after a cache refresh, `BitwardenCliService.LoadVaultExtrasAsync` runs `bw list items --trash`, `bw list items --archived` and `bw list collections`. These items stay in a side cache in `CompanionOps`, so they do not appear in Command Palette search. `getItem`, `reveal` and `copy` also find them there. The side cache is erased when the vault locks. `--archived` returns the active items for an account that cannot archive, so only items with an `archivedDate` are kept. A favicon comes only from the `FaviconService` disk cache: the companion never downloads an icon.
- **Secrets.** `listItems` and `getItem` return no secret values; `getItem` returns only flags for the secret fields that exist. A secret crosses the pipe only for `reveal` (one field) and `getTotp` (the current code). `copy` runs in the extension with `SecureClipboardService`, so the secret does not go to the companion and the clipboard clear timer continues after a window closes. `openUri` opens only `http` and `https` addresses.
- **Master password re-prompt.** For an item with re-prompt, `getItem` returns no field data and `reveal`, `copy`, `getTotp` and `openUri` return the `reprompt` error outside the Command Palette re-prompt grace period (`RepromptPage.IsWithinGracePeriod`). `verifyReprompt` uses `BitwardenCliService.VerifyMasterPasswordAsync` and the same failure cooldown as the Command Palette re-prompt page, starts the grace period, and runs one wrapped operation for the same item without the gate, so a grace period of 0 prompts for each operation. `getEditableItem`, `getPasswordHistory` and a password history `copy` have the same gate.

### Vault writes

The companion edits items through typed operations. The companion never sends `bw` JSON. `Companion/CompanionOps.Writes.cs` validates the request and builds the `bw` item JSON. `Services/BitwardenCliService.Writes.cs` runs the write.

- **Edit data.** `getEditableItem` runs `bw get item <id>` and returns an `EditableItem`: all fields of the five item types, custom fields with their type and linked field, and the revision date. Only the edit form uses it. The secrets cross the pipe once, when the form opens.
- **JSON.** A create starts from the `bw get template item` shape. An edit starts from the current `bw get item` JSON and changes only the keys that the form owns. Passkeys (`login.fido2Credentials`), password history, attachments and other keys go back to `bw` unchanged. `bw edit` replaces each item-type object with the request object, so a key that is not sent is lost.
- **Owner and collections.** `bw edit` does not change the organisation, and it merges collection ids. The companion saves the item first, then runs `moveItem` (`bw move`) to move a personal item into an organisation, or `setCollections` (`bw edit item-collections`) to change the collections of an organisation item. `bw` cannot move an item back to the personal vault, so the owner list is disabled for an organisation item.
- **Validation.** Every item, folder, organisation and collection id must pass `Guid.TryParse` before a `bw` process starts. The name is required, a URI must parse (except a regular-expression match), an expiration month is 1 to 12, a linked field must point to a field of the item type, and an SSH key needs the private key, public key and fingerprint.
- **Process.** `bw create`, `bw edit`, `bw move` and `bw edit item-collections` read base64-encoded JSON on standard input. The JSON never goes into the process arguments, which contain only the verb and GUIDs. Writes log only the `bw` verb. One `SemaphoreSlim` serialises the whole write (revision check, write, verification). Each mutating `bw` process also holds the sync gate until it exits, like `bw sync`, because both write `data.json`. A lock does not stop a running write or sync.
- **Conflicts.** Before each write the extension reads the item again with `bw get item` and compares `revisionDate` with the revision that the companion sent. A mismatch returns the `conflict` error and runs no write. A server "out of date" response also returns `conflict`. The edit form then offers **Reload** (discard the local edits) or **Keep editing**.
- **Verification.** After the write process exits, the extension reads the item again and compares the parsed fields that it sent: name, notes, favourite, re-prompt, folder, custom fields, the item-type fields, the URIs and their match, and the passkey count. Delete, restore and archive check `deletedDate` and `archivedDate`; a permanent delete checks that the item is gone; move and collections check the organisation and the collection ids. A mismatch returns `failed`. A verified write replaces the item in the cache and raises `CacheUpdated`, so Command Palette and the companion show the new version. The trash and archive side cache loads again on the next list request.
- **Archive.** `bw archive item` archives. `bw restore item` restores an item from the trash, or from the archive when the item is not in the trash.
