# Visualizer Extension for Command Palette — Agent Guide

Single source of truth for coding agents (Claude Code, Codex, …) working in this repo — CLAUDE.md
just imports this file. The original build plan (with the spike history and the full gotcha
back-story) lives in `notes/visualizer-extension-plan.md`.

A PowerToys **Command Palette** extension whose whole product is one pinnable **Dock band**: a live
audio spectrum visualizer for whatever the machine is playing. WASAPI **loopback** capture of the
default render endpoint (no microphone, no per-app hooks, no audio dependencies) → hand-rolled FFT
→ 8 log-spaced bands → block glyphs (U+2581..U+2588) mutated into a dock button's title at
~15 fps. .NET 10 / C# / MSIX, self-contained single-file JIT (trim/AOT deliberately OFF;
`AllowUnsafeBlocks` ON for the COM vtable interop). Extension SDK pinned to 0.11.260520004
(`Directory.Packages.props`) — the first with `PlainTextContent`, which the canvas page needs;
minimum host is therefore CmdPal 0.11 (note the PowerToys version requirement in the app
description at release time).

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

Several render surfaces (two dock bands, two pages) over one shared capture; everything else is
scaffold. What the host can and cannot render — and why each surface uses the channel it does —
is measured and documented in `notes/rendering.md`; consult it before inventing a new surface.

- **The render channel (both surfaces): in-place mutation.** Stable `ListItem` instances returned
  from `GetItems()` forever; each tick mutates `.Title` — the host caches view models by
  `IListItem` reference (dock: `DockBandViewModel`; palette list: `ListItemViewModel`, verified to
  handle per-item Title `PropChanged`) and repaints just that element. `ItemsChanged` is NEVER
  raised. The `INotifyItemsChanged` add/remove accessors are the de-facto visible/hidden hooks:
  observer adds are **refcounted** (host may add twice); a `SpectrumSource` lease + `RenderLoop`
  start at the first observer, torn down at the last — a hidden surface costs nothing.
- `Pages/VisualizerDockBand.cs` — the band (from `GetDockBands()`, wrapped in a `CommandItem`).
  ONE item, 8 vertical block glyphs (U+2581..U+2588) — 8 is the measured title-budget max, see
  Gotchas. Click opens `VisualizerCanvasPage`; volume mixer is a `MoreCommands` right-click
  action. THREE bands are registered permanently — block bars (8×8), braille (22×4), and
  blocks + VU dot (a 16-step green→red peak-colored icon, `Rendering/VuPalette.cs` +
  pre-baked in-memory PNG dots as stream-backed IconData in `Rendering/VuDotIcons.cs` — data-URI
  icons don't render, see Gotchas): the user picks the dock style by
  pinning any of them from the Dock's band manager (dock styles are bands, not settings —
  pinning IS the style picker).
