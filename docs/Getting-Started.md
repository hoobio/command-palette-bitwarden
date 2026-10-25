# Getting Started

## Prerequisites

- **Windows 10 (19041+)** or later with [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/) enabled
- **[Bitwarden CLI](https://bitwarden.com/help/cli/)** (`bw`) installed and available on your `PATH`, or configured via the **CLI Path Override** [setting](Settings)

## Installation

### From Microsoft Store (Recommended)

**[Get it from Microsoft Store](https://apps.microsoft.com/detail/9P5KS8T80MV3)** - easiest way to install with automatic updates.

### From GitHub Releases

1. Download the `.msix` package for your architecture (x64 or ARM64) from [Releases](https://github.com/hoobio/command-palette-bitwarden/releases/latest)
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
git clone https://github.com/hoobio/command-palette-bitwarden.git
cd hoobi-bitwarden-command-palette/HoobiBitwardenCommandPaletteExtension
dotnet build -c Debug -p:Platform=x64
```

## First Launch

1. Open the Command Palette (default: `Win + Ctrl + Space`)
2. Type **"Bitwarden"** to open the vault browser
3. If your vault is locked, select **Unlock Vault**. The companion window opens and asks for your master password. Command Palette stays open behind it. If the companion is not available, the item opens a form in Command Palette instead. See [Unlock](Companion-Window#unlock).
4. Once unlocked, vault items are cached locally and refreshed every 5 minutes

### API key login

If your account uses FIDO2 (WebAuthn) or Duo two-step login, the CLI cannot complete a password login. Turn on **Use API Key Authentication** in [Settings](Settings) and log in with your [personal API key](https://bitwarden.com/help/personal-api-key/) instead:

1. In the Bitwarden web vault, go to **Settings > Security > Keys** and select **View API key**.
2. Select **Login to Bitwarden**. The companion window opens. Paste the **Client ID** and **Client Secret** into it and select **Log in**. The window stays open when you switch to the web vault, so the values are kept. Command Palette stays open behind it. If the companion is not available, the item opens a form in Command Palette instead.
3. Unlock the vault with your master password.

The extension stores the API key in Windows Credential Manager. When the CLI session ends, the extension logs in again with the stored key, so you only enter your master password. API key login skips two-step login. The API key cannot decrypt the vault without the master password.

## Setting Your Server

Select **Set Bitwarden Server** from the vault browser. The companion window opens, where you pick a region (**Bitwarden Cloud** for `bitwarden.com`, **Bitwarden EU** for `https://vault.bitwarden.eu`) or enter a self-hosted server URL. If the companion is not available, the item opens a list in Command Palette instead. See [Server URL](Companion-Window#server-url). The default is `https://vault.bitwarden.com`. Only the exact hosts `bitwarden.com` and its subdomains are treated as cloud. A self-hosted server on a host that only contains "bitwarden.com" in its name is treated as self-hosted.

## Sign-in Security

- Pressing Enter in a password field submits the form. Pressing Enter in the login email field needs PowerToys 0.101 or later.
- When the extension downloads the Bitwarden CLI, it accepts the file only if the Authenticode signature is valid (with a revocation check) and the signer is exactly `Bitwarden Inc.`.
- For biometric unlock, the extension checks that the pipe server is a Bitwarden-signed binary and writes the result to the debug log. The extension drops unencrypted responses from the pipe.
- During a Windows Hello prompt, Command Palette stays open, so you can type in the search box as soon as the prompt closes.
