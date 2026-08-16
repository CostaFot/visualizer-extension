using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VisualizerExtension.Properties;
using Windows.Foundation;

namespace VisualizerExtension;

// The proper in-palette visualizer (TODO #12): a monospace character canvas with user-selectable
// fill styles (TODO #13, read pull-style from VisualizerSettingsManager on every tick — a change
// applies on the next frame, no restart): classic vertical spectrum bars with slowly-falling peak
// caps (TODO #6), or a Winamp-style oscilloscope
// trace (TODO #14). The canvas is a ContentPage
// with ONE stable PlainTextContent — the host renders it in guaranteed monospace (Cascadia Mono/Consolas)
// and a repaint is a single TextBlock.Text assignment, no parse and no element-tree rebuild. See
// notes/rendering.md for why every alternative (stacked list rows, markdown code block, adaptive
// card SVG) lost.
//
// Render channel: mutate _content.Text per frame; PropChanged rides the host's global 40 ms batch
// (~20-24 fps ceiling — our 15 fps fits under it) and equal frames are deduped on both sides of
// the COM boundary. ItemsChanged is NEVER raised — for content pages that would rebuild the whole
// content control (flicker); GetContent() returns the same instance forever.
//
// Same visibility lifecycle as the other surfaces: the host subscribes ItemsChanged when the page
// loads and unsubscribes when it unloads, so the re-implemented add/remove accessors are the
// de-facto Loaded/Unloaded hooks; observer adds are refcounted, a SpectrumSource lease +
// RenderLoop live from first observer to last.
internal sealed partial class VisualizerCanvasPage : ContentPage, INotifyItemsChanged, IDisposable
{
    private const int BandCount = 20;
    private const int Rows = 14;      // vertical cells; 8 sub-steps each = 112 vertical steps
    private const int BarWidth = 2;   // glyph cells of ink per bar
    private const int GapWidth = 1;   // blank cells between bars (spaces are safe in monospace)
    private const int Columns = (BandCount * (BarWidth + GapWidth)) - GapWidth;
    private const int LineLength = Columns + 1; // + '\n'

    // Peak caps: hold at the recent maximum, then fall slowly (classic EQ gravity).
    private const int PeakHoldTicks = 11;       // ~0.75 s at 15 fps
    private const float PeakFallPerTick = 0.03f;

    // Keep in sync with SpectrumCapture's band folding — used only for the axis labels.
    private const float MinFrequency = 40f;
    private const float MaxFrequency = 16000f;

    private readonly SpectrumSource _source;
    private readonly PlainTextContent _content;
    private readonly IContent[] _contents;
    private readonly string _baselineFrame;

    // Render state — touched only from RenderLoop ticks (serialized; loop dispose drains). The
    // scratch holds the whole canvas: Rows bar lines + one static axis line (written once).
    private readonly float[] _bands = new float[BandCount];
    private readonly float[] _levels = new float[BandCount];
    private readonly float[] _peaks = new float[BandCount];
    private readonly int[] _peakHold = new int[BandCount];
    private readonly char[] _scratch = new char[(Rows * LineLength) + Columns];
    private string _lastFrame;

    private PageStyle _style = PageStyle.VerticalBars;

