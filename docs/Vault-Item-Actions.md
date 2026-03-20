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

## Manual Sync

**Sync Vault** triggers a full `bw sync` followed by a cache refresh.
