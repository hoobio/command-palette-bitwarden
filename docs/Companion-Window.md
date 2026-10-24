# Companion Window

The companion window shows a vault item in its own window next to Command Palette. You can keep the window open beside another app and copy fields from it.

## Window commands

- **Open in window** is in the more-commands menu of each vault item (`Ctrl+Shift+O`), after the commands for the item type and before **View in Web Vault**. The Enter and `Ctrl+Enter` actions of the item do not change.
- **Open vault** is a top-level command. It opens the vault window.

If the window for an item is already open, the command brings that window to the front and loads the item again. You can open windows for more than one item at the same time.

## Vault window

The vault window has the same layout as Bitwarden Desktop. It is read-only in this release.

- **Filters** (left): All items, Favourites, each item type (Login, Card, Identity, Secure note, SSH key), each folder and No folder, each collection, each organisation, Archive and Trash. Collections and organisations show only when your account has them. Archive shows only when you have archived items. All items and the other filters do not include items in the archive or in the trash.
- **Items** (middle): the name, and below it the username, card brand and last four digits, email or SSH fingerprint. A login shows its website icon when Command Palette already downloaded it. The search box filters the list as you type. It finds text in the name, that subtext and the website hosts. It does not search notes.
- **Item details** (right): the same view as the item window. Select **Open in own window** to open the item in an item window, which you can keep on top of other windows.

| Key | Action |
|-----|--------|
| `Ctrl+F` | Go to the search box |
| `Up`, `Down` | Select the previous or next item |
| `Enter` | Open the selected item in its own window |
| `Esc` | Clear the search |

When the vault changes, for example after a sync, the vault window and every item window load the new data. The selected item stays selected.

## Item window

The item window shows the item name, its type icon, and its folder and organization. The fields are grouped as Bitwarden Desktop groups them:

| Item type | Groups |
|-----------|--------|
| Login | Login credentials (username, password, verification code), Autofill options (websites) |
| Card | Card details (cardholder name, brand, number, expiration date, security code) |
| Identity | Personal details (name, username, company), Contact info (email, phone, address) |
| Secure note | Additional options (notes) |
| SSH key | SSH key (private key, public key, fingerprint) |

All item types also show Custom fields, Additional options (notes) and Item history (created, last edited, password updated) when the item has them.

- Secret fields (password, card number, security code, SSH private key, hidden custom fields, notes) are masked. Select the eye button to show or hide one value.
- The copy button copies through the extension. Secrets are excluded from clipboard history and cleared after the time set in [Settings](Settings), also after you close the window.
- The verification code changes at the end of each period. The ring shows the seconds left in the current period.
- The open button on a website opens it in your browser. Only `http` and `https` addresses open.

The window has a close button only, so it does not minimise or maximise. You can resize it.

### Always on top

Right-click the title bar or an empty area of the window and select **Always on top** to keep the window above other windows. New item windows use the same choice until the companion stops.

## Master password re-prompt

For an item with master password re-prompt, the window shows no field values until you verify your master password. Select **Verify master password** and enter your master password. The window uses the same re-prompt grace period as Command Palette (see [Settings](Settings)). A successful verification starts the grace period. During the grace period, you can show, copy and open values without a prompt. When the grace period is 0, the window asks for your master password for each show, copy or open action, and the verification code is masked. Five failed attempts start the same 30-second cooldown as the Command Palette re-prompt.

If the item changes in the vault while the window is open, the window loads the new version and does not show or copy the old value.

## Locked vault

When the vault locks or auto-locks, every item window removes the values it shows, stops the verification code and shows that the vault is locked. The vault window removes the list and the item details and shows that the vault is locked. Unlock the vault in Command Palette. The vault window and the open item windows then load the items again.

See [Architecture](Architecture) for the process model and the pipe protocol.
