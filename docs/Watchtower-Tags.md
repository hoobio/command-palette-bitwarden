# Watchtower Tags

Watchtower provides visual security warnings on vault items, similar to Bitwarden's reports and 1Password's Watchtower feature.

## Tags

Items display colored tags based on password health checks:

| Tag | Color | Condition |
|-----|-------|-----------|
| **Weak** | Red | Bitwarden has flagged the item's password as weak (via `passwordStrength`) |
| **Old Password** | Orange | Password hasn't been changed in over 365 days (based on `passwordRevisionDate`) |
| **HTTP** | Red | Login URI uses `http://` instead of `https://` |
| **Reused** | Orange | Password is shared across multiple vault items |
| **No Password** | Gray | Login item has no password set |

## Reuse Detection

The extension counts password occurrences across all cached login items. If more than one item shares the same password, all of them receive the **Reused** tag.

## Configuration

Watchtower tags can be toggled in **Settings → Show Watchtower Tags** (default: on). When disabled, none of the security warning tags are shown, reducing visual noise for users who prefer a cleaner list.

## Tag Display Order

On a single item, tags appear in this order:

1. **Recent** (green) — if this was the last copied item
2. **★** (gold) — if the item is a favorite
3. **Context** (blue) — if the item matches an open app/browser tab
4. **TOTP** (green) — live code with countdown
5. **Watchtower** tags — Weak, Old Password, HTTP, Reused, No Password
