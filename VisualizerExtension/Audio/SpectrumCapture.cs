using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace VisualizerExtension;

// Captures the default render endpoint via WASAPI loopback on a background thread and serves
// log-spaced spectrum band levels computed with a hand-rolled FFT. Dependency-free: raw COM vtable
// function pointers (delegate* unmanaged), no NAudio, no ComImport RCWs. Loopback on the render
// endpoint needs NO microphone capability for a full-trust packaged desktop app (this extension is
// one). Ported from the proven MediaControlsExtension PR #1 spike (verified live 2026-08).
//
// Lifecycle: the capture thread starts in the constructor and stops on Dispose — the owning band
// creates/disposes an instance as it becomes visible/hidden, so nothing runs while hidden.
//
// Loopback delivers NO packets during silence — "no packet in N ms" IS the silence signal
// (TryReadBands returns false), never something to block on.
internal sealed unsafe partial class SpectrumCapture : IDisposable
{
    private const int FftSize = 2048; // power of two; ~43 ms window at 48 kHz
    private const float MinFrequency = 40f;
    private const float MaxFrequency = 16000f;
    private const int SilenceTimeoutMilliseconds = 250;
    private const float GainDecayPerFrame = 0.995f;

    private const uint ClsContextAll = 0x17;
    private const uint StreamFlagsLoopback = 0x00020000;
    private const uint BufferFlagSilent = 0x2;
    private const long BufferDurationHns = 2_000_000; // 200 ms

    // Vtable slot indices (after IUnknown's 0..2) — vtable order is the ABI, verified against the
    // Windows SDK headers. Only the methods we call are typed.
    private const int GetDefaultAudioEndpointSlot = 4;  // IMMDeviceEnumerator
    private const int ActivateSlot = 3;                 // IMMDevice
    private const int InitializeSlot = 3;               // IAudioClient
    private const int GetMixFormatSlot = 8;             // IAudioClient
    private const int StartSlot = 10;                   // IAudioClient
    private const int GetServiceSlot = 14;              // IAudioClient
    private const int GetBufferSlot = 3;                // IAudioCaptureClient
    private const int ReleaseBufferSlot = 4;            // IAudioCaptureClient
    private const int GetNextPacketSizeSlot = 5;        // IAudioCaptureClient
    private const int ReleaseSlot = 2;                  // IUnknown

    private static readonly Guid MMDeviceEnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid MMDeviceEnumeratorInterfaceId = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid AudioClientInterfaceId = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid AudioCaptureClientInterfaceId = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    private const int RenderDataFlow = 0;    // eRender
    private const int MultimediaRole = 1;    // eMultimedia

    private readonly Lock _gate = new();
    private readonly float[] _ring = new float[FftSize];
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly float[] _hann = new float[FftSize];
    private readonly Thread _thread;
    private int _ringPos;
    private long _lastPacketTicks;
    private volatile int _sampleRate = 48000;
    private volatile bool _stop;
    private float _gain = 1e-4f;

