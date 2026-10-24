---
name: dev-deploy
description: Build, test, register and restart the Bitwarden Command Palette extension on this machine. Load before any dotnet build/test/publish, Add-AppxPackage/Remove-AppxPackage, or stopping the HoobiBitwardenCommandPaletteExtension process in this repo.
---

# Dev deploy

Read the "Local Build & Deploy" section of `AGENTS.md` at the repo root before the first build, test or deploy command. It holds the commands, the worktree remove-then-register steps and the identity check.

Rules:

- Never stop only `HoobiBitwardenCommandPaletteExtension`. Command Palette keeps its connection to the dead process and fails every call with `0x800706BA` until PowerToys restarts. Stop PowerToys, `Microsoft.CmdPal.UI` and the extension together, and start PowerToys again in the same command (`Start-Process 'shell:AppsFolder\Microsoft.PowerToysWin32'`).
- A build or test that fails on a locked `HoobiBitwardenCommandPaletteExtension.exe` means a Dev package runs from this checkout's `bin`. Use the full kill, build, register and restart cycle, or the VS Code task "Build, Kill & Deploy (Debug x64)".
- After a deploy, the output `AppxManifest.xml` identity ends in `.Dev` and `Get-AppxPackage` shows this checkout as `InstallLocation`.
- Run `git checkout -- HoobiBitwardenCommandPaletteExtension/Package.appxmanifest` after every build. The stamped manifest is never committed.
