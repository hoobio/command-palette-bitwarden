# Companion WinUI 3 App — Phase 1 Handoff

This document briefs an agent picking up the new **companion WinUI 3 application** for the
`hoobio/command-palette-bitwarden` repo. It is self-contained: read it top to bottom before
writing code. Phase 1 is deliberately scoped; later phases (full settings UI, onboarding wizard,
server-config "test connection") are out of scope and called out in section 4.

---

## 1. Why this exists

The product today is a PowerToys **Command Palette (CmdPal) extension** for Bitwarden. A 1-star
store review ("you need to be a developer to use this") drove a usability push. CmdPal extension
settings/forms can only render **Adaptive Cards** (no reactive UI, "click Save to re-render" feel,
no expanders), which caps how good the create/edit and onboarding experiences can be.

Decision (already made with the maintainer): add a **companion WinUI 3 window** that runs in its
own process and is launched from the extension. It gives us full native UI (reactive, validated,
proper controls) for the rich flows, while the palette keeps the fast search/copy/TOTP flows.

Phase 1 delivers the create/edit/generate/rotate experience in WinUI and replaces the login flow.
It does **not** move settings out of the palette yet.

---

## 2. Architecture and hard constraints (read this first)

- **The CmdPal extension is an out-of-process COM server** (full-trust, MSIX-packaged, AOT +
  trimmed + single-file in Release). The CmdPal host owns the visual tree and only renders the
  content types it knows. You **cannot** inject WinUI XAML into the palette. The companion is a
  **separate WinUI 3 process** that you author freely.
- **Packaging:** ship the companion in the **same MSIX** as a second `<Application>` entry
  (likely `uap:VisualElements … AppListEntry="none"` so it doesn't add a Start tile), so it runs
  with the **same package identity** and shares `%LOCALAPPDATA%`. Add an MSBuild target to publish
  the companion for the matching RID (win-x64 / win-arm64) and include its output as package
  content. Use **self-contained Windows App SDK** (`WindowsAppSDKSelfContained=true`) so it adds no
  framework dependency. Mixed AOT (extension) + JIT (companion) in one MSIX is fine — separate
  exes, separate publish settings. The companion has **no** AOT/trim constraint.
- **The Bitwarden CLI (`bw`) is the security boundary and the only writer to the vault.** Never
  reverse-engineer the API. All vault reads/writes go through `bw`.
- **Session/CLI ownership — the key design decision.** The extension holds the unlocked session
  (in memory, optionally persisted to Windows Credential Manager via "Remember Session"). The
  companion does **not** independently have a session. **Recommended model: the extension is the
  vault/CLI/session authority; the companion is a UI client that drives it over IPC.** The
  companion sends intents ("get item X", "save item X with these fields", "generate", "rotate
  field", "unlock"); the extension executes them through its existing `BitwardenCliService` (which
  has the session), persists, syncs, verifies, refreshes its own palette list, and returns the
  result. Benefits: one session owner, no session-key duplication, and **live palette updates are
  automatic** because the extension is the one making the change.
  - Alternative (simpler IPC, worse coupling): the companion runs `bw` itself using the session
    read from `SessionStore`/Credential Manager, and only signals the extension to "refresh". This
    only works reliably when "Remember Session" is on, and duplicates the CLI wrapper. **Prefer the
    authority model above.** If you choose this alternative, document why.
- **IPC mechanism:** a **named pipe** is the right tool (both ends are full-trust, same package).
  There is already a named-pipe precedent in the codebase: `Services/DesktopIpcService.cs` talks to
  the Bitwarden Desktop app over a Windows named pipe — mirror its patterns (pipe naming, framing,
  encryption-if-needed). The extension hosts the pipe server; the companion is the client. Define a
  small JSON request/response protocol (see 3.2 for the Phase 1 message set).
- **Shared settings** live at `%LOCALAPPDATA%\HoobiBitwardenCommandPalette\settings.json`
  (`BitwardenSettingsManager`). Generator defaults (section 3.5) must be **structured so they can be
  read from settings later**, even though the settings UI doesn't exist yet — bake in secure
  defaults now, behind a single "generator options" accessor that currently returns constants.
- **Launch:** the extension launches the companion from a command's `Invoke()` (the extension is
  full-trust, so `Process.Start` of the companion exe in the package works; protocol activation is
  the more robust MSIX option). Pass context (e.g. target item id, mode) via launch args or the
  initial IPC handshake.
