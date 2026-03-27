# Vault Item Actions

Each vault item type exposes different actions. Select an item to trigger the default action, or press the actions shortcut to see all available actions.

## Login Items

| Action | Description | Clipboard |
|--------|-------------|----------|
| Open in Browser | Opens the first URI in the default browser | N/A |
| Copy Username | Copies the username | Standard |
| Copy Password | Copies the password | Secure (excluded + auto-clear) |
| Copy TOTP Code | Copies the current TOTP code (if configured) | Secure |
| Copy Custom Fields | One action per custom field | Secure for hidden fields, standard for others |

## Card Items

| Action | Description | Clipboard |
|--------|-------------|----------|
| Copy Card Number | Copies the full card number | Secure |
| Copy Security Code | Copies the CVV/CVC | Secure |
| Copy Cardholder Name | Copies the name on the card | Standard |

## Identity Items

| Action | Description | Clipboard |
|--------|-------------|----------|
| Copy Username | Copies the identity username | Standard |
| Copy Email | Copies the email address | Standard |
| Copy Phone | Copies the phone number | Standard |

## SSH Key Items

See [SSH Quick Connect](SSH-Quick-Connect) for full details.

| Action | Description | Clipboard |
|--------|-------------|----------|
| Copy Public Key | Copies the public key | Standard |
| Copy Fingerprint | Copies the key fingerprint | Standard |
| Open SSH Session | Runs `ssh <host>` if the item has a `host` custom field | N/A |

## Custom Fields

All custom fields on an item get their own copy action. **Hidden** custom fields are treated as sensitive (clipboard exclusion + auto-clear), while **text** and **boolean** fields use standard clipboard.

## Master Password Re-prompt

Vault items with **Master password re-prompt** enabled require you to re-enter your master password before any data can be accessed. This applies to all item types.

When an item has re-prompt enabled:

- A **🔒** (padlock) tag is shown on the item in the vault list
- Secure Note subtitles are masked ("Protected")
- Selecting the item (Enter key) opens a master password verification form before performing the default action
- **All** copy actions — including normally non-sensitive fields like Username, Cardholder Name, and Email — require verification
- Non-copy actions (Open in Browser, View in Web Vault) via context menu remain unprotected
- After entering the correct password, a toast confirms the action and a configurable **grace period** begins
- During the grace period, subsequent actions on any protected item execute instantly without any prompt
- The grace period is cleared when the vault is locked or you log out

This matches the Bitwarden desktop/web vault behaviour where re-prompt gates access to all fields on a protected item.

## Manual Sync

**Sync Vault** triggers a full `bw sync` followed by a cache refresh.
