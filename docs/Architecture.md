# Architecture

## Technology Stack

- **.NET 10** / C# targeting `net10.0-windows10.0.26100.0`
- **Microsoft.CommandPalette.Extensions SDK** - WinRT-based extension API for PowerToys Command Palette
- **Win32 P/Invoke** via `LibraryImport` - clipboard APIs, window management
- **UI Automation COM Interop** - browser address bar URL extraction
- **System.Security.Cryptography** - TOTP code generation (RFC 6238 HMAC), password and passphrase generation (`RandomNumberGenerator`)
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
│   ├── CompanionOps.Attachments.cs    # Attachment download, upload and delete, local path validation
│   ├── CompanionOps.Generator.cs      # Generated values, generator history, Quick Rotate
│   ├── CompanionOps.Settings.cs       # Settings schema and validated writes for the settings window
│   └── CompanionOps.Writes.cs         # Typed edit DTOs to and from bw item JSON, write requests
├── Helpers/
│   └── VaultItemHelper.cs             # Icons, tags, default commands, context menus
├── Models/
│   └── BitwardenItem.cs               # Vault item data model (JSON deserialization)
├── Resources/
│   └── eff_large_wordlist.txt         # EFF large word list for passphrases (embedded resource, CC BY 3.0 US)
├── Pages/
│   ├── HoobiBitwardenCommandPaletteExtensionPage.cs  # Main vault list (DynamicListPage)
│   ├── LoginPage.cs                    # Email/password login form
│   ├── SetServerPage.cs               # Server URL configuration form
│   └── UnlockVaultPage.cs             # Master password unlock form
├── Services/
│   ├── AccessTracker.cs               # Access history + recent item tracking
│   ├── BitwardenCliService.cs         # CLI interaction, caching, search, sorting
│   ├── BitwardenCliService.LiveSync.cs  # Starts/stops the notifications listener, debounced sync
│   ├── BitwardenCliService.VaultAttachments.cs  # bw attachment commands, attachment parsing, error mapping
│   ├── BitwardenCliService.VaultExtras.cs  # Folders, organisations and collections for the companion edit form
│   ├── BitwardenCliService.Writes.cs  # Verified vault writes (edit, delete, archive, move, collections)
│   ├── BitwardenSettingsManager.cs    # Extension settings (toggles + choices)
│   ├── ContextAwarenessService.cs     # Window detection, URL extraction, context scoring
│   ├── FaviconService.cs              # Favicon download, disk/memory cache, icon refresh events
│   ├── HubProtocol.cs                 # SignalR MessagePack framing, CLI state and hub URL parsing
│   ├── ICliProcess.cs                 # Process abstraction for unit testing
│   ├── NotificationsService.cs        # WebSocket connection to the notifications hub
│   ├── PasswordGenerator.cs           # Password and passphrase generation (RandomNumberGenerator, EFF word list)
│   ├── ProtectedSecret.cs             # In-memory encryption for secrets (CryptProtectMemory)
│   ├── SecureClipboardService.cs      # Clipboard security + auto-clear
│   ├── CredentialStore.cs             # Windows Credential Manager wrapper
│   ├── SessionStore.cs               # Session key and API key credentials
│   └── SvgRasterizer.cs              # SVG to PNG conversion via SkiaSharp
└── Assets/                            # Icons and images
HoobiBitwardenCompanion/               # Avalonia companion window app
├── Program.cs                         # Reads the pipe handles, starts Avalonia
├── Services/WindowCoordinator.cs      # Pipe requests from the extension, window registry
├── Services/CompanionSettings.cs      # Generator options file (no generated values)
├── ViewModels/                        # Item, field, edit form and generator view models
├── Views/                             # Item window, item view, edit form, generator dialog and window, toast
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
| `ApiKeyStore` | Static | Reads/writes the personal API key (client ID and secret) to Windows Credential Manager for API key login |
| `AccessTracker` | Static | Tracks item access times (persisted to JSON) and recent-copy state (5-min in-memory timer) |
| `BitwardenSettingsManager` | Instance | Manages user-configurable settings via Command Palette settings UI |
| `FaviconService` | Static | Downloads favicons (PNG/SVG) with 7-day disk cache and 5-min negative cache; fires `IconCached` to refresh the list UI |
| `NotificationsService` | Instance (while unlocked, Live Sync on) | Listens to the server's notifications hub and requests a sync when the vault changes |

## CLI Process Use

Each `bw` call starts a Node.js process, so the extension starts as few as it can:

