# Search & Filtering

## Basic Search

Type any text in the vault browser to search across item names, usernames, URIs, and notes. Results are ranked by relevance with smart sorting applied on top.

You can also type anywhere in the Command Palette; matching vault items appear as **fallback suggestions** without needing to open the Bitwarden page first.

## Search Filters

Use prefix syntax to narrow results. Filters can be combined freely.

| Filter | Example | Description |
|--------|---------|-------------|
| `is:fav` / `is:favorite` | `is:fav github` | Show only favorite items |
| `is:protected` / `is:locked` / `is:reprompt` | `is:protected` | Items with master password re-prompt enabled |
| `folder:<name>` | `folder:Work` | Filter by folder name (partial match) |
| `has:totp` / `has:otp` / `has:2fa` / `has:mfa` | `has:totp` | Items with TOTP configured |
| `has:passkey` / `has:fido2` / `has:webauthn` / `has:passwordless` | `has:passkey` | Items with a passkey configured |
| `has:password` | `has:password` | Items with a password set |
| `has:url` | `has:url` | Items with at least one URI |
| `has:notes` | `has:notes` | Items with notes |
| `url:<partial>` / `host:<partial>` | `url:github.com` | Login items matching a URL |
| `type:<type>` | `type:login` | Filter by item type |
| `org:<id>` | `org:myorg` | Filter by organization |
| `is:weak` | `is:weak` | Logins with a password shorter than 8 characters |
| `is:old` / `is:stale` | `is:old` | Logins whose password hasn't changed in over a year |
| `is:insecure` / `is:http` | `is:insecure` | Logins with an `http://` URI |
| `is:watchtower` / `is:flagged` | `is:watchtower` | Any login triggering a Watchtower warning |

### Item Types

Valid values for `type:` filter: `login`, `card`, `identity`, `securenote`, `sshkey`

### Combining Filters

```
is:fav has:totp folder:Personal
```

This shows only favorite items with TOTP that are in a folder containing "Personal".

## Sort Order

Items are sorted in this priority order:

1. **Recently copied** - the last item you copied from (pinned for 5 minutes, shown with a "Recent" tag)
2. **Favorites** - items marked as favorites in Bitwarden
3. **Context match** - items matching your currently open applications (scored by Z-order proximity)
4. **Last accessed** - items you've interacted with recently via this extension
5. **Revision date** - most recently modified items
6. **Alphabetical** - by item name

When a text search is active, relevance score takes priority, with the above tiers as tiebreakers.
