# Companion Window

The companion window shows a vault item in its own window next to Command Palette. You can keep the window open beside another app and copy fields from it.

## Window commands

- **Open in window** is in the more-commands menu of each vault item (`Ctrl+Shift+O`), after the commands for the item type and before **View in Web Vault**. The Enter and `Ctrl+Enter` actions of the item do not change.
- **Open vault** is a top-level command. In this release it opens a placeholder vault window. The vault list comes in a later release.

If the window for an item is already open, the command brings that window to the front and loads the item again. You can open windows for more than one item at the same time.

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

When the vault locks or auto-locks, every item window removes the values it shows, stops the verification code and shows that the vault is locked. Unlock the vault in Command Palette and open the item again.

See [Architecture](Architecture) for the process model and the pipe protocol.