- **UI standard (hard requirement):** the companion is **WinUI 3** and must **strictly follow
  Fluent 2** design standards. Replicate `D:\earmark`; fall back to `D:\WinUI-Gallery` for any
  control/pattern earmark doesn't demonstrate. Full detail in section 5.

---

## 3. Phase 1 scope (deliverables)

Build incrementally; each sub-section is a reviewable PR. Follow the repo's conventional-commit +
gitmoji style (section 9). Work on a feature branch off `main` (after PR #171, the CLI-install
work, has merged).

### 3.1 Companion app skeleton + packaging
- New WinUI 3 project (`net10.0-windows10.0.26100.0`), self-contained Windows App SDK.
- Packaged into the existing MSIX as a second `<Application>` (hidden app list entry).
- MSBuild wiring so `dotnet publish` of the extension produces both exes for the target RID and the
  companion lands in the package.
- A no-op shell window that builds, deploys, and launches via the existing VS Code task
  ("Build, Kill & Deploy (Debug x64)", see `.vscode/tasks.json`) before adding features.
- Style per section 5 from day one.

### 3.2 Extension ↔ companion IPC (live updates)
Stand up the named-pipe channel and the Phase 1 command set. Suggested request types
(extension executes, returns a result, and refreshes its own palette list where state changed):
- `Unlock` / `UnlockWithBiometrics` / `Login(...)` — drive the auth flows (3.3).
- `GetStatus` — returns vault status (locked/unlocked/unauthenticated/CLI-not-found).
- `GetItem(id)` — full item (all fields, custom fields, item type, icon info).
- `SaveItem(id, payload)` — edit/create, then sync, then **verify persistence** (3.6).
- `Generate(options)` — password or passphrase (3.5); used by the generator UIs.
- `QuickRotate(id)` — convenience server-side path for 3.8 (or compose from Generate + SaveItem).
- Extension → companion events/notifications as needed (status changed, save complete).

When the extension applies a change it must call its existing refresh path
(`BitwardenCliService.RefreshCacheAsync()` → `CacheUpdated` → the palette page rebuilds) so the
palette reflects edits immediately. That is the "drive live updates" requirement.

### 3.3 WinUI-driven login flow (replaces the palette login UX)
- Replace the in-palette login/unlock (`Pages/LoginPage.cs`, `Pages/UnlockVaultPage.cs`) entry
  point with a WinUI window offering a clean, validated login + unlock experience.
- **Keep the same Windows Hello path.** Biometric unlock currently goes through
  `Services/DesktopIpcService.cs` (talks to the Bitwarden Desktop app for Windows Hello). Reuse it;
  do not reimplement biometrics. The flow, 2FA, device-verification, and server-set logic already
  exist in `BitwardenCliService` (`LoginAsync`, `UnlockAsync`, `UnlockWithBiometricsAsync`,
  `SubmitDeviceVerificationAsync`, `SetServerUrlAsync`) — drive them via IPC.
- The palette should still show a sensible state while the companion handles auth, and reflect the
  unlocked vault once done (via the live-update channel).

### 3.4 Item detail / edit window (closes part of #57, see image set)
Reference: GitHub issue [#57](https://github.com/hoobio/command-palette-bitwarden/issues/57)
("Create / Edit Vault Items"). The maintainer's Phase 1 spec (with screenshots) supersedes the
issue's Adaptive-Card wording — build it in WinUI:
- The palette's per-item **"Open"** action becomes **"Open in Web Vault"** (keep the existing
  web-vault deep link behavior; just relabel/repoint).
- A new per-item action opens a **WinUI detail window** showing **all** fields for the item
  (login fields, custom fields, notes, URIs, etc.), matching the layout in the provided screenshot
  (login credentials, autofill URLs, custom fields, item history).
