# SSH Keys

SSH key items support a quick-connect feature that launches an SSH session directly from the Command Palette.

## Quick Connect

Add a custom field named `host` to an SSH key item with a value in `user@hostname` format. When set, the default action (Enter) launches a session immediately:

```
ssh user@hostname
```

This calls `ssh` directly in your default console host. The item also gets an **Open SSH Session** context action.

### host Field Format

The value must match `user@hostname`. Letters, digits, `.`, `+`, `-`, and `_` are accepted on both sides. Invalid or missing values fall back to opening the item in the Bitwarden app.

Valid examples:

```
admin@server.example.com
deploy@192.168.1.10
git@github.com
```
