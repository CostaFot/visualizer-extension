# CmdPal rendering limits — what the host can actually draw (TODO #12 investigation)

Investigated 2026-08-16 from the host source at `C:\Users\jarla\code\PowerToys\src\modules\cmdpal\`
(main @ 3d0c3bdb29) plus the *installed* host (CmdPal 0.11.11762, unpacked from the MSIX under
`%LOCALAPPDATA%\PowerToys\WinUI3Apps\CmdPal\`). All file:line cites are into that source tree.
Verdict first, evidence after.

## Verdict

**The page visualizer canvas is `PlainTextContent` (`FontFamily.Monospace`) on a `ContentPage`.**
Host-guaranteed monospace (`Cascadia Mono, Consolas` — `PlainTextContentViewer.xaml.cs:96`), a
repaint is literally one `TextBlock.Text` assignment (`UpdateText()` in
`PlainTextContentViewer.xaml.cs:70-78` — no parse, no element-tree rebuild), identical frames are
dropped on BOTH sides of the COM boundary (`BaseObservable.cs:50-53` extension-side,
`ContentPlainTextViewModel.cs:95-99` host-side), whitespace is preserved exactly, and the text gets
its own horizontal-capable `ScrollViewer` whose position survives updates. Requires SDK ≥
0.11.260520004 and host ≥ CmdPal 0.11 (PR #43964) — both satisfied here; note the PowerToys
minimum in the app description when releasing.

Every alternative loses:

- **Stacked ListPage rows** (glyph columns spanning rows): dead on arrival. Row pitch is a
  hard-locked **44 px** (`ListItemsView.xaml:39` `SingleRowListViewItemHeight`, applied as fixed
  `Height` AND `MinHeight` at `:264-265` — required for virtualization, see the "BEAR LOADING"
  comment at `:35-38`) against ~14-19 px of title ink (`FontSize=14`, `:387`) → vertical bars
  read as dashed ribbons with a ~2:1 gap-to-ink ratio, and there is no extension-reachable knob.
  Plus an unconditional **28 px icon column + 12 px spacing** (`:358`, `:356`) ≈ 52 px left indent
  even with an empty icon (the `IconBox` never collapses, `IconBox.cs:179-182`).
- **Markdown code block on a ContentPage**: every `Body` set is a full Markdig re-parse plus a
  complete `RichTextBlock`/`Paragraph`/`Run` teardown-and-rebuild (the toolkit `MarkdownTextBlock`
  has no incremental path), it kills text selection each frame, there is no horizontal scroll
  (`ContentPage.xaml:142` is vertical-only) — and the killer: **CmdPal never sets
  `CodeBlockFontFamily`** (`ContentPage.xaml:25-33` styles inline code only), so the code block
  may render in *proportional* Segoe UI. Unfixable from extension code.
- **Adaptive card (`FormContent`) with live SVG**: real vector graphics — the Performance Monitor
  extension pushes 1 Hz SVG charts as `data:image/svg+xml;utf8,` URIs by reassigning
  `FormContent.DataJson` (`ChartHelper.cs:71-75`, `ContentFormViewModel.cs:116-145`) — but every
  card update calls `RenderAdaptiveCard` + `ContentGrid.Children.Clear()` + full subtree rebuild
  (`ContentFormControl.xaml.cs:126-146`). Fine at 1 Hz, unusable at 15 fps. (Worth remembering
  for a static/slow surface; also note the icon-SVG gotcha in AGENTS.md is about *icons* — SVG
  data URIs DO work inside adaptive cards and markdown image bodies.)

## The update pipeline (applies to every surface)

- Extension-side property setters (`BaseObservable.SetProperty`) no-op on equal values, and
  `PropChanged` is raised **synchronously on the caller's thread across the process boundary**
  (`BaseObservable.cs:26`) — keep raising it from RenderLoop pool ticks, never from anything
  latency-sensitive.
- Host-side, ALL property updates funnel through a **process-global 40 ms one-shot batch timer**
  (`BatchUpdateManager.cs:18`, the "30 ms" comment is stale) → **effective repaint ceiling
  ~20-24 fps**, and extra frames coalesce rather than queue. Our 15 fps sits under it. Bonus:
  everything dirtied inside one window flushes as ONE coherent UI-thread pass — multi-item frames
  can't tear.
- `_pendingProps` does NOT dedup property names (`ExtensionObjectViewModel.cs:135-136`) — self-
  throttle to one property set per frame per object (we already do: push-only-on-change).
- **Never raise `ItemsChanged` during animation.** For content pages it's a cliff:
  `Model_ItemsChanged` → `FetchContent()` re-RPCs `GetContent()`, builds brand-new content view
  models (no `Equals` override on `ContentViewModel` → reference compare → zero matches), and
  swaps the `ItemsRepeater` element — full control re-instantiation, i.e. flicker
  (`ContentPageViewModel.cs:59-94`, `ListHelpers.cs:75-166`). Mutate a **stable content
  instance** returned forever from `GetContent()` and drive frames purely via `PropChanged`.
  (List pages: same rule, milder penalty — item VMs ARE cached by model reference,
  `ListItemViewModel.cs:192-194`.)

## PlainTextContent specifics

- Viewer: `ExtViews\Controls\PlainTextContentViewer.xaml(.cs)`. Monospace forced at `:96`;
  `WrapWords=false` → `TextWrapping=NoWrap` + horizontal scroll; selection/copy/zoom
  (Ctrl+/−/0) come free.
- **Do not touch `FontFamily` or `WrapWords` per frame** — those run a layout-invalidation hack
  (`Text=""` then restore, `:150-155`). Only mutate `Text`.
- Glyph coverage: Cascadia Mono (and the Consolas fallback) both cover Block Elements
  U+2580-259F, so block bars and U+2594 caps are safe, and ASCII spaces are safe too (monospace —
  unlike the dock title, where a space falls back differently and breaks the grid). **Braille
  U+2800-28FF in Cascadia Mono is unverified** — test before designing a braille page mode.

## ListPage findings worth keeping (for the rows page and future list work)

- Typing in the palette **fuzzy-filters and reorders** a plain `IListPage`'s rows against their
  titles (`ListViewModel.cs:163-214`, `:754-782`) — glyph-run titles get scrambled/dropped.
  `IDynamicListPage` bypasses the host filter entirely (`ListViewModel.cs:529-540`); a visualizer
  list page should always be a `DynamicListPage` that ignores the search text.
- A `ListItem` whose command has no name can be classified `ListItemType.Separator` (derived from
  the command, `ListItemViewModel.cs:102-107`) and render as a 1 px rule. Give canvas rows a
  named command.
- Empty `Title` silently falls back to the command's `Name` (`CommandItemViewModel.cs:62`) —
  never let a mutating title become "".
- List Title row font: 14 px, `CharacterSpacing=12` (uniform, alignment-safe), no `MaxWidth`,
  usable width ≈ list width − 84 px (`ListItemsView.xaml:383-392`). Subtitle sits BESIDE the
  title (not below); empty subtitle just yields width back. ~20-22 rows fit a 1080p palette
  before scrolling/virtualization.

## Other surfaces inventoried (future options)

- **Details pane renders markdown and updates in place**: hold a stable `IDetails` and mutate
  `Body` → `DetailsViewModel` re-fetches just that property through the normal 40 ms batch
  (`DetailsViewModel.cs:36-65`). Changing the `Details` *object* instead trips a 100/150 ms
  debounce + scroll reset (`ShellPage.xaml.cs:432-481`). A slow-rate side surface, not a canvas.
- **Tags**: arbitrary RGBA fore/background (`idl:142-163`, `ColorHelpers`), swappable at runtime
  (rebuilt on `Tags` PropChanged, `ListItemViewModel.cs:274-295`), but max 3 visible per row —
  TODO #3's color hook on list surfaces.
- **Grid layouts** (`GridProperties`): Small 32 px icon cells / Medium 100 px / Gallery 160 px
  tiles (`ListItemsView.xaml:464-597`). Icon-only cells; a 2-D icon grid is possible but per-cell
  updates re-run `IconSource` creation — heavier than any text channel. Not virtualized
  (`WrapPanel`, `:655-662`).
- **ImageContent** renders at original resolution (`ImageContentViewer.xaml:36`) — the
  unconstrained static-image path (relevant to TODO #10 previews, not animation).
