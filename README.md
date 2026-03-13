# Command Palette Extension for Bitwarden

A free, open-source [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/) extension for Bitwarden. Instant credential search and copy, directly from your keyboard.

Built as an alternative to 1Password Quick Access after their unjustified price increases. Same experience, powered by your Bitwarden vault.

[![Get it from Microsoft Store](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9P5KS8T80MV3)
&nbsp;&nbsp;&nbsp;&nbsp;
[![Download from GitHub Releases](https://img.shields.io/github/v/release/hoobio/command-palette-bitwarden?label=Download%20from%20GitHub&logo=github&style=for-the-badge&color=181717)](https://github.com/hoobio/command-palette-bitwarden/releases/latest)

---

[![Build & Release](https://img.shields.io/github/actions/workflow/status/hoobio/command-palette-bitwarden/build.yaml?branch=main&label=Build&logo=github-actions)](https://github.com/hoobio/command-palette-bitwarden/actions/workflows/build.yaml)
[![CodeQL](https://img.shields.io/github/actions/workflow/status/hoobio/command-palette-bitwarden/codeql.yml?branch=main&label=CodeQL&logo=github)](https://github.com/hoobio/command-palette-bitwarden/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![GitHub Stars](https://img.shields.io/github/stars/hoobio/command-palette-bitwarden?style=social)](https://github.com/hoobio/command-palette-bitwarden/stargazers)
[![GitHub Issues](https://img.shields.io/github/issues/hoobio/command-palette-bitwarden)](https://github.com/hoobio/command-palette-bitwarden/issues)
[![Last Commit](https://img.shields.io/github/last-commit/hoobio/command-palette-bitwarden)](https://github.com/hoobio/command-palette-bitwarden/commits/main)

![Preview](preview.png)

## Features

- **Vault search** with fallback suggestions across all item types
- **Secure clipboard**: passwords excluded from clipboard history, auto-cleared on a configurable timer
- **Smart sorting**: recently used, favorites, and context-matched items float to the top
- **TOTP display**: live codes with countdown timers
- **Search filters**: prefix syntax like `is:fav`, `folder:Work`, `has:totp`, `url:github`
- **Watchtower tags**: visual warnings for weak, old, or reused passwords
- **Context awareness**: detects open apps and browser tabs to surface relevant items
- **Custom field copy**, **SSH quick-connect**, **manual vault sync**, and more

> **📖 [Full documentation on the Wiki](../../wiki)**: [search filters](../../wiki/Search-and-Filtering), [context awareness](../../wiki/Context-Awareness), [clipboard security](../../wiki/Clipboard-Security), [settings](../../wiki/Settings), and [item actions](../../wiki/Vault-Item-Actions).

## Prerequisites

- **Windows 10 (19041+)** with [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/) enabled
- **[Bitwarden CLI](https://bitwarden.com/help/cli/)** (`bw`) on your `PATH`

## Installation

### Microsoft Store

**[Get it from the Microsoft Store](https://apps.microsoft.com/detail/9P5KS8T80MV3)** for automatic updates.

> The Store version may lag behind GitHub Releases due to Microsoft's certification process. Install from GitHub if you need the latest version immediately.

### GitHub Releases

1. Download the `.msix` for your architecture (x64 or ARM64) from [Releases](../../releases)
2. Install the signing certificate ([`HoobiBitwardenCommandPaletteExtension.cer`](HoobiBitwardenCommandPaletteExtension.cer)) to the **Trusted People** store:
   - Double-click the `.cer` file → **Install Certificate** → **Local Machine** → **Trusted People** → Finish
   - Or via PowerShell (admin):
     ```powershell
     Import-Certificate -FilePath .\HoobiBitwardenCommandPaletteExtension.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
     ```
3. Double-click the `.msix` to install, or:
   ```powershell
   Add-AppxPackage -Path .\HoobiBitwardenCommandPaletteExtension_x64.msix
   ```

### From Source

```powershell
git clone https://github.com/your-username/hoobi-bitwarden-command-palette.git
cd hoobi-bitwarden-command-palette/HoobiBitwardenCommandPaletteExtension
dotnet build -c Debug -p:Platform=x64
```

## Usage

1. Open the Command Palette (`Win + Ctrl + Space`)
2. Type **"Bitwarden"** to open the vault browser, or just start typing. Matching items appear as fallback results
3. If your vault is locked, you'll be prompted for your master password
4. Click an item for its default action, or open the context menu for more options

## Security

- Master passwords are passed to the Bitwarden CLI via **environment variables**, not command-line arguments
- Session keys are stored in **Windows Credential Manager** when "Remember Session" is enabled
- Sensitive clipboard data (passwords, TOTP, card numbers) is excluded from Windows clipboard history and auto-cleared
- Vault cache is **in-memory only**, cleared on lock/exit
- No vault data is written to disk (only access timestamps for sorting)
- All search input is **regex-escaped** before use

## Building

```powershell
# Debug
dotnet build -p:Platform=x64

# Release
dotnet publish -c Release -p:Platform=x64
```

## License

[MIT](LICENSE)
