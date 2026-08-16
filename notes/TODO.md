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

## 4. ~~Braille rendering spike~~ VERDICT 2026-08-16: keep BOTH bands

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

**Resolved after the live side-by-side: NEITHER retires.** Both bands stay registered permanently —
picking the style IS the pinning: the user pins blocks, braille, or both from the Dock's band
manager. This is the dock's de-facto style switch, so item 13's settings work only needs to cover
the PAGE style (and any future dock renderers just become additional bands).

Spike history: the glyph mapping was extracted
into `Rendering/ISpectrumRenderer.cs` (pure levels→frame strategy — the seam item 13's style
setting will switch on): `BlockBarsRenderer` (the original 8×8) and `BrailleBarsRenderer`
(11 cells → 22 bars × 4 levels; every column keeps its bottom dot lit — baseline cell U+28C0 —
so the blank-cell trap can't fire). The provider now registers a SECOND dock band,
`com.costafotiadis.visualizer.dock.spectrum.braille` ("Visualizer (braille)"), sharing the one
`SpectrumSource`.

## 5. Stereo mirror mode

Classic Winamp look: bass in the center, L/R channels spreading outward. Needs per-channel FFT
instead of the current mono downmix in `AppendSamples` (keep two rings, or interleave and split at
read time). Doubles FFT cost — still trivial at 2048 samples. Natural "mode" entry alongside
item 4's render style.

## 6. ~~Peak-hold caps~~ DONE (palette canvas) 2026-08-16

Shipped with item 12's `VisualizerCanvasPage`: per-band `_peaks[]` latches upward, holds ~0.75 s,
then falls 0.03/tick until the bar catches it; rendered as U+2594 (UPPER ONE EIGHTH — hangs at the
top of its cell) in the cell above the bar top. The dock variant stays unbuilt by choice: with only
8 vertical levels per glyph a cap usually coincides with the bar top — revisit only if a dock
style ever wants it (item 13).

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

## 9. ~~Scrolling spectrogram view~~ DONE 2026-08-16 (shipped as item 13's second style)

Shipped as `RenderSpectrogram` in `VisualizerCanvasPage`: rows = time (newest at the bottom,
scrolling up), columns = the same band layout as the bars (so the frequency axis applies
unchanged), intensity = blank → shade ramp U+2591/2592/2593 → full block, fed by INSTANTANEOUS
levels (no attack/decay smoothing). During silence zero-rows scroll in until the canvas drains
blank, then frame dedup kicks in. Selected via the item-13 page-style setting.

Live verdict 2026-08-16: functional but "goofy"-looking (the bars style reads fine after its
LED-matrix pass; this one hasn't had a look pass). Candidates when it gets one: longer history via
a taller canvas, per-cell shade tuning, maybe braille density — do it alongside the color work
(item 3).

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

## 12. ~~Proper VERTICAL visualizer in the page~~ DONE 2026-08-16 (canvas shipped; more styles → #13)

The investigation pass ran first, from host source (findings + file:line evidence in
**`notes/rendering.md`** — read that before touching any render surface). Outcome of the three
candidate channels:

- **Stacked-rows ListPage EQ: dead.** Hard-locked 44 px row pitch vs ~14-19 px of title ink →
  dashed ribbons with a ~2:1 gap, plus an unremovable ~52 px icon indent. No host knob.
- **Markdown code block: dead.** Full Markdig re-parse + RichTextBlock rebuild per frame, and the
  host never sets a monospace `CodeBlockFontFamily`, so the "grid" may render proportional.
- **Winner: `PlainTextContent` (`FontFamily.Monospace`) on a ContentPage** — host-guaranteed
  Cascadia Mono/Consolas, repaint = one `TextBlock.Text` assignment, equal frames deduped both
  sides of the COM boundary, ~20-24 fps ceiling (host-global 40 ms batch). Needs SDK 0.11 / host
  CmdPal 0.11 — project bumped to SDK 0.11.260520004 and .NET 10 for it.

Shipped as `Pages/VisualizerCanvasPage.cs`: 20 vertical bars × 14 rows (lower-partial blocks,
112 vertical steps), peak-hold caps (item 6), static frequency-axis footer; dock clicks and the
top-level entry now land there. The v1 rows page survives behind the top-level item's context menu
("Visualizer (rows)"), converted to `DynamicListPage` so palette typing can't fuzzy-scramble its
rows. Verified live 2026-08-16: the canvas renders and animates correctly. First polish pass same
day, after a screen recording showed the bars reading as accidental-looking broken tiles: the
viewer's TextBlock line spacing makes stacked cells discrete (unfixable — see rendering.md), so
the bars style now leans into the LED-matrix look — faint U+00B7 dot grid in unlit cells ("off
LEDs"), U+2500 floating line as the peak cap (U+2594 was invisibly thin at cell size). Further
look tuning belongs with color (#3).

**Design north star: classic Winamp.** The remaining looks, feasibility-mapped to the (now
proven) character canvas:

- **Spectrum analyzer** — vertical bars + slowly-falling peak caps (item 6). THE look — BUILT,
  this is what `VisualizerCanvasPage` renders.
- **Stereo mirror analyzer** — item 5's center-out L/R layout, same renderer.
- **Oscilloscope** — the waveform line. Very Winamp, and cheap on data: SpectrumCapture's ring
  already holds the raw samples, it just doesn't expose them — add a `TryReadWaveform(float[])`
  next to `TryReadBands` (same gate, decimate the ring into N columns). Braille dots (item 4) are
  the natural pen for a scope trace; block ramp works as a fallback.
- **Fire/gradient tints** — item 3's color work, within host limits (tags/icons, no text color).
- **AVS / MilkDrop** — out of scope, we render text in a list host, not shaders. Don't try.

Pick the winner by: looks right > refresh rate > code simplicity. (The horizontal rows page is now
the secondary entry; item 13's style setting decides whether it stays at all.)

## 13. ~~Visualizer style switch in settings~~ DONE 2026-08-16 (page style; dock needs none)

Shipped as `Settings/VisualizerSettingsManager.cs` (JsonSettingsManager singleton mirroring
AgentsPanelExtension's UsageSettingsManager; persisted to `visualizer.settings.json` under the
CmdPal settings folder; `Settings = ...Instance.Settings` in the provider surfaces it in the
palette's Settings UI). One `ChoiceSetSetting` "pageStyle": **Vertical bars with peak caps**
(default) / **Scrolling spectrogram** (item 9). `VisualizerCanvasPage` reads it PULL-STYLE every
tick and resets per-style render state on change — switching applies on the next frame, no
restart. Same toolkit quirk as the reference repo: the visible field label comes from
`Description`, not `Label`.

- **Dock style needs NO setting** — item 4's verdict: each dock style is its own registered band
  and the user pins the ones they want; future dock renderers just add bands.
- Future styles (oscilloscope — DONE, item 14; a canvas horizontal-bars mode; stereo mirror,
  item 5) just extend the choice list + add a render method.
- Still open from the original plan's Step 4: settings for bar count / fps / decay / idle
  behavior / colors (item 3) — the settings scaffold they'll live in now exists.

## 14. ~~Oscilloscope page style~~ DONE 2026-08-16 (verified live same day)

The Winamp waveform trace from item 12's north star, shipped as the third item-13 page style,
alongside the hub page restructure (single top-level entry → static ListPage menu: canvas, rows,
volume mixer, settings — mirroring AgentsPanelExtension's UsagePage).
Data side: `SpectrumCapture.TryReadWaveform(float[])` — the ring already held the raw samples —
decimates the newest 1024 samples (~21 ms at 48 kHz, a Winamp-ish scope span) into one value per
canvas column via **signed peak per chunk** (averaging ~17 samples would low-pass the trace and
flatten drums to a resting line), then applies its own slow auto-gain — linear and
sign-preserving, unlike the bands' sqrt (a scope trace must not be loudness-warped). Same
"no packet in 250 ms" silence contract; exposed through `SpectrumSource` under the same
single-reader gate.

Render side: `RenderOscilloscope` in `VisualizerCanvasPage` draws a connected trace on the same
LED-matrix grid as the bars — each of the 59 columns lights its sample's cell (full block) plus
the vertical run bridging to the previous column (a bare one-cell-per-column plot shatters into
confetti when the wave moves more than one row per column); unlit cells keep the U+00B7 "off LED"
grid. Silence renders the flat centerline, which dedupes to zero cross-proc cost. The static
footer is now per-style (`WriteAxis`): frequency labels for bars/spectrogram, blank for the
scope (a time-domain trace has no frequency axis).

**Pen decision:** blocks, not braille — `notes/rendering.md` verifies Block Elements coverage in
Cascadia Mono/Consolas but flags braille U+28xx as UNVERIFIED there, and the viewer's row seams
make the canvas discrete anyway. If braille ever gets measured and covered, a higher-res scope
pen is a drop-in change to `RenderOscilloscope`.
