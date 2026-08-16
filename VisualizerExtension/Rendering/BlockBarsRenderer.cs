namespace VisualizerExtension;

// The original style: one vertical block glyph (U+2581..U+2588) per band, 8 levels each.
internal sealed class BlockBarsRenderer : ISpectrumRenderer
{
    // 8 is the measured maximum that fits the host's title budget: TitleText is 12px "Segoe UI"
    // with MaxWidth=100 and CharacterEllipsis (DockItemControl.xaml), Segoe UI has no block
    // elements so DirectWrite falls back to Segoe UI Symbol, where U+2581..U+2588 advance
    // 11.256 px at 12 px — 8 bars = 90.0 px fits, 9 = 101.3 px already trims to "…".
    private const int Bars = 8;

    private readonly char[] _frame = new char[Bars];

    public int BarCount => Bars;

    // U+2581 LOWER ONE EIGHTH BLOCK repeated — the all-quiet frame.
    public string Baseline { get; } = new((char)0x2581, Bars);

    public string Render(float[] levels)
    {
        for (var i = 0; i < Bars; i++)
        {
            _frame[i] = (char)(0x2581 + (int)(levels[i] * 7.99f));
        }

        return new string(_frame);
    }
}
