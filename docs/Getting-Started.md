# Getting Started

## Prerequisites

- **Windows 10 (19041+)** or later with [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/) enabled
- **[Bitwarden CLI](https://bitwarden.com/help/cli/)** (`bw`) installed and available on your `PATH`

## Installation

### From Microsoft Store (Recommended)

**[Get it from Microsoft Store](https://apps.microsoft.com/detail/9P5KS8T80MV3)** — easiest way to install with automatic updates.

### From GitHub Releases

1. Download the `.msix` package for your architecture (x64 or ARM64) from [Releases](https://github.com/hoobio/hoobi-bitwarden-command-palette/releases/latest)
2. Install the signing certificate to the **Trusted People** store:
   ```powershell
   Import-Certificate -FilePath .\HoobiBitwardenCommandPaletteExtension.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```
3. Install the package:
   ```powershell
   Add-AppxPackage -Path .\HoobiBitwardenCommandPaletteExtension_x64.msix
   ```

### From Source

```powershell
git clone https://github.com/hoobio/hoobi-bitwarden-command-palette.git
cd hoobi-bitwarden-command-palette/HoobiBitwardenCommandPaletteExtension
dotnet build -c Debug -p:Platform=x64
```

## First Launch

1. Open the Command Palette (default: `Win + Ctrl + Space`)
2. Type **"Bitwarden"** to open the vault browser
3. If your vault is locked, you'll be prompted for your master password
4. Once unlocked, vault items are cached locally and refreshed every 5 minutes

## Setting Your Server

If you use a self-hosted Bitwarden server, select **Set Bitwarden Server** from the vault browser and enter your server URL. The default is `https://vault.bitwarden.com`.
