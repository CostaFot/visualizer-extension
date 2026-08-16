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

## 2. Replace "open volume mixer" click with our own visualizer page

Opening `ms-settings:apps-volume` is a placeholder, not a product. Clicking the dock button should
open OUR page: a big, cool visualizer inside the palette window — a page command on a dock button
opens the palette at that page (`DockControl.xaml.cs → InvokeItem`), which is exactly the behavior
we avoided for v0 and actually want here.

The palette gives far more leeway than the ~100 DIP dock button:
- A `ListPage` can render many rows — e.g. one row per band with wide bar strings, or a tall
  multi-row "equalizer" drawn as rows of block glyphs.
- A `ContentPage` with Markdown (code block = monospace) could draw a genuinely 2-D spectrum
  (rows × columns of block/braille characters) — worth a spike to see refresh-rate limits on the
  content-change pipeline, which is NOT the proven 15-fps dock channel (that proof covered
  per-item Title mutation only).

Keep `OpenVolumeMixerCommand` around as a `MoreCommands` context action on the item rather than
deleting it.

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
