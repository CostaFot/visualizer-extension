# Releasing

Modeled on AgentsPanelExtension's `notes/releasing.md` (itself from MarketExtension's); see
`notes/store-readiness.md` for the full release-prep checklist. Current version: **1.0.0.0**.

## Version bump — all sites together, one dedicated commit

Three sites, no version literals in code (csproj `<Version>` mirrors `<AppxPackageVersion>`
automatically):

| File | Field |
|---|---|
| `VisualizerExtension/VisualizerExtension.csproj` | `<AppxPackageVersion>` (`<Version>` follows it automatically) |
| `VisualizerExtension/Package.appxmanifest` | `Identity Version=` |
| `VisualizerExtension/app.manifest` | `assemblyIdentity version=` |

MSIX wants 4-part `Major.Minor.Build.Revision`. Bump one-liner:

```powershell
$files = @("VisualizerExtension/VisualizerExtension.csproj",
           "VisualizerExtension/Package.appxmanifest",
           "VisualizerExtension/app.manifest")
$files | ForEach-Object { (Get-Content $_) -replace '0\.1\.0\.0','<NEW>' | Set-Content $_ }
```

Also update the "Current version" line at the top of this file. Commit message = the version
(e.g. `1.0.0.0`). Never amend.

## Signing cert

The repo reuses AgentsPanelExtension's self-signed cert (same Publisher CN,
`CN=3D57AA92-97A9-42D2-8CB0-4207D9145514`) — local copy at `VisualizerExtension/signing.pfx`
(git-ignored), secrets `SIGNING_CERT_PFX` + `SIGNING_CERT_PASSWORD` set on this repo. **Valid
until 2027-08-14** — regenerate before then (`create-signing-cert.ps1`, or reuse whatever
AgentsPanel regenerates to) and refresh the secrets in BOTH repos.

## Release (once the workflows land — E13/E14)

⚠️ **User-only step** — agents never run `gh workflow run` (or commit) on their own; see AGENTS.md.

```
gh workflow run release-msix.yml --ref main -f release_notes="…"
```

Reads the version from the csproj, builds x64 + ARM64, bundles with makeappx, signs with the
self-signed cert (secrets `SIGNING_CERT_PFX` + `SIGNING_CERT_PASSWORD`), creates the GitHub
Release tagged `v<version>` (tag is created by the workflow — don't tag by hand).

## Partner Center

Download the `.msixbundle` from the GitHub Release → Partner Center → app → new submission →
Packages → upload → update description/notes → submit. The Store re-signs the package. For WinGet,
download the **Store-signed** bundle back from Partner Center and re-upload it to the same GitHub
Release (self-signed fails WinGet's sandbox validation) — then run `update-winget.yml`.

## Local build caveats

- Always `dotnet build VisualizerExtension.sln -p:Platform=x64` — without the flag MSBuild picks
  ARM64 (alphabetically first in the sln) and the package won't deploy on this x64 machine.
- Agents build only; deployment happens from Rider (see AGENTS.md — never `Add-AppxPackage`).
