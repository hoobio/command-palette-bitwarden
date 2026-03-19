# Changelog

## [1.6.3](https://github.com/hoobio/command-palette-bitwarden/compare/v1.6.2...v1.6.3) (2026-03-19)


### Bug Fixes

* server config presets, device verification OTP, and version display ([#90](https://github.com/hoobio/command-palette-bitwarden/issues/90)) ([d2deb2f](https://github.com/hoobio/command-palette-bitwarden/commit/d2deb2fb153793f55e7c7a2597bfe8c75494d053))


### Build System

* **deps:** bump actions/attest-build-provenance from 2 to 4 ([#83](https://github.com/hoobio/command-palette-bitwarden/issues/83)) ([64ac5a0](https://github.com/hoobio/command-palette-bitwarden/commit/64ac5a054ec0b370fcc7c5abf7f6f9eaddd1a2d3))
* **deps:** bump actions/checkout from 4 to 6 ([#84](https://github.com/hoobio/command-palette-bitwarden/issues/84)) ([eaa5076](https://github.com/hoobio/command-palette-bitwarden/commit/eaa50768b10fe40b02dcef6c6e34494fac8222f3))
* **deps:** bump actions/github-script from 7 to 8 ([#80](https://github.com/hoobio/command-palette-bitwarden/issues/80)) ([2785f65](https://github.com/hoobio/command-palette-bitwarden/commit/2785f657a57a66e03fbbfc42d059e111ebe374f6))
* **deps:** bump peter-evans/create-or-update-comment from 4 to 5 ([#82](https://github.com/hoobio/command-palette-bitwarden/issues/82)) ([8f8dd7a](https://github.com/hoobio/command-palette-bitwarden/commit/8f8dd7ae6fb7b6086de6f47da1edf958795ff252))
* **deps:** bump peter-evans/find-comment from 3 to 4 ([#81](https://github.com/hoobio/command-palette-bitwarden/issues/81)) ([ac7daaa](https://github.com/hoobio/command-palette-bitwarden/commit/ac7daaa9dc78ec26323d8154404d86ed1c07a538))


### Continuous Integration

* extract build and wack into reusable workflows to fix required check naming ([#87](https://github.com/hoobio/command-palette-bitwarden/issues/87)) ([734e5de](https://github.com/hoobio/command-palette-bitwarden/commit/734e5def42762b9bfb8864eb323ee1659eb5244b))
* skip build/lint/wack on PRs with no code changes ([#85](https://github.com/hoobio/command-palette-bitwarden/issues/85)) ([657edee](https://github.com/hoobio/command-palette-bitwarden/commit/657edee236dfe955f324cfc37a24fe1c895ce0d6))

## [1.6.2](https://github.com/hoobio/command-palette-bitwarden/compare/v1.6.1...v1.6.2) (2026-03-13)


### Bug Fixes

* resolve warmup race condition causing vault status hang ([#77](https://github.com/hoobio/command-palette-bitwarden/issues/77)) ([f1e6fb0](https://github.com/hoobio/command-palette-bitwarden/commit/f1e6fb0e4655e6ae48611768dc48fb8538319bcb))


### Documentation

* note that Store version may lag behind GitHub Releases ([#73](https://github.com/hoobio/command-palette-bitwarden/issues/73)) ([515ab67](https://github.com/hoobio/command-palette-bitwarden/commit/515ab67d9cb6ef09fb53c5717ab4c76be1906efb))


### Miscellaneous Chores

* update repo URLs to hoobio/command-palette-bitwarden ([#76](https://github.com/hoobio/command-palette-bitwarden/issues/76)) ([7caaecc](https://github.com/hoobio/command-palette-bitwarden/commit/7caaeccf18dfa138f199251332bf49a99f907ef3))


### Continuous Integration

* add linting, 255 unit tests, coverage, WACK, and CodeQL ([#74](https://github.com/hoobio/command-palette-bitwarden/issues/74)) ([9e8fd13](https://github.com/hoobio/command-palette-bitwarden/commit/9e8fd1376b19ad976611246f810f3ea30ad69713))
* trigger CodeQL on release-please PRs ([#79](https://github.com/hoobio/command-palette-bitwarden/issues/79)) ([419bba4](https://github.com/hoobio/command-palette-bitwarden/commit/419bba421788a470744adc025af0f207357c3eb4))
* trigger CodeQL on release-please PRs via manifest path filter ([419bba4](https://github.com/hoobio/command-palette-bitwarden/commit/419bba421788a470744adc025af0f207357c3eb4))

## [1.6.1](https://github.com/hoobio/command-palette-bitwarden/compare/v1.6.0...v1.6.1) (2026-03-10)


### Bug Fixes

* add website icon caching and fix loading indicator during vault actions ([#71](https://github.com/hoobio/command-palette-bitwarden/issues/71)) ([5cdf91e](https://github.com/hoobio/command-palette-bitwarden/commit/5cdf91e347ff85607266e3bcfb78362af03e5cd2))


### Miscellaneous Chores

* rename store title to Command Palette Extension for Bitwarden ([#68](https://github.com/hoobio/command-palette-bitwarden/issues/68)) ([92811d8](https://github.com/hoobio/command-palette-bitwarden/commit/92811d870b43ac842d30da3b12ba9220d36da5ec))

## [1.6.0](https://github.com/hoobio/command-palette-bitwarden/compare/v1.5.2...v1.6.0) (2026-03-10)


### Features

* 1Password parity features - clipboard, context awareness, tags, search syntax ([#63](https://github.com/hoobio/command-palette-bitwarden/issues/63)) ([5ff2011](https://github.com/hoobio/command-palette-bitwarden/commit/5ff201110be20287d8666d07ef260d34c29131bb))
* add auto-lock timeout, background refresh, and live sync status ([ded724a](https://github.com/hoobio/command-palette-bitwarden/commit/ded724a062b05d2dc41a37ae5f869cd56137d652))
* add context item limit and tag visibility settings ([5b39135](https://github.com/hoobio/command-palette-bitwarden/commit/5b391353bae28d2f354b53f08a43ec4c5fccaf9d))
* add is:weak, is:old, is:insecure, is:watchtower search filters ([adcbeed](https://github.com/hoobio/command-palette-bitwarden/commit/adcbeed1d382f33ef09dd6226be0b7eb9eaf2557))
* add passkey tag and has:passkey search filter ([066a618](https://github.com/hoobio/command-palette-bitwarden/commit/066a618b2ac2f6c88e0d39a7af0032f533665d67))
* replace ShowTotpTag with TotpTagStyle choice setting (off/static/live) ([fd5afb4](https://github.com/hoobio/command-palette-bitwarden/commit/fd5afb49cad708701c0aff855978c2fd9ede17c5))
* respect Bitwarden URI match type in context detection ([5f367a3](https://github.com/hoobio/command-palette-bitwarden/commit/5f367a32c76b7fd13aa12937a5cc4d9b45763c71))
* split login into two-step flow for 2FA usability ([8a0f30b](https://github.com/hoobio/command-palette-bitwarden/commit/8a0f30b0f1538a72ec9fc064c9e836cf7b26c21f))
* wire up 2FA flow, clear search on vault ops, context limit and tag settings ([727b426](https://github.com/hoobio/command-palette-bitwarden/commit/727b42616d67fe922fb37c6bbb7b721c28d77aae))


### Bug Fixes

* address security and correctness findings from code audit ([c77f981](https://github.com/hoobio/command-palette-bitwarden/commit/c77f9816f75b60e4cba0712734304bf252a66e62))
* cap context tag to top N items matching the context item limit ([c9393b1](https://github.com/hoobio/command-palette-bitwarden/commit/c9393b1a2cd73c133e595b6810f532c40dbd5902))
* preserve context tag cap in TOTP timer tick ([c34cee3](https://github.com/hoobio/command-palette-bitwarden/commit/c34cee36b8108a27d018cf2adfc05537efd575d7))
* re-sort context remainder list without context boost ([3009ae2](https://github.com/hoobio/command-palette-bitwarden/commit/3009ae29e5cbebfac659fa45d2fca850eb75291f))
* show all items when context limit is set, not only context matches ([80d8bc1](https://github.com/hoobio/command-palette-bitwarden/commit/80d8bc14c3cfb163c26c25cc3770672c321c589e))


### Documentation

* add missing settings to Settings.md ([21212ce](https://github.com/hoobio/command-palette-bitwarden/commit/21212ceca5d7acc78f9751767e928e386c9bb433))


### Styles

* reformat build.yaml and release-please-config.json ([92841c8](https://github.com/hoobio/command-palette-bitwarden/commit/92841c86b73890e2e017e3aed1f679f40e400082))


### Build System

* stamp manifest version via MSBuild XmlPoke and remove from release-please extra-files ([9a626e6](https://github.com/hoobio/command-palette-bitwarden/commit/9a626e621ff1b9d14b73e325ab4cb08956bda875))


### Continuous Integration

* add pre-release job for non-release main pushes ([47b0a03](https://github.com/hoobio/command-palette-bitwarden/commit/47b0a03c341702243c2d0a7e9b15ae28fb313bdd))
* add wiki sync workflow ([4d1d972](https://github.com/hoobio/command-palette-bitwarden/commit/4d1d9724118bf8553a7d021229d83529437ad36c))
* allow release-please to run on workflow_dispatch ([ef31806](https://github.com/hoobio/command-palette-bitwarden/commit/ef318065c501761ca2070dc9cddbf3991c76d81f))
* explicitly set hidden:false for all changelog sections ([124a7c8](https://github.com/hoobio/command-palette-bitwarden/commit/124a7c8e9f2c12af5bc03c0f6ba6aaf28a98328b))
* generate commit-based release notes for pre-releases ([90f2c5c](https://github.com/hoobio/command-palette-bitwarden/commit/90f2c5c566844d9f148ac6956752f0b498c77f6b))
* prune old pre-releases, keep 3 per version ([69c5df0](https://github.com/hoobio/command-palette-bitwarden/commit/69c5df0462c87cf2e149664b45538c7d8d8cb986))
* remove inline release-type so release-please uses config file ([040d037](https://github.com/hoobio/command-palette-bitwarden/commit/040d037761c5a586e21bce4b3b4d068777f6e805))
* remove schema from manifest to fix release-please parsing ([96ca9d8](https://github.com/hoobio/command-palette-bitwarden/commit/96ca9d878dda99943e9e5f4225f4ec5df68ab142))
* trigger release-please with fresh branch ([09802fa](https://github.com/hoobio/command-palette-bitwarden/commit/09802fa5633e23ffc15e1980c5e57e16c1077e72))
* use next version from release PR for pre-releases and include all commit types in changelog ([00c8219](https://github.com/hoobio/command-palette-bitwarden/commit/00c82199c298e87c68f573c11014afeee7564dac))
* use non-semver pre-release tag to avoid poisoning release-please ([e8be016](https://github.com/hoobio/command-palette-bitwarden/commit/e8be016cac1adb125189f1562b9cc2af13a7970a))
* use semver pre-release tags with make_latest:false for proper sorting ([6e9904a](https://github.com/hoobio/command-palette-bitwarden/commit/6e9904a742c98b27e3cafd15ecabd0f09f061c20))

## [1.5.2](https://github.com/hoobio/command-palette-bitwarden/compare/v1.5.1...v1.5.2) (2026-03-09)


### Bug Fixes

* add id-token and attestations permissions for build provenance ([dcd20d8](https://github.com/hoobio/command-palette-bitwarden/commit/dcd20d88c3eb9f6a4176c5c7b7a7758d1ef9d4dd))
* add id-token and attestations permissions for build provenance ([#31](https://github.com/hoobio/command-palette-bitwarden/issues/31)) ([85d2b15](https://github.com/hoobio/command-palette-bitwarden/commit/85d2b15e85398272686adfb7da70dc164efcf4ed))
* align all version files with v1.5.1 release ([724e208](https://github.com/hoobio/command-palette-bitwarden/commit/724e208501f47c1172c659ce46ead4e1594bd572))
* align all version files with v1.5.1 release (AB[#0](https://github.com/hoobio/command-palette-bitwarden/issues/0)) ([#43](https://github.com/hoobio/command-palette-bitwarden/issues/43)) ([724e208](https://github.com/hoobio/command-palette-bitwarden/commit/724e208501f47c1172c659ce46ead4e1594bd572))
* move attestation to release job and fix release-please title check ([#35](https://github.com/hoobio/command-palette-bitwarden/issues/35)) ([1a99a41](https://github.com/hoobio/command-palette-bitwarden/commit/1a99a41e85c156ddf6418754e04575d8e5918f84))
* restore release-please baseline to v1.5.0 (AB[#0](https://github.com/hoobio/command-palette-bitwarden/issues/0)) ([#40](https://github.com/hoobio/command-palette-bitwarden/issues/40)) ([572d2c7](https://github.com/hoobio/command-palette-bitwarden/commit/572d2c7f2f1215d0940f74bf29e57ac8e13171cc))
* use generic updater for manifest version and reset to 1.5.0 ([#38](https://github.com/hoobio/command-palette-bitwarden/issues/38)) ([ddd116b](https://github.com/hoobio/command-palette-bitwarden/commit/ddd116b4984f5e09d5766bea1249bfb82f3ce97e))
* use generic updater for manifest version so release-please updates it correctly ([#37](https://github.com/hoobio/command-palette-bitwarden/issues/37)) ([84a41e1](https://github.com/hoobio/command-palette-bitwarden/commit/84a41e1bc77d79c94f7be1079f45465d5d8073f3))
* use pull_request_target for title check and sync manifest version ([dd000e4](https://github.com/hoobio/command-palette-bitwarden/commit/dd000e4e46d52647257963fb6bf652ce1abee275))
* use pull_request_target for title checks and sync manifest version ([#33](https://github.com/hoobio/command-palette-bitwarden/issues/33)) ([fffdbc1](https://github.com/hoobio/command-palette-bitwarden/commit/fffdbc1bd41df43d5bff7af76427dba6eaadf5e0))

## [1.5.1](https://github.com/hoobio/command-palette-bitwarden/compare/v1.5.0...v1.5.1) (2026-03-09)


### Bug Fixes

* add build attestation ([#29](https://github.com/hoobio/command-palette-bitwarden/issues/29)) ([71b501c](https://github.com/hoobio/command-palette-bitwarden/commit/71b501cb81d943dd5bbdd1c04113a63224244ba3))
* add build attestation for MSIX packages ([e134583](https://github.com/hoobio/command-palette-bitwarden/commit/e13458377436709030b2db55f069298631e88a60))

## [1.5.0](https://github.com/hoobio/command-palette-bitwarden/compare/v1.4.1...v1.5.0) (2026-03-09)


### Features

* improve vault UX with loading states, error handling, and toast messages ([9383b60](https://github.com/hoobio/command-palette-bitwarden/commit/9383b6065e0ddd2bfd093ec0b4a6c02bbbc68f3d))
* keep palette open for Lock/Logout/SetServer with loading states ([fa1c23f](https://github.com/hoobio/command-palette-bitwarden/commit/fa1c23f0b96a86abd75f7da4fedaf71ac7a1c40f))


### Bug Fixes

* clear vault cache immediately before lock and logout ([1b1d368](https://github.com/hoobio/command-palette-bitwarden/commit/1b1d36839976205aaeaeaaf61eb03b12118a937d))

## [1.4.1](https://github.com/hoobio/command-palette-bitwarden/compare/v1.4.0...v1.4.1) (2026-03-09)


### Bug Fixes

* correct release-please to check refs/heads/main ([#23](https://github.com/hoobio/command-palette-bitwarden/issues/23)) ([b44010e](https://github.com/hoobio/command-palette-bitwarden/commit/b44010eab5de63fe10e78dd0a748b92c1f923ae8))
* correct release-please to run on refs/heads/main (AB[#0](https://github.com/hoobio/command-palette-bitwarden/issues/0)) ([#20](https://github.com/hoobio/command-palette-bitwarden/issues/20)) ([de597e6](https://github.com/hoobio/command-palette-bitwarden/commit/de597e6e086ebcd27a08cecb7762023d1cff3930))
* SDK update, Win32 credentials, lock vault, UX improvements and workflow fixes (AB[#0](https://github.com/hoobio/command-palette-bitwarden/issues/0)) ([#17](https://github.com/hoobio/command-palette-bitwarden/issues/17)) ([0709164](https://github.com/hoobio/command-palette-bitwarden/commit/07091642785e2c06dac7157bd27fd3409c32058f))
* sync release-please workflow configuration from dev (AB[#0](https://github.com/hoobio/command-palette-bitwarden/issues/0)) ([#18](https://github.com/hoobio/command-palette-bitwarden/issues/18)) ([68bd60b](https://github.com/hoobio/command-palette-bitwarden/commit/68bd60bbc23181f0013cbb78da92f9b2be435f21))

## [1.4.0](https://github.com/hoobio/command-palette-bitwarden/compare/v1.3.0...v1.4.0) (2026-03-09)


### Features

* SDK update, Win32 credentials, lock vault and UX improvements ([#11](https://github.com/hoobio/command-palette-bitwarden/issues/11)) ([a5cc96b](https://github.com/hoobio/command-palette-bitwarden/commit/a5cc96b9d71912fbde900cd0d5d44cb5c9dc577b))

## [1.3.0](https://github.com/hoobio/command-palette-bitwarden/compare/v1.2.0...v1.3.0) (2026-03-06)


### Features

* add login flow, logout, server config, 2FA support, and session persistence ([1a873c8](https://github.com/hoobio/command-palette-bitwarden/commit/1a873c8898225731661042ea2153b956ee0eda60))

## [1.2.0](https://github.com/hoobio/command-palette-bitwarden/compare/v1.1.1...v1.2.0) (2026-03-06)


### Features

* configure Store identity and update signing cert to match Store publisher ([03ae6d9](https://github.com/hoobio/command-palette-bitwarden/commit/03ae6d989c13e9c766c7a26b93bf40b45c54ebbb))

## [1.1.1](https://github.com/hoobio/command-palette-bitwarden/compare/v1.1.0...v1.1.1) (2026-03-06)


### Bug Fixes

* correct Extenstion typo in package identity and COM registration ([8b22036](https://github.com/hoobio/command-palette-bitwarden/commit/8b22036243ed3eab56ff34ce69efeaec5e456d6d))

## [1.1.0](https://github.com/hoobio/command-palette-bitwarden/compare/v1.0.0...v1.1.0) (2026-03-06)


### Features

* sign MSIX packages with self-signed certificate in CI ([a76696f](https://github.com/hoobio/command-palette-bitwarden/commit/a76696fc3bf3c5787729f099c90835f31fd1a94a))

## 1.0.0 (2026-03-06)


### Bug Fixes

* Set-Content encoding to utf8NoBOM to prevent BOM in Package.appxmanifest ([b3cae4c](https://github.com/hoobio/command-palette-bitwarden/commit/b3cae4c1fa6216a152d13589d69b1e877b905f4e))
* use case-sensitive regex with Identity lookbehind for version stamp ([5878809](https://github.com/hoobio/command-palette-bitwarden/commit/5878809f4057d602389fe5d865eb853ae1cc790e))
* use File.WriteAllText with explicit UTF8NoBOM to stamp manifest ([7a44e99](https://github.com/hoobio/command-palette-bitwarden/commit/7a44e99dc74901be4e7eba82502d8344ffe18fa3))
