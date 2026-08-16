using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VisualizerExtension.Properties;
using Windows.Foundation;

namespace VisualizerExtension;

// The big in-palette visualizer: one row per frequency band, each row's title a smooth horizontal
// bar growing left to right. Opened by clicking the dock band's button (a page command on a dock
// button opens the palette at that page) or from the extension's top-level command.
//
// Rendering rides the SAME per-item mutation channel as the dock band: stable ListItem instances,
// mutate .Title per frame, push-only-on-change, ItemsChanged never raised — the palette's
// ListItemViewModel handles per-item Title property changes and repaints just that row (verified
// in the host source). Bars are built from Block Elements: full blocks U+2588 plus one LEFT
// partial block U+2589..U+258F as the fractional tip. In Segoe UI Symbol (the DirectWrite
// fallback that serves these) the partial glyphs' advances are proportional to their ink
// (measured 2026-08-16: 0.109em..0.938em), so bar length tracks the level continuously —
// 32 cells x 8 eighths = 256 horizontal steps per bar.
//
// Same lifecycle as the dock band: refcounted INotifyItemsChanged add/remove accessors acquire a
// SpectrumSource lease + RenderLoop while the page is open and tear both down when it closes.
//
// DynamicListPage, not ListPage: a plain list page's rows get fuzzy-filtered and REORDERED against
// their titles the moment the user types in the palette search box (host ListViewModel) — fatal
// when titles are glyph runs. IDynamicListPage bypasses the host filter entirely; the search text
// is simply ignored.
internal sealed partial class VisualizerPage : DynamicListPage, INotifyItemsChanged, IDisposable
{
    private const int BandCount = 8;
    private const int CellCount = 32; // bar width in glyph cells; 8 sub-steps per cell

    // Keep in sync with SpectrumCapture's band folding — used only for the row labels.
    private const float MinFrequency = 40f;
    private const float MaxFrequency = 16000f;

    // U+258F LEFT ONE EIGHTH BLOCK — the all-quiet sliver every bar decays to (never an empty
    // string: a row whose title vanishes would collapse its text block).
    private const string BaselineBar = "\u258F";

    private readonly SpectrumSource _source;
    private readonly ListItem[] _rows;
    private readonly IListItem[] _items;

    // Render state — touched only from RenderLoop ticks (serialized; loop dispose drains).
    private readonly float[] _bands = new float[BandCount];
    private readonly float[] _levels = new float[BandCount];
    private readonly string[] _lastFrames = new string[BandCount];
    private readonly char[] _scratch = new char[CellCount];

    // Lifecycle state — guarded by _gate (host add/remove vs. Dispose).
    private readonly object _gate = new();
    private IDisposable? _lease;
    private RenderLoop? _loop;
    private int _observers;
    private bool _disposed;

    // Same visibility contract as the dock band: never raised, refcounted adds.
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
            Log.Info("Page", "Page visible — capture running");
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
            Log.Info("Page", "Page hidden — capture stopped");
        }
    }

    public VisualizerPage(SpectrumSource source)
    {
        _source = source;
        Id = "com.costafotiadis.visualizer.page.spectrum";
        Title = Resources.Command_Visualizer;
        Icon = new IconInfo("\uE8D6"); // Segoe Audio glyph

        var noOp = new NoOpCommand();
        _rows = new ListItem[BandCount];
        for (var k = 0; k < BandCount; k++)
        {
            _rows[k] = new ListItem(noOp)
            {
                Title = BaselineBar,
                Subtitle = BandLabel(k),
                Icon = new IconInfo(string.Empty),
            };
            _lastFrames[k] = BaselineBar;
        }

        _items = [.. _rows];
    }

    // Same instances forever — the per-item mutation channel depends on it.
    public override IListItem[] GetItems() => _items;

    // The rows are a canvas, not search results — typing filters nothing.
    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
    }

    private void StartLocked()
    {
        _lease = _source.Acquire();
        _loop = new RenderLoop("Page", RenderFrame);
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
        for (var k = 0; k < BandCount; k++)
        {
            _lastFrames[k] = BaselineBar;
            _rows[k].Title = BaselineBar;
        }
    }

    // RenderLoop tick — pool thread, exceptions handled by the loop.
    private bool RenderFrame()
    {
        var hasAudio = _source.TryReadBands(_bands);

        for (var k = 0; k < BandCount; k++)
        {
            var target = hasAudio ? Math.Clamp(_bands[k], 0f, 1f) : 0f;
            _levels[k] = target > _levels[k] ? target : _levels[k] * 0.72f;

            var frame = RenderBar(_levels[k]);
            if (!string.Equals(frame, _lastFrames[k], StringComparison.Ordinal))
            {
                _lastFrames[k] = frame;
                _rows[k].Title = frame;
            }
        }

        return hasAudio;
    }

    // Level 0..1 → full blocks + one fractional LEFT partial block tip, floor of one sliver.
    private string RenderBar(float level)
    {
        var eighths = Math.Clamp((int)((level * CellCount * 8f) + 0.5f), 1, CellCount * 8);
        var full = eighths / 8;
        var tip = eighths % 8;

        var n = 0;
        for (var i = 0; i < full; i++)
        {
            _scratch[n++] = '\u2588';
        }

        if (tip > 0)
        {
            // U+2589 (7/8) .. U+258F (1/8): code point = U+2588 + (8 - eighths of ink).
            _scratch[n++] = (char)(0x2588 + (8 - tip));
        }

        return new string(_scratch, 0, n);
    }

    // "40 Hz – 85 Hz", "7.6 kHz – 16 kHz" — same log spacing as the capture's folding.
    private static string BandLabel(int band)
    {
        var lo = MinFrequency * MathF.Pow(MaxFrequency / MinFrequency, (float)band / BandCount);
        var hi = MinFrequency * MathF.Pow(MaxFrequency / MinFrequency, (float)(band + 1) / BandCount);
        return Strings.Format(Resources.Band_Range, FormatFrequency(lo), FormatFrequency(hi));
    }

    private static string FormatFrequency(float hz) =>
        hz < 999.5f
            ? Strings.Format(Resources.Unit_Hz, MathF.Round(hz))
            : Strings.Format(Resources.Unit_kHz, MathF.Round(hz / 1000f, 1));

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
