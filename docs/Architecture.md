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
│   ├── BitwardenSettingsManager.cs    # Extension settings (toggles + choices)
│   ├── ContextAwarenessService.cs     # Window detection, URL extraction, context scoring
│   ├── FaviconService.cs              # Favicon download, disk/memory cache, icon refresh events
│   ├── ICliProcess.cs                 # Process abstraction for unit testing
│   ├── ProtectedSecret.cs             # In-memory encryption for secrets (CryptProtectMemory)
│   ├── SecureClipboardService.cs      # Clipboard security + auto-clear
│   ├── SessionStore.cs               # Windows Credential Manager wrapper
│   └── SvgRasterizer.cs              # SVG to PNG conversion via SkiaSharp
└── Assets/                            # Icons and images
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

## In-Memory Secret Protection

Passwords, TOTP secrets, notes, card numbers, card security codes, hidden custom fields, the session key and the pending master password during a 2FA login are kept in `ProtectedSecret` instances. Each instance encrypts its value with `CryptProtectMemory` and a key that is bound to this process. A value is decrypted into a pinned buffer for a copy, a search, a TOTP calculation or a list subtitle, and the buffer is zeroed after use. The cached secrets are erased when the vault locks or logs out. This protection applies to the long-lived copies that the extension keeps. Plaintext also passes through the `bw` CLI output, the Command Palette form input, the clipboard, the secure-note subtitle and card last-4 digits in the list, and the `BW_SESSION`/`BW_MP` environment of `bw` child processes.

## Extension Registration

The extension is a COM out-of-process server. `Program.cs` registers it with the PowerToys host. The host calls `IExtension.GetProvider()` to get the `CommandsProvider`, which supplies the main vault page and the fallback search item.
