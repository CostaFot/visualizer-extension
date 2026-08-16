# Music Visualizer Extension — build plan (handoff doc)

> **For a future Claude session.** This file contains everything needed to build a standalone
> "music visualizer in the CmdPal Dock" extension in a NEW repo. It was written 2026-08-15 after a
> successful spike in THIS repo (agents-panel-extension, used only as a testbed — the spike code
> will be/was reverted from the working tree, but survives in this doc and in git history).
> Treat every "verified" claim below as tested on this machine on that date.

## Spike verdict (the go signal)

A dock band **sustains ~15 fps repaints** through the out-of-proc property-change pipeline —
verified live with two buttons animating simultaneously (one audio-reactive, one synthetic sine
loop). Smooth, no lag, no queue buildup. First-party bands only ever proved 1 Hz (TimeDate clock,
PerfMonitor), so this was the make-or-break unknown, and it passed. A real visualizer extension
is viable.

Spike code (reference implementation, ~250 lines): `AgentsPanelExtension/Pages/VisualizerDockPage.cs`
plus a one-`CommandItem` band registration in `AgentsPanelCommandsProvider.cs`. If reverted from
the tree, recover with `git log --all --oneline -- '*VisualizerDockPage*'` in this repo.

## Step 1 — scaffold the new repo

1. Scaffold the same way THIS repo was made: copy the structure from
   `C:\Users\jarla\code\MarketExtension` (its `reference/` folder holds the pristine AdbExtension
   blank-extension files; its `CLAUDE.md` + `notes/cmdpal-toolkit.md` + `notes/releasing.md` are
   the CmdPal knowledge base). This repo (agents-panel-extension) is the newer template of the
   two — prefer copying its csproj/manifest/solution shape.
2. Rename everything: solution/project/namespace, **new extension COM GUID** (the `[Guid]` on the
   extension class — must be freshly generated, never reused across extensions), **new package
   identity** in `Package.appxmanifest`, new settings filename, new logo assets.
3. Copy the conventions files: `AGENTS.md` (trim provider-specific content) + the thin `CLAUDE.md`
   that imports it, `.editorconfig`, the `Log`/`Strings` helpers.
4. Keep the project properties identical: .NET 9, self-contained single-file JIT, **trim/AOT OFF**
   (the COM interop below needs it), x64 + ARM64 platforms.
5. Build command (alphabetical-platform trap): `dotnet build <sln> -p:Platform=x64` — without the
   flag MSBuild picks ARM64 and the package won't deploy.
6. **Claude builds only, never deploys** — the developer deploys from Rider. No `Add-AppxPackage`,
   ever (see AGENTS.md for the incident that made this rule).

## Step 2 — the dock band (port the spike)

Copy `VisualizerDockPage.cs` from this repo/history as the starting point. The mechanics that make
it work (all verified against the CmdPal host source in `C:\Users\jarla\code\PowerToys`):

- **A band = `CommandItem` wrapping an `IListPage`**, returned from the provider's
  `GetDockBands()`. Each `GetItems()` row renders as one button. The band's command **`Id` must be
  non-empty and unique** or the host silently drops/conflates the band.
- **Per-button canvas** (host template `Microsoft.CmdPal.UI/Dock/DockItemControl.xaml` +
  `DockControl.xaml`): 16 DIP icon + Title (12px, `MaxWidth=100`, ellipsized, no scrolling) +
  Subtitle (10px tertiary, `MaxWidth=100`) + auto tooltip "Title\nSubtitle". No custom controls,
  no text colors, no adaptive cards. Users can hide titles/subtitles per band; compact docks drop
  subtitles.
- **The 15-fps channel is in-place mutation**: keep STABLE `ListItem` instances and mutate
  `.Title`/`.Subtitle`/`.Icon` — the host (`DockBandViewModel.cs`) caches view models **by
  `IListItem` reference** and listens to per-item property changes, repainting just that button.
  Never rebuild the item array per frame; `GetItems()` returns the same instances forever and
  `ItemsChanged` is never raised.
- **Timer lifecycle**: re-implement `INotifyItemsChanged`; the host subscribes when the band
  becomes visible and unsubscribes when hidden, so the `add`/`remove` accessors are
  Loaded/Unloaded. Refcount `add`s (host may add twice); start the render timer at 1 observer,
  stop at 0. Mutating from a `System.Timers.Timer` pool thread is safe (TimeDate's clock band is
  the first-party precedent).
- **The renderer is text**: 10 bars = 10 block characters, U+2581..U+2588 are consecutive so
  `(char)(0x2581 + (int)(level * 7.99))`. 10 chars fits the 100px title budget. Blank the item
  icons (`new IconInfo(string.Empty)`) so all pixels go to the bars.
- **Push-only-on-change**: compare the rendered string to the last pushed one and skip identical
  frames — silence then costs nothing cross-proc.
- **Clicks can be real actions**: an `InvokableCommand` on a dock button executes in place — the
  palette window only opens for page commands (`DockControl.xaml.cs → InvokeItem`). Right-click
  menus come from `MoreCommands` on the `ListItem`.

## Step 3 — audio input, two tiers

**Tier 1 — peak meter (shipped in the spike, keep as fallback/low-power mode).**
`IAudioMeterInformation.GetPeakValue()` on the default render endpoint: free loudness read, no
audio capture, no mic capability, no dependencies. One value → pseudo-spectrum via per-bar shape
weights + smoothed random flutter, fast attack / `*0.72` exponential decay, `Math.Sqrt(peak)` for
perceived loudness. The complete COM interop (verified working; vtable order matters, only leading
methods typed):

