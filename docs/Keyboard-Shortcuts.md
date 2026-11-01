# Keyboard Shortcuts

Each action in the more-commands menu of a vault item has a default shortcut. The **Keyboard shortcuts** page changes the shortcuts and chooses which action Enter runs.

Open the page from the **Shortcuts** section of the [settings window](Companion-Window#settings-window). It replaces the settings list inside the same window. The back arrow in the page header, or `Esc`, returns to the settings. `Esc` while a shortcut dialog is open cancels the capture only. Changes take effect at once and the vault list refreshes.

## Default shortcuts

| Item type | Action | Shortcut |
|-----------|--------|----------|
| All items | Open in companion | `Ctrl+O` |
| All items | View in Web Vault | `Ctrl+Shift+O` |
| Login | Copy Username | `Ctrl+C` |
| Login | Copy Password | `Ctrl+Shift+C` |
| Login | Copy OTP | `Ctrl+Alt+C` |
| Login | Open in Browser | None |
| Login | Rotate password | None |
| Card | Copy Card Number | `Ctrl+C` |
| Card | Copy Code | `Ctrl+Shift+C` |
| Card | Copy Cardholder | `Ctrl+Alt+C` |
| Card | Copy Expiration | `Ctrl+E` |
| Identity | Copy Email | `Ctrl+C` |
| Identity | Copy Name | `Ctrl+Shift+C` |
| Identity | Copy Phone | `Ctrl+Alt+C` |
| Identity | Copy Username | `Ctrl+U` |
| Identity | Copy Address | `Ctrl+Shift+A` |
| Secure note | Copy Notes | `Ctrl+C` |
| SSH key | Copy Public Key | `Ctrl+C` |
| SSH key | Copy Fingerprint | `Ctrl+Shift+C` |
| SSH key | Open SSH Session | None |

The custom field copy actions have no shortcut and cannot be changed.

## Change a shortcut

1. Select the pencil button of an action.
2. Press the new key combination. The dialog shows the keys as you press them.
3. Select **Save**.

**Reset** in the dialog sets the default shortcut. **Clear** removes the shortcut, so the action has none. **Cancel** closes the dialog and changes nothing. A **Reset** button next to an action shows when its shortcut differs from the default. **Reset all** at the bottom of the window restores every shortcut and every Enter action.

The extension checks each shortcut before it saves it:

- A shortcut must include Ctrl or Alt. A bare key or a Shift-only combination would fire while you type in the search box.
- Enter on its own is reserved for the Enter action.
- A shortcut cannot be used by two actions of the same item type. The actions under **All items** apply to every item type. The dialog shows the name of the action that already uses the shortcut.
- The Windows key is not supported.

## Enter action

The first row of each group is the **Enter action**, the action that Enter runs for items of that type. The choices are the actions of that item type and the **All items** actions.

| Item type | Default Enter action |
|-----------|----------------------|
| Login | Open in Browser |
| SSH key | Open SSH Session |
| Card, Identity, Secure note | Open in companion |

The more-commands menu does not show the Enter action when that action has no shortcut, because Enter already runs it. Give the action a shortcut to show it in the menu again.

The **All items** row sets the Enter action for card, identity and secure note items that have no setting of their own. **Reset** next to the Enter action of those types removes their setting, so they follow **All items** again.

If the chosen action is not available for an item, Enter runs the default behaviour described in [Vault Item Actions](Vault-Item-Actions#default-action-and-open-in-companion). Examples are Copy Password on a login with no password and Open in Browser on a login with no web URL.

## Storage

The extension saves only the changes to `%LOCALAPPDATA%\HoobiBitwardenCommandPalette\hotkeys.json`, next to `settings.json`. If the file is missing or cannot be read, the default shortcuts apply and the extension writes a line to the debug log.
