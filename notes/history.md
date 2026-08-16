# Development record — shipped, removed, cancelled

Moved out of `AGENTS.md` 2026-08-16 when that file was slimmed to the AgentsPanelExtension
pattern. This is the project's outcome diary; current truth lives in `AGENTS.md`, the original
build plan in `visualizer-extension-plan.md`, and the rendering evidence in `rendering.md`.

## Feature-complete (2026-08-16, user's call)

The project is done as-is. No open roadmap. The TODO list (`notes/TODO.md`) was closed out and
DELETED 2026-08-16 — everything still open in it (beat detection, stereo mirror, more settings
like bar count / fps / decay / idle behavior, real logo assets, everything from the plan note's
Step 4) was cancelled, not deferred. Remaining work is maintenance only: bug fixes and
host-compat breakage.

## What shipped (all verified live)

- The Tier-2 loopback+FFT dock band with visibility lifecycle, idle throttle, and disposal.
- The ellipsis fix (8-bar dock title budget, measured — see the Gotchas in `AGENTS.md`).
- The CmdPal-rendering-limits investigation (`rendering.md`) and the vertical
  `VisualizerCanvasPage` with peak-hold caps it produced.
- The braille dock band (verdict: both bands stay; pinning IS the dock style picker).
- The settings scaffold + page-style switch.
- The oscilloscope page style (`TryReadWaveform` on the capture + a blocks-pen connected trace;
  braille pen deliberately skipped as unverified in Cascadia Mono).
- The built-in self-test (runtime-synthesized tone-ladder + sweep played via WinRT `MediaPlayer`,
  surfaced in the hub and every band's right-click menu).
- Color: the shared `VuPalette` ramp and the third "blocks + VU dot" dock band — with the
  verdicts that the plain-text canvas CANNOT be colored and `Page.AccentColor` is host-ignored
  (`rendering.md` § "Color channels"). The VU dot itself got a shrug (user's not a fan visually)
  but STAYS as the live proof that a dock band's icon can be swapped at runtime.

## Built, then removed (same-day user verdicts — don't resurrect)

- The horizontal-rows `VisualizerPage` v1 — proved the palette render channel, brought nothing
  over the canvas.
- The scrolling-spectrogram page style — looked "goofy"; legacy persisted `"spectrogram"`
  settings values fall back to bars.

## Cancelled without building

- The battery-aware throttle.
- The stereo mirror mode.
- Everything from the closed TODO list (see above).

## Scope decision (2026-08-16): NOT a media-player app

No SMTC integration — no now-playing metadata, album art, or play/pause/track controls. The
extension visualizes the machine's audio, full stop; media controls are MediaControlsExtension's
turf. Don't re-propose SMTC features (the plan note predates this decision and still mentions
them).

## Release status

Never released as of 2026-08-16. Later the same day the user decided to pursue a release —
tracked in `store-readiness.md` (the `Assets/` PNGs are still placeholders copied from
AgentsPanelExtension and are the biggest blocker; the pipeline follows MarketExtension's
`notes/releasing.md` as proven again by AgentsPanelExtension).
