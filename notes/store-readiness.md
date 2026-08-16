# Store readiness checklist

The ordered path from feature-complete extension to Microsoft Store — plus GitHub Releases and
WinGet — following the pipeline proven twice by the reference repos: MarketExtension (Store ID
9MV7M639533Q) and AgentsPanelExtension (Store ID 9N8KK0W45HG8). Source playbooks: AgentsPanel's
`notes/store-readiness.md` (this file's template, with per-item history), MarketExtension's
`notes/releasing.md` + `notes/winget-publishing.md` + `notes/store-listing.md`.

Tags: **[user]** = only the developer can do it (Partner Center, art, certs, secrets, Rider).
**[agent]** = a coding agent can do it end-to-end (verified by `dotnet build … -p:Platform=x64`).
**[either]** = drafting work either can start.

Feature scope stays FROZEN (see AGENTS.md) — nothing here adds features; this is packaging,
legal, listing, and pipeline work only. The built-in "Test visualizer" command is the demo story:
the app is self-demonstrating with zero accounts and zero credentials, which makes certification
notes far simpler than the reference repos'.

Already verified done (audited 2026-08-16): fresh COM GUID
(`50051ad1-efd9-485e-bdd9-8f09ae9ea05a`, matches `CreateInstance ClassId`); real Partner Center
publisher (`CN=3D57AA92-97A9-42D2-8CB0-4207D9145514`) in manifest + csproj (same proven CN as the
reference repos); version `0.1.0.0` in sync across csproj / `Package.appxmanifest` /
`app.manifest`, with csproj `<Version>` mirroring `<AppxPackageVersion>` (no version literals in
code); `<Company>/<Product>/<Copyright>` set; minimal capabilities (`runFullTrust` only — **no
microphone capability**, loopback needs none); manifest description written; MIT LICENSE;
`create-signing-cert.ps1` present (cert NOT yet generated); `build-check.yml` present; GitHub
remote configured. The manifest's `AppExtension Id="ID"` is fine — AgentsPanel shipped through
certification with the same value.

---

## A. Certification blockers — identity & branding

- [ ] **A1 [user] Real app logo + MSIX asset regen.** The entire `Assets/` folder is byte-copied
  AgentsPanelExtension placeholders — a blocker (two Store apps must not share an icon). Flow
  proven on AgentsPanel: create/park a 1280px master at `listing/visualizer_logo_1280.png` → VS
  Manifest Designer → Visual Assets → generate from the square source with **"Apply recommended
  padding" OFF** (already-padded art double-insets) → agent downsizes the in-package base copy to
  256px if oversized.
- [ ] **A2 [agent] Wire the real logo in-app.** After A1, verify every in-CmdPal icon site
  (extension entry, hub page, canvas page, band `CommandItem`s) references the new logo via
  `IconHelpers.FromRelativePath` and none still shows placeholder art. MSIX tile/Store assets and
  in-CmdPal icons are two separate systems (MarketExtension `notes/releasing.md` § Icons).
- [ ] **A3 [user] Partner Center name reservation.** Reserve "Visualizer for Command Palette";
  confirm the assigned `Identity Name` matches `CostaFotiadis.VisualizerforCommandPalette` in
  `Package.appxmanifest` + csproj `AppxPackageIdentityName`. Publisher CN already matches the
  account.

## B. Certification blockers — content & legal

- [ ] **B4 [agent] Hosted privacy policy + terms.** Partner Center requires a privacy-policy URL.
  Create `docs/{index,privacy,terms}.html` + `style.css` modeled on AgentsPanel's `docs/`,
  adapted to this app's much simpler story: audio is captured via WASAPI loopback of the default
  output device, processed entirely in memory, never recorded, stored, or transmitted; **no
  microphone access**; no network access at all; no accounts, no credentials, zero telemetry; the
  only file written is the local settings JSON. [user] then enables GitHub Pages (deploy workflow
  is E13).
- [ ] **B5 [agent] README to release shape.** Restructure to the reference repos' README shape:
  badges, dock-strip hero screenshot, **non-affiliation note** (not affiliated with or endorsed
  by Microsoft or the PowerToys team), Installation section (requires **PowerToys 0.11+** — the
  SDK pin means CmdPal ≥0.11 is the minimum host), per-feature screenshot sections, FAQ, MIT
  footer. Stub the Store badge + winget one-liner in an HTML comment to uncomment after approval.
  Drop the internal "maintenance only, see AGENTS.md" line — that's contributor-facing, keep it
  out of the storefront-facing README (or move it to a Contributing note).
- [ ] **B6 [either] Store listing copy → `notes/store-listing.md`.** Short + full description
  modeled on MarketExtension's `notes/store-listing.md`. Must include: the PowerToys 0.11+
  requirement (AGENTS.md flags this for the app description at release time — also append it to
  the `Package.appxmanifest` description); the non-affiliation paragraph; honest claims only —
  "visualizes what the machine is playing" (loopback of the default render endpoint; no
  microphone). Honest-caveat candidates: exclusive-mode/DRM audio streams don't appear in
  loopback, and per-device capture follows the *default* output device.
- [ ] **B7 [either] Certification test notes → `notes/certification-notes.md`.** A reviewer has
  no music playing. Notes must say: install PowerToys ≥0.11, open Command Palette, find
  "Visualizer", pin a band via the Dock's band manager, then run the built-in **"Test
  visualizer"** command (hub row or any band's right-click menu) — the app plays its own test
  signal and lights up with no accounts or content needed. Include the standard `runFullTrust`
  justification (CmdPal COM extension-host requirement).

## C. First-run / UX sweep

- [ ] **C8 [agent] Silent-machine first run.** Verify (by code reading — behavior ships already)
  what a fresh user sees with no audio playing: bands show the idle floor glyphs, canvas shows an
  empty grid + footer, nothing looks broken. If anything reads as "broken" rather than "quiet",
  surface it to the user before changing behavior (scope is frozen).
- [ ] **C9 [agent] String sweep.** Confirm every user-facing string is in
  `Properties/Resources.resx` with `Resources.Designer.cs` in lock-step (convention says yes —
  verify before submission; certification screenshots freeze wording).

## D. Versioning & build hygiene

- [ ] **D10 [agent] `notes/releasing.md` for this repo.** Copy AgentsPanel's shape: bump table —
  `VisualizerExtension/VisualizerExtension.csproj` `<AppxPackageVersion>` (`<Version>` follows) /
  `Package.appxmanifest` `Identity Version=` / `app.manifest` `assemblyIdentity version=` — plus
  the PowerShell bump one-liner and the release-workflow command. All three sites verified in
  sync at `0.1.0.0` today.
- [ ] **D11 [agent] Repo hygiene check.** `git ls-files` for stray `.idea/` tracking; confirm sln
  platform configs are intentional; keep the preview `WindowsSdkPackageVersion` pin (both
  reference repos shipped on it). The ARM64-first alphabetical trap remains — `-p:Platform=x64`
  stays mandatory in every build.
- [ ] **D12 [user] Pick the ship version.** Reference repos shipped at `1.0.0.0`; bump all three
  sites in one dedicated commit (message = the version) as ship-order step 1.

## E. Release infrastructure (copy from the reference repos, rename)

- [ ] **E13 [agent] Workflows.** Copy from AgentsPanel's `.github/workflows/` and rename the env
  vars (`DISPLAY_NAME`/`EXTENSION_NAME`/`FOLDER_NAME`): `release-msix.yml` (x64+ARM64 → makeappx
  bundle → signtool → GitHub Release) and `deploy-pages.yml` (for B4). `build-check.yml` already
  exists. Later, post-Store-approval: `update-winget.yml` from MarketExtension for repeat WinGet
  submissions.
- [ ] **E14 [user] Signing cert + secrets.** Run `VisualizerExtension/create-signing-cert.ps1`
  (CN already matches the manifest), set `SIGNING_CERT_PFX` + `SIGNING_CERT_PASSWORD` repo
  secrets (none set as of the audit), back up the pfx + password (git-ignored). Cert expiry ~1
  year — note the re-run date. Later, for WinGet: `WINGET_TOKEN` (classic PAT, public_repo).
- [ ] **E15 [user] Screenshots → `listing/`.** Framed-on-gradient like AgentsPanel's `listing/`:
  the pinned dock band strip (hero, 3:1), the Dock band manager showing all three styles, the
  canvas page in bars style, the oscilloscope style, the hub page (16:9 Store-ready). Keep raw
  captures as `base_screenshot_*`. Store ceiling is 3840×2160 — downscale/pad the wide strip.
  Tip: capture while `tools/spectrum-test.wav` plays so the bars are lit and repeatable.

## F. Store submission & post-Store

- [ ] **F16 [user] Submit to Partner Center.** Run `release-msix.yml` → download the
  `.msixbundle` from the GitHub Release → Partner Center → new submission → upload → listing copy
  (B6) + screenshots (E15) + privacy URL (B4) + certification notes (B7) → submit. The Store
  re-signs the bundle.
- [ ] **F17 [user, then agent] WinGet.** After Store approval only. Package ID
  `CostaFotiadis.VisualizerForCommandPalette` (27-char second segment — fits the 32/segment
  limit). Must point at the **Store-signed** bundle downloaded from Partner Center and
  re-uploaded to the GitHub Release — the self-signed CI bundle fails WinGet's sandbox
  validation. Locale yaml `Tags:` must include `windows-commandpalette-extension` (enables
  CmdPal's one-click install). First submission manual (`wingetcreate new` → hand-edit →
  `winget validate` → submit, from the `C:\Users\jarla\code\winget-pkgs` fork); afterwards copy
  `update-winget.yml` **[agent]** for repeat submissions. Then uncomment the README Store badge +
  winget snippet (B5).

## G. Recommended, not blocking

- [ ] **G18 [either] FAQ caveats.** Consider README/listing FAQ entries for the honest caveats:
  exclusive-mode/DRM audio doesn't show up in loopback; the visualizer follows the default output
  device; silence throttles the refresh (by design).
- [ ] **G19 [agent] Dead-asset check.** `Assets/LockScreenLogo.scale-200.png` is unreferenced
  (and, inherited from the reference repos, not even a valid PNG). Precedent: both reference
  repos shipped it through certification inert — keep unless the user says otherwise.

---

## Ship order (the twice-proven pipeline)

1. Bump versions (D10 table) — one dedicated commit, message = the version. Never amend.
2. **[user]** `gh workflow run release-msix.yml --ref main -f release_notes="…"` → self-signed
   msixbundle + GitHub Release. (Agents never run workflows — AGENTS.md.)
3. Partner Center: upload the bundle, attach listing copy / screenshots / privacy URL /
   certification notes, submit (Store re-signs it).
4. Download the Store-signed bundle from Partner Center → re-upload to the same GitHub Release.
5. WinGet manifests pointing at the Store-signed bundle (manual first time, `update-winget.yml`
   after).