- **Item icon** must be displayed (the website/favicon icon). The extension already has
  `Services/FaviconService.cs`; reuse it. If the user edits the autofill URL, the icon should be
  re-resolved for the new URL.
- **Edit** affordance (pencil icon) makes fields editable.
- **Copy icon** (standard copy glyph) next to each field copies its value to the clipboard. Reuse
  the secure-clipboard behavior (`Services/SecureClipboardService.cs`, which honors auto-clear).
- **Regenerate** affordance on every **secret/hidden** field (the login password plus any custom
  field of type "hidden"): opens the generator (3.5) and replaces that field's value.
- **Save** runs the persist-and-verify path (3.6).

### 3.5 Password / passphrase generator (shared WinUI component)
Build one reusable generator control used by 3.4, 3.7, and 3.8. It mirrors Bitwarden's own
generator (see image 1 = password, image 2 = passphrase):

**Password mode**
- Length (5–128). Include toggles: `A-Z`, `a-z`, `0-9`, `!@#$%^&*` (symbols). Minimum numbers,
  minimum special. "Avoid ambiguous characters" toggle.

**Passphrase mode**
- Number of words (3–20; 6+ recommended). Word separator. "Capitalize". "Include number".

**Implementation note:** prefer generating via the CLI for correctness rather than rolling your own
RNG/policy — `bw generate` supports all of these flags
(`--length --uppercase --lowercase --number --special --min-number --min-special --ambiguous`, and
`--passphrase --words --separator --capitalize --includeNumber`). Route through the IPC `Generate`
command so it runs under the extension. Local generation is acceptable only if you use a CSPRNG and
faithfully implement the policy; document the choice.

**Defaults:** ship **secure defaults** (e.g. length 20, all character classes on, min-number 1,
min-special 1, avoid-ambiguous on; passphrase 6 words). Read them through a single
"generator options" accessor that **today returns constants but is shaped to be sourced from
settings later** (do not hardcode them inline at call sites). A future settings UI will make them
configurable.

### 3.6 Save = persist + verify + refresh (CRITICAL — data-loss safety)
This is the highest-risk requirement. When the user generates/changes a secret and saves, that value
**must** reach the web vault, because they will immediately set the same value in the downstream
service. If the save or sync silently fails, they lose the only copy.

Save (and Quick Rotate) must:
1. Apply the change via `bw edit item <id> <base64-encoded-json>` (add an `EditItemAsync` to
   `BitwardenCliService`); confirm the CLI reports success.
2. Trigger `bw sync` to push to the server.
3. **Verify persistence** — do not trust step 2's exit code alone. Re-fetch the item
   (`bw get item <id>`) and confirm the new value is present server-side (or otherwise prove the
   sync landed). Only then report success.
4. Update the extension state (refresh cache → palette reflects the change immediately).
5. On any failure at steps 1–3, **surface a loud, explicit error** and do **not** report success.
   The clipboard-copy convenience (3.8) must not imply the value was saved if persistence failed.

### 3.7 "Generate standalone password" command (closes part of #98)
- New top-level CmdPal command (e.g. "Generate password" / "Generate standalone secret" — word it
  cleanly) registered in `HoobiBitwardenCommandPaletteExtensionCommandsProvider`.
- Opens a standalone WinUI generator window (same component as 3.5) that generates an
  **un-persisted** value. No vault write. Offer copy-to-clipboard. Password and passphrase modes.