    public SpectrumCapture()
    {
        for (var i = 0; i < FftSize; i++)
        {
            this._hann[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
        }

        this._thread = new(this.CaptureLoop)
        {
            IsBackground = true,
            Name = "Visualizer.Loopback",
        };
        this._thread.Start();
    }

    // Computes band levels (0..1) from the newest captured samples. Returns false when no audio has
    // arrived recently (silence). Single-reader: only the render timer calls this.
    public bool TryReadBands(float[] bands)
    {
        if (Environment.TickCount64 - Volatile.Read(ref this._lastPacketTicks) > SilenceTimeoutMilliseconds)
        {
            return false;
        }

        lock (this._gate)
        {
            var pos = this._ringPos;
            for (var i = 0; i < FftSize; i++)
            {
                this._re[i] = this._ring[(pos + i) & (FftSize - 1)] * this._hann[i];
            }
        }

        Array.Clear(this._im);
        Fft(this._re, this._im);

        float sampleRate = this._sampleRate;
        var frameMax = 1e-6f;
        for (var k = 0; k < bands.Length; k++)
        {
            // Log-spaced band edges (linear bins make everything look like bass).
            var lo = MinFrequency * MathF.Pow(MaxFrequency / MinFrequency, (float)k / bands.Length);
            var hi = MinFrequency * MathF.Pow(MaxFrequency / MinFrequency, (float)(k + 1) / bands.Length);
            var loBin = Math.Clamp((int)(lo * FftSize / sampleRate), 1, (FftSize / 2) - 1);
            var hiBin = Math.Clamp((int)(hi * FftSize / sampleRate), loBin + 1, FftSize / 2);

            var magnitude = 0f;
            for (var bin = loBin; bin < hiBin; bin++)
            {
                var m = MathF.Sqrt((this._re[bin] * this._re[bin]) + (this._im[bin] * this._im[bin]));
                magnitude = MathF.Max(magnitude, m);
            }

            // Gentle treble tilt so bass energy doesn't dwarf everything else.
            magnitude *= MathF.Pow(hi / MinFrequency, 0.3f);
            bands[k] = magnitude;
            frameMax = MathF.Max(frameMax, magnitude);
        }

        // Slow auto-gain: normalize against a decaying running maximum; sqrt for perceived loudness.
        this._gain = MathF.Max(this._gain * GainDecayPerFrame, frameMax);
        for (var k = 0; k < bands.Length; k++)
        {
            bands[k] = MathF.Sqrt(Math.Clamp(bands[k] / this._gain, 0f, 1f));
        }

        return true;
    }

    public void Dispose()
    {
        this._stop = true;
        this._thread.Join(500);
    }

    private void CaptureLoop()
    {
        _ = CoInitializeEx(0, 0); // MTA; RPC_E_CHANGED_MODE is fine to ignore
        while (!this._stop)
        {
            nint enumerator = 0;
            nint device = 0;
            nint audioClient = 0;
            nint captureClient = 0;
            try
            {
                Marshal.ThrowExceptionForHR(CoCreateInstance(
                    MMDeviceEnumeratorClassId,
                    0,
                    ClsContextAll,
                    MMDeviceEnumeratorInterfaceId,
                    out enumerator));

                var getDefaultAudioEndpoint = (delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)GetMethod(enumerator, GetDefaultAudioEndpointSlot);
                nint devicePointer = 0;
                Marshal.ThrowExceptionForHR(getDefaultAudioEndpoint(enumerator, RenderDataFlow, MultimediaRole, &devicePointer));
                device = devicePointer;

                var activate = (delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)GetMethod(device, ActivateSlot);
                var audioClientId = AudioClientInterfaceId;
                nint audioClientPointer = 0;
                Marshal.ThrowExceptionForHR(activate(device, &audioClientId, ClsContextAll, 0, &audioClientPointer));
                audioClient = audioClientPointer;

                var getMixFormat = (delegate* unmanaged[Stdcall]<nint, nint*, int>)GetMethod(audioClient, GetMixFormatSlot);
                nint format = 0;
                Marshal.ThrowExceptionForHR(getMixFormat(audioClient, &format));

                int channels;
                int sampleRate;
                try
                {
                    // WAVEFORMATEX field offsets: nChannels @2, nSamplesPerSec @4, wBitsPerSample @14.
                    channels = *(ushort*)(format + 2);
                    sampleRate = (int)*(uint*)(format + 4);
                    var bitsPerSample = *(ushort*)(format + 14);
                    if (channels < 1 || bitsPerSample != 32)
                    {
                        // Shared-mode mix format is float32 in practice; bail and retry.
                        throw new InvalidOperationException("Unexpected mix format.");
                    }

                    var initialize = (delegate* unmanaged[Stdcall]<nint, int, uint, long, long, nint, nint, int>)GetMethod(audioClient, InitializeSlot);
                    Marshal.ThrowExceptionForHR(initialize(audioClient, 0, StreamFlagsLoopback, BufferDurationHns, 0, format, 0));
                }
                finally
                {
                    Marshal.FreeCoTaskMem(format);
                }

                this._sampleRate = sampleRate;

                var getService = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)GetMethod(audioClient, GetServiceSlot);
                var captureClientId = AudioCaptureClientInterfaceId;
                nint captureClientPointer = 0;
                Marshal.ThrowExceptionForHR(getService(audioClient, &captureClientId, &captureClientPointer));
                captureClient = captureClientPointer;

                var start = (delegate* unmanaged[Stdcall]<nint, int>)GetMethod(audioClient, StartSlot);
                Marshal.ThrowExceptionForHR(start(audioClient));

                var getNextPacketSize = (delegate* unmanaged[Stdcall]<nint, uint*, int>)GetMethod(captureClient, GetNextPacketSizeSlot);
                var getBuffer = (delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, nint, nint, int>)GetMethod(captureClient, GetBufferSlot);
                var releaseBuffer = (delegate* unmanaged[Stdcall]<nint, uint, int>)GetMethod(captureClient, ReleaseBufferSlot);

                while (!this._stop)
                {
                    uint packetFrames = 0;
                    Marshal.ThrowExceptionForHR(getNextPacketSize(captureClient, &packetFrames));
                    while (packetFrames > 0 && !this._stop)
                    {
                        byte* data = null;
                        uint frames = 0;
                        uint flags = 0;
                        Marshal.ThrowExceptionForHR(getBuffer(captureClient, &data, &frames, &flags, 0, 0));
                        if ((flags & BufferFlagSilent) == 0 && frames > 0)
                        {
                            this.AppendSamples((float*)data, (int)frames, channels);
                            Volatile.Write(ref this._lastPacketTicks, Environment.TickCount64);
                        }

                        Marshal.ThrowExceptionForHR(releaseBuffer(captureClient, frames));
                        Marshal.ThrowExceptionForHR(getNextPacketSize(captureClient, &packetFrames));
                    }

                    Thread.Sleep(15);
                }
            }
            catch (COMException)
            {
                // Device invalidated / default changed; tear down and rebind.
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                Release(captureClient);
                Release(audioClient);
                Release(device);
                Release(enumerator);
            }

            if (!this._stop)
            {
                Thread.Sleep(500);
            }
        }
    }

