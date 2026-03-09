# GitHub Copilot Instructions

## Git Commit Message Format

All commit messages MUST follow the [Conventional Commits](https://www.conventionalcommits.org/) specification for release-please compatibility.

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
