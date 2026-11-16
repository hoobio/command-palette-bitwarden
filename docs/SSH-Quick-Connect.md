# SSH Keys

SSH key items support a quick-connect feature that launches an SSH session directly from the Command Palette.

## Quick Connect

### Companion Editor

The companion item editor has an **SSH host** box for SSH key items. Enter the value in `user@hostname` format. The box stores it in the `host` custom field, so other Bitwarden clients see the same value. An empty box removes the field.

The box does not appear when the item already has a hidden `host` field. That field stays in the custom fields list and is not changed.

### Other Bitwarden Clients

Add a custom field named `host` to an SSH key item with a value in `user@hostname` format. When set, the default action (Enter) launches a session immediately:

```
ssh user@hostname
```

This calls `ssh` directly in your default console host. The item also gets an **Open SSH Session** context action.

### host Field Format

The value must match `user@hostname`. Letters, digits, `.`, `-` and `_` are accepted on both sides. The user also accepts `+` and `\`, so a Windows domain user such as `DOMAIN\user@hostname` is valid. The value cannot start with `-`. Invalid or missing values fall back to opening the item in the Bitwarden app.

Valid examples:

```
admin@server.example.com
deploy@192.168.1.10
git@github.com
```