### 3.8 "Quick Rotate" context menu (closes #98)
Reference: [#98](https://github.com/hoobio/command-palette-bitwarden/issues/98).
- For each vault item with a **single hidden field** (the common case: one password), add a
  per-item context action **"Quick Rotate"**.
- It opens the generator (3.5) targeting that item's specific secret field, then on submit:
  generates → **persists + verifies (3.6)** → copies the new value to the clipboard → refreshes the
  palette. The clipboard copy is for pasting into the downstream service, so persistence must be
  confirmed first (3.6 step 5).

---

## 4. Explicitly OUT of scope for Phase 1
- No settings page in the companion. Settings stay in the CmdPal palette for now. (The generator
  defaults must still be *shaped* for future settings sourcing — section 3.5.)
- No onboarding/first-run wizard, no server-config "test connection" UI. (The CLI install service
  already exists — see `Services/BitwardenCliInstaller.cs` — and is UI-agnostic for later reuse.)
- No general vault browsing in the companion; the palette remains the browser. The companion owns
  login, item detail/edit, generate, and rotate.
- Do not start the broader UI migration; keep the surface to the list above.

---

## 5. Styling (non-negotiable: WinUI 3 + strict Fluent 2)
**This must be a WinUI 3 app and must strictly follow Fluent 2 design standards.** Use built-in
WinUI 3 / Windows App SDK controls, theme resources, and the Fluent 2 type ramp, spacing scale,
corner radii, elevation, and motion. Do not hand-roll bespoke styling where a Fluent 2 control or
theme resource exists; do not deviate from Fluent 2 spacing/typography. Light, Dark, and High
Contrast must all work via theme resources (no hardcoded colors except where a documented
theme-aware brush is required).

**Reference hierarchy (use in this order):**
1. **`D:\earmark`** — the `Earmark.App` WinUI 3 project (also at `D:\hoobi-audio`). This is the
   maintainer's house style; replicate it. Lift its conventions:
   - `net10.0-windows10.0.26100.0`, Windows App SDK; **Mica** backdrop applied in code
     (theme-aware), the WinUI `TitleBar` control, `NavigationView` where a shell is needed.
   - `App.xaml` defines the Fluent 2 spacing scale tokens (`SpacingXSmall`…`SpacingXXLarge`),
     `CardCornerRadius` = 8 / `ControlCornerRadius` = 4, `ContentMaxWidth`, theme dictionaries
     (Default/Light/HighContrast), and text/card styles (`PageHeaderTextStyle`, `SectionCardStyle`,
     etc.). **Reuse the same token set** so the two apps read as one product.
   - Settings-style rows use `CommunityToolkit.WinUI.Controls` `SettingsCard` / `SettingsExpander`
     (relevant for the generator's grouped option panels).
2. **`D:\WinUI-Gallery` (fallback)** — when earmark has no example of a control/pattern you need,
   use the WinUI 3 Gallery as the canonical Fluent 2 reference for that control's correct usage,
   styling, and states. Match the Gallery's idiomatic Fluent 2 implementation rather than inventing
   one.

If neither reference covers a pattern, follow the official Fluent 2 / Windows App SDK design
guidance and keep it consistent with earmark's tokens.

---

## 6. Key existing code to read (in this repo)
- `HoobiBitwardenCommandPaletteExtension/Services/BitwardenCliService.cs` — the CLI wrapper:
  status, login/unlock/biometrics, device verification, list/parse, sync, server config, the
  status-coalescing machine, and `ApplyInstalledCli`. **You will add `EditItemAsync`,
  `GetItemAsync`, and `GenerateAsync` here** (the extension is the CLI authority).
- `HoobiBitwardenCommandPaletteExtension/Models/BitwardenItem.cs` — the item model (types, login,
  custom fields incl. hidden, URIs, TOTP, reprompt, organization).
- `HoobiBitwardenCommandPaletteExtension/Services/DesktopIpcService.cs` — named-pipe client to the
  Bitwarden Desktop app (Windows Hello). Pattern reference for the new companion IPC, and reuse for
  biometric unlock.
- `HoobiBitwardenCommandPaletteExtension/Services/FaviconService.cs` — item/website icons (#57 icon
  requirement).
- `HoobiBitwardenCommandPaletteExtension/Services/SecureClipboardService.cs` — clipboard with
  auto-clear (copy buttons).
- `HoobiBitwardenCommandPaletteExtension/Services/BitwardenSettingsManager.cs` — shared settings
  (generator-defaults accessor lives adjacent conceptually).
- `HoobiBitwardenCommandPaletteExtension/Pages/LoginPage.cs`, `Pages/UnlockVaultPage.cs`,
  `Pages/HoobiBitwardenCommandPaletteExtensionPage.cs` — the flows you're replacing/triggering, and
  how commands/list items + `RaiseItemsChanged` work.
- `HoobiBitwardenCommandPaletteExtension/HoobiBitwardenCommandPaletteExtensionCommandsProvider.cs` —
  where top-level commands are registered (3.7).
- `HoobiBitwardenCommandPaletteExtension/Services/BitwardenCliInstaller.cs` — UI-agnostic CLI
  install service (future onboarding reuse; out of Phase 1 scope but informs the model).
- `HoobiBitwardenCommandPaletteExtension/Package.appxmanifest` — single-project MSIX manifest you'll
  extend with the second `<Application>`.
- `.vscode/tasks.json` — "Build, Kill & Deploy (Debug x64)" registers a loose build to PowerToys for
  fast local iteration (no signing needed; Developer Mode).

---

## 7. `bw` CLI reference for the new operations
- Edit: `bw get item <id>` → modify JSON → `bw encode` (or base64 the JSON) → `bw edit item <id> <encoded>`.
- Read back (verify): `bw get item <id>`.
- Sync: `bw sync`.
- Generate password: `bw generate --length 20 --uppercase --lowercase --number --special --min-number 1 --min-special 1` (add `--ambiguous` to allow ambiguous chars; omit a class flag to exclude it).
- Generate passphrase: `bw generate --passphrase --words 6 --separator - --capitalize --includeNumber`.
- All commands need the session (`BW_SESSION`) which the **extension** holds — run them through
  `BitwardenCliService`, not from the companion directly (section 2).

---

## 8. Acceptance criteria and testing
- Build + deploy locally via the VS Code "Build, Kill & Deploy (Debug x64)" task; verify the
  companion launches from the palette and IPC round-trips.
- Login + Windows Hello unlock work from the WinUI window; the palette reflects the unlocked vault
  live.
- Item detail window shows all fields + the correct icon; edit + per-field copy + regenerate work.
- **Persistence safety (must demonstrate):** generate a new password, save, and confirm the value is
  present after a fresh `bw get item` / on the web vault before success is reported; force a sync
  failure and confirm the UI reports failure and does not imply success.
- "Generate standalone password" produces a value without touching the vault.
- "Quick Rotate" on a single-hidden-field item rotates, persists+verifies, copies, and refreshes the
  palette.
- Existing tests stay green (`dotnet test -p:Platform=x64`). Add unit tests for the new
  `BitwardenCliService` methods (the repo mocks the process layer via `ICliProcess` /
  `FakeProcessFactory`; follow `*MockedTests.cs`). There is one pre-existing,
  environment-dependent test failure (`VaultItemHelperTests.RecordVerification_DoesNotPersistAcrossProcesses`)
  caused by a stale `grace.json` in local AppData; it passes on clean CI and is unrelated.

---

## 9. Conventions
- Commits: Conventional Commits + gitmoji (e.g. `feat: ✨ …`, `fix: 🐛 …`). **Never** add an
  AI-attribution trailer/footer.
- PR titles: this repo's PR-title check allows gitmoji after the type (`feat: ✨ …`). Note the
  sibling `hoobio/pipeline-tools` repo's check requires the subject to start with a letter (no
  leading gitmoji) — not relevant here but good to know if you touch pipeline-tools.
- Default to a git worktree for non-trivial work; commit and push regularly; never push to `main`
  or open a PR without being told to.
- Releases use release-please (feat → minor bump). `main` is protected.

---

## 10. Status at handoff
- Merged to `main`: vault-unlock reliability (#169), SBOM→Dependency-Track v2 hierarchy + a
  pipeline-tools v2.3.1 release (#170).
- In review: **PR #171** — one-click CLI install (winget → verified zip download → manual),
  unsigned-namespace publisher for dev/PR builds, status-machine robustness, install
  watchdog/timeout. **Branch the companion work off `main` once #171 merges**, since it touches
  `BitwardenCliService` (you'll extend the same file) and the CLI-not-found flow.