    private void AppendSamples(float* data, int frames, int channels)
    {
        lock (this._gate)
        {
            var pos = this._ringPos;
            for (var f = 0; f < frames; f++)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++)
                {
                    sum += data[(f * channels) + c];
                }

                this._ring[pos] = sum / channels;
                pos = (pos + 1) & (FftSize - 1);
            }

            this._ringPos = pos;
        }
    }

    // In-place iterative radix-2 Cooley-Tukey.
    private static void Fft(float[] re, float[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2f * MathF.PI / len;
            var stepRe = MathF.Cos(angle);
            var stepIm = MathF.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                var curRe = 1f;
                var curIm = 0f;
                for (var k = 0; k < len / 2; k++)
                {
                    var even = i + k;
                    var odd = i + k + (len / 2);
                    var tRe = (re[odd] * curRe) - (im[odd] * curIm);
                    var tIm = (re[odd] * curIm) + (im[odd] * curRe);
                    re[odd] = re[even] - tRe;
                    im[odd] = im[even] - tIm;
                    re[even] += tRe;
                    im[even] += tIm;
                    var nextRe = (curRe * stepRe) - (curIm * stepIm);
                    curIm = (curRe * stepIm) + (curIm * stepRe);
                    curRe = nextRe;
                }
            }
        }
    }

    private static nint GetMethod(nint instance, int slot)
    {
        return (*(nint**)instance)[slot];
    }

    private static void Release(nint instance)
    {
        if (instance == 0)
        {
            return;
        }

        var release = (delegate* unmanaged[Stdcall]<nint, uint>)GetMethod(instance, ReleaseSlot);
        _ = release(instance);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid classId,
        nint outer,
        uint classContext,
        in Guid interfaceId,
        out nint instance);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);
}
