# Settings

Open settings from the gear icon in the vault browser. All settings take effect immediately.

## Available Settings

### Remember Session

| | |
|---|---|
| **Type** | Toggle |
| **Default** | Off |
| **Description** | Persist the Bitwarden session key in Windows Credential Manager so the vault stays unlocked across restarts. When disabled, the session key is stored only in memory and lost when the extension process exits. |

### Show Watchtower Tags

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Display security warning tags (Weak, Old Password, HTTP, Reused, No Password) on vault items. Disable for a cleaner list without security indicators. See [Watchtower Tags](Watchtower-Tags) for details. |

### Context Awareness

| | |
|---|---|
| **Type** | Toggle |
| **Default** | On |
| **Description** | Detect open applications and browser tabs to boost matching vault items. When disabled, no window information is captured and no context boosting or tagging occurs. See [Context Awareness](Context-Awareness) for details. |

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
