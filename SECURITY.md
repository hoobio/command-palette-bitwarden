# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest release | Yes |
| Previous minor | Best-effort |
| Older | No |

Only the latest release receives security fixes. Users are encouraged to stay up to date via the [Microsoft Store](https://apps.microsoft.com/detail/9P5KS8T80MV3) or [GitHub Releases](https://github.com/hoobio/command-palette-bitwarden/releases/latest).

## Reporting a Vulnerability

**Please do not open a public issue for security vulnerabilities.**

Instead, report vulnerabilities privately using [GitHub Security Advisories](https://github.com/hoobio/command-palette-bitwarden/security/advisories/new).

Include:

- A description of the vulnerability
- Steps to reproduce or proof of concept
- The potential impact
- Any suggested fix (optional)

You should receive an initial response within 72 hours. The advisory will remain private until a fix is released.

## Security Model

This extension handles sensitive credential data. Here's how it's protected:

### Credential Handling

- Master passwords are passed to the Bitwarden CLI via **environment variables**, never command-line arguments (which are visible in process listings)
- Session keys are stored in **Windows Credential Manager** when "Remember Session" is enabled
- No vault data is written to disk — the cache is **in-memory only** and cleared on lock/exit

### Clipboard Security

- Sensitive clipboard data (passwords, TOTP codes, card numbers) is excluded from Windows clipboard history
- Clipboard contents are auto-cleared on a configurable timer (default: 30 seconds)
- Non-sensitive fields (usernames, emails) use standard clipboard operations

### Input Handling

- All search input is **regex-escaped** before use to prevent injection
- Brand slugs for card icons are sanitized to alphanumeric characters only
- URLs are constructed using `Uri.EscapeDataString` where user input is involved

### Network

- The extension itself makes no network requests beyond favicon/icon fetching from the configured Bitwarden server
- The `internetClient` capability is declared because the Bitwarden CLI requires network access for vault sync

### Supply Chain

- Dependencies are managed via [Dependabot](https://github.com/hoobio/command-palette-bitwarden/blob/main/.github/dependabot.yml)
- [CodeQL](https://github.com/hoobio/command-palette-bitwarden/actions/workflows/codeql.yml) runs on every push to `main` and on all pull requests
- Release builds are attested with [GitHub Artifact Attestation](https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations)
- MSIX packages pass [Windows App Certification Kit (WACK)](https://learn.microsoft.com/windows/uwp/debug-test-perf/windows-app-certification-kit) testing in CI

## Disclosure Policy

When a vulnerability is confirmed:

1. A fix is developed in a private fork or branch
2. A new release is published with the fix
3. The security advisory is published on GitHub with credit to the reporter
4. The CHANGELOG notes the security fix
