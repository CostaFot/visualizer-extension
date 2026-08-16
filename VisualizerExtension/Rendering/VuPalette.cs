using System;

namespace VisualizerExtension;

// The one green -> amber -> red "VU" color ramp for every color surface — today that's the
// VU dock band's dot icon (the removed rows page's level chips used it too). Band levels are
// already perceptual (the capture applies sqrt loudness), so a linear hue walk over the level
// reads right.
//
// Quantized to StepCount discrete steps so surfaces push color only when the step changes:
// unlike title mutations, an icon swap or a tag reassign makes the host re-fetch and rebuild
// view-model state cross-proc, so the quantization IS the throttle (a handful of pushes per
// second during active music instead of one per tick).
internal static class VuPalette
{
    public const int StepCount = 16;

    // Level 0..1 -> palette step. Step 0 is the "off LED" only silence decays to.
    public static int StepFor(float level) => Math.Clamp((int)(level * StepCount), 0, StepCount - 1);

    // Step 0: dim desaturated green — visibly "off" but never transparent (a dock icon that
    // vanishes changes the button's width). Steps 1..15: full-strength hue walk 120° (green)
    // down to 0° (red).
    public static (byte R, byte G, byte B) Rgb(int step)
    {
        if (step <= 0)
        {
            return HsvToRgb(120f, 0.45f, 0.35f);
        }

        var t = (step - 1) / (float)(StepCount - 2);
        return HsvToRgb(120f * (1f - t), 0.85f, 0.95f);
    }

    // Standard HSV -> RGB, h in degrees 0..360, s/v in 0..1.
    private static (byte R, byte G, byte B) HsvToRgb(float h, float s, float v)
    {
        var c = v * s;
        var x = c * (1f - MathF.Abs(((h / 60f) % 2f) - 1f));
        var m = v - c;

        var (r, g, b) = (int)(h / 60f) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return (ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(float channel) => (byte)((channel * 255f) + 0.5f);
}
