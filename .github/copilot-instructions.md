# GitHub Copilot Instructions

## Git Commit Message Format

All commit messages MUST follow the [Conventional Commits](https://www.conventionalcommits.org/) specification for release-please compatibility.
Commits and pull requests in this repo DO NOT require an Azure DevOps work item number as this repo is not associated with an Azure DevOps project.

### Required Format

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

### Commit Types

Use these standard types (release-please compatible):

- **feat**: A new feature (triggers MINOR version bump)
- **fix**: A bug fix (triggers PATCH version bump)
- **chore**: Maintenance tasks, dependency updates, tooling changes (no version bump)
- **docs**: Documentation only changes (no version bump)
- **style**: Code style changes (formatting, no functional changes, no version bump)
- **refactor**: Code refactoring without feature/fix changes (no version bump)
- **perf**: Performance improvements (triggers PATCH version bump)
- **test**: Adding or updating tests (no version bump)
- **build**: Build system or external dependency changes (no version bump)
- **ci**: CI/CD pipeline changes (no version bump)
- **revert**: Revert a previous commit

### Breaking Changes

For breaking changes (triggers MAJOR version bump):

- Add `!` after type: `feat!: breaking change`
- Or include `BREAKING CHANGE:` in footer

### Examples

Good:

```
feat: add Dev suffix to Debug builds for side-by-side installation
fix: clear vault cache immediately before locking
chore: add WACK testing as separate job
docs: update README with installation steps
ci: configure release-please to update Package.appxmanifest
```

Bad (DON'T USE):

```
🐛 fix bug
Fixed the lock issue
WIP
Update
```

### Scope (Optional)

Can specify affected area: `feat(auth): add OAuth support`

### Issue Reference

Include work item in description: `fix: resolve login timeout (AB#123)`

### Multiple Changes — PR Description Format

This repo uses **squash merges**, so the PR description becomes the commit message. To represent multiple changes in one PR (each gets its own changelog entry), add additional conventional commit messages as footers at the **bottom** of the PR description body:

```
feat: add primary feature description

Optional body text explaining the PR.

fix(utils): secondary fix description
BREAKING-CHANGE: describe breaking change if applicable
feat(utils): another feature in the same PR
```

- Each footer entry must follow the same `type(scope): description` format
- `BREAKING-CHANGE:` footer triggers a MAJOR version bump
- Additional entries must appear **after** any free-form body text
- Only `feat`, `fix`, `perf`, and `revert` types produce changelog entries; `ci`, `test`, `docs`, `chore` do not
- These additional entries each produce their own changelog line

## Notes

- First line limited to 72 characters
- Description uses imperative mood ("add" not "adds" or "added")
- No period at end of description
- Emoji are NOT allowed (incompatible with release-please)

---

## Command Palette Extension SDK Reference

This project uses the **Microsoft Command Palette Extensions SDK** (`Microsoft.CommandPalette.Extensions` NuGet package) from [PowerToys](https://github.com/microsoft/PowerToys). The SDK is a WinRT-based API.

When you need to look up SDK types, interfaces, properties, or capabilities, use these sources **in priority order**:

### 1. Microsoft Docs (fastest)

- https://learn.microsoft.com/windows/powertoys/command-palette/
- Search for specific types/interfaces on Microsoft Learn

### 2. GitHub Documentation

- https://github.com/microsoft/PowerToys/tree/main/src/modules/cmdpal/extensionsdk/docs
- Contains markdown guides for extension development

### 3. GitHub Samples

- https://github.com/microsoft/PowerToys/tree/main/src/modules/cmdpal/Exts
- Real extension implementations showing patterns for ListItem, Tags, DynamicListPage, etc.

### 4. GitHub Source Code (authoritative)

- **IDL (full API surface):** https://raw.githubusercontent.com/microsoft/PowerToys/main/src/modules/cmdpal/extensionsdk/Microsoft.CommandPalette.Extensions/Microsoft.CommandPalette.Extensions.idl
- **Toolkit C# wrappers:** https://github.com/microsoft/PowerToys/tree/main/src/modules/cmdpal/extensionsdk/Microsoft.CommandPalette.Extensions.Toolkit
- Key files: `ListItem.cs`, `Tag.cs`, `DynamicListPage.cs`, `ColorHelpers.cs`, `StatusMessage.cs`, `CommandResult.cs`
- Use `fetch_webpage` to read raw source files directly from GitHub

### 5. Inspecting NuGet DLLs (last resort)

- Package location: `~/.nuget/packages/microsoft.commandpalette.extensions/<version>/`
- WinMD metadata: `winmd/Microsoft.CommandPalette.Extensions.winmd`
- Toolkit DLL: `lib/net8.0-windows10.0.19041.0/Microsoft.CommandPalette.Extensions.Toolkit.dll`
- Search PDB files for type/member names using binary string matching
- Use `[System.Reflection.Assembly]::LoadFrom()` in PowerShell if possible

### Key SDK Types

- `DynamicListPage` - Base class for searchable list pages (`IsLoading`, `GetItems()`, `UpdateSearchText()`)
- `ListItem` - Display item with `Title`, `Subtitle`, `Icon`, `Tags`, `Details`, `MoreCommands`
- `Tag` - Colored label with `Text`, `Foreground`, `Background` (using `OptionalColor`/`ColorHelpers`)
- `ContentPage` / `FormContent` - Adaptive Card-based forms
- `CommandResult` - Return value from commands (`Dismiss`, `GoBack`, `KeepOpen`, `ShowToast`, etc.)
- `StatusMessage` - Status bar message with `MessageState` (`Info`, `Success`, `Warning`, `Error`)
- `IconInfo` - Icon from Segoe MDL2 Assets unicode or image URL

---

## PowerShell Terminal Commands

- In PowerShell, the escape character is a backtick (`` ` ``), **not** a backslash (`\`)
- When constructing multi-line strings or escaping quotes in terminal commands, use `` `" `` not `\"`
- Example: `gh pr create --body "line one`nline two"` not `"line one\nline two"`

---

## Code Coverage

Every C# source file touched in a PR must meet **50% line coverage** (measured against unit tests in `HoobiBitwardenCommandPaletteExtension.Tests`). The CI pipeline enforces this and will fail the PR if the threshold is not met.

- When adding or modifying logic in a `.cs` file, add or update tests in the corresponding test file under `HoobiBitwardenCommandPaletteExtension.Tests/`
- Files listed in `.github/coverage-exclusions.json` are exempt from the threshold (e.g. UI-only pages that can't be unit tested)
- Test files themselves (`*.Tests/**`) are excluded from coverage measurement

---

## Wiki Documentation (`docs/`)

The `docs/` folder contains GitHub Wiki pages documenting all extension functionality. **Keep these pages up to date when making changes.**

### When to Update

- **Adding a feature**: Add relevant details to the appropriate wiki page, or create a new page if the feature is significant. Update [Home.md](../docs/Home.md) and [\_Sidebar.md](../docs/_Sidebar.md) if adding a new page.
- **Changing behavior**: Update the page that describes the affected feature.
- **Adding/changing settings**: Update [Settings.md](../docs/Settings.md) with the new setting's type, default, and description.
- **Changing sort order or tags**: Update [Search-and-Filtering.md](../docs/Search-and-Filtering.md) and/or [Watchtower-Tags.md](../docs/Watchtower-Tags.md).
- **Changing architecture/services**: Update [Architecture.md](../docs/Architecture.md).
