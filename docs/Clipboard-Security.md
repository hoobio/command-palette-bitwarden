# Clipboard Security

The extension uses a custom clipboard service with two security features for sensitive data.

## Clipboard History Exclusion

When copying sensitive credentials (passwords, TOTP codes, card numbers, security codes), the extension sets the `ExcludeClipboardContentFromMonitorProcessing` clipboard format. This tells Windows to exclude the copied data from clipboard history and cloud clipboard sync, so sensitive values don't persist in your clipboard history or sync across devices.

Non-sensitive data (usernames, public keys, cardholder names) uses the standard clipboard without this exclusion.

## Auto-Clear

After copying sensitive data, the clipboard is automatically cleared after a configurable delay. Before clearing, the extension checks whether the clipboard contents have been replaced by another application. If the user has already copied something else, the clear is skipped.

### Sensitive vs Non-Sensitive

| Sensitive (excluded + auto-cleared) | Non-sensitive (standard clipboard) |
|--------------------------------------|-------------------------------------|
| Passwords                            | Usernames                           |
| TOTP codes                           | Public SSH keys                     |
| Card numbers                         | Cardholder names                    |
| Security codes (CVV)                 | Fingerprints                        |
| Custom field values (hidden type)    | Email addresses                     |
|                                      | Phone numbers                       |

## Configuration

| Setting | Description | Default |
|---------|-------------|---------|
| **Auto-Clear Clipboard** | Enable/disable automatic clearing of sensitive clipboard data | On |
| **Clipboard Clear Delay** | How long to wait before clearing (10s, 15s, 30s, 60s, 2 min) | 10 seconds |
