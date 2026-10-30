# Releasing TimeTug for Windows

The release workflow (`.github/workflows/windows-release.yml`) builds the unsigned Microsoft Store package for x64 and ARM64, bundles them and uploads the result as a **workflow artifact**. It does not submit it anywhere: no Partner Center credential exists in this repository and no step talks to Partner Center. The owner takes `store-upload` to Partner Center by hand; phase 9 adds the submit step in its own change. A repository check (`scripts/ci/check-repository-contracts.py`) fails any change that gives the workflow another trigger, a write permission, a store or publishing step, or a secret outside the `release` environment.

## What starts a run

- **Publishing a GitHub release.** A tag `vX.Y.Z` on a release that is not marked pre-release builds the stable package. A suffixed tag (`v1.2.0-beta.1`) builds a **flight** package (`TimeTug_1.2.0.0_x64-flight.msix`): the same Store identity and the same `X.Y.Z.0` version (the Store reserves the revision, so a tag `v1.2.3` is package version `1.2.3.0` and anything that is not `X.Y.Z` is refused); the owner picks a higher `X.Y.Z` for each flight. A stable-form tag on a pre-release, and a tag that is not a version, are skipped.
- **A manual run** (`Actions > Windows release > Run workflow`): `version` is `X.Y.Z` (or `X.Y.Z-suffix` for a flight), without the `v`; `ref` is the default branch or a version tag whose version equals `version`; blank builds the existing tag `v<version>` (the workflow cannot create a tag). Any other ref is refused.

The decision is made by `scripts/release/resolve-release.ps1` (unit-tested), in a job that has no secret and no environment. The same job runs `assert-on-default-branch.ps1`: the ref must be the default branch or an ancestor of its tip, so a tag on a commit that was never merged cannot build with the secrets (the approval in the Actions page is by environment, not by commit; keep tag creation owner-only in the repository rulesets as well).

## The owner's approval

Both build jobs run in the `release` environment. **Create it before the first run** (Settings > Environments > New environment > `release`): required reviewer the owner, deployment branches limited to `main` and the `v*` tags. GitHub creates an environment that does not exist yet when a workflow first names it, with no protection, so an unprotected `release` must never exist. Nothing builds, and no OAuth value is readable, until the owner approves the run.

The environment holds:

| Kind | Name | What |
| --- | --- | --- |
| Secret | `GOOGLE_OAUTH_CLIENT_ID`, `GOOGLE_OAUTH_CLIENT_SECRET` | the Google Desktop client (the same values as the Mac release; both or neither) |
| Secret | `MICROSOFT_OAUTH_CLIENT_ID` | the Microsoft Entra application (client) id |
| Variable | `PARTNER_CENTER_PACKAGE_NAME` | Partner Center, Product identity, `Package/Identity/Name` |
| Variable | `PARTNER_CENTER_PUBLISHER` | `Package/Identity/Publisher` (the `CN=...` string) |
| Variable | `PARTNER_CENTER_PUBLISHER_DISPLAY_NAME` | `Package/Properties/PublisherDisplayName` |
| Variable | `PARTNER_CENTER_APP_NAME` | the reserved app name (`Package/Properties/DisplayName`) |

The variables are not secret (they are in every Store package) but they are the owner's to copy from Partner Center: nothing in this repository invents them. While one is missing the build stops and names the placeholder (`__PARTNER_CENTER_*__`). The OAuth values are written into a generated, git-ignored source file for the build and deleted afterwards; they are never printed, logged, put in an artifact name or committed, and `check-release-package.ps1` is run on the result.

## What a run produces

- `store-package-x64` and `store-package-ARM64`: the unsigned `.msix` and a single-architecture `.msixupload` per architecture, plus `build-info.txt` (flavour, version, commit; no ids). 30 days.
- `store-upload`: `TimeTug_<version>_x64_arm64.msixupload` (a `.msixbundle` of both architectures in the zip shape Partner Center takes). 30 days. This is the file the owner uploads.

The packages are unsigned on purpose: the Store signs what it accepts. The Direct (website) flavour is built with the same script but is not part of this workflow; it needs a signing certificate that does not exist yet.

## The dry run (no approval, no secret)

`.github/workflows/windows-release-dryrun.yml` runs on every pull request and by hand (`gh workflow run "Windows release dry run"`). It builds the Store flavour with **fake** OAuth ids, a throwaway certificate created in the job and a throwaway identity (`BinaryCompanion.TimeTug.ReleaseDryRun`), runs `check-release-package.ps1` (including the leak check: the fake ids must be in `TimeTug.Presentation.dll` and in no other file), installs the package, starts it, runs the Windows App Certification Kit on x64 (the ARM64 runner has no kit; see `docs/spikes/2026-10-store-packaging.md`) and bundles the two architectures. The jobs `release-dry-run (x64)`, `release-dry-run (ARM64)` and `release-dry-run (bundle)` can be made required checks by the owner once they have been stable for a while.

## Before the first real run (owner)

1. Reserve the app name in Partner Center and copy the identity values into the four variables above.
2. Create the `release` environment with the owner as reviewer and add the three secrets.
3. Run the workflow by hand with `ref` = `main` for a trial, approve it in the Actions page, download `store-upload`, unzip the `.msixbundle` and run `scripts/ci/check-release-package.ps1 -Package <an .msix from store-package-x64>` on it.
4. Upload the trial `store-upload` file to Partner Center as a package flight (not a submission to everyone). The package is unsigned, so Windows refuses it outside the Store; to try an installable build use the dry run's Dev-signed package instead (`release-dryrun-x64` artifact: trust its `dryrun.cer`, then install).
5. Once a Store flight exists, install it on a clean user profile and complete the first sign-in with real accounts (manual checklist).
