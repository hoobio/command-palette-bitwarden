# Context Awareness

Context awareness detects your currently open applications and browser tabs, then boosts matching vault items to the top of results and tags them with a blue **Context** label.

## How It Works

When the Command Palette opens, the extension captures the **top 5 visible windows** by Z-order (the order windows are stacked on screen). Each window is identified by its process name and window title. Browser windows also get URL extraction.

Items matching the topmost window receive the highest context boost. Items matching the second window receive a slightly lower boost, and so on. This means the app you were just using gets the strongest priority.

### Browser Matching

For browser windows (Chrome, Edge, Firefox, Brave, Opera, Vivaldi, Arc, etc.), matching uses multiple strategies:

1. **URL extraction via UI Automation** — reads the actual URL from the browser's address bar using the Windows UI Automation COM API
2. **URL from title parsing** — falls back to extracting domain-like strings from the browser window title
3. **Host matching** — compares extracted URL hosts against vault item URIs (with subdomain matching, e.g., `mail.google.com` matches `google.com`)
4. **Name matching** — checks if the vault item's name appears in the browser title (e.g., item "GitHub" matches title "Dashboard · GitHub - Google Chrome")

### Application Matching

For non-browser applications, matching checks:

1. **Process name ↔ item name** — both directions, so `steam` matches "Steam" and `steamwebhelper` matches "Steam"
2. **Window title ↔ item name** — window title "Steam" matches an item named "Steam"
3. **Process name ↔ item URIs** — process `discord` matches items with URIs containing "discord"
4. **Window title ↔ item URI hosts** — for items with URLs

### Z-Order Scoring

The extension collects up to 5 windows behind the Command Palette, deduplicating by process ID. Each matching window position yields a descending score:

| Window Position | Score |
|----------------|-------|
| 1st (topmost)   | 5     |
| 2nd              | 4     |
| 3rd              | 3     |
| 4th              | 2     |
| 5th              | 1     |

Vault items matching no windows receive a score of 0. This scoring is used as a tiebreaker in the sort order (after Recent and Favorites).

## Configuration

Context awareness can be disabled in **Settings → Context Awareness** for users who prefer not to have foreground app detection. When disabled, no window information is captured and no context boosting or tagging occurs.

## Supported Browsers

Chrome, Microsoft Edge, Firefox, Brave, Opera, Vivaldi, Arc, Thorium, Waterfox, LibreWolf
