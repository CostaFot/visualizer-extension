using System;
using System.Threading;

namespace VisualizerExtension;

// Shares ONE SpectrumCapture between the surfaces that render it (the dock band and the palette
// page — the dock is always visible, so both being live at once is the normal case, not the
// exception). The capture exists only while at least one lease is held: surfaces Acquire() on
// becoming visible and dispose the lease on hiding, so a fully hidden visualizer still costs
// nothing.
//
// The read methods are serialized under the gate because SpectrumCapture assumes a single
// reader (TryReadBands reuses FFT scratch buffers, both readers mutate their auto-gain) — two
// concurrent readers would corrupt each other. The work is microseconds at 2048 samples, so the
// lock is cheap. Callers may pass arrays of different lengths; each call computes its own
// folding/decimation.
internal sealed class SpectrumSource : IDisposable
{
    private readonly object _gate = new();
    private SpectrumCapture? _capture;
    private int _leases;
    private bool _disposed;

    // Start (or keep) capturing; dispose the returned lease to release. Capture starts on the
    // first lease and stops when the last one is released.
    public IDisposable Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (++_leases == 1)
            {
                _capture = new SpectrumCapture();
            }

            Log.Info("Source", $"Lease acquired ({_leases} active)");
            return new Lease(this);
        }
    }

    // Latest band levels (0..1); false during silence or when no lease holds the capture alive.
    public bool TryReadBands(float[] bands)
    {
        lock (_gate)
        {
            return _capture?.TryReadBands(bands) ?? false;
        }
    }

    // Latest waveform snapshot (-1..1, oldest first); same contract as TryReadBands.
    public bool TryReadWaveform(float[] samples)
    {
        lock (_gate)
        {
            return _capture?.TryReadWaveform(samples) ?? false;
        }
    }

    private void Release()
    {
        lock (_gate)
        {
            if (_leases > 0 && --_leases == 0 && !_disposed)
            {
                _capture?.Dispose();
                _capture = null;
            }

            Log.Info("Source", $"Lease released ({_leases} active)");
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
            _capture?.Dispose();
            _capture = null;
        }
    }

    // Idempotent so a surface double-disposing its lease can't steal someone else's refcount.
    private sealed class Lease(SpectrumSource owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release();
            }
        }
    }
}
