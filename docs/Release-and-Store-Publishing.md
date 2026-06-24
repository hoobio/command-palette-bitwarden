# Release & Store Publishing

How a tagged release reaches the Microsoft Store.

## Pipeline overview

1. `build.yaml` (release-please) cuts a release, builds + signs the per-architecture `.msix`/`.msi`, and attaches them to the GitHub release.
2. Publishing the release fires `release: published`, which triggers [`store-publish.yml`](../.github/workflows/store-publish.yml).
3. `store-publish.yml` downloads the per-arch `.msix` release assets, combines them into a single signed `.msixbundle`, attaches that bundle to the release, and submits it to the Store along with the listing's "What's new" notes.

A bundle is used for the Store because the `msstore` CLI uploads exactly one package file per submission and matches existing packages by extension; two loose `.msix` can't both update one submission, but one `.msixbundle` can.

## "What's new" notes

The Store listing's release notes come from [`HoobiBitwardenCommandPaletteExtension/Assets/WHATS_NEW.md`](../HoobiBitwardenCommandPaletteExtension/Assets/WHATS_NEW.md). Edit it before tagging a release; the workflow reads it at the release tag and writes it to the submission's `Listings.en-us.BaseListing.ReleaseNotes`.

## Required configuration

Repository **secrets**: `AZURE_TENANT_ID`, `AZURE_AD_CLIENT_ID`, `AZURE_AD_CLIENT_SECRET`, `SELLER_ID` (the Entra app registration must be added to Partner Center → User management with the **Manager** role), plus the existing `SIGNING_CERTIFICATE` / `SIGNING_CERTIFICATE_PASSWORD`.

Repository **variable**: `SIGNING_CERT_THUMBPRINT`.

Product ID (`9P5KS8T80MV3`) is hard-coded in the workflow `env`. GitHub Actions publishing is supported for **free** products only.

## Manual re-run

Re-publish any tag from the Actions tab: run **Publish to Microsoft Store** with the `tag` input (e.g. `v1.11.0`).

## One-time migration note

The first bundle submission has to retire the two loose `.msix` packages from the previous (manual) submissions. The workflow does this automatically: it marks every non-`.msixbundle` package `PendingDelete` before committing. On the first run, confirm in Partner Center that the committed submission contains only the `.msixbundle`. Every run after that is bundle-to-bundle and the cleanup step is a no-op.
