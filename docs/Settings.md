# Settings

Open settings from the gear icon in the vault browser. All settings take effect immediately. The **Bitwarden settings** command opens the same settings in the companion window (see [Settings window](Companion-Window#settings-window)). The **Open settings window** button at the top of the Bitwarden page in Command Palette Settings opens the same window.

## Server

The companion settings window starts with a **Server** section: **Region** (**Bitwarden.com**, **Bitwarden.eu** or **Self-hosted**), **Server URL** (read-only for **Bitwarden.com** and **Bitwarden.eu**, editable for **Self-hosted**) and, for **Self-hosted**, a collapsed **Advanced** row with optional Web Vault, API, Identity, Icons, Notifications, Events and Key Connector URLs. It is not part of the Command Palette settings page. A change logs you out. See [Server URL](Companion-Window#server-url).

## Available Settings

### Remember Session

| | |
|---|---|
| **Type** | Toggle |
| **Default** | Off |
| **Description** | Persist the Bitwarden session key in Windows Credential Manager so the vault stays unlocked across restarts. When disabled, the session key is stored only in memory and lost when the extension process exits. |

### Use API Key Authentication

| | |
|---|---|
| **Type** | Toggle |
| **Default** | Off |
| **Description** | Log in with your [personal API key](https://bitwarden.com/help/personal-api-key/) (`bw login --apikey`) instead of your email and master password. Use this when your account uses FIDO2 or Duo two-step login. The login form asks for the Client ID and Client Secret, and the extension stores them in Windows Credential Manager. When you are logged out, the extension logs in again with the stored key, so you only unlock with your master password. Changing this setting logs you out and deletes the stored API key and session key. Logging out also deletes the stored API key. See [API key login](Getting-Started#api-key-login). |

### Unlock with Windows Hello

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Use Windows Hello through the Bitwarden Desktop app over IPC. The Desktop app must run with **Allow browser integration** on. When this setting is on, the unlock page, the master password re-prompt page and the master password panel of the [companion window](Companion-Window#master-password-re-prompt) show a Windows Hello button: **Unlock with Windows Hello** to unlock, **Verify with Windows Hello** for a re-prompt. The extension runs the Windows Hello check and applies the same cooldown and grace period as a master password. In the companion window, a green dot on this row means the extension can reach the Desktop app and a red dot means it cannot; no dot shows while the setting is off. |

### Automatically Prompt for Biometrics

| | |
|---|---|
| **Type** | Toggle (shown under **Unlock with Windows Hello**) |
| **Default** | On |
| **Description** | Start Windows Hello when you open the Bitwarden page on a locked vault, so you do not select **Unlock Vault** first. The prompt starts only from that page open: not when PowerToys or Command Palette starts, not on the Command Palette home page, not from a Dock band refresh, and not when the vault locks while you are on another page. If the vault locks while the Bitwarden page is open, select **Unlock Vault** or open the page again. After a cancelled or failed Windows Hello prompt, the page does not prompt again until the vault unlocks. With **Remember Session** on, the extension first tries the stored session and prompts only when that fails. |

### Show Website Icons

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Download and display website favicons for login items. Icons are fetched from `icons.bitwarden.net` (cloud) or your self-hosted server's `/icons/` endpoint, then cached locally for 7 days. SVG icons that reference external resources are not shown. Cached icons are deleted when you log out. Disable for privacy; a generic icon will be shown instead. Right after the vault unlocks, items show a generic icon until the extension has confirmed which server the vault is on, so a self-hosted vault never requests icons from Bitwarden's cloud. See [Bitwarden's website icon documentation](https://bitwarden.com/help/website-icons/) for more details. |

### Auto-Lock Timeout

| | |
|---|---|
| **Type** | Choice (dropdown) |
| **Default** | Never |
| **Options** | Never, 1 minute, 2 minutes, 5 minutes, 15 minutes, 30 minutes, 1 hour |
| **Description** | Automatically lock the vault after a period of inactivity. The timer resets whenever you copy a vault item. Selecting **Never** disables the inactivity lock entirely. |

### Show Tags

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Master switch for tags on vault items. When off, no tags are shown (including the Context tag) and the five tag settings below (Watchtower, Protected, Passkey, TOTP, Organisation) are hidden in the companion window. |

### Watchtower

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

### Protected

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Display a "🔒" tag on vault items that require master password re-prompt before accessing sensitive fields. |

### Passkey

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Display a "Passkey" tag on vault items that have a FIDO2 credential registered for passwordless sign-in. |

### TOTP

| | |
|---|---|
| **Type** | Choice (dropdown) |
| **Default** | Static |
| **Options** | Off, Static, Live |
| **Description** | Controls how TOTP-enabled vault items are tagged. **Off** hides the tag entirely, **Static** shows a "2FA" badge, and **Live** displays the current code with a countdown timer that updates every second. |

### Organisation

| | |
|---|---|
| **Type** | Choice (dropdown) |
| **Default** | Name |
| **Options** | Initials, Name, Off |
| **Description** | Controls how items belonging to a Bitwarden organization are tagged. **Initials** shows a compact avatar-style badge (single-word orgs get one letter, e.g. `Nintex` → `N`; multi-word orgs get up to three initials, e.g. `Test Organization` → `TO`). **Name** shows the organization name on the tag (truncated to 20 characters). **Off** hides the tag entirely. Each organization gets a stable, deterministic colour from a pastel palette, so two items from the same organization always render in the same colour. |

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

### Live Sync

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Syncs the vault within a few seconds of a change on another device, the same way the Bitwarden apps do. While the vault is unlocked, the extension keeps a WebSocket connection open to your server's notifications service. This works with Bitwarden cloud (US and EU), self-hosted Bitwarden and Vaultwarden. It signs in with the access token that the Bitwarden CLI stores in plaintext in its `data.json`. Turn it off to keep the extension from reading that token. See [Architecture](Architecture#live-sync) for details. |

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
| **Description** | Path to the Bitwarden CLI. Accepts a directory containing `bw`, or a direct path to `bw`/`bw.exe` (e.g. `C:\tools\bw-portable` or `C:\tools\bw-portable\bw.exe`). Leave empty to use the default PATH-based resolution. When set, this always takes precedence over a CLI installed via the one-click installer, and the installer never overwrites it. While the box is empty, the companion window shows the `bw` path in use as the placeholder text, and hovering the box shows the full path and where it came from. |

### Use CLI Path as Data Directory

| | |
|---|---|
| **Type** | Toggle |
| **Default** | Off |
| **Description** | Store Bitwarden CLI data (`data.json`) alongside the CLI executable instead of the default location. Sets the `BITWARDENCLI_APPDATA_DIR` environment variable to the CLI directory. Useful for fully portable Bitwarden setups. The companion window shows this setting under **CLI Path Override**, and only while that box is not empty. |

### CLI Data Directory Override

| | |
|---|---|
| **Type** | Text |
| **Default** | *(empty)* |
| **Description** | Custom directory for Bitwarden CLI data (`data.json`). Sets the `BITWARDENCLI_APPDATA_DIR` environment variable. Takes precedence over the portable directory toggle. Leave empty to use the default location, or the portable CLI directory if the toggle above is enabled. While the box is empty, the companion window shows the folder that holds `data.json` now as the placeholder text, and hovering the box shows the full path and where it came from. |

### Debug Logging

| | |
|---|---|
| **Type** | Toggle |
| **Default** | Off |
| **Description** | Enable debug logging to help diagnose issues. When enabled, the extension records timestamped log entries for all key operations (CLI calls, session verification, cache refreshes, status checks) in an in-memory buffer (up to 500 entries) and writes them to files in the log folder (see **Logs** below). A **Copy Debug Log** command appears at the bottom of the vault browser so you can copy the log to your clipboard and paste it into a GitHub issue. No sensitive data (passwords, session keys, search text) is logged. The section that holds this setting is named **Diagnostics**. |

### Logs

| | |
|---|---|
| **Type** | Link (companion window only) |
| **Description** | Appears under **Debug Logging** in the companion window. It opens the folder `%LOCALAPPDATA%\HoobiBitwardenCommandPalette\logs` in File Explorer, and creates the folder if it does not exist. The Command Palette settings page does not show it. |

### Re-prompt Grace Period

| | |
|---|---|
| **Type** | Choice (dropdown) |
| **Default** | 1 minute |
| **Options** | No grace period, 30 seconds, 1 minute, 2 minutes, 5 minutes |
| **Description** | After verifying your master password for a protected item, skip re-verification for this duration. During the grace period, protected actions execute instantly without any prompt. Set to **No grace period** to always require re-entry. The grace period is cleared when the vault is locked or you log out. |
