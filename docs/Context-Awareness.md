# Context Awareness

Context awareness detects your currently open applications and browser tabs, then boosts matching vault items to the top of results and tags them with a blue **Context** label.

## How It Works

When the Command Palette opens, the extension captures the **top 5 visible windows** by Z-order. Each window is identified by its process name and window title. Browser windows also get URL extraction.

Items matching the topmost window receive the highest context boost. Items matching the second window receive a slightly lower boost, and so on.

### Browser Matching

For browser windows (Chrome, Edge, Firefox, Brave, Opera, Vivaldi, Arc, etc.), matching uses multiple strategies:

1. **URL extraction via UI Automation** - reads the actual URL from the browser's address bar
2. **URL from title parsing** - falls back to extracting domain-like strings from the browser window title
3. **URI match detection** - respects each vault item's URI match detection setting (see below)
4. **Name matching** - checks if the vault item's name appears in the browser title (e.g., item "GitHub" matches title "Dashboard · GitHub - Google Chrome")

### URI Match Detection

Context matching respects the **match detection** setting configured on each URI in Bitwarden:

| Match Type | Behavior |
|-----------|----------|
| **Default / Base Domain** | Subdomain-inclusive matching (e.g., `mail.google.com` matches `google.com`) |
| **Host** | Exact host only (e.g., `app.example.com` does not match `example.com`) |
| **Starts With** | Browser URL must start with the vault URI |
| **Exact** | Browser URL must exactly match the vault URI |
| **Regular Expression** | Vault URI is treated as a regex pattern |
| **Never** | URI is excluded from context matching entirely |

### Application Matching

For non-browser applications, matching checks:

1. **Process name vs item name** - both directions, so `steam` matches "Steam" and `steamwebhelper` matches "Steam"
2. **Window title vs item name** - window title "Steam" matches an item named "Steam"
3. **Process name vs item URIs** - process `discord` matches items with URIs containing "discord"
4. **Window title vs item URI hosts** - for items with URLs

### Z-Order Scoring

The extension collects up to 5 windows behind the Command Palette, deduplicating by process ID. Each matching window position yields a descending score:

| Window Position | Score |
|----------------|-------|
| 1st (topmost)   | 5     |
| 2nd              | 4     |
| 3rd              | 3     |
| 4th              | 2     |
| 5th              | 1     |

Vault items matching no windows receive a score of 0. This score is used as a tiebreaker in the sort order (after Recent and Favorites).

## Configuration

Context awareness can be disabled in **Settings > Context Awareness**. When disabled, no window information is captured and no context boosting or tagging occurs.

## Supported Browsers

Chrome, Microsoft Edge, Firefox, Brave, Opera, Vivaldi, Arc, Thorium, Waterfox, LibreWolf