- **Version probe.** `bw --version` runs once for each CLI executable. The result is kept until the path or the last write time of the executable changes, the CLI configuration changes, or a probe fails.
- **Login state.** When there is no session key and `data.json` has state version 60 or later, the vault status (locked or logged out) and the server URL come from the CLI `data.json`: the active account (`global_account_activeAccountId`) with an access token is locked, else logged out. The server URL is `urls.base` of the account or global environment, as `bw status` reports it, so it is empty for the cloud regions. When `data.json` is missing, cannot be read or has an older state version, `bw status` is used. A session key is always checked with `bw status`, `bw sync` and `bw list folders`, because only the CLI can confirm that the key decrypts the vault.
- **Background refresh.** The item cache refreshes (`bw list folders`, `bw list items`, `bw list organizations`) after the background refresh interval only when the last write time or the size of `data.json` changed since the last refresh, or the last refresh is more than 30 minutes old. `bw list` reads only `data.json`, which changes after a sync or a write. A refresh after a sync, a write or an unlock always runs.
- **Fallback search.** The root Command Palette fallback item searches the cache only when the vault is unlocked. While the vault is locked, it checks the vault status only when a saved session (**Remember Session**) or `BW_SESSION` can unlock it, and at most once every 30 seconds. Otherwise it starts no `bw` process.
- **Password history.** `bw list items` includes the password history of each item, so the cache keeps the old passwords as `ProtectedSecret`. The companion lists and copies them with no `bw` process. `bw get item` runs only for an item whose cached data has no password history field.

## Live Sync

The Bitwarden apps get vault changes from the server's notifications hub, a SignalR endpoint at `{notificationsUrl}/hub`. The `bw` CLI does not listen to it. With the **Live Sync** setting on, `NotificationsService` connects to the hub while the vault is unlocked:

- The hub URL comes from the CLI environment in `data.json`: an explicit notifications URL, else `{base}/notifications` for a self-hosted server, else `notifications.bitwarden.com` or `notifications.bitwarden.eu` for the cloud region.
- The access token is read from `user_<id>_token_accessToken` for the active account and sent in the `Authorization` header, so it does not appear in proxy access logs. The token stays in memory as plaintext for the life of the connection, unlike the session key, and is not logged. The refresh token is not read.
- The hub protocol is SignalR MessagePack, because Vaultwarden does not support the JSON protocol. The client sends a SignalR ping every 15 seconds.
- Vault change types (cipher, folder, vault, settings and organization syncs) start a `bw sync` after a 2-second debounce. Sends, auth requests and logout notifications are ignored. All `bw sync` runs in the extension are serialised, because two concurrent runs can interleave their `data.json` writes.
- When the token expires within 5 minutes, after a 401, or after a connection that stayed up for at least a minute drops, the client runs `bw sync` before it reconnects. `bw sync` refreshes the token in `data.json` and gets changes missed while offline. Reconnects use a jittered backoff up to 10 minutes.
- Locking, logging out, changing the CLI configuration or turning the setting off stops the listener.

## In-Memory Secret Protection

Passwords, old passwords from the password history, TOTP secrets, notes, card numbers, card security codes, SSH private keys, hidden custom fields, the session key and the pending master password during a 2FA login are kept in `ProtectedSecret` instances. Each instance encrypts its value with `CryptProtectMemory` and a key that is bound to this process. A value is decrypted into a pinned buffer for a copy, a search, a TOTP calculation or a list subtitle, and the buffer is zeroed after use. The cached secrets are erased when the vault locks or logs out. This protection applies to the long-lived copies that the extension keeps. Plaintext also passes through the `bw` CLI output, the Command Palette form input, the clipboard, the secure-note subtitle and card last-4 digits in the list, and the `BW_SESSION`/`BW_MP` environment of `bw` child processes.

## Extension Registration

The extension is a COM out-of-process server. `Program.cs` registers it with the PowerToys host. The host calls `IExtension.GetProvider()` to get the `CommandsProvider`, which supplies the main vault page and the fallback search item.

## Companion Window Process

The companion window (see [Companion Window](Companion-Window)) is a separate Avalonia 12 app, `HoobiBitwardenCompanion`, with FluentAvaloniaUI and CommunityToolkit.Mvvm. It is a thin UI. The extension stays the package owner and the vault backend: the `bw` CLI, the session key, the item cache and the clipboard stay in the extension.

