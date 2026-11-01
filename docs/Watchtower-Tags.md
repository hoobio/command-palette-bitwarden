# Watchtower Tags

Watchtower provides visual security warnings on vault items, similar to Bitwarden's built-in reports.

## Tags

Items display colored tags based on security checks:

| Tag | Color | Condition |
|-----|-------|-----------|
| **Weak** | Red | Password is shorter than 8 characters |
| **Old** | Orange | Password hasn't changed in over 365 days (based on `passwordRevisionDate` or item revision date) |
| **Insecure URL** | Red | Login has a URI using `http://` instead of `https://` |

## Configuration

Watchtower tags can be toggled in **Settings > Show Tags > Watchtower** (default: on). When disabled, none of the security warning tags are shown. Turning off **Show Tags** hides every tag, including these.

## Tag Display Order

On a single item, tags appear in this order:

1. **Recent** (green) - last copied item (pinned 5 minutes)
2. **🔒** (amber) - item requires master password re-prompt (configurable via **Settings > Show Tags > Protected**)
3. **★** (gold) - favorite
4. **Context** (blue) - matches an open app/browser tab
5. **2FA / TOTP** (green) - live code or static badge
6. **Passkey** (blue) - item has a FIDO2 passkey
7. **Initials / org name** - item belongs to a Bitwarden organization, shown as an avatar-style initials badge (e.g. `N` for Nintex, `TO` for Test Organization) or the full organization name. Colour is stable per organization (deterministic palette). Style configurable via **Settings > Show Tags > Organisation**
8. **Collection names** (grey) - one tag per collection that holds the item, sorted by name. Names longer than 20 characters are truncated, and the tooltip shows the full name
9. **Weak**, **Old**, **Insecure URL** (Watchtower)