- `Pages/VisualizerHubPage.cs` — the top-level palette entry: a static `ListPage` menu (mirrors
  AgentsPanelExtension's UsagePage shape, minus the live state) listing the canvas, the test
  signal, the volume mixer, and the settings form. No lifecycle, no lease — the canvas page
  acquires its own when opened.
- `Commands/PlayTestSignalCommand.cs` + `Audio/TestSignal.cs` — the built-in self-test:
  "Test visualizer" (hub row + every band's right-click menu) synthesizes the tone-ladder + sweep
  at runtime (same math as `tools/generate-spectrum-test.ps1` — keep them in sync) and plays it
  through WinRT `MediaPlayer`; re-invoking restarts rather than layering.
- `Pages/VisualizerCanvasPage.cs` — THE in-palette visualizer:
  a `ContentPage` holding one stable `PlainTextContent`
  (`FontFamily.Monospace` — host-guaranteed Cascadia Mono/Consolas), drawn as a 2-D character
  canvas with a static per-style footer (frequency axis; blank for the scope) and two fill
  styles read pull-style from settings on every tick: **vertical bars** — 30 bars × 20 rows
  (sized to fill the default 800×480 palette window; lower-partial blocks U+2581..U+2588 =
  160 vertical steps, spaces are grid-safe in monospace)
  with peak-hold caps (U+2594, hold-then-fall) — and **oscilloscope** — the
  newest ~21 ms of raw waveform (`TryReadWaveform`) as a blocks-pen connected trace. Frames mutate `_content.Text` only (push-only-on-change);
  `ItemsChanged` is never raised — on content pages it rebuilds the whole content control.
- `Settings/VisualizerSettingsManager.cs` — JsonSettingsManager singleton (mirrors
  AgentsPanelExtension's UsageSettingsManager; persists to `visualizer.settings.json`), surfaced
  via `Settings = ...Instance.Settings` in the provider. One choice today: page style. Values are
  read pull-style per tick — changes apply next frame, no restart. Toolkit quirk: a setting's
  visible label is `Description`, not `Label`.
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
- `VisualizerCommandsProvider.cs` — wires source → pages → bands; one top-level `CommandItem`
  opening the hub + the two dock bands. Provider `Dispose` disposes bands, pages, then source.
- Deliberately **NO Rx** anywhere (the visualizer avoids the whole
  Rx-gate↔STA deadlock class — keep it that way).

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
`-BandCount` in sync with `BarCount` if the bar count ever changes; `-Play` plays it too). The
same signal also ships in-app as the "Test visualizer" command (`Audio/TestSignal.cs` —
keep its constants in sync with the ps1), so the user can trigger it themselves too.
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
- ⚠️ **`data:` URIs do NOT work as icons — ANY of them, PNG included** (corrected 2026-08-16
  after the VU dot shipped blank: the host's IconPathConverter feeds the URI to WinUI 3
  `BitmapImage.UriSource`, which doesn't support the `data:` scheme — it fails *asynchronously*,
  so no fallback fires and the icon renders as nothing). File paths ending in `.svg` render as
  vector, http URLs work, emoji give color; for generated in-memory images use **stream-backed
  `IconData` (`IRandomAccessStreamReference`)** with the icon STRING left empty (a non-empty
  string wins over the stream in the host loader) — that's the channel `Rendering/VuDotIcons.cs`
  uses. Evidence in `notes/rendering.md` § "Color channels".
- ⚠️ **The dock title budget is exactly 8 block glyphs** (measured 2026-08-16, fixing the
  trailing-"…" bug): the host's TitleText is 12px "Segoe UI", `MaxWidth=100`, CharacterEllipsis;
  Segoe UI has no Block Elements so DirectWrite falls back to **Segoe UI Symbol** where
  U+2581..U+2588 advance 11.256 px — 8 bars = 90 px fits, 9 = 101.3 px trims to "…". Don't raise
  `BarCount` above 8 without changing glyph set. The old "width jitter" worry is RESOLVED for
  blocks: all eight ramp glyphs have identical advances, the button cannot breathe. ⚠️ Braille
  (U+28xx, the braille band's glyph set) is NOT uniform: blank U+2800 is 7.81 px vs 9.04 px for
  every dotted cell — never emit the blank cell or the button breathes (the renderer keeps every
  column's bottom dot lit as the floor — see `Rendering/BrailleBarsRenderer.cs`).

## Status: FEATURE-COMPLETE (2026-08-16, user's call)

**The project is done as-is. No open roadmap.** The TODO list (`notes/TODO.md`) was closed out
and DELETED 2026-08-16 — everything still open in it (beat detection, stereo mirror, more
settings like bar count / fps / decay / idle behavior, real logo assets, everything from the plan
note's Step 4) was cancelled, not deferred. Don't propose new features, don't re-propose
cancelled or removed ones, and don't resurrect anything below. Remaining work is maintenance
only: bug fixes and host-compat breakage.

What shipped (all verified live): the Tier-2 loopback+FFT dock band with visibility lifecycle,
idle throttle, and disposal; the ellipsis fix (8-bar budget, measured); the
CmdPal-rendering-limits investigation (`notes/rendering.md`) and the vertical
`VisualizerCanvasPage` with peak-hold caps it produced; the braille dock band (verdict: both
bands stay, pinning IS the dock style picker); the settings scaffold + page-style switch; the
oscilloscope page style (`TryReadWaveform` on the capture + a blocks-pen connected trace, braille
pen deliberately skipped as unverified in Cascadia Mono); the built-in self-test
(runtime-synthesized tone-ladder + sweep played via WinRT `MediaPlayer`, surfaced in the hub and
every band's right-click menu); and color — the shared `VuPalette` ramp and the third
"blocks + VU dot" dock band — with the verdicts that the plain-text canvas CANNOT be colored and
`Page.AccentColor` is host-ignored (`notes/rendering.md` § "Color channels").

Built-then-removed (same-day user verdicts — don't resurrect): the horizontal-rows
`VisualizerPage` v1 (proved the palette render channel, brought nothing over the canvas) and the
scrolling-spectrogram page style (looked "goofy"; legacy persisted `"spectrogram"` settings
values fall back to bars). Cancelled without building: the battery-aware throttle and the stereo
mirror mode. The VU dot itself got a shrug (user's not a fan visually) but STAYS as the live
proof that a dock band's icon can be swapped at runtime.

Never released, and no release is planned. If that ever changes: the `Assets/` PNGs are still
placeholders copied from AgentsPanelExtension and must be replaced first, and the release flow
lives in MarketExtension's `notes/releasing.md`.

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
