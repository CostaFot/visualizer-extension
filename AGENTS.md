# Visualizer Extension for Command Palette — Agent Guide

Single source of truth for coding agents (Claude Code, Codex, …) working in this repo — CLAUDE.md
just imports this file. The original build plan (with the spike history and the full gotcha
back-story) lives in `notes/visualizer-extension-plan.md`.

A PowerToys **Command Palette** extension whose whole product is one pinnable **Dock band**: a live
audio spectrum visualizer for whatever the machine is playing. WASAPI **loopback** capture of the
default render endpoint (no microphone, no per-app hooks, no audio dependencies) → hand-rolled FFT
→ 8 log-spaced bands → block glyphs (U+2581..U+2588) mutated into a dock button's title at
~15 fps. .NET 9 / C# / MSIX, self-contained single-file JIT (trim/AOT deliberately OFF;
`AllowUnsafeBlocks` ON for the COM vtable interop).

## Reference projects — use them A LOT

Scaffolded from **AgentsPanelExtension** (`C:\Users\jarla\code\agents-panel-extension`, same
author) — structure, conventions, and csproj/manifest shape all mirror it; when a pattern question
comes up, look there first. Its ancestor **MarketExtension**
(`C:\Users\jarla\code\MarketExtension`) holds the CmdPal knowledge base
(`notes/cmdpal-toolkit.md`, `notes/releasing.md`) and the pristine AdbExtension blank-extension
files in `reference/`. The Tier-2 capture/FFT code was proven live in
**MediaControlsExtension PR #1** (github.com/CostaFot/MediaControlsExtension/pull/1). Host dock
internals: `C:\Users\jarla\code\PowerToys\src\modules\cmdpal\Microsoft.CmdPal.UI\Dock\` plus
`Microsoft.CmdPal.UI.ViewModels\Dock\DockBandViewModel.cs`.

## Architecture in one screen

Two render surfaces over one shared capture; everything else is scaffold.

- **The render channel (both surfaces): in-place mutation.** Stable `ListItem` instances returned
  from `GetItems()` forever; each tick mutates `.Title` — the host caches view models by
  `IListItem` reference (dock: `DockBandViewModel`; palette list: `ListItemViewModel`, verified to
  handle per-item Title `PropChanged`) and repaints just that element. `ItemsChanged` is NEVER
  raised. The `INotifyItemsChanged` add/remove accessors are the de-facto visible/hidden hooks:
  observer adds are **refcounted** (host may add twice); a `SpectrumSource` lease + `RenderLoop`
  start at the first observer, torn down at the last — a hidden surface costs nothing.
- `Pages/VisualizerDockBand.cs` — the band (from `GetDockBands()`, wrapped in a `CommandItem`).
  ONE item, 8 vertical block glyphs (U+2581..U+2588) — 8 is the measured title-budget max, see
  Gotchas. Click opens `VisualizerPage`; volume mixer is a `MoreCommands` right-click action.
- `Pages/VisualizerPage.cs` — the big in-palette visualizer and the top-level palette entry: one
  row per band, titles are horizontal bars (full U+2588 blocks + one left-partial U+2589..U+258F
  tip; partial advances are ink-proportional in Segoe UI Symbol so bar length is continuous —
  32 cells × 8 = 256 steps), subtitles are the band's frequency range.
- `Helpers/RenderLoop.cs` — the shared pump: ~15 fps timer, **idle throttle** (2 Hz after ~3 s of
  silence, still sampling → snaps back on audio; a pinned band must not burn CPU all day),
  every tick exception-wrapped (a throw on a pool thread kills the process), and a draining
  `Dispose` (waits out the in-flight tick) so owners can tear down right after it returns — never
  call it from inside the tick.
- `Audio/SpectrumSource.cs` — refcounted owner of the ONE `SpectrumCapture` shared by both
  surfaces (the dock is always visible, so band + page live together is the normal case).
  `TryReadBands` is serialized under its gate (the capture reuses FFT scratch buffers).
- `Audio/SpectrumCapture.cs` — the input. Dependency-free WASAPI loopback: raw COM vtable calls
  via `delegate* unmanaged[Stdcall]` + slot indices (no NAudio, no ComImport RCWs). Own background
  thread fills a 2048-sample ring; `TryReadBands` does Hann-window → radix-2 FFT → caller-sized
  log-spaced bands (40 Hz–16 kHz) → treble tilt → slow auto-gain → sqrt loudness. Loopback
  delivers NO packets during silence — "no packet in 250 ms" IS the silence signal (`TryReadBands`
  returns false), never something to block on. On `COMException` (device change) the loop tears
  down and rebinds after 500 ms. Constructor starts the thread, `Dispose` stops it — only
  `SpectrumSource` creates/disposes it.
- `VisualizerCommandsProvider.cs` — wires source → page → band; one top-level `CommandItem`
  opening the page + one dock band. Provider `Dispose` disposes band, page, then source.
- Deliberately **NO Rx** anywhere (the visualizer avoids the whole
  Rx-gate↔STA deadlock class — keep it that way) and no settings yet (bar count / fps / idle
  behavior are a planned CmdPal settings page, see the plan note).

## Build & Deploy

`dotnet build VisualizerExtension.sln -p:Platform=x64` — ⚠️ without the platform flag MSBuild picks
**ARM64** (alphabetically first in the sln) on this x64 machine and the package won't deploy.

⚠️ **Agents: BUILD ONLY — never deploy/register the package.** The developer runs and deploys the
extension from **Rider**; deployment is their job, not the agent's. Verification stops at a clean
`dotnet build` — after that, tell the user "ready to deploy from Rider". Do NOT run
`Add-AppxPackage` (any form), and do NOT `Stop-Process` the extension to free a locked package — a
locked package means a live deployment that isn't yours to replace. (AgentsPanelExtension's
AGENTS.md records the incident that made this rule: a forced re-register left three duplicate
palette entries and required a full uninstall to recover.)

## Testing the visualizer (the "play the test sound" routine)

When the user asks to **test** the visualizer (they have it deployed and pinned), play
`tools/spectrum-test.wav` through the default output — e.g.
`(New-Object System.Media.SoundPlayer('tools\spectrum-test.wav')).PlaySync()` — and tell them
what to expect: **one 1.2 s tone per band at each band's geometric center, lighting one bar at a
time left → right, then an 8 s log sweep 40 Hz → 16 kHz gliding a single peak across all bars.**
Healthy caveats: neighboring bars glow a little (Hann leakage + treble tilt) and the first tone
can overshoot until auto-gain settles. Failure signs: trailing "…" (title over budget), button
width changing (glyph-width jitter), bars out of order, or nothing moving during audible sound.
The wav is generated by `tools/generate-spectrum-test.ps1` (parameterized — regenerate with
`-BandCount` in sync with `BarCount` if the bar count ever changes; `-Play` plays it too).
Playing audio is fine and expected; deploying the extension remains the user's job (below).

## Project conventions

- New commands → `Commands/`, extend `InvokableCommand`. New pages → `Pages/`. Audio/DSP →
  `Audio/`. Flat `VisualizerExtension` namespace everywhere (folder ≠ namespace, same as the
  reference repos).
- No hardcoded user-facing strings — `Properties/Resources.resx` + hand-maintained
  `Resources.Designer.cs` (dotnet build does NOT regen it; keep them in lock-step or regen from VS).
- Logging: `Log.Info/Warn/Error(tag, msg)`, all `[Conditional("DEBUG")]` — Release ships silent.
- **Fail loud** on genuinely-wrong states; degrade-and-log for expected external failures (device
  changes, format surprises).
- AOT/trim intentionally OFF — but unlike the reference repos, `AllowUnsafeBlocks` is ON solely
  for `Audio/SpectrumCapture.cs`; keep unsafe code confined there.

## Gotchas (inherited from the spike + reference repos — all verified the hard way)

- ⚠️ **The Write tool mangles glyph/backslash-escape characters.** Always write Segoe glyphs and
  block characters as ASCII `\uXXXX` escapes in source and **byte-check after editing**. During
  the original spike, perl (`\u` = uppercase) and GNU sed (`\u` also special) BOTH ate the
  backslash during repair.
- ⚠️ **Never rebuild the item array per frame.** The host caches by `IListItem` reference — a new
  array/new items per tick destroys the 15-fps channel. `GetItems()` returns the same instances
  forever.
- ⚠️ **Page-activation:** `GetItems()` runs before `ItemsChanged` is subscribed — first paint must
  come from construction-time data (the stable-item pattern sidesteps this entirely).
- ⚠️ **Never raise `ItemsChanged` synchronously inside the host's subscription**, and no Rx on any
  delivery path (STA/gate deadlock class — see AgentsPanelExtension's threading notes).
- ⚠️ **Dock bands require a non-empty, unique command `Id`** or the band silently vanishes / gets
  conflated. This band's: `com.costafotiadis.visualizer.dock.spectrum`.
- ⚠️ **Timer callbacks and the capture thread must never throw** — an unhandled exception on
  either kills the extension process. `RenderFrame` is wrapped; the capture loop catches
  `COMException`/`InvalidOperationException` and rebinds.
- ⚠️ **`data:image/svg+xml` URIs do NOT work as icons** (the host sniffs the URI *extension* for
  `.svg`); file paths ending in `.svg` render as vector, PNG data URIs and http URLs work, emoji
  give color.
- ⚠️ **The dock title budget is exactly 8 block glyphs** (measured 2026-08-16, fixing the
  trailing-"…" bug): the host's TitleText is 12px "Segoe UI", `MaxWidth=100`, CharacterEllipsis;
  Segoe UI has no Block Elements so DirectWrite falls back to **Segoe UI Symbol** where
  U+2581..U+2588 advance 11.256 px — 8 bars = 90 px fits, 9 = 101.3 px trims to "…". Don't raise
  `BarCount` above 8 without changing glyph set. The old "width jitter" worry is RESOLVED for
  blocks: all eight ramp glyphs have identical advances, the button cannot breathe. ⚠️ Braille
  (U+28xx, the planned higher-res mode) is NOT uniform: blank U+2800 is 7.81 px vs 9.04 px for
  every dotted cell — never emit the blank cell or the button breathes (see `notes/TODO.md` #4).

## Status / roadmap

Shipped so far: Tier-2 loopback+FFT dock band with visibility lifecycle, idle throttle, and
disposal; the ellipsis fix (8-bar budget, measured); the in-palette `VisualizerPage` v1
(horizontal bars — proves the palette render channel, looks silly, superseded by TODO #12).
**Next up: `notes/TODO.md`** — headline items: a proper VERTICAL page visualizer after a
CmdPal-rendering-limits investigation (#12), user-selectable visualizer styles via settings (#13),
color exploration (#3). Also still open, from
`notes/visualizer-extension-plan.md` Step 4: settings page (bar count, target fps, decay, idle
behavior), Tier-1 peak-meter low-power mode as a settings choice, real PNG logo assets (current
`Assets/` PNGs are placeholders copied from AgentsPanelExtension — replace before any release),
`notes/releasing.md` + release workflow when it's time to ship.

**Scope decision (2026-08-16): this is NOT a media-player app.** No SMTC integration — no
now-playing metadata, album art, or play/pause/track controls. The extension visualizes the
machine's audio, full stop; media controls are MediaControlsExtension's turf. Don't re-propose
SMTC features (the plan note predates this decision and still mentions them).

## Git

- Never amend commits (`git commit --amend`) — always create a new commit, unless explicitly asked
  to amend in the moment.
- **Never commit on your own** — leave changes in the working tree and let the user review; commit
  only when explicitly asked in that moment (a general "commit when done" in a plan does not carry
  over to later work).
- **Never run GitHub workflows** (`gh workflow run`, `gh run rerun`, or anything else that triggers
  CI/releases) — those are the user's to fire, only on an explicit ask in the moment.
