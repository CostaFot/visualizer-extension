using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VisualizerExtension.Properties;
using Windows.Foundation;

namespace VisualizerExtension;

// The product: a Command Palette Dock band whose single button renders a live audio spectrum as
// block glyphs (U+2581..U+2588) in the button title, ~15 fps while audio plays.
//
// The 15-fps channel is IN-PLACE MUTATION: GetItems() returns the same ListItem instance forever
// and each frame mutates its Title — the host caches dock view models by IListItem reference and
// listens to per-item property changes, repainting just that button. The item array is never
// rebuilt and ItemsChanged is never raised (a first paint from construction-time data sidesteps
// the "GetItems() runs before ItemsChanged is subscribed" trap entirely).
//
// Lifecycle: the host subscribes ItemsChanged when the band becomes visible and unsubscribes when
// it's hidden, so the add/remove accessors are the de-facto Loaded/Unloaded hooks. Observer adds
// are REFCOUNTED (the host may add twice): loopback capture + render timer start at the first
// observer and are torn down at the last one — a hidden band costs nothing.
//
// Idle throttle: after ~3 s of silence the timer drops to 2 Hz (still sampling, so it snaps back
// the moment audio returns). The dock is always visible; a pinned band must not burn CPU all day.
internal sealed partial class VisualizerDockBand : ListPage, INotifyItemsChanged, IDisposable
{
    private const int BarCount = 10;
    private const double ActiveIntervalMs = 66;   // ~15 fps while audio plays
    private const double IdleIntervalMs = 500;    // 2 Hz once silence settles
    private const long IdleAfterMs = 3000;        // silence duration before throttling

    // U+2581 LOWER ONE EIGHTH BLOCK repeated — the all-quiet frame, and the first paint.
    private static readonly string BaselineFrame = new('\u2581', BarCount);

    private readonly ListItem _visualizerItem;
    private readonly IListItem[] _items;

    // Render state — touched only from timer callbacks (one short tick at a time).
    private readonly float[] _bands = new float[BarCount];
    private readonly float[] _levels = new float[BarCount];
    private readonly char[] _frame = new char[BarCount];
    private string _lastFrame = BaselineFrame;
    private long _lastAudioTicks;
    private bool _idle;

    // Lifecycle state — guarded by _gate (host add/remove vs. Dispose vs. render tick).
    private readonly object _gate = new();
    private System.Timers.Timer? _timer;
    private SpectrumCapture? _capture;
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
            Log.Info("Band", "Band visible — capture running");
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
            Log.Info("Band", "Band hidden — capture stopped");
        }
    }

    public VisualizerDockBand()
    {
        // Dock bands require a non-empty, unique command Id or the host silently drops/conflates
        // the band.
        Id = "com.costafotiadis.visualizer.dock.spectrum";
        Title = Resources.Band_Title;
        Icon = new IconInfo("\uE8D6"); // Segoe Audio glyph — band icon in the dock's band manager

        // The one stable item = the one dock button. Icon deliberately blank so every pixel of the
        // ~100 DIP title budget goes to the bars (10 block glyphs fit).
        _visualizerItem = new ListItem(new OpenVolumeMixerCommand())
        {
            Title = BaselineFrame,
            Subtitle = string.Empty,
            Icon = new IconInfo(string.Empty),
        };
        _items = [_visualizerItem];
    }

    // Same instances forever — see the class comment.
    public override IListItem[] GetItems() => _items;

    private void StartLocked()
    {
        _capture = new SpectrumCapture();
        _lastAudioTicks = Environment.TickCount64;
        _idle = false;
        _timer = new System.Timers.Timer(ActiveIntervalMs) { AutoReset = true };
        _timer.Elapsed += (_, _) => RenderFrame();
        _timer.Start();
    }

    private void StopLocked()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        _capture?.Dispose();
        _capture = null;

        // Reset so the next show starts from a clean baseline instead of a stale frame.
        Array.Clear(_levels);
        _lastFrame = BaselineFrame;
        _visualizerItem.Title = BaselineFrame;
    }

    // Timer tick (pool thread — safe for cross-proc item mutation; TimeDate's clock band is the
    // first-party precedent). MUST NEVER THROW: an unhandled timer exception kills the process.
    private void RenderFrame()
    {
        SpectrumCapture? capture;
        System.Timers.Timer? timer;
        lock (_gate)
        {
            capture = _capture;
            timer = _timer;
        }

        if (capture is null || timer is null)
        {
            return;
        }

        try
        {
            var hasAudio = capture.TryReadBands(_bands);
            var now = Environment.TickCount64;
            if (hasAudio)
            {
                _lastAudioTicks = now;
            }

            var idle = !hasAudio && now - _lastAudioTicks > IdleAfterMs;
            if (idle != _idle)
            {
                _idle = idle;
                timer.Interval = idle ? IdleIntervalMs : ActiveIntervalMs;
                Log.Info("Band", idle ? "Silence — throttled to 2 Hz" : "Audio — back to 15 fps");
            }

            for (var i = 0; i < BarCount; i++)
            {
                // Fast attack, exponential decay; then level → one of the 8 block glyphs.
                var target = hasAudio ? Math.Clamp(_bands[i], 0f, 1f) : 0f;
                _levels[i] = target > _levels[i] ? target : _levels[i] * 0.72f;
                _frame[i] = (char)(0x2581 + (int)(_levels[i] * 7.99f));
            }

            // Push-only-on-change: identical frames (silence) cost nothing cross-proc.
            var frame = new string(_frame);
            if (!string.Equals(frame, _lastFrame, StringComparison.Ordinal))
            {
                _lastFrame = frame;
                _visualizerItem.Title = frame;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Band", "Render tick failed", ex);
        }
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
