# TODO — to be picked up later

Recorded 2026-08-16 after the first live deploy of the scaffold. Items 1–3 are the original
annoyances; 4–10 came from the same day's brainstorm. Scope guard: this is NOT a media-player app
(see AGENTS.md) — nothing here may grow SMTC/now-playing/track-control features.

## 1. ~~Visual bug: three dots ("…") at the end of the dock visualizer~~ FIXED 2026-08-16

It was the host's `CharacterEllipsis` trimming, exactly as suspected. Root cause, measured
(GlyphTypeface advances, verified against `DockItemControl.xaml`): TitleText is 12px "Segoe UI"
with `MaxWidth=100`; Segoe UI contains no Block Elements, DirectWrite falls back to **Segoe UI
Symbol**, where U+2581..U+2588 all advance **11.256 px** at 12 px. 10 bars = 112.6 px → trimmed;
9 = 101.3 px → still trimmed; **8 = 90.0 px → fits**. Fix: `BarCount` 10 → 8.

Byproduct: the plan's open "width jitter" check is resolved — all eight ramp glyphs have identical
advances in Segoe UI Symbol, so the block renderer cannot make the button breathe. (Braille can —
see item 4.)

## 2. ~~Replace "open volume mixer" click with our own visualizer page~~ DONE 2026-08-16

Shipped as `Pages/VisualizerPage.cs`: a `ListPage` with one stable row per band; row titles are
smooth horizontal bars (full U+2588 blocks + one left-partial U+2589..U+258F tip — measured:
partial-block advances in Segoe UI Symbol are ink-proportional, so bar length is continuous;
32 cells × 8 eighths = 256 steps), subtitles are frequency-range labels. Dock click → the page;
volume mixer demoted to the dock item's right-click `MoreCommands`; top-level palette entry also
opens the page. Capture is now shared between band and page via refcounted
`Audio/SpectrumSource.cs` (both visible at once is the normal case — the dock is always up), and
the timer + idle-throttle + never-throw + draining-dispose logic was extracted to
`Helpers/RenderLoop.cs`.

De-risked before building: the palette's `ListItemViewModel` handles per-item Title `PropChanged`
(host source, `ListItemViewModel.cs` case nameof(model.Title)) — same channel as the dock. Still
to verify live: actual page refresh smoothness at 15 fps (mechanism exists; rate unproven).
The `ContentPage`/Markdown 2-D spectrum idea stays open under item 9.

## 3. Color customization (e.g. green → red toward peak)

Explore color, with eyes open about host constraints (all verified in the plan/spike notes):
- **Dock button titles have NO text color** — no rich text, no per-char color in the title run.
  Color in the dock can only come from the item **icon** (emoji give color; PNG data URIs work;
  SVG data URIs do NOT) — e.g. a small colored dot/emoji icon that shifts green→amber→red with the
  current peak while the bars stay monochrome.
- **In our own palette page** (item 2) there's more room: list item **tags** have color support in
  the toolkit, and a ContentPage could color via Markdown-supported constructs. A green→red
  gradient across bands, or whole-frame color by loudness, becomes feasible there.
