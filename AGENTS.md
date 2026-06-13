# Agent Instructions

Guidance for AI coding agents (Copilot, Claude Code, etc.) working in this repository.

## Local Build & Deploy

This is an out-of-process Command Palette extension (a packaged WinRT COM server). To see a code change live you must rebuild, re-register the package, and restart PowerToys.

VS Code task **Build, Kill & Deploy (Debug x64)** (`.vscode/tasks.json`) runs the full cycle: kill PowerToys, build, register the loose package, restart PowerToys. The equivalent commands:

```powershell
# 1. Stop PowerToys, Command Palette, and the extension host so files unlock
@('PowerToys','Microsoft.CmdPal.UI','HoobiBitwardenCommandPaletteExtension') +
  ((Get-Process | Where-Object ProcessName -like 'Microsoft.CmdPal.Ext.*').ProcessName | Select-Object -Unique) |
  Select-Object -Unique | ForEach-Object { Get-Process $_ -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }

# 2. Build (Debug stamps the side-by-side "Dev" channel identity)
dotnet build .\HoobiBitwardenCommandPaletteExtension\HoobiBitwardenCommandPaletteExtension.csproj -c Debug /p:Platform=x64

# 3. Register the loose layout, then restart PowerToys
$out = '.\HoobiBitwardenCommandPaletteExtension\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
Copy-Item "$out\..\..\..\..\..\Assets\*" "$out\Assets" -Recurse -Force
Add-AppxPackage -Register -Path "$out\AppxManifest.xml" -ForceUpdateFromAnyVersion
Start-Process 'shell:AppsFolder\Microsoft.PowerToysWin32'
```

Debug builds install as a separate **Dev** channel (`Hoobi.BitwardenCommandPaletteExtension.Dev`, display name suffixed `(Dev)`) so they sit side by side with a production install. Test against the **(Dev)** entry in Command Palette.

### Working in a git worktree (important)

`Add-AppxPackage -Register` deduplicates on package **identity + version**. The Dev identity and version (`1.10.0.0`) are the same across every checkout, so if a Dev package is already registered (e.g. pointing at the main checkout's `bin`), registering again from a worktree's `bin` is silently skipped (`Deployed.` prints, but `InstallLocation` does not change). You then run old code and think the change "did nothing".

When deploying from a worktree, **always remove-then-register and then verify** - registering in place is silently skipped:

```powershell
# 1. Remove any existing Dev registration (wherever it points), then register from THIS worktree
Get-AppxPackage -Name 'Hoobi.BitwardenCommandPaletteExtension.Dev' | Remove-AppxPackage -ErrorAction SilentlyContinue
$out = "<worktree>\HoobiBitwardenCommandPaletteExtension\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64"
Add-AppxPackage -Register -Path "$out\AppxManifest.xml" -ForceUpdateFromAnyVersion

# 2. VERIFY (do not skip): identity must be .Dev and InstallLocation must be your worktree bin
$p = Get-AppxPackage -Name 'Hoobi.BitwardenCommandPaletteExtension.Dev'
"$($p.Name) -> $($p.InstallLocation)"   # Name must end in .Dev; path must be your worktree
```

If `Name` has no `.Dev` suffix, or `InstallLocation` is not your worktree, the deploy is wrong - do not stop there.

### Release-build trap (this is the one that bites)

The per-channel identity is stamped into `Package.appxmanifest` at build time, and the stamp is **incremental**. If you run a `-c Release` build first (e.g. for lint/test parity) and then a `-c Debug` build in the same checkout, the Debug build can skip regenerating the output `AppxManifest.xml`, leaving it stamped with the **Release** identity. Registering that loose layout installs the *production* channel from your Debug bin - exactly the "I deployed but nothing changed / wrong channel" symptom.

Guard against it: when deploying from a worktree, do a clean Debug build with the channel pinned explicitly, and check the output manifest before registering:

```powershell
Remove-Item -Recurse -Force "<worktree>\HoobiBitwardenCommandPaletteExtension\bin\x64\Debug" -ErrorAction SilentlyContinue
dotnet build .\HoobiBitwardenCommandPaletteExtension\HoobiBitwardenCommandPaletteExtension.csproj -c Debug /p:Platform=x64 /p:PackageChannel=Dev
Select-String -Path "$out\AppxManifest.xml" -Pattern '<Identity Name'   # must show ...Extension.Dev
```

Stamping note: a Debug build rewrites the tracked `Package.appxmanifest` to the Dev identity as a side effect. **Don't commit that change** - `git checkout -- HoobiBitwardenCommandPaletteExtension/Package.appxmanifest` before committing.

## Git Commit Message Format

All commit messages MUST follow [Conventional Commits](https://www.conventionalcommits.org/) for release-please compatibility. Commits and PRs in this repo do NOT require an Azure DevOps work item number (this repo isn't associated with an ADO project).

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

### Notes

- First line limited to 72 characters
- Description uses imperative mood ("add" not "adds" or "added")
- No period at end of description
- Emoji are NOT allowed (incompatible with release-please)

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
- Read raw source files directly from GitHub

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

## PowerShell Terminal Commands

- In PowerShell, the escape character is a backtick (`` ` ``), **not** a backslash (`\`)
- When constructing multi-line strings or escaping quotes in terminal commands, use `` `" `` not `\"`
- Example: `gh pr create --body "line one`nline two"` not `"line one\nline two"`

## Code Coverage

Every C# source file touched in a PR must meet **50% line coverage** (measured against unit tests in `HoobiBitwardenCommandPaletteExtension.Tests`). The CI pipeline enforces this and will fail the PR if the threshold is not met.

- When adding or modifying logic in a `.cs` file, add or update tests in the corresponding test file under `HoobiBitwardenCommandPaletteExtension.Tests/`
- Files listed in `.github/coverage-exclusions.json` are exempt from the threshold (e.g. UI-only pages, Win32/COM interop that can't be unit tested)
- Test files themselves (`*.Tests/**`) are excluded from coverage measurement

## Wiki Documentation (`docs/`)

The `docs/` folder contains GitHub Wiki pages documenting all extension functionality. **Keep these pages up to date when making changes.**

### When to Update

- **Adding a feature**: Add relevant details to the appropriate wiki page, or create a new page if the feature is significant. Update [Home.md](docs/Home.md) and [\_Sidebar.md](docs/_Sidebar.md) if adding a new page.
- **Changing behavior**: Update the page that describes the affected feature.
- **Adding/changing settings**: Update [Settings.md](docs/Settings.md) with the new setting's type, default, and description.
- **Changing sort order or tags**: Update [Search-and-Filtering.md](docs/Search-and-Filtering.md) and/or [Watchtower-Tags.md](docs/Watchtower-Tags.md).
- **Changing architecture/services**: Update [Architecture.md](docs/Architecture.md).
