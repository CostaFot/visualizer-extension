using System;

namespace VisualizerExtension;

// The built-in test signal — a runtime synthesis of tools/spectrum-test.wav, same math
// as tools/generate-spectrum-test.ps1 (keep the two in sync): one 1.2 s sine tone at the
// geometric center of each of the 8 dock bands in order (lighting one bar at a time, left to
// right), then an 8 s logarithmic sweep 40 Hz -> 16 kHz gliding a single peak across all bars.
// 20 ms fades on every segment avoid clicks. Synthesizing beats packaging the wav as Content:
// nothing new in the MSIX, and the signal can never drift from the generator. Baked once on
// first use (~1.5 MB: 17.6 s of 16-bit mono PCM at 44.1 kHz) and cached for the process
// lifetime.
internal static class TestSignal
{
    // Matches SpectrumCapture's analysis range and the blocks dock band's BarCount — the ladder
    // puts one tone at each band's geometric center.
    private const int BandCount = 8;
    private const double MinFrequency = 40;
    private const double MaxFrequency = 16000;

    private const int SampleRate = 44100;
    private const double ToneSeconds = 1.2;
    private const double SweepSeconds = 8;
    private const double Amplitude = 0.35;
    private const double FadeSeconds = 0.02;

    private static readonly Lazy<byte[]> _wav = new(Build);

    // The complete signal as an in-memory 16-bit mono WAV file (RIFF header included).
    public static byte[] Wav => _wav.Value;

    private static byte[] Build()
    {
        var toneSamples = (int)(SampleRate * ToneSeconds);
        var sweepSamples = (int)(SampleRate * SweepSeconds);
        var totalSamples = (BandCount * toneSamples) + sweepSamples;

        var wav = new byte[44 + (totalSamples * 2)];
        WriteWavHeader(wav, totalSamples);
        var offset = 44;

        // The tone ladder: geometric band centers, matching SpectrumCapture's log spacing.
        for (var band = 0; band < BandCount; band++)
        {
            var frequency = MinFrequency * Math.Pow(MaxFrequency / MinFrequency, (band + 0.5) / BandCount);
            for (var i = 0; i < toneSamples; i++)
            {
                WriteSample(wav, ref offset, Math.Sin(2 * Math.PI * frequency * i / SampleRate), i, toneSamples);
            }
        }

        // The log sweep, phase-accumulated so the frequency glide is continuous.
        var k = Math.Log(MaxFrequency / MinFrequency);
        var phase = 0.0;
        for (var i = 0; i < sweepSamples; i++)
        {
            var frequency = MinFrequency * Math.Exp(k * i / sweepSamples);
            phase += 2 * Math.PI * frequency / SampleRate;
            WriteSample(wav, ref offset, Math.Sin(phase), i, sweepSamples);
        }

        return wav;
    }

    private static void WriteSample(byte[] wav, ref int offset, double sine, int index, int count)
    {
        var fade = Math.Min(1.0, Math.Min(
            index / (SampleRate * FadeSeconds),
            (count - index) / (SampleRate * FadeSeconds)));
        var value = (short)Math.Round(Amplitude * fade * sine * 32767);
        wav[offset++] = (byte)value;
        wav[offset++] = (byte)((uint)value >> 8);
    }

    private static void WriteWavHeader(byte[] wav, int sampleCount)
    {
        var dataLen = sampleCount * 2;
        WriteAscii(wav, 0, "RIFF");
        WriteInt32(wav, 4, 36 + dataLen);
        WriteAscii(wav, 8, "WAVEfmt ");
        WriteInt32(wav, 16, 16); // fmt chunk length
        WriteInt16(wav, 20, 1); // PCM
        WriteInt16(wav, 22, 1); // mono
        WriteInt32(wav, 24, SampleRate);
        WriteInt32(wav, 28, SampleRate * 2); // byte rate
        WriteInt16(wav, 32, 2); // block align
        WriteInt16(wav, 34, 16); // bits per sample
        WriteAscii(wav, 36, "data");
        WriteInt32(wav, 40, dataLen);
    }

    private static void WriteAscii(byte[] wav, int offset, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            wav[offset + i] = (byte)text[i];
        }
    }

    private static void WriteInt32(byte[] wav, int offset, int value)
    {
        wav[offset] = (byte)value;
        wav[offset + 1] = (byte)(value >> 8);
        wav[offset + 2] = (byte)(value >> 16);
        wav[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteInt16(byte[] wav, int offset, short value)
    {
        wav[offset] = (byte)value;
        wav[offset + 1] = (byte)((uint)value >> 8);
    }
}
