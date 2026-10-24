# Companion Window

The companion window shows a vault item in its own window next to Command Palette. You can keep the window open beside another app and copy fields from it.

## Window commands

- **Open in window** is in the more-commands menu of each vault item (`Ctrl+Shift+O`), after the commands for the item type and before **View in Web Vault**. The Enter and `Ctrl+Enter` actions of the item do not change.
- **Open vault** is a top-level command. It opens the vault window.
- **Rotate password** is in the more-commands menu of each login, after **Open in window**. It opens the item window and asks before it replaces the password (see [Quick Rotate](#quick-rotate)). It never rotates without the question.

If the window for an item is already open, the command brings that window to the front and loads the item again. You can open windows for more than one item at the same time.

## Vault window

The vault window has the same layout as Bitwarden Desktop.

- **Filters** (left): All items, Favourites, each item type (Login, Card, Identity, Secure note, SSH key), each folder and No folder, each collection, each organisation, Archive and Trash. Collections and organisations show only when your account has them. Archive shows only when you have archived items. All items and the other filters do not include items in the archive or in the trash.
- **Items** (middle): the name, and below it the username, card brand and last four digits, email or SSH fingerprint. A login shows its website icon when Command Palette already downloaded it. The search box filters the list as you type. It finds text in the name, that subtext and the website hosts. It does not search notes.
- **Item details** (right): the same view as the item window. Select **Open in own window** to open the item in an item window, which you can keep on top of other windows.
- **New item** opens an empty form for the item type that you select. **Clone** opens a form with a copy of the selected item, named "<name> - Clone". A clone does not copy passkeys.
- **Generator** opens the [password generator](#password-generator).

### Folders

Select **+** next to the Folders header to create a folder. Right-click a folder to **Rename** or **Delete** it. The name cannot be empty; spaces at the start and end are removed. **Delete** asks first and tells you that the items in the folder move to No folder. You cannot undo a folder delete. The filter list loads again after each change.

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

### Attachments

The item details show an **Attachments** group with the name and size of each file.

- The download button opens a save dialog. Bitwarden writes the file to the place that you select, so the file content never goes through the companion.
- **Add attachment** opens a file dialog and adds the file to the item. A file can be up to 500 MB.
- The red delete button asks first. You cannot undo an attachment delete.

Attachments need Bitwarden Premium, or an organisation item in an organisation that allows them. The window shows a message when Bitwarden refuses a file because of the plan, the 500 MB limit or the storage space. For an item with master password re-prompt, the attachments show after you verify, and each download, upload and delete asks for your master password outside the grace period.

### Always on top

Right-click the title bar or an empty area of the window and select **Always on top** to keep the window above other windows. New item windows use the same choice until the companion stops.

## Edit an item

Select **Edit** in the vault window or an item window. The form has the fields of Bitwarden Desktop:

| Item type | Fields |
|-----------|--------|
| Login | Username, password, authenticator key (TOTP), websites with match detection (default, base domain, host, starts with, exact, regular expression, never) |
| Card | Cardholder name, brand, number, expiration month and year, security code |
| Identity | Title, first, middle and last name, username, company, national insurance number, passport number, licence number, email, phone, address 1 to 3, city or town, county or state, postcode, country |
| Secure note | Notes |
| SSH key | Private key (masked, with a show button), public key, fingerprint. **Import from private key** fills the public key and fingerprint from an OpenSSH private key. |

All item types also have name, folder, favourite, master password re-prompt, notes and custom fields (text, hidden, boolean, linked). A linked field points to a field of the item type. You can add, remove and reorder websites and custom fields. When your account has organisations, **Owner** and **Collections** set the organisation of a new item, move a personal item into an organisation or change the collections of an organisation item. An organisation item needs at least one collection. A new organisation item, or a clone of one, is created directly in the organisation and never passes through your personal vault. An item cannot move back to your personal vault.

Bank account, driver's licence and passport items show read-only, with no **Edit** button.

- **Save** checks that the name is set, each new custom field has a name and the expiration month is 1 to 12. A website can be any text, as in Bitwarden. A boolean custom field keeps its stored value until you change the switch. The extension then saves the item and reads it back to confirm the change.
- **Cancel**, or closing the window, asks before it discards changes that are not saved.
- If the item changed in the vault after you started to edit it, for example from another device, **Save** asks you to **Reload** the item and discard your changes, or **Keep editing**. The extension syncs the vault first, so **Reload** shows the server version.
- A save waits for Bitwarden for as long as it takes. If Bitwarden does not confirm a new item, the window says so and asks before it saves another copy. Check the vault list first: the item can already be there.
- The generate button next to the password opens the [password generator](#password-generator). **Use password** puts the value in the password field.

The item details also have a red trash-can button (**Delete**) and, for a login, **Password history** and **Rotate password**. Password history shows the date of each old password. The values are masked, and the copy button copies through the extension.

The trash-can button opens a dialog that names the item. For an active item it offers **Archive**, **Permanently delete** and **Cancel**. When Bitwarden cannot archive the item (for example an organisation item, or an account without archive), **Move to Trash** replaces **Archive**, so a recoverable option stays available. In the trash or the archive, the button is **Delete permanently** and the dialog offers **Permanently delete** and **Cancel**; **Restore** is a separate button. The dialog stays open while Bitwarden runs the change and shows an error in the dialog when the change fails.

## Password generator

The generator has three tabs:

| Tab | Options | Default |
|-----|---------|---------|
| Password | Length (5 to 128), uppercase, lowercase, numbers, special characters (`!@#$%^&*`), minimum numbers and minimum special characters (0 to 9), avoid ambiguous characters | 20 characters, uppercase, lowercase and numbers, at least 1 number |
| Passphrase | Number of words (3 to 20), word separator (one character), capitalise, include number | 4 words, separated by `-` |
| Username | Plus-addressed email (`jane+abc12345@example.com`, from the email that you enter) or catch-all email (`abc12345@example.com`, from the domain that you enter) | Plus-addressed |

The Bitwarden CLI makes passwords and passphrases. The extension makes usernames, because the CLI has no username generator; the random part is 8 letters and digits from a cryptographic random number generator. **Regenerate** makes a new value, the copy button copies it through the extension with the clipboard clear timer, and **Use password** (from the edit form only) puts it in the password field. The username tab is not available from the edit form.

**Generator history** lists the last 50 values with their type and time. The values are masked; the copy button copies through the extension. The history is kept in the extension memory only, is never written to disk, and is erased when the vault locks. The companion saves the generator options, but not the values, in `%LOCALAPPDATA%\HoobiBitwardenCommandPalette\companion-generator.json`.

## Quick Rotate

**Rotate password** (in the item details of a login, or in the Command Palette more-commands menu) replaces the password of a login in one step:

1. It asks "Replace the password for <name>? The old password is kept in password history."
2. It makes a password with the saved generator options (a password, or a passphrase when the passphrase tab was used last).
3. It saves the item with the same checks as **Save**: the item must not have changed in the vault, and the extension reads the item back and compares the password.
4. It copies the new password through the extension with the clipboard clear timer.

If Bitwarden does not confirm the save, nothing is copied and the window asks you to check the item. If the item changed in the vault, nothing is replaced or copied. For an item with master password re-prompt, Rotate asks for your master password outside the grace period.

## Master password re-prompt

For an item with master password re-prompt, the window shows no field values until you verify your master password. Select **Verify master password** and enter your master password. The window uses the same re-prompt grace period as Command Palette (see [Settings](Settings)). A successful verification starts the grace period. During the grace period, you can show, copy and open values without a prompt. When the grace period is 0, the window asks for your master password for each show, copy or open action, and the verification code is masked. **Edit**, **Clone** and **Password history** use the same re-prompt. **Save**, **Archive**, **Move to Trash**, **Permanently delete**, **Restore**, owner or collection changes, attachment download, upload and delete, and **Rotate password** also ask for your master password outside the grace period, as the Bitwarden web vault does. When the grace period is 0, a save that also moves the item into an organisation asks twice. Five failed attempts start the same 30-second cooldown as the Command Palette re-prompt.

If the item changes in the vault while the window is open, the window loads the new version and does not show or copy the old value.

## Locked vault

When the vault locks or auto-locks, every item window removes the values it shows, closes an open edit form without saving, stops the verification code and shows that the vault is locked. The vault window removes the list and the item details and shows that the vault is locked. An open generator removes its value and history. Unlock the vault in Command Palette. The vault window and the open item windows then load the items again.

See [Architecture](Architecture) for the process model and the pipe protocol.
