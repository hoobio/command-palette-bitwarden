# Release & Store Publishing

How a tagged release reaches the Microsoft Store.

## Pipeline overview

Everything lives in [`build.yaml`](../.github/workflows/build.yaml):

1. `build-msix` builds + signs the per-architecture `.msix`; `build-msi` wraps each into an `.msi`.
2. The `bundle` job combines the per-arch `.msix` into one signed `.msixbundle` and uploads it as an artifact.
3. The `release` job (and `pre-release`, for prereleases) attaches that bundle, plus the `.msix`/`.msi`, to the **draft** release before publishing. This is required: the repo uses immutable releases, so assets can only be added while the release is still a draft.
4. The `store-publish` job (real releases only) takes the bundle artifact and submits it to the Store with the listing's "What's new" notes.

A bundle is used for the Store because the `msstore` CLI uploads exactly one package file per submission and matches existing packages by extension; two loose `.msix` can't both update one submission, but one `.msixbundle` can.

## "What's new" notes

The Store listing's release notes come from [`HoobiBitwardenCommandPaletteExtension/Assets/WHATS_NEW.md`](../HoobiBitwardenCommandPaletteExtension/Assets/WHATS_NEW.md). Edit it before tagging a release; the `store-publish` job reads it and writes it to the submission's `Listings.en-us.BaseListing.ReleaseNotes`.

## Required configuration

Repository **secrets**: `AZURE_TENANT_ID`, `AZURE_AD_CLIENT_ID`, `AZURE_AD_CLIENT_SECRET`, `SELLER_ID` (the Entra app registration must be added to Partner Center → User management with the **Manager** role), plus the existing `SIGNING_CERTIFICATE` / `SIGNING_CERTIFICATE_PASSWORD`.

Repository **variable**: `SIGNING_CERT_THUMBPRINT`.

Product ID (`9P5KS8T80MV3`) is hard-coded in the `store-publish` job `env`. GitHub Actions publishing is supported for **free** products only.

## Re-running a failed Store publish

The Store publish runs as part of the release pipeline. If the `store-publish` job fails (and the rest of the release succeeded), re-run just that job from the failed workflow run in the Actions tab; it re-downloads the bundle artifact and resubmits.

## One-time migration note

The first bundle submission has to retire the two loose `.msix` packages from the previous (manual) submissions. The workflow does this automatically: it marks every non-`.msixbundle` package `PendingDelete` before committing. On the first run, confirm in Partner Center that the committed submission contains only the `.msixbundle`. Every run after that is bundle-to-bundle and the cleanup step is a no-op.
