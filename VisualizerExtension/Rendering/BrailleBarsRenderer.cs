namespace VisualizerExtension;

// The high-resolution spike (TODO #4): braille patterns (U+2800–U+28FF) give a 2x4 dot matrix
// per character — 2 bars per glyph at 4 vertical levels each, the trick TUI tools like btop use.
// Measured 2026-08-16 in Segoe UI Symbol (the DirectWrite fallback serving the dock's 12px
// "Segoe UI" title): dotted braille cells advance 9.041 px → 11 cells = 99.5 px fits
// MaxWidth=100 → 22 bars vs the blocks' 8, trading vertical resolution (4 levels vs 8).
internal sealed class BrailleBarsRenderer : ISpectrumRenderer
{
    private const int Cells = 11;

    // ⚠️ Measured trap: blank U+2800 advances 7.811 px — narrower than every dotted cell
    // (9.041 px) — so emitting it makes the button breathe horizontally. Every column therefore
    // keeps its bottom dot lit as the floor (the braille analogue of the U+2581 baseline), which
    // is why both mask tables start at one dot, not zero.
    //
    // Cumulative dot masks indexed by (lit dots - 1), built bottom→top. Left column is dots
    // 7,3,2,1 (0x40,0x04,0x02,0x01); right column is dots 8,6,5,4 (0x80,0x20,0x10,0x08).
    private static readonly int[] LeftColumn = [0x40, 0x44, 0x46, 0x47];
    private static readonly int[] RightColumn = [0x80, 0xA0, 0xB0, 0xB8];

    private readonly char[] _frame = new char[Cells];

    public int BarCount => Cells * 2;

    // U+28C0 (dots 7+8: both bottom dots lit) repeated — the all-quiet frame.
    public string Baseline { get; } = new((char)0x28C0, Cells);

    public string Render(float[] levels)
    {
        for (var cell = 0; cell < Cells; cell++)
        {
            var left = LeftColumn[(int)(levels[cell * 2] * 3.99f)];
            var right = RightColumn[(int)(levels[cell * 2 + 1] * 3.99f)];
            _frame[cell] = (char)(0x2800 | left | right);
        }

        return new string(_frame);
    }
}
