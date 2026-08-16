namespace VisualizerExtension;

// A dock-title render style: a pure mapping from smoothed band levels to the glyph string that
// becomes the button title. One instance per band; Render runs only on that band's RenderLoop
// ticks (serialized), so implementations may reuse scratch buffers.
//
// Contract: Render(levels) receives exactly BarCount levels in 0..1 and must return a frame with
// the same pixel width every time — the title budget is MaxWidth=100 with CharacterEllipsis, and
// any advance-width variation makes the button breathe (see the braille blank-cell trap).
// Baseline is the all-quiet frame: the first paint and what the band resets to when hidden.
internal interface ISpectrumRenderer
{
    int BarCount { get; }

    string Baseline { get; }

    string Render(float[] levels);
}
