# Contributing to Command Palette Extension for Bitwarden

Thanks for your interest in contributing! This guide will help you get started.

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code.

## Getting Started

### Prerequisites

- **Windows 10 (19041+)** or later
- [PowerToys](https://github.com/microsoft/PowerToys/releases) with Command Palette enabled
- [Bitwarden CLI](https://bitwarden.com/help/cli/) (`bw`) on your `PATH`
- [.NET 9 SDK](https://dot.net) (or later)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/) with the C# Dev Kit

### Building

```powershell
git clone https://github.com/hoobio/command-palette-bitwarden.git
cd command-palette-bitwarden
dotnet build HoobiBitwardenCommandPaletteExtension -c Debug -p:Platform=x64
```

### Running Tests

```powershell
dotnet test -p:Platform=x64
```

### Local Deployment

Register the debug build as a Command Palette extension:

```powershell
Add-AppxPackage -Register -Path .\HoobiBitwardenCommandPaletteExtension\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\AppX\AppxManifest.xml -ForceUpdateFromAnyVersion
```

Then restart PowerToys to pick up the extension.

## How to Contribute

### Reporting Bugs

Open a [Bug Report](https://github.com/hoobio/command-palette-bitwarden/issues/new?template=bug_report.yml) issue. Include:

- Steps to reproduce
- Expected vs actual behavior
- Your OS version, PowerToys version, and Bitwarden CLI version
- Screenshots or logs if applicable

### Suggesting Features

Open a [Feature Request](https://github.com/hoobio/command-palette-bitwarden/issues/new?template=feature_request.yml) issue. Describe the problem you're trying to solve and the solution you'd like.

### Submitting Code

1. **Fork** the repository and create a branch from `main`
2. **Make your changes** — keep them focused and minimal
3. **Add or update tests** — every changed `.cs` file must meet the 50% line coverage threshold
4. **Update documentation** — if your change affects behavior, update the relevant [wiki page](docs/) and/or README
5. **Open a pull request** against `main`

## Conventions

### Commit Messages

All commits must follow [Conventional Commits](https://www.conventionalcommits.org/). This project uses [release-please](https://github.com/googleapis/release-please) for automated releases.

```
<type>[optional scope]: <description>
```

| Type | Purpose | Version Bump |
|------|---------|-------------|
| `feat` | New feature | Minor |
| `fix` | Bug fix | Patch |
| `perf` | Performance improvement | Patch |
| `docs` | Documentation only | None |
| `test` | Add/update tests | None |
| `refactor` | Code change (no feature/fix) | None |
| `chore` | Maintenance, deps, tooling | None |
| `ci` | CI/CD changes | None |
| `build` | Build system changes | None |
| `style` | Formatting, no logic change | None |
| `revert` | Revert a previous commit | Depends |

- Use imperative mood: "add feature" not "added feature"
- No emoji — they break release-please
- Keep the first line under 72 characters
- Add `!` after the type for breaking changes: `feat!: remove legacy API`

### PR Titles

PR titles **must** match Conventional Commits format — this is enforced by CI. The PR title becomes the squash-merge commit message.

### Code Quality

- **Coverage**: Every changed `.cs` file must reach 50% line coverage. CI enforces this automatically.
- **Linting**: The Release build uses `TreatWarningsAsErrors`. Fix all warnings before submitting.
- **Self-documenting code**: Avoid unnecessary comments. Code should be readable on its own.
- **No over-engineering**: Don't add features, abstractions, or config beyond what's needed.

### Wiki Documentation

The `docs/` folder contains GitHub Wiki pages synced via CI. Update them when:

- Adding a feature → add to the relevant page (or create one)
- Changing behavior → update the page describing that feature
- Adding/changing settings → update [Settings.md](docs/Settings.md)

## Development Tips

### Project Structure

```
HoobiBitwardenCommandPaletteExtension/     # Main extension
  Commands/       # InvokableCommand implementations
  Helpers/        # Static utility classes
  Models/         # Data models (BitwardenItem, etc.)
  Pages/          # DynamicListPage implementations
  Services/       # Core services (CLI, clipboard, settings, etc.)
HoobiBitwardenCommandPaletteExtension.Tests/ # xUnit tests
docs/             # Wiki documentation (synced to GitHub Wiki)
.github/          # CI workflows, templates, Dependabot config
```

### SDK Reference

This extension uses the [Microsoft Command Palette Extensions SDK](https://learn.microsoft.com/windows/powertoys/command-palette/). Key types:

- `DynamicListPage` — searchable list pages
- `ListItem` — display items with title, subtitle, icon, tags
- `InvokableCommand` — actions triggered by list items
- `CommandResult` — what happens after a command runs (dismiss, toast, navigate)

### Debugging

Use VS Code tasks (defined in `.vscode/tasks.json`) for the build-deploy cycle:

- **Build, Kill & Deploy (Debug x64)** — builds, stops PowerToys, deploys the extension, restarts PowerToys

## Questions?

Open a [Discussion](https://github.com/hoobio/command-palette-bitwarden/issues) or check the [Wiki](https://github.com/hoobio/command-palette-bitwarden/wiki) for existing documentation.
