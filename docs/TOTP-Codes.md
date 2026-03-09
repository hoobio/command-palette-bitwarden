# TOTP Codes

Vault items with TOTP (Time-based One-Time Password) configured display a live countdown tag directly on the list item.

## Display

TOTP codes are shown as a green tag in the format:

> **🔑 123456 (25s)**

The tag updates every second via a background timer. The countdown shows the number of seconds remaining before the current code expires (typically a 30-second window).

## Copying

Select **Copy TOTP Code** from the item's actions to copy the current code. It's copied through the secure clipboard service — excluded from clipboard history and auto-cleared.

## Base32 Key Handling

The extension strips whitespace and hyphens from TOTP secret keys before decoding. This handles cases where secrets were stored with spaces (e.g., `JBSW Y3DP EHPK 3PXP` → `JBSWY3DPEHPK3PXP`).

## Filtering

Use `has:totp` or `has:otp` in search to find all items with TOTP configured:

```
has:totp folder:Work
```