```csharp
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorComObject { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint); // eRender=0, eMultimedia=1
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    void Activate(ref Guid iid, int clsCtx /*CLSCTX_ALL=0x17*/, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object activated);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation
{
    void GetPeakValue(out float peak);
}
```

Wrap reads in try/catch `COMException` → null the cached interface and rebind next tick (handles
default-device changes). Never let the timer callback throw.

**Tier 2 — real spectrum (the actual product).** WASAPI **loopback capture** + FFT:

- Easiest: NAudio's `WasapiLoopbackCapture` (render-endpoint loopback needs no microphone
  capability for a full-trust packaged desktop app — this extension is one). Alternative: raw
  `IAudioClient` interop with `AUDCLNT_STREAMFLAGS_LOOPBACK` to stay dependency-free.
- FFT: hand-rolled radix-2 (~40 lines) over a 1024–2048 sample window, Hann window, then fold
  bins into ~10 **log-spaced** bands (linear bins make everything look like bass), normalize with
  a slow auto-gain (running max with decay), same attack/decay smoothing as Tier 1.
- Capture runs its own thread; the render timer only READS the latest band levels — never do
  audio work on the render tick.
- Loopback capture delivers **no data during silence** (no samples flow) — treat "no packet in
  N ms" as silence, don't block on it.

## Step 4 — product hardening (the spike deliberately skipped these)

- **Idle throttle**: after ~3 s of silence drop the timer to ~2 Hz (keep sampling so it snaps
  back on audio). The dock is always visible — a pinned band must not burn CPU all day. The spike
  ran flat-out 15 fps on purpose; do NOT ship that.
- **`IDisposable`**: the spike leaks its timer by design (CA1001 warned). The page should own and
  dispose timer + capture.
- **Width jitter check**: block glyphs looked uniform-width in the spike, but verify the button
  doesn't "breathe" horizontally with real spectrum data; if it does, the glyph run may be
  falling back to a variable-width font — consider padding or fewer bars.
- **Settings** (CmdPal settings page): bar count, target fps (10/15/20), decay speed, peak-meter
  vs loopback mode, idle behavior.
- **Click/context actions**: click → open Volume Mixer or play/pause via SMTC
  (`GlobalSystemMediaTransportControlsSessionManager` — also gives track title/artist for the
  button subtitle or a second "now playing" button). Right-click → mode toggles.
- **Strings to resx** (`Resources.resx` + hand-maintained `Resources.Designer.cs` in lock-step);
  the spike hardcoded strings deliberately.
- **Icon**: band icon needs a real PNG for the Store; spike used Segoe glyph `\uE8D6` (Audio).

## Gotchas that WILL bite (all hit during this repo's development or the spike)

1. **The Write tool mangles glyph/backslash-escape characters.** Always write Segoe glyphs and
   any `\uXXXX` as ASCII escape text and **byte-check after editing** (`grep | xxd`). During the
   spike, a raw glyph went in, then perl (`\u` = uppercase) and GNU sed (`\u` also special) BOTH
   ate the backslash during repair — it took `sed 's/.../\\\\uE8D6/'` to restore it.
2. **`GetItems()` runs before `ItemsChanged` is subscribed** — first paint must come from data
   that exists at construction (the stable-item pattern sidesteps this entirely).
3. **Never raise ItemsChanged synchronously inside the host's subscription** and never deliver
   Rx emissions under a lock into `RaiseItemsChanged` (STA/gate deadlock — see this repo's
   AGENTS.md threading notes). The visualizer avoids Rx entirely; keep it that way.
4. **Band `Id` non-empty + unique**, or the band vanishes / gets conflated.
5. **`data:image/svg+xml` URIs do NOT work as icons** (the host sniffs the URI *extension* for
   `.svg` — `IconPathConverter.cpp`); a file path ending in `.svg` renders as vector, PNG data
   URIs and http URLs work, emoji give color. Untested but likely: animated GIF icons auto-play
   (BitmapImage path).

## Reference material

- Host dock internals: `C:\Users\jarla\code\PowerToys\src\modules\cmdpal\Microsoft.CmdPal.UI\Dock\`
  (`DockItemControl.xaml[.cs]`, `DockControl.xaml[.cs]`, and
  `..\Microsoft.CmdPal.UI.ViewModels\Dock\DockBandViewModel.cs` for the reference-equality
  caching + property-change plumbing).
- First-party precedents: `ext/Microsoft.CmdPal.Ext.TimeDate/NowDockBand.cs` (1 s in-place Title
  mutation, MoreCommands, blank icon), `ext/Microsoft.CmdPal.Ext.PerformanceMonitor/` (stable
  items mutated from an update event, dynamic battery icon), `extensionsdk/.../Dock/WrappedDockItem.cs`
  (band-from-items helper, no page needed).
- CmdPal toolkit lore: `C:\Users\jarla\code\MarketExtension\notes\cmdpal-toolkit.md`; release
  flow: `notes\releasing.md`; Store/WinGet: this repo's `notes\store-readiness.md`.

## Cleanup reminder for THIS repo

Revert the spike here once the new repo exists: delete
`AgentsPanelExtension/Pages/VisualizerDockPage.cs`, drop the "Visualizer" `CommandItem` from
`AgentsPanelCommandsProvider.cs`, and delete this file if it has served its purpose (or move it
into the new repo's `notes/`).
