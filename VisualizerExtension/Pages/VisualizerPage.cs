using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VisualizerExtension.Properties;
using Windows.Foundation;

namespace VisualizerExtension;

// The v1 rows visualizer: one row per frequency band — static frequency label (title) first,
// then a smooth horizontal bar growing left to right (subtitle: the palette renders it BESIDE
// the title, after it, so the moving edge is always the row's last text and the label never
// shifts), then the band's color chip (tag).
//
// Rendering rides the SAME per-item mutation channel as the dock band: stable ListItem instances,
// mutate .Subtitle per frame, push-only-on-change, ItemsChanged never raised — the palette's
// ListItemViewModel handles per-item Title/Subtitle property changes and repaints just that row
// (verified in the host source). Bars are built from Block Elements: full blocks U+2588 plus one LEFT
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
    // string: an empty subtitle collapses and the row would reflow when the bar reappears).
    private const string BaselineBar = "\u258F";

    // TODO #3: each row carries one tag as a color chip running the VuPalette ramp with the
    // band's level — one cached single-tag array per step, shared by every row (the host builds
    // its own per-row tag view models from these; the Tag models are only ever read). The chip
    // glyph is inked in the pill's own background color so it reads as a solid swatch, while
    // keeping the tag a stable width (an empty-text tag collapses to a sliver). Reassigning
    // .Tags makes the host rebuild that row's tag view models (TagViewModel reads once — there
    // is no per-tag property channel), so chips update push-only-on-step-change.
    private static readonly ITag[][] LevelTags = BuildLevelTags();

    private readonly SpectrumSource _source;
    private readonly ListItem[] _rows;
    private readonly IListItem[] _items;

    // Render state — touched only from RenderLoop ticks (serialized; loop dispose drains).
    private readonly float[] _bands = new float[BandCount];
    private readonly float[] _levels = new float[BandCount];
    private readonly string[] _lastFrames = new string[BandCount];
    private readonly int[] _tagSteps = new int[BandCount];
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
            // Label first, bar second: the title is the static frequency label and the SUBTITLE
            // carries the animated bar (the palette renders the subtitle beside the title, after
            // it) — with the bar first, the label slid left/right on every width change.
            _rows[k] = new ListItem(noOp)
            {
                Title = BandLabel(k),
                Subtitle = BaselineBar,
                Icon = new IconInfo(string.Empty),
                Tags = LevelTags[0],
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
            _rows[k].Subtitle = BaselineBar;
            if (_tagSteps[k] != 0)
            {
                _tagSteps[k] = 0;
                _rows[k].Tags = LevelTags[0];
            }
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
                _rows[k].Subtitle = frame;
            }

            var step = VuPalette.StepFor(_levels[k]);
            if (step != _tagSteps[k])
            {
                _tagSteps[k] = step;
                _rows[k].Tags = LevelTags[step];
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

    private static ITag[][] BuildLevelTags()
    {
        // U+25CF BLACK CIRCLE — never rendered as ink (foreground == background), it only gives
        // the chip its fixed width. Built from the code point, not an escape (see AGENTS.md on
        // tooling mangling glyph escapes).
        var chip = ((char)0x25CF).ToString();

        var sets = new ITag[VuPalette.StepCount][];
        for (var step = 0; step < sets.Length; step++)
        {
            var (r, g, b) = VuPalette.Rgb(step);
            var color = ColorHelpers.FromRgb(r, g, b);
            sets[step] = [new Tag(chip) { Foreground = color, Background = color }];
        }

        return sets;
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