    // Oscilloscope scratch: one waveform sample per canvas column, fully rewritten each tick.
    private readonly float[] _waveform = new float[Columns];

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
            Log.Info("Canvas", "Page visible — capture running");
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
            Log.Info("Canvas", "Page hidden — capture stopped");
        }
    }

    public VisualizerCanvasPage(SpectrumSource source)
    {
        _source = source;
        Id = "com.costafotiadis.visualizer.page.canvas";
        Title = Resources.Command_Visualizer;
        Icon = new IconInfo("\uE8D6"); // Segoe Audio glyph

        // Newlines are fixed; the axis line (min / geometric-center / max frequency) is static.
        Array.Fill(_scratch, ' ');
        for (var row = 0; row < Rows; row++)
        {
            _scratch[(row * LineLength) + Columns] = '\n';
        }

        WriteAxis(_style);

        _baselineFrame = RenderCanvas();
        _lastFrame = _baselineFrame;

        // ONE stable content instance forever — the per-property mutation channel depends on it.
        _content = new PlainTextContent(_baselineFrame)
        {
            FontFamily = FontFamily.Monospace,
            WrapWords = false,
        };
        _contents = [_content];
    }

    public override IContent[] GetContent() => _contents;

    private void StartLocked()
    {
        _lease = _source.Acquire();
        _loop = new RenderLoop("Canvas", RenderFrame);
    }

    private void StopLocked()
    {
        // Loop first (its Dispose waits out any in-flight tick), then the lease, then the reset —
        // so no tick can repaint over the baseline or read a dead capture.
        _loop?.Dispose();
        _loop = null;
        _lease?.Dispose();
        _lease = null;

        ResetRenderState();
        _lastFrame = _baselineFrame;
        _content.Text = _baselineFrame;
    }

    // Clears all per-style render state so styles never bleed into each other (a fresh style must
    // not start from stale bar levels). The scratch's bar area is fully rewritten by every style
    // each frame, so it needs no clearing.
    private void ResetRenderState()
    {
        Array.Clear(_levels);
        Array.Clear(_peaks);
        Array.Clear(_peakHold);
    }

    // RenderLoop tick — pool thread, exceptions handled by the loop.
    private bool RenderFrame()
    {
        // Pull-style settings read: a style change applies on this very frame.
        var style = VisualizerSettingsManager.Instance.PageStyle;
        if (style != _style)
        {
            _style = style;
            ResetRenderState();
            WriteAxis(style);
        }

        bool hasAudio;
        string frame;
        if (style == PageStyle.Oscilloscope)
        {
            hasAudio = _source.TryReadWaveform(_waveform);
            frame = RenderOscilloscope(hasAudio);
        }
        else
        {
            hasAudio = _source.TryReadBands(_bands);
            frame = RenderBars(hasAudio);
        }

        // Push-only-on-change: identical frames (settled silence) cost nothing cross-proc.
        if (!string.Equals(frame, _lastFrame, StringComparison.Ordinal))
        {
            _lastFrame = frame;
            _content.Text = frame;
        }

        return hasAudio;
    }

    private string RenderBars(bool hasAudio)
    {
        for (var k = 0; k < BandCount; k++)
        {
            // Fast attack, exponential decay — same feel as the dock band.
            var target = hasAudio ? Math.Clamp(_bands[k], 0f, 1f) : 0f;
            _levels[k] = target > _levels[k] ? target : _levels[k] * 0.72f;

            // Peak cap: latch upward, hold, then fall linearly until the bar catches it.
            if (_levels[k] >= _peaks[k])
            {
                _peaks[k] = _levels[k];
                _peakHold[k] = PeakHoldTicks;
            }
            else if (_peakHold[k] > 0)
            {
                _peakHold[k]--;
            }
            else
            {
                _peaks[k] = Math.Max(_levels[k], _peaks[k] - PeakFallPerTick);
            }
        }

        return RenderCanvas();
    }

    // The oscilloscope (TODO #14, from #12's Winamp north star): the newest ~21 ms of raw
    // waveform (SpectrumSource.TryReadWaveform, one sample per canvas column) drawn as a
    // connected trace on the same LED-matrix grid as the bars — each column lights its sample's
    // cell plus the vertical run bridging to the previous column, because a bare one-cell-per-
    // column plot shatters into confetti whenever the wave moves more than one row per column.
    // Pen is the full block (Block Elements are the glyphs rendering.md verifies for this canvas;
    // braille U+28xx is explicitly UNVERIFIED in Cascadia Mono — don't switch pens without
    // measuring), unlit cells keep the faint U+00B7 "off LED" grid. Silence draws the flat
    // centerline, which then dedupes to zero cross-proc cost.
    private string RenderOscilloscope(bool hasAudio)
    {
        var previous = RowOfSample(hasAudio ? _waveform[0] : 0f);
        for (var x = 0; x < Columns; x++)
        {
            var current = RowOfSample(hasAudio ? _waveform[x] : 0f);
            var top = Math.Max(current, previous);
            var bottom = Math.Min(current, previous);
            for (var cell = 0; cell < Rows; cell++)
            {
                // cell counts from the bottom; the scratch is written top line first.
                var offset = ((Rows - 1 - cell) * LineLength) + x;
                _scratch[offset] = cell >= bottom && cell <= top ? (char)0x2588 : (char)0x00B7;
            }

            previous = current;
        }

        return new string(_scratch);
    }

    // Sample -1..1 -> cell row counted from the bottom (positive up).
    private static int RowOfSample(float sample) =>
        Math.Clamp((int)((sample + 1f) * 0.5f * Rows), 0, Rows - 1);

    // (Re)writes the static footer line for the active style: the log frequency axis for the
    // bars, blank for the oscilloscope (a time-domain trace has no frequency axis).
    private void WriteAxis(PageStyle style)
    {
        Array.Fill(_scratch, ' ', Rows * LineLength, Columns);
        if (style != PageStyle.Oscilloscope)
        {
            WriteAxisLabel(FormatFrequency(MinFrequency), 0f);
            WriteAxisLabel(FormatFrequency(MathF.Sqrt(MinFrequency * MaxFrequency)), 0.5f);
            WriteAxisLabel(FormatFrequency(MaxFrequency), 1f);
        }
    }

    // Draws every bar into the scratch and snapshots it. The viewer's TextBlock has natural line
    // spacing we cannot remove, so stacked cells NEVER touch — the canvas is a discrete LED
    // matrix, not a continuous column, and the rendering leans into that: unlit cells above each
    // bar show a faint middle-dot grid (U+00B7 — the "off LEDs", making the tiling look
    // deliberate), lit cells fill bottom-up with full blocks U+2588 under a LOWER-partial tip
    // U+2581..U+2587 (code point 0x2580 + eighths of ink; floor of one eighth so silent bands
    // keep a baseline sliver), and the peak cap is a centered box-drawing line U+2500 floating in
    // its cell when it sits clear above the bar. The extras are Cascadia Mono/Consolas-covered
    // with uniform advances; built from char codes so no escape can be mangled in transit.
    private string RenderCanvas()
    {
        for (var k = 0; k < BandCount; k++)
        {
            var x = k * (BarWidth + GapWidth);
            var eighths = Math.Clamp((int)((_levels[k] * Rows * 8f) + 0.5f), 1, Rows * 8);
            var peakEighths = Math.Clamp((int)((_peaks[k] * Rows * 8f) + 0.5f), 1, Rows * 8);
            var barTopCell = (eighths - 1) / 8;
            var capCell = (peakEighths - 1) / 8;

            for (var cell = 0; cell < Rows; cell++)
            {
                var ink = eighths - (cell * 8);
                var glyph = ink >= 8 ? '\u2588'
                    : ink > 0 ? (char)(0x2580 + ink)
                    : cell == capCell && capCell > barTopCell ? (char)0x2500
                    : (char)0x00B7;

                // cell counts from the bottom; the scratch is written top line first.
                var offset = ((Rows - 1 - cell) * LineLength) + x;
                for (var w = 0; w < BarWidth; w++)
                {
                    _scratch[offset + w] = glyph;
                }
            }
        }

        return new string(_scratch);
    }

    // Writes a static label onto the axis line, its center anchored at `position` (0..1 across
    // the canvas width), clamped to the edges.
    private void WriteAxisLabel(string label, float position)
    {
        var start = Math.Clamp(
            (int)((position * Columns) - (label.Length / 2f) + 0.5f), 0, Columns - label.Length);
        label.CopyTo(0, _scratch, (Rows * LineLength) + start, label.Length);
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
