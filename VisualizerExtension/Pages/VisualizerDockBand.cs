using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VisualizerExtension.Properties;
using Windows.Foundation;

namespace VisualizerExtension;

// The product: a Command Palette Dock band whose single button renders a live audio spectrum as
// glyphs in the button title (which glyphs is the injected ISpectrumRenderer's call — block bars
// or braille), ~15 fps while audio plays. Clicking the button opens the big in-palette visualizer
// (the injected page command — VisualizerCanvasPage); the volume mixer lives in the right-click
// menu.
//
// The 15-fps channel is IN-PLACE MUTATION: GetItems() returns the same ListItem instance forever
// and each frame mutates its Title — the host caches dock view models by IListItem reference and
// listens to per-item property changes, repainting just that button. The item array is never
// rebuilt and ItemsChanged is never raised (a first paint from construction-time data sidesteps
// the "GetItems() runs before ItemsChanged is subscribed" trap entirely).
//
// Lifecycle: the host subscribes ItemsChanged when the band becomes visible and unsubscribes when
// it's hidden, so the add/remove accessors are the de-facto Loaded/Unloaded hooks. Observer adds
// are REFCOUNTED (the host may add twice): a SpectrumSource lease + RenderLoop start at the first
// observer and are torn down at the last — a hidden band costs nothing. The idle throttle (2 Hz
// after ~3 s of silence) lives in RenderLoop.
internal sealed partial class VisualizerDockBand : ListPage, INotifyItemsChanged, IDisposable
{
    // How levels become glyphs is the renderer's business (block bars, braille, ... — one band
    // per style, each with its own unique Id; the title-budget measurements live with each
    // renderer). This class owns everything else: lifecycle, smoothing, and the
    // push-only-on-change title channel.
    private readonly ISpectrumRenderer _renderer;

    private readonly SpectrumSource _source;
    private readonly ListItem _visualizerItem;
    private readonly IListItem[] _items;

    // TODO #3: the VU-dot variant gives the button a small icon whose color tracks the current
    // peak over the VuPalette ramp (dock titles have no text color — the icon is the dock's only
    // color channel). Cached IconInfo per step, pushed only when the quantized step changes; the
    // host re-fetches the icon on the item's Icon PropChanged (verified in CommandItemViewModel).
    private readonly bool _vuDot;

    // Render state — touched only from RenderLoop ticks (serialized; loop dispose drains).
    private readonly float[] _bands;
    private readonly float[] _levels;
    private string _lastFrame;
    private int _dotStep;

    // Lifecycle state — guarded by _gate (host add/remove vs. Dispose).
    private readonly object _gate = new();
    private IDisposable? _lease;
    private RenderLoop? _loop;
    private int _observers;
    private bool _disposed;

    // Never raised — the stable-item channel repaints per-item — so the handlers aren't even
    // stored; implementing the event is purely what exposes the visibility lifecycle. Refcount
    // adds: the host may subscribe twice without an intervening remove, and each remove must
    // balance exactly one add.
    event TypedEventHandler<object, IItemsChangedEventArgs> INotifyItemsChanged.ItemsChanged
    {
        add
        {
            lock (_gate)
            {
                _observers++;
                if (_observers == 1 && !_disposed)
                {
                    StartLocked();
                }
            }
            Log.Info("Band", $"Band visible — capture running ({Title})");
        }
        remove
        {
            lock (_gate)
            {
                if (_observers > 0 && --_observers == 0)
                {
                    StopLocked();
                }
            }
            Log.Info("Band", $"Band hidden — capture stopped ({Title})");
        }
    }

    public VisualizerDockBand(SpectrumSource source, ICommand page, ISpectrumRenderer renderer, string id, string title, bool vuDot = false)
    {
        _source = source;
        _renderer = renderer;
        _vuDot = vuDot;
        _bands = new float[renderer.BarCount];
        _levels = new float[renderer.BarCount];
        _lastFrame = renderer.Baseline;

        // Dock bands require a non-empty, unique command Id or the host silently drops/conflates
        // the band.
        Id = id;
        Title = title;
        Icon = new IconInfo("\uE8D6"); // Segoe Audio glyph — band icon in the dock's band manager

        // The one stable item = the one dock button. Icon blank on the plain bands so every pixel
        // of the ~100 DIP title budget goes to the bars (each renderer sizes itself to that
        // budget); the VU band instead carries the step-0 dot from the first paint, so showing
        // color never changes the button's width. Its command is the page: clicking opens the
        // palette at the big visualizer.
        _visualizerItem = new ListItem(page)
        {
            Title = renderer.Baseline,
            Subtitle = string.Empty,
            Icon = vuDot ? VuDotIcons.Icon(0) : new IconInfo(string.Empty),
            MoreCommands = [new CommandContextItem(new OpenVolumeMixerCommand())],
        };
        _items = [_visualizerItem];
    }

    // Same instances forever — see the class comment.
    public override IListItem[] GetItems() => _items;

    private void StartLocked()
    {
        _lease = _source.Acquire();
        _loop = new RenderLoop("Band", RenderFrame);
    }

    private void StopLocked()
    {
        // Loop first (its Dispose waits out any in-flight tick), then the lease, then the reset —
        // so no tick can repaint over the baseline or read a dead capture.
        _loop?.Dispose();
        _loop = null;
        _lease?.Dispose();
        _lease = null;

        Array.Clear(_levels);
        _lastFrame = _renderer.Baseline;
        _visualizerItem.Title = _renderer.Baseline;
        if (_vuDot && _dotStep != 0)
        {
            _dotStep = 0;
            _visualizerItem.Icon = VuDotIcons.Icon(0);
        }
    }

    // RenderLoop tick — pool thread (safe for cross-proc item mutation; TimeDate's clock band is
    // the first-party precedent), exceptions handled by the loop.
    private bool RenderFrame()
    {
        var hasAudio = _source.TryReadBands(_bands);

        for (var i = 0; i < _levels.Length; i++)
        {
            // Fast attack, exponential decay; the renderer turns the smoothed levels into glyphs.
            var target = hasAudio ? Math.Clamp(_bands[i], 0f, 1f) : 0f;
            _levels[i] = target > _levels[i] ? target : _levels[i] * 0.72f;
        }

        // Push-only-on-change: identical frames (silence) cost nothing cross-proc.
        var frame = _renderer.Render(_levels);
        if (!string.Equals(frame, _lastFrame, StringComparison.Ordinal))
        {
            _lastFrame = frame;
            _visualizerItem.Title = frame;
        }

        // The VU dot follows the loudest band; same push-only-on-change idea, with the palette's
        // quantization keeping icon swaps to a handful per second.
        if (_vuDot)
        {
            var peak = 0f;
            for (var i = 0; i < _levels.Length; i++)
            {
                peak = Math.Max(peak, _levels[i]);
            }

            var step = VuPalette.StepFor(peak);
            if (step != _dotStep)
            {
                _dotStep = step;
                _visualizerItem.Icon = VuDotIcons.Icon(step);
            }
        }

        return hasAudio;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopLocked();
        }
    }
}
