# TODO — to be picked up later

Recorded 2026-08-16 after the first live deploy of the scaffold. Ordered roughly by annoyance.

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
- The page becomes the natural home for now-playing metadata (SMTC) alongside the visuals.

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
