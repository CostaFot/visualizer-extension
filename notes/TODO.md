# TODO — to be picked up later

Recorded 2026-08-16 after the first live deploy of the scaffold. Items 1–3 are the original
annoyances; 4–10 came from the same day's brainstorm. Scope guard: this is NOT a media-player app
(see AGENTS.md) — nothing here may grow SMTC/now-playing/track-control features.

## 1. Visual bug: three dots ("…") at the end of the dock visualizer

Observed live: the band's button shows the 10 bars followed by what looks like three dots.
Almost certainly the host's text **ellipsis**: the dock title is 12px with `MaxWidth=100` and
trims with `…` when it doesn't fit (`DockItemControl.xaml`), so 10 block glyphs at whatever width
the fallback font gives them is apparently just over the 100 DIP budget — the trimmed tail renders
as the dots. Related to the plan's open "width jitter" check (block glyphs may not be rendering
from a fixed-width font run).

Fix ideas, in order to try:
- Drop to 9 (or 8) bars and see if the dots disappear — cheapest experiment, tells us the budget.
- Check which font actually renders U+2581..U+2588 in WinUI and whether a run is measurable at
  ≤100 DIP for 10 chars; maybe thinner glyphs (U+2581-style eighths are full-width; braille
  patterns U+2800.. are narrower) buy more bars per pixel.
- If bar count changes, keep `BarCount` the single knob (it already is) and consider it the future
  settings-page value.

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

U+2800–U+28FF braille patterns give a 2x4 dot matrix per character — roughly 2x the horizontal
resolution of block glyphs in the same width budget, so 16–20 bars where blocks give 10 (it's the
trick TUI tools like btop use). Braille glyphs may also be narrower than the block eighths, which
could dodge the ellipsis bug (item 1) as a side effect. Spike: render the same spectrum both ways,
compare width, jitter, and readability at dock size. Renderer stays a pure function of
`_levels[]`, so this is a candidate for a "render style" setting later.

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
