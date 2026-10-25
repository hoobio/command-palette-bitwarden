# Companion Window

The companion window shows a vault item in its own window next to Command Palette. You can keep the window open beside another app and copy fields from it. A full vault window is planned.

## Window commands

- **Open in window** is the first command in the more-commands menu of each vault item (`Ctrl+Shift+O`). Command Palette runs the first more-command on `Ctrl+Enter`, so `Ctrl+Enter` opens the item window.
- The Enter action of a note, card, identity, login without a URL or SSH key without a host opens the item window. Logins with a URL still open the website, and SSH keys with a host still open the SSH session. If the companion is not available, Enter opens the web vault.
- **Rotate password** is in the more-commands menu of each login that has a password, after the commands for the item type. It opens the item window and then the **Rotate password** dialog (see [Quick Rotate](#quick-rotate)). It never rotates until you select **Save**.
- **New Bitwarden item** is a top-level command in Command Palette. It opens an empty item window in create mode (see [New item](#new-item)). Search for it with words such as "new login", "new password", "note", "card" or "identity". You can also set an alias for it in Command Palette.
- **Generate password** is a top-level command in Command Palette. It opens the [password generator](#password-generator) in its own window.
- When the vault is locked, **New Bitwarden item** and **Generate password** show a message in Command Palette and open no window.

The window opens in front with keyboard focus. Command Palette gives the extension the right to set the foreground window for each command, and the extension passes that right to the companion. If the window for an item is already open, the command brings that window to the front and loads the item again. You can open windows for more than one item at the same time.

## Scrolling

Every scroll area scrolls smoothly with the mouse wheel. Press the middle mouse button on a scroll area and move the pointer up or down to scroll. The farther the pointer is from the start point, the faster the scroll. Click, press `Esc`, or release the button after a move to stop.

## Item window

The title bar shows the item's icon and name, then the pencil button (**Edit**) and the red trash-can button (**Delete**) to the left of the close button. Edit and Delete have the size and hover style of caption buttons, and are disabled while the master password panel shows. At the top of the window are the item name, its website or card brand icon (or its type icon) and badges for the folder, organisation, favourite and master password re-prompt. The name is shortened when the window is narrow. For a login that has a password, a **Rotate** link is next to the **Password** label. The icons come from the same cache as Command Palette. The fields are grouped as Bitwarden Desktop groups them. Each group is one block of rows with its name above it:

| Item type | Groups |
|-----------|--------|
| Login | Login credentials (username, password, verification code), Autofill options (websites) |
| Card | Card details (cardholder name, brand, number, expiration date, security code) |
| Identity | Personal details (name, username, company), Contact info (email, phone, address) |
| Secure note | Additional options (notes) |
| SSH key | SSH key (private key, public key, fingerprint) |

All item types also show Custom fields, Additional options (notes) and Item history (created, last edited, password updated) when the item has them.

- Secret fields (password, card number, security code, SSH private key, hidden custom fields, notes) are masked. Select the eye button to show or hide one value.
- You cannot select the text of a shown password, card number, security code or verification code, so `Ctrl+C` cannot go around the extension's clipboard. A shown note or SSH private key lets you select part of the text. `Ctrl+C` or **Copy** in its menu sends the selected range to the extension, and the extension copies that part of its own value through the secure clipboard. The companion never puts the selection on the clipboard.
- For a login, a **Password history** group with **Show password history** is below the fields. It shows the date of each old password. The values are masked, and the copy button copies through the extension.
- The copy button copies through the extension. Secrets are excluded from clipboard history and cleared after the time set in [Settings](Settings), also after you close the window.
- The verification code changes at the end of each period. The ring shows the seconds left in the current period. The ring and the number turn yellow for the last 5 seconds.
- The open button on a website opens it in your browser. Only `http` and `https` addresses open.
- A failed request shows as a yellow message at the top of the item. Red is used only for actions that you cannot undo. When an action fails because the item changed in the vault, the window reloads the item and a yellow message at the top says so.
- The result of a successful action (copy, save, rotate, attach, download or delete an attachment, copy from the generator) shows in a small toast at the bottom centre of the monitor that holds the window, above the taskbar, as in Command Palette. The toast names the field or file, never its value. It does not take focus, has no taskbar button and goes away after about 2 seconds. A new result replaces the text and starts the 2 seconds again.

- While a change that goes to Bitwarden runs (save, rotate, attachment upload, download or delete, archive or delete), a progress bar and a caption show under the item header, for example "Saving the new password to Bitwarden…" or "Uploading attachment…". The caption names the action, not each step, because the extension sends one reply when the action is done.

When the vault changes, for example after a sync, every item window loads the new data.

The window has a close button only, so it does not minimise or maximise. You can resize it.

### Attachments

The item window shows an **Attachments** group with the name and size of each file.

- The download button opens a save dialog. Bitwarden writes the file to the place that you select, so the file content never goes through the companion.
- **Add attachment**, the last row of the group, opens a file dialog and adds the file to the item. A file can be up to 500 MB.
- The red delete button asks first. You cannot undo an attachment delete.

Attachments need Bitwarden Premium, or an organisation item in an organisation that allows them. The window shows a message when Bitwarden refuses a file because of the plan, the 500 MB limit or the storage space. For an item with master password re-prompt, the attachments show after you verify, and each download, upload and delete asks for your master password outside the grace period.

### Always on top

Right-click the title bar or an empty area of the window and select **Always on top** to keep the window above other windows. A check mark shows in the menu and a pin shows in the title bar while it is on. New item windows use the same choice until the companion stops.

## Edit an item

Select **Edit** in the item window. The form opens from the extension's vault cache. An identity item, or an item with a boolean or linked custom field, an empty custom field, two custom fields with the same name or an empty website, opens after the Bitwarden CLI reads it, because the cache does not keep those values. **Save** reads the item from the Bitwarden CLI again, so passkeys and password history are kept. The form has the fields of Bitwarden Desktop:

| Item type | Fields |
|-----------|--------|
| Login | Username, password, authenticator key (TOTP), websites with match detection (default, base domain, host, starts with, exact, regular expression, never) |
| Card | Cardholder name, brand, number, expiration month and year, security code |
| Identity | Title, first, middle and last name, username, company, national insurance number, passport number, licence number, email, phone, address 1 to 3, city or town, county or state, postcode, country |
| Secure note | Notes |
| SSH key | Private key (masked, with a show button), public key, fingerprint. **Import from private key** fills the public key and fingerprint from an OpenSSH private key. |

All item types also have name, folder, favourite, master password re-prompt, notes and custom fields (text, hidden, boolean, linked). A linked field points to a field of the item type. You can add, remove and reorder websites and custom fields. When your account has organisations, **Owner** and **Collections** move a personal item into an organisation or change the collections of an organisation item. An organisation item needs at least one collection. An item cannot move back to your personal vault.

Bank account, driver's licence and passport items show read-only, with no **Edit** button.

- **Save** and **Cancel** are at the bottom right of the window while the form is open, and the header shows an **Editing** badge. The header and the title bar show the name as you type it.
- **Save** checks that the name is set, each new custom field has a name and the expiration month is 1 to 12. A missing name shows a yellow ring and a message below the name; the other checks show a yellow message above the form. A website can be any text, as in Bitwarden. A boolean custom field keeps its stored value until you change the switch. The extension then saves the item and reads it back to confirm the change.
- **Cancel**, or closing the window, asks before it discards changes that are not saved.
- If the item changed in the vault after you started to edit it, for example from another device, **Save** asks you to **Reload** the item and discard your changes, or **Keep editing**. The extension syncs the vault first, so **Reload** shows the server version.
- A save waits for Bitwarden for as long as it takes. If Bitwarden does not confirm the change, the window asks you to **Reload** the item to see what the vault holds.
- The generate button next to the password opens the [password generator](#password-generator). **Use password** puts the value in the password field.
- In a password box of the form or of the master password panel, press and hold the eye button to show the value. The value is masked again when you release the button.

**Add website** and **Add field** are the last rows of their groups. The reorder and remove buttons of websites and custom fields are not red, because **Cancel** undoes them.

The trash-can button opens a dialog that names the item. It offers **Archive**, **Permanently delete** and **Cancel**, one above the other so that each fits in a narrow window. When Bitwarden cannot archive the item (for example an organisation item, or an account without archive), **Move to Trash** replaces **Archive**, so a recoverable option stays available. The dialog stays open while Bitwarden runs the change and shows an error in the dialog when the change fails. After an archive, a move to the trash or a permanent delete, the window closes.

## New item

**New Bitwarden item** in Command Palette opens an item window in create mode. It is a top-level command and is also in the Bitwarden page list, after the vault items; type "new" to move it to the top. The header shows the type icon and a **New item** badge. The form starts as a login. The **Type** box at the top of the form changes it to a secure note, card, identity or SSH key. A type change keeps the name, folder, notes and favourite, and clears the fields of the old type. A new item can go into an organisation: select an **Owner** and at least one collection.

**Save** has the same checks as an edit, and the extension creates the item with `bw create item`, reads it back to confirm it and adds it to the vault cache, so it shows in Command Palette. The window then shows the saved item read-only. A later **Open in window** for the item brings this window to the front.

**Cancel**, or closing the window, closes a new item window. It asks first when you typed something. If Bitwarden does not confirm the create, the window says so and keeps the form. The next **Save** asks **Save another copy?** before it sends the item again, because Bitwarden may have saved the first one. When the vault locks, new item windows close without saving.

## Password generator

The generate button in the edit form opens the generator in a dialog. **Generate password** in Command Palette (a top-level command, also in the Bitwarden page list) opens it in its own window. Only one generator window is open at a time: the command brings the open window to the front. The generator has two tabs:

| Tab | Options | Default |
|-----|---------|---------|
| Password | Length (5 to 128), uppercase, lowercase, numbers, special characters (`!@#$%^&*`), minimum numbers and minimum special characters (0 to 9), avoid ambiguous characters | 20 characters, uppercase, lowercase, numbers and special characters, at least 1 number and 1 special character, ambiguous characters avoided |
| Passphrase | Number of words (3 to 20), word separator (one character), capitalise, include number | 4 words, separated by `-` |

The Bitwarden CLI makes the values. **Regenerate** makes a new value, the copy button copies it through the extension with the clipboard clear timer and shows **Copied** in the toast, and **Use password** puts it in the password field.

The generator window has **Save as new item** at the bottom. It opens a [new item](#new-item) window as a login, with the generated value in the password field. The edit form and the **Rotate password** dialog do not show this button.

**Generator history** lists the last 50 values with their type and time. The values are masked; the copy button copies through the extension. The history is kept in the extension memory only, is never written to disk, and is erased when the vault locks. The companion saves the generator options, but not the values, in `%LOCALAPPDATA%\HoobiBitwardenCommandPalette\companion-generator.json`.

## Quick Rotate

**Rotate** (next to the **Password** label in the item window of a login that has a password) or **Rotate password** (in the Command Palette more-commands menu) opens the **Rotate password** dialog in the item window:

1. The **Current password** row is at the top. It is masked. Press and hold the eye button to show it; it is masked and cleared again when you release the button. The copy button copies it through the extension.
2. The generator is below it, with the saved options. Change the options or select **Regenerate** until you have the password that you want.
3. **Save** saves the value that the dialog shows. The extension uses its own copy of that generated value, so the saved password is the one that you saw. The save has the same checks as an edit: the item must not have changed in the vault, and the extension reads the item back and compares the password. The old password goes into password history.
4. The extension copies the new password with the clipboard clear timer.

**Cancel** closes the dialog and changes nothing. If Bitwarden does not confirm the save, nothing is copied and the window asks you to check the item. If the item changed in the vault, nothing is replaced or copied. For an item with master password re-prompt, the reveal, the copy and **Save** ask for your master password outside the grace period. The dialog closes when the master password panel shows, because the panel is behind it.

## Master password re-prompt

For an item with master password re-prompt, the window shows the item header and one inline panel, and no field values, until you verify your master password. The panel has a password box that has focus when the panel shows, and a **Verify** button. `Enter` also verifies. While the extension checks the password, the box and the button are disabled and the button shows a progress ring. A wrong password shows a message under the box, selects the text and returns focus to the box. When **Integrate with Bitwarden Desktop App** is on in [Settings](Settings#integrate-with-bitwarden-desktop-app), a **Use Windows Hello** button shows under **Verify**. The extension asks Bitwarden Desktop for the Windows Hello check, so the companion cannot report a verification by itself. The progress ring shows until you finish the Windows Hello prompt. A cancelled or failed check shows a message under the box and counts as a failed attempt. Every action that needs the master password (show, copy, **Edit**, **Delete** and the other actions below) shows this same panel in place of the fields. `Esc` cancels the panel when it waits for an action. The window uses the same re-prompt grace period as Command Palette (see [Settings](Settings)). A successful verification starts the grace period, and the extension sends the end time of the grace period with the item. While the grace period runs, the fields show a thin bar and the text **Locks in 0:42** that count down. At zero the window clears the shown values and the verification code, and the panel shows again. **Keep open** next to the countdown stops it for this window, so the fields stay shown. The extension grace period still ends, so a later show or copy asks for your master password again. The countdown also shows after you reload the item. A verification in Command Palette does not start the countdown in a window that is already open until the window reloads the item. During the grace period, you can show, copy and open values without a prompt. When the grace period is 0, the window asks for your master password for each show, copy or open action, and the verification code is masked. **Edit** and **Password history** use the same re-prompt. **Save**, **Archive**, **Move to Trash**, **Permanently delete**, owner or collection changes, attachment download, upload and delete, and **Rotate password** also ask for your master password outside the grace period, as the Bitwarden web vault does. When the grace period is 0, a save that also moves the item into an organisation asks twice. Five failed attempts start the same 30-second cooldown as the Command Palette re-prompt.

If the item changes in the vault while the window is open, the window loads the new version and does not show or copy the old value.

## Locked vault

When the vault locks or auto-locks, every item window removes the values it shows, closes an open edit form without saving, stops the verification code and shows that the vault is locked. New item windows close without saving. An open generator, in a dialog or in its own window, removes its value and history. Unlock the vault in Command Palette. The open item windows then load the item again.

See [Architecture](Architecture) for the process model and the pipe protocol.
