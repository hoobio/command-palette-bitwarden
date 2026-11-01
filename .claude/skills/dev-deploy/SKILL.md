---
name: dev-deploy
description: Build, test, register and restart the Bitwarden Command Palette extension on this machine. Load before any dotnet build/test/publish, Add-AppxPackage/Remove-AppxPackage, or stopping the HoobiBitwardenCommandPaletteExtension process in this repo.
---

# Dev deploy

Read the "Local Build & Deploy" section of `AGENTS.md` at the repo root before the first build, test or deploy command. It holds the commands, the worktree remove-then-register steps and the identity check.

Rules:

- When the Dev package is already registered from this checkout's `bin`, redeploy with the VS Code task "Build & Reload (Debug x64)": stop the Dev extension and companion, build, and `Start-Process 'x-cmdpal://reload'` in the same command. PowerToys keeps running.
- Never stop `HoobiBitwardenCommandPaletteExtension` without a reload or a PowerToys restart in the same command. Command Palette keeps its connection to the dead process and fails every call with `0x800706BA` until it reloads.
- A first deploy, a deploy from another checkout or worktree, or a manifest change uses the full cycle, or the VS Code task "Build, Kill & Deploy (Debug x64)". Remove-then-register while PowerToys runs leaves a duplicate entry in Command Palette Settings.
- After a deploy, the output `AppxManifest.xml` identity ends in `.Dev` and `Get-AppxPackage` shows this checkout as `InstallLocation`.
- Run `git checkout -- HoobiBitwardenCommandPaletteExtension/Package.appxmanifest` after every build. The stamped manifest is never committed.
