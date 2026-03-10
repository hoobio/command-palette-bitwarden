# Command Palette Extension for Bitwarden

A free, open-source replacement for 1Password Quick Access, built for the [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/) and Bitwarden. Born out of frustration with 1Password's unwarranted price increases, this extension brings the same instant credential search and copy experience, powered by your Bitwarden vault, directly into the PowerToys Command Palette.

[![Get it from Microsoft Store](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9P5KS8T80MV3)
&nbsp;&nbsp;&nbsp;&nbsp;
[![Download from GitHub Releases](https://img.shields.io/github/v/release/hoobio/hoobi-bitwarden-command-palette?label=Download%20from%20GitHub&logo=github&style=for-the-badge&color=181717)](https://github.com/hoobio/hoobi-bitwarden-command-palette/releases/latest)

---

[![Build & Release](https://img.shields.io/github/actions/workflow/status/hoobio/hoobi-bitwarden-command-palette/build.yaml?branch=main&label=Build&logo=github-actions)](https://github.com/hoobio/hoobi-bitwarden-command-palette/actions/workflows/build.yaml)
[![CodeQL](https://img.shields.io/github/actions/workflow/status/hoobio/hoobi-bitwarden-command-palette/codeql.yml?branch=main&label=CodeQL&logo=github)](https://github.com/hoobio/hoobi-bitwarden-command-palette/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![GitHub Stars](https://img.shields.io/github/stars/hoobio/hoobi-bitwarden-command-palette?style=social)](https://github.com/hoobio/hoobi-bitwarden-command-palette/stargazers)
[![GitHub Issues](https://img.shields.io/github/issues/hoobio/hoobi-bitwarden-command-palette)](https://github.com/hoobio/hoobi-bitwarden-command-palette/issues)
[![Last Commit](https://img.shields.io/github/last-commit/hoobio/hoobi-bitwarden-command-palette)](https://github.com/hoobio/hoobi-bitwarden-command-palette/commits/main)

![Preview](preview.png)

## Features

- **Vault search** with fallback suggestions — search all item types directly from the Command Palette
- **Secure clipboard** — passwords excluded from clipboard history, auto-cleared on a configurable timer
- **Favorites & smart sorting** — recently used, favorites, and context-matched items float to the top
- **TOTP display** — live codes with countdown timers shown as tags
- **Search filters** — prefix syntax like `is:fav`, `folder:Work`, `has:totp`, `url:github`
- **Watchtower tags** — visual warnings for weak, old, or insecure passwords
- **Context awareness** — detects your open apps and browser tabs to boost relevant vault items
- **Custom field copy**, **SSH quick-connect**, **manual vault sync**, and more

> **📖 [Full documentation on the Wiki](../../wiki)** — detailed guides for [search filters](../../wiki/Search-and-Filtering), [context awareness](../../wiki/Context-Awareness), [clipboard security](../../wiki/Clipboard-Security), [settings](../../wiki/Settings), and [all item actions](../../wiki/Vault-Item-Actions).

## Prerequisites

- **Windows 10 (19041+)** or later with [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/) enabled
- **[Bitwarden CLI](https://bitwarden.com/help/cli/)** (`bw`) installed and available on your `PATH`

## Installation

### From Microsoft Store

**[Get it from Microsoft Store](https://apps.microsoft.com/detail/9P5KS8T80MV3)** — easiest way to install with automatic updates.

> **Note:** The Store version may lag behind GitHub Releases due to Microsoft's certification process, which can be slow and unreliable. If you need the latest version immediately, install from GitHub Releases instead.

### From GitHub Releases

1. Download the `.msix` package for your architecture (x64 or ARM64) from [Releases](../../releases)
2. Download and install the signing certificate ([`HoobiBitwardenCommandPaletteExtension.cer`](HoobiBitwardenCommandPaletteExtension.cer)) to the **Trusted People** store:
   - Double-click the `.cer` file → **Install Certificate** → **Local Machine** → **Place all certificates in the following store** → **Trusted People** → Finish
   - Or via PowerShell (admin):
     ```powershell
     Import-Certificate -FilePath .\HoobiBitwardenCommandPaletteExtension.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
     ```
3. Double-click the `.msix` to install, or use PowerShell:
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

1. Open the Command Palette (default: `Win + Ctrl + Space`)
2. Type **"Bitwarden"** to open the vault browser, or just start typing. Matching items appear as fallback results
3. If your vault is locked, you'll be prompted for your master password
4. Click an item for its default action, or use the context menu for more options (copy password, TOTP, etc.)

## Security

- Master passwords are sent to the Bitwarden CLI via **environment variables** (never as command-line arguments visible in process lists)
- Session keys are stored in **Windows Credential Manager** (OS-level encryption) when "Remember Session" is enabled
- **Sensitive clipboard data** (passwords, TOTP codes, card numbers, security codes) is excluded from Windows clipboard history
- Clipboard is **automatically cleared** after a configurable delay for sensitive data

## Building

```powershell
# Debug
dotnet build -p:Platform=x64

# Release (with trimming)
dotnet publish -c Release -p:Platform=x64
```

## License

[MIT](LICENSE)
- The vault cache is held **in-memory only** and cleared on lock/exit
- All user search input is **regex-escaped** before use in pattern matching
- No vault data is written to disk (only access timestamps for sorting)

## License

MIT
