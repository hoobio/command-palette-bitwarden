# Vault Item Actions

Each vault item type exposes different actions. Select an item to see its available actions.

## Login Items

| Action | Description | Clipboard |
|--------|-------------|-----------|
| Copy Username | Copies the username | Standard |
| Copy Password | Copies the password | Secure (excluded + auto-clear) |
| Copy TOTP Code | Copies the current TOTP code (if configured) | Secure |
| Copy Custom Fields | One action per custom field | Secure for hidden fields, standard for others |

## Card Items

| Action | Description | Clipboard |
|--------|-------------|-----------|
| Copy Card Number | Copies the full card number | Secure |
| Copy Security Code | Copies the CVV/CVC | Secure |
| Copy Cardholder Name | Copies the name on the card | Standard |

## Identity Items

| Action | Description | Clipboard |
|--------|-------------|-----------|
| Copy Username | Copies the identity username | Standard |
| Copy Email | Copies the email address | Standard |
| Copy Phone | Copies the phone number | Standard |

## SSH Key Items

| Action | Description | Clipboard |
|--------|-------------|-----------|
| Copy Public Key | Copies the public key | Standard |
| Copy Fingerprint | Copies the key fingerprint | Standard |
| SSH Quick Connect | If the item has a `host` custom field, runs `ssh <host>` in a new terminal window | N/A |

## Custom Fields

All custom fields on an item get their own copy action. **Hidden** custom fields are treated as sensitive (clipboard exclusion + auto-clear), while **text** and **boolean** fields use standard clipboard.

## Manual Sync

The **Sync Vault** action triggers a full `bw sync` followed by a cache refresh. The vault also auto-syncs every 5 minutes.