- If we generate tiny PNG data-URI icons on the fly for color, cache the generated URIs per color
  step (don't allocate per frame) and keep push-only-on-change semantics.
- Color choice (theme, gradient on/off) is a natural future settings-page entry alongside bar
  count / fps / decay.

## 4. Braille rendering spike (may also fix item 1)

U+2800–U+28FF braille patterns give a 2x4 dot matrix per character — 2 bars per glyph at 4 levels
each (it's the trick TUI tools like btop use). Measured 2026-08-16 while fixing item 1, in Segoe
UI Symbol (the DirectWrite fallback that serves both blocks and braille for the dock's 12px
"Segoe UI" title): dotted braille cells advance **9.041 px** → 11 cells fit MaxWidth=100 (99.5 px)
→ **up to 22 bars** vs the blocks' 8, at the cost of only 4 vertical levels vs 8.

⚠️ Measured trap: **blank U+2800 advances 7.811 px — narrower than every dotted cell (9.041 px)**,
so a renderer that emits the blank cell makes the button breathe horizontally. Always keep at
least one dot lit per cell (a bottom-row dot as the baseline, like the current U+2581 floor).
Spike: render the same spectrum both ways, compare readability at dock size. Renderer stays a
pure function of `_levels[]`, so this is a candidate for a "render style" setting later.

## 5. Stereo mirror mode

Classic Winamp look: bass in the center, L/R channels spreading outward. Needs per-channel FFT
instead of the current mono downmix in `AppendSamples` (keep two rings, or interleave and split at
read time). Doubles FFT cost — still trivial at 2048 samples. Natural "mode" entry alongside
item 4's render style.

## 6. Peak-hold caps

The little marker that lingers at the recent per-bar maximum and decays slowly, like hardware EQs.
With only 8 vertical levels per glyph in the dock it's subtle (a cap often coincides with the bar
top); on the palette page's bigger canvas (item 2) it's where this really pays off. Implementation
is one extra `float[] _peaks` with its own slower decay; rendering decides how to show it.

## 7. Beat detection driving pulse/color

Onset detection on bass-band energy (running average + threshold, ~20 lines) → a beat signal the
renderers can use: flash/pulse the item icon, or key item 3's color shifts to beats instead of raw
peaks. Keep it strictly derived from the already-captured spectrum — no extra audio work on the
render tick.

## 8. Battery-aware throttle

On DC power, cap the active rate (e.g. 15 → 10 fps) via `Windows.System.Power.PowerManager`
(WinRT, no extra deps; subscribe to `PowerSupplyStatusChanged`, read pull-style on the tick).
Fits the "a pinned band must not burn CPU all day" philosophy; combine with the existing idle
throttle rather than adding a second timer-juggling path.

## 9. Scrolling spectrogram view (extends item 2)

For the palette page: a waterfall — rows = time, columns = bands, intensity via the block-glyph
ramp (or braille, item 4) in a monospace code block. Depends on the same unproven
page-refresh-rate question flagged in item 2; spike the refresh ceiling first, the spectrogram is
just what to draw once the channel is proven.

## 10. Real logo: the bars ARE the brand

When replacing the placeholder `Assets/` PNGs (AgentsPanelExtension leftovers — must happen before
any release): a mark that is literally a green→red spectrum of bars doubles as the brand, the
Store tile, and the eventual settings-page icon. One SVG master → export the full
scale/targetsize PNG matrix the manifest expects.

## 11. Built-in self-test using tools/spectrum-test.wav

The test signal (born 2026-08-16 while verifying the item-1 fix) could ship in the app: a
"Test visualizer" command (top-level or right-click MoreCommands) that plays the tone-ladder +
sweep through the default output so users can see every bar respond without hunting for music.
Needs the wav packaged as Content (csproj currently only includes Assets/**/*.png) or synthesized
at runtime (the generator math is ~40 lines, see tools/generate-spectrum-test.ps1). Pure
visualizer scope — it plays a local file, no media integration.

## 12. Proper VERTICAL visualizer in the page (v1 horizontal rows prove the channel, look silly)

The shipped VisualizerPage (item 2) proved the palette list repaints per-row title mutations fast
enough — but 8 horizontal bars-per-row is not a visualizer, it's a bar chart. The real thing wants
classic VERTICAL bars rising from a baseline. Requires an investigation pass over what CmdPal can
actually render and how far each channel can be pushed — the host source is local
(C:\Users\jarla\code\PowerToys\src\modules\cmdpal\), so answer from source, not guesswork:

- **Stacked-rows vertical EQ on a ListPage**: N rows = N vertical slices; row r's title renders
  every band's slice at that height using ONLY the U+2581..U+2588 ramp (uniform 11.256 px
  advances — columns align across rows IF every cell is a ramp glyph; a space or any non-ramp
  char breaks the grid, same class of trap as item 4's blank braille). Vertical resolution
  becomes rows × 8. Open questions: row spacing/padding between ListItems visually breaks the
  columns into ribbons — how bad? Can rows render with no icon column indent?
- **ContentPage + Markdown code block**: monospace, one control, true 2-D character canvas
  (vertical bars, spectrogram — subsumes item 9). Unknowns: the refresh channel for content
  (RaiseContentsChanged? per-body property change?), its ceiling in fps, flicker on update, and
  whether the markdown renderer keeps up at 10–15 fps without tearing.
- **Anything richer**: does the toolkit/host offer grids, images-as-content, details panes,
  adaptive-card-like surfaces we could abuse? What do first-party extensions do for dense visuals?
  Inventory what exists before inventing; document findings here or in a notes/rendering.md.

**Design north star: classic Winamp.** Copy the iconic looks, feasibility-mapped to our
character canvas:

- **Spectrum analyzer** — vertical bars + slowly-falling peak caps (item 6). THE look; this is
  what item 12 builds.
- **Stereo mirror analyzer** — item 5's center-out L/R layout, same renderer.
- **Oscilloscope** — the waveform line. Very Winamp, and cheap on data: SpectrumCapture's ring
  already holds the raw samples, it just doesn't expose them — add a `TryReadWaveform(float[])`
  next to `TryReadBands` (same gate, decimate the ring into N columns). Braille dots (item 4) are
  the natural pen for a scope trace; block ramp works as a fallback.
- **Fire/gradient tints** — item 3's color work, within host limits (tags/icons, no text color).
- **AVS / MilkDrop** — out of scope, we render text in a list host, not shaders. Don't try.

Pick the winner by: looks right > refresh rate > code simplicity. The horizontal page stays until
this lands.

## 13. Visualizer style switch in settings

Once more than one renderer exists, the user picks the style — per surface — via the planned
CmdPal settings page (JsonSettingsManager, mirror AgentsPanelExtension's UsageSettingsManager):

- **Dock style**: blocks-8 (current) / braille-high-res (item 4) / stereo mirror (item 5) /
  oscilloscope (item 12's waveform read).
- **Page style**: horizontal bars (current v1) / vertical EQ with peak caps / oscilloscope /
  spectrogram (items 12, 6, 9) — i.e. the Winamp set.
- Renderers are already pure functions of the levels array, so a style is just "which render
  function + which band count" — wire the choice as a pull-style read on each tick like the
  reference repo's settings, no restart needed. Settings also eventually carry bar count / fps /
  decay / idle behavior / colors (item 3) per the original plan's Step 4.