- **Packaging.** The extension csproj references the companion project with `Targets="Build;Publish"` and adds the self-contained publish output under `Companion\` as content. The loose layout that `Add-AppxPackage -Register` reads and the MSIX payload both contain `Companion\HoobiBitwardenCompanion.exe`. The manifest is unchanged. The companion is a child process of the packaged extension, so it has the package identity.
- **Process lifetime.** `Companion/CompanionHost.cs` starts one companion when the vault cache loads (after warmup or an unlock), so the first window does not wait for the companion to start. If the vault is locked, logged out or not set up, the companion starts on first use. The companion opens no window until a `showItem`, `rotateItem`, `newItem` or `showGenerator` request. At start it builds one hidden item window and closes it, which loads the item window XAML and styles before the first real window. The extension reuses the running companion. If the companion has exited, the next request starts a new one. Closing a window does not stop the companion. The companion stops when its input pipe reaches end of file, which occurs when the extension process ends.
- **Transport.** Two anonymous pipes, one for each direction. The client handles are passed as command-line arguments and inherited by the child. Each message is one line of JSON: `{ id, type: req|res|evt, op, payload, error }`, serialised with a source-generated `JsonSerializerContext`. `Shared/PipeChannel.cs` is the one channel class for both ends: it correlates responses by id, sends events, cancels pending requests and fails them on end of file. The contracts and the channel are in `Shared/` and compiled into both projects.
- **Operations.** Only typed operations are accepted. Companion to extension: `listFilters`, `getItem`, `reveal`, `copy`, `getTotp`, `openUri`, `verifyReprompt`, `getEditableItem`, `getPasswordHistory`, `createItem`, `updateItem`, `deleteItem`, `archiveItem`, `moveItem`, `setCollections`, `downloadAttachment`, `uploadAttachment`, `deleteAttachment`, `generate`, `listGeneratorHistory`, `copyGenerated`, `rotatePassword`, `getSettings`, `setSetting`. Extension to companion: `showItem`, `rotateItem` (opens the item window and the Rotate password dialog; the request returns before the dialog), `newItem` (opens a create window; the request returns before the companion reads the filters), `showGenerator` (opens the one generator window), `showSettings` (opens the one settings window; `getSettings` returns the settings of `BitwardenSettingsManager` in order, and `setSetting` validates the key and value, sets the value and raises `Settings.SettingsChanged`), the `settingsChanged` event when that event fires and the companion runs, the `vaultChanged` event when the item cache refreshes (`BitwardenCliService.CacheUpdated`, which also fires after a Live Sync), and the `locked` event when the vault locks. Requests time out after 60 seconds, except the companion's write requests (`updateItem`, `deleteItem`, `archiveItem`, `moveItem`, `setCollections`, the attachment operations, `rotatePassword`, and `verifyReprompt` when it wraps one), which have no companion-side timeout because the extension write chain can take longer (`createItem` and a Windows Hello `verifyReprompt` also have none); a timed-out extension request ends the companion process tree, so the next request starts a new one. `reveal`, `copy` and `openUri` carry the item revision date and return the `stale` error when the cached item changed. `copy` can carry a character range (`start`, `length`) for a secret field: the extension copies that part of its own value through `SecureClipboardService`, and returns `invalid` for a range outside the value or on a field that is not a secret. `Companion/CompanionOps.cs` validates every item id with `Guid.TryParse`, reads the cached item, and decrypts a `ProtectedSecret` only for the response.
- **Filters and icons.** `listFilters` returns the folder, collection and organization names for the edit form. The folder and organization names come from the item cache. On the first `listFilters` request after a cache refresh or a write, `BitwardenCliService.LoadCollectionsAsync` runs `bw list collections`; the result stays in `CompanionOps` until the next refresh and is dropped when the vault locks. The item cache holds only active items, because `bw list items` leaves out deleted and archived items, so the item window shows active items only. `getItem` sends the path of the item's favicon or card brand icon when the `FaviconService` disk cache has it: the companion never downloads an icon.
- **Secrets.** `getItem` returns no secret values; `getItem` returns only flags for the secret fields that exist. A secret crosses the pipe only for `reveal` (one field), `getTotp` (the current code), `getEditableItem` (the edit form) and `generate` (the new value). `copy` runs in the extension with `SecureClipboardService`, so the secret does not go to the companion and the clipboard clear timer continues after a window closes. `openUri` opens only `http` and `https` addresses.
- **Master password re-prompt.** For an item with re-prompt, `getItem` returns no field data and `reveal`, `copy`, `getTotp` and `openUri` return the `reprompt` error outside the Command Palette re-prompt grace period (`RepromptPage.IsWithinGracePeriod`). `verifyReprompt` uses `BitwardenCliService.VerifyMasterPasswordAsync`, or with `biometric: true` and no password `BitwardenCliService.VerifyWithBiometricsAsync` (Windows Hello through Bitwarden Desktop; refused with `invalid` when **Integrate with Bitwarden Desktop App** is off, and `getItem` sets `biometricAvailable` only when it is on), and the same failure cooldown as the Command Palette re-prompt page, starts the grace period, returns the end of the grace period as `Grace` (an absolute UTC time and the length in seconds; `getItem` returns it too, and it is absent when the grace period is 0), and runs one wrapped operation for the same item without the gate, so a grace period of 0 prompts for each operation. `getEditableItem`, `getPasswordHistory` and a password history `copy` have the same gate. `updateItem`, `deleteItem`, `archiveItem`, `moveItem` and `setCollections` have it too, as in the Bitwarden web vault; the write gate reads the re-prompt flag from the cache or `bw get item`, so it also covers items that are not cached. The companion asks for the master password and sends the write again wrapped in `verifyReprompt`.

### Vault writes

The companion edits items through typed operations. The companion never sends `bw` JSON. `Companion/CompanionOps.Writes.cs` validates the request and builds the `bw` item JSON. `Services/BitwardenCliService.Writes.cs` runs the write.

- **Edit data.** `getEditableItem` returns an `EditableItem`: all fields of the five item types, custom fields with their type and linked field, and the revision date. Only the edit form uses it. The secrets cross the pipe once, when the form opens. The extension builds it from the vault cache, with no `bw` process, when the cache holds every value that the form edits (`BitwardenItem.EditableFromCache`). The cache does not keep identity details, boolean and linked custom fields, empty custom field values, duplicate custom field names or empty URIs. For an item with one of these, `getEditableItem` runs `bw get item <id>`. An item window asks for the folder, collection and organisation lists (`listFilters`) when it loads, so **Edit** does not wait for the `bw list` calls behind them.
- **JSON.** An edit starts from the current `bw get item` JSON, read at save time, and changes only the keys that the form owns. Passkeys (`login.fido2Credentials`), password history, attachments and other keys go back to `bw` unchanged. `bw edit` replaces each item-type object with the request object, so a key that is not sent is lost.
- **Owner and collections.** `bw edit` does not change the organisation, and it merges collection ids. The companion saves the item first, then runs `moveItem` (`bw move`) to move a personal item into an organisation, or `setCollections` (`bw edit item-collections`) to change the collections of an organisation item. `moveItem` and `setCollections` need at least one collection. `bw` cannot move an item back to the personal vault, so the owner list is disabled for an organisation item.
- **Validation.** Every item, folder, organisation and collection id must pass `Guid.TryParse` before a `bw` process starts. The name is required, an expiration month is 1 to 12, a linked field must point to a field of the item type, and an SSH key needs the private key, public key and fingerprint. As in Bitwarden, a URI can be any text or null, and a custom field can have no name (the edit form asks for a name only on a field added in the form). The companion edits only the five item types it knows; other types show read-only.
- **Process.** `bw edit`, `bw move` and `bw edit item-collections` read base64-encoded JSON on standard input. The JSON never goes into the process arguments, which contain only the verb and GUIDs. Writes log only the `bw` verb. One `SemaphoreSlim` serialises the whole write (revision check, write, verification). Each mutating `bw` process also holds the sync gate until it exits, like `bw sync`, because both write `data.json`. A lock does not stop a running write or sync.
- **Timeouts.** A mutating `bw` process gets 5 minutes (`WriteTimeoutMs`) and is not stopped when it times out: stopping `bw` while it flushes `data.json` can leave the file half-written. After the timeout the extension detaches the process, waits for it to exit (it holds the sync gate), runs `bw sync`, and reads the item back. A matching edit is reported as saved; otherwise the write returns `unknown`, and the edit form offers **Reload** to show what the vault holds.
- **Conflicts.** Before each write the extension reads the item again with `bw get item` and compares `revisionDate` with the revision that the companion sent. A mismatch returns the `conflict` error and runs no write. A server "out of date" response runs `bw sync`, puts the server version of the item in the cache, and then returns `conflict`. The edit form then offers **Reload** (discard the local edits) or **Keep editing**.
- **Cache refresh.** A cache refresh that started before a verified write finished is dropped and runs again, so a `bw list items` result read before the write does not bring back the old item.
- **Verification.** After the write process exits, the extension reads the item again and compares the parsed fields that it sent: name, notes, favourite, re-prompt, folder, custom fields, the item-type fields, the URIs and their match, and the passkey count. Delete and archive check `deletedDate` and `archivedDate`; a permanent delete checks that `bw get item` answers "Not found"; move and collections check the organisation and the collection ids. A mismatch returns `failed`. A verified write replaces the item in the cache and raises `CacheUpdated`, so Command Palette and the companion show the new version. An archived or deleted item leaves the cache, and its item window closes.
- **Archive.** `bw archive item` archives. An account that cannot archive, or an organisation item, gets **Move to Trash** (`bw delete item`) in its place.
- **Shared write path.** Attachment upload and delete, and Quick Rotate use the same `WriteAsync` as item writes: the write lock, the sync gate, the 5-minute timeout without a stop, the read-back verification and the cache update.

### Attachments, generator and Quick Rotate

- **Attachments.** `getItem` lists each attachment as id, file name and size text. The download URL that `bw` returns is never parsed or sent. The attachment bytes never cross the pipe: the companion shows a file picker and sends only the path that the user picked, and `bw` reads or writes that file. The extension rejects a path that is not a fully qualified drive path (`C:\...`), a UNC or device path (`\\server`, `\\?\`, `\\.\`), a relative path, a path with `..`, `"`, wildcard or stream characters, a trailing separator, a leading `-`, or a reserved device name (`NUL`, `CON`, `COM1`). The path goes into the arguments in quotes; an attachment id must be letters and digits and must be on the cached item, because `bw` falls back to a file name search. Download runs `bw get attachment <id> --itemid <guid> --output "<path>"` with the 5-minute timeout and no stop on timeout, and passes when the file is new and not larger than the encrypted size that `bw` reports. Upload (`bw create attachment --file "<path>" --itemid <guid>`) checks a 500 MB limit first; upload and delete (`bw delete attachment <id> --itemid <guid>`) run through the shared write path with the revision check and verify the attachment list. "Premium" in the `bw` output returns `premium`, "Max file size" returns `tooLarge` and "Not enough storage" returns `noStorage`. All three operations have the re-prompt gate.
- **Generator.** `generate` makes the value in the extension process with `Services/PasswordGenerator.cs`. The `bw` CLI is not used. Every random choice uses `RandomNumberGenerator` (`GetInt32` and `Shuffle`). The typed `GeneratorOptions` are validated first: length 5 to 128, minimum numbers and minimum special characters 0 to 9, at least one character type, or 3 to 20 words and a separator that is one character from an allowlist, a space or empty. Any other value returns `invalid`. A password uses Bitwarden's character sets (`avoid ambiguous` removes `l`, `I`, `O`, `0` and `1`): it has the minimum numbers and special characters, one uppercase and one lowercase character when those types are on, and random characters from all the enabled types for the remaining length, in a shuffled order. A passphrase picks words from the EFF large word list (`Resources/eff_large_wordlist.txt`, an embedded resource), without the 4 words that contain a hyphen, as in Bitwarden. `capitalize` makes the first letter of each word uppercase and `includeNumber` adds one digit to the end of one word. The output must have the requested length (password) or no space (passphrase), else `generate` returns `failed`.
- **Generator history.** The extension keeps the last 20 generated values as `ProtectedSecret` in memory, so that `copyGenerated` and `rotatePassword` can find them by entry id. These values are not listed. A value goes into the listed history (the last 50) only when it is copied with `copyGenerated` or saved by `rotatePassword`. **Use password** in the edit form does not add a value. No value is written to disk. All values are erased when the vault locks or the extension stops. A value that leaves either list is disposed. `listGeneratorHistory` returns only entry ids, kinds and dates; `copyGenerated` copies one entry through `SecureClipboardService`. The companion saves the generator options (not values) in `%LOCALAPPDATA%\HoobiBitwardenCommandPalette\companion-generator.json`, which the package virtualises.
- **Create.** `createItem` takes the same typed item as `updateItem` with no id. `CompanionOps.CreateItemAsync` validates it, builds the `bw` item JSON (with the owner and at least one collection for an organisation item) and runs `bw create item` with the JSON on standard input, through the shared write path without a revision check. The extension reads the new item back by the id that `bw` prints, compares it and adds it to the cache, so it shows in Command Palette. When `bw` does not answer in time or the read-back fails, the extension refreshes the cache and returns `unknown`, never `failed`, because the item can exist. The companion then asks before it sends the item again.
- **Quick Rotate.** `rotatePassword` takes the item id, its revision date and either a generator entry id or the generator options (password or passphrase). The companion sends the entry id of the value that the Rotate password dialog shows, so the extension saves its own remembered copy of that value and adds that entry to the history once. With options, the extension generates the password with `PasswordGenerator`. It has the re-prompt gate and saves it with `EditItemAsync`, the same revision check and parsed field compare as `updateItem` (`bw` adds the old password to the password history). Only a verified save copies the new password with `SecureClipboardService`; a conflict, a failed save or an unconfirmed save copies nothing. The new password does not cross the pipe.
