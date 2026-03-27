# Settings

Open settings from the gear icon in the vault browser. All settings take effect immediately.

## Available Settings

### Remember Session

| | |
|---|---|
| **Type** | Toggle |
| **Default** | Off |
| **Description** | Persist the Bitwarden session key in Windows Credential Manager so the vault stays unlocked across restarts. When disabled, the session key is stored only in memory and lost when the extension process exits. |

### Show Website Icons

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Download and display website favicons for login items. Icons are fetched from `icons.bitwarden.net` (cloud) or your self-hosted server's `/icons/` endpoint, then cached locally for 7 days. Disable for privacy; a generic icon will be shown instead. See [Bitwarden's website icon documentation](https://bitwarden.com/help/website-icons/) for more details. |

### Auto-Lock Timeout

| | |
|---|---|
| **Type** | Choice (dropdown) |
| **Default** | Never |
| **Options** | Never, 1 minute, 2 minutes, 5 minutes, 15 minutes, 30 minutes, 1 hour |
| **Description** | Automatically lock the vault after a period of inactivity. The timer resets whenever you copy a vault item. Selecting **Never** disables the inactivity lock entirely. |

### Show Watchtower Tags

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Display security warning tags (Weak, Old, Insecure URL) on vault items. See [Watchtower Tags](Watchtower-Tags) for details. |

### Context Awareness

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Detect open applications and browser tabs to boost matching vault items. When disabled, no window information is captured and no context boosting or tagging occurs. See [Context Awareness](Context-Awareness) for details. |

### Show Context Tag

| | |
|---|---|
| **Type** | Toggle |
| **Default** | Off |
| **Description** | Display a "Context" tag on vault items that match the foreground application. Requires Context Awareness to be enabled. |

### Show Protected Tag

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Display a "🔒" tag on vault items that require master password re-prompt before accessing sensitive fields. |

### Show Passkey Tag

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Display a "Passkey" tag on vault items that have a FIDO2 credential registered for passwordless sign-in. |

### TOTP Tag Style

| | |
|---|---|
| **Type** | Choice (dropdown) |
| **Default** | Static |
| **Options** | Off, Static (show 2FA badge only), Live (show live code + countdown) |
| **Description** | Controls how TOTP-enabled vault items are tagged. **Off** hides the tag entirely, **Static** shows a "2FA" badge, and **Live** displays the current code with a countdown timer that updates every second. |

### Context Items Limit

| | |
|---|---|
| **Type** | Choice (dropdown) |
| **Default** | 3 items |
| **Options** | 1 item, 2 items, 3 items, 5 items, 10 items, Unlimited |
| **Description** | Maximum number of context-matched vault items to show when no search text is entered. Set to **Unlimited** to show all matching items. |

### Background Refresh Interval

| | |
|---|---|
| **Type** | Choice (dropdown) |
| **Default** | 5 minutes |
| **Options** | Never, 5 minutes, 15 minutes, 30 minutes, 1 hour |
| **Description** | How often vault items are automatically refreshed in the background while the vault is open. The time since last sync is shown on the Sync Vault item. Set to **Never** to only sync manually. |

### Auto-Clear Clipboard

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Automatically clear sensitive data from the clipboard after a configurable delay. See [Clipboard Security](Clipboard-Security) for what counts as sensitive. |

### Clipboard Clear Delay

| | |
|---|---|
| **Type** | Choice (dropdown) |
| **Default** | 10 seconds |
| **Options** | 10 seconds, 15 seconds, 30 seconds, 60 seconds, 2 minutes |
| **Description** | How long to wait before auto-clearing sensitive clipboard data. Only applies when Auto-Clear Clipboard is enabled. |

### CLI Path Override

| | |
|---|---|
| **Type** | Text |
| **Default** | *(empty)* |
| **Description** | Path to the Bitwarden CLI. Accepts a directory containing `bw`, or a direct path to `bw`/`bw.exe` (e.g. `C:\tools\bw-portable` or `C:\tools\bw-portable\bw.exe`). Leave empty to use the default PATH-based resolution. |

### Use CLI Path as Data Directory

| | |
|---|---|
| **Type** | Toggle |
| **Default** | Off |
| **Description** | Store Bitwarden CLI data (`data.json`) alongside the CLI executable instead of the default location. Sets the `BITWARDENCLI_APPDATA_DIR` environment variable to the CLI directory. Requires **CLI Path Override** to be set. Useful for fully portable Bitwarden setups. |

### CLI Data Directory Override

| | |
|---|---|
| **Type** | Text |
| **Default** | *(empty)* |
| **Description** | Custom directory for Bitwarden CLI data (`data.json`). Sets the `BITWARDENCLI_APPDATA_DIR` environment variable. Takes precedence over the portable directory toggle. Leave empty to use the default location, or the portable CLI directory if the toggle above is enabled. |

### Debug Logging

| | |
|---|---|
| **Type** | Toggle |
| **Default** | Off |
| **Description** | Enable debug logging to help diagnose issues. When enabled, the extension records timestamped log entries for all key operations (CLI calls, session verification, cache refreshes, status checks) in an in-memory buffer (up to 500 entries). A **Copy Debug Log** command appears at the bottom of the vault browser so you can copy the log to your clipboard and paste it into a GitHub issue. Logs are kept in memory only and cleared when the extension process restarts. No sensitive data (passwords, session keys) is logged. |

### Re-prompt Grace Period

| | |
|---|---|
| **Type** | Choice (dropdown) |
| **Default** | 1 minute |
| **Options** | No grace period, 30 seconds, 1 minute, 2 minutes, 5 minutes |
| **Description** | After verifying your master password for a protected item, skip re-verification for this duration. During the grace period, protected actions execute instantly without any prompt. Set to **No grace period** to always require re-entry. The grace period is cleared when the vault is locked or you log out. |
