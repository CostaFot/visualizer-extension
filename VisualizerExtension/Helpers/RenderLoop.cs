using System;
using System.Threading;

namespace VisualizerExtension;

// Drives a render callback at ~15 fps while audio plays, throttling to 2 Hz after ~3 s of
// silence (the callback reports whether audio was present; still sampling while idle means it
// snaps back the moment audio returns). Shared by the dock band and the palette page so the
// subtle parts live in one place:
//
// - Timer callbacks run on pool threads where an unhandled exception KILLS the process, so every
//   tick is wrapped — the callback can't take the extension down.
// - Dispose stops the timer and WAITS for any in-flight tick to finish, so the owner can tear
//   down whatever the callback touches (its capture lease, its items) immediately after Dispose
//   returns. Callers must therefore never invoke Dispose from inside the callback.
internal sealed class RenderLoop : IDisposable
{
    private const int ActiveIntervalMs = 66;   // ~15 fps while audio plays
    private const int IdleIntervalMs = 500;    // 2 Hz once silence settles
    private const long IdleAfterMs = 3000;     // silence duration before throttling

    private readonly Timer _timer;
    private readonly Func<bool> _renderFrame;
    private readonly string _tag;
    private long _lastAudioTicks;
    private bool _idle;
    private int _disposed;

    // Starts ticking immediately. renderFrame returns true when audio was present this frame.
    public RenderLoop(string tag, Func<bool> renderFrame)
    {
        _tag = tag;
        _renderFrame = renderFrame;
        _lastAudioTicks = Environment.TickCount64;
        _timer = new Timer(_ => Tick(), null, 0, ActiveIntervalMs);
    }

    private void Tick()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            var hasAudio = _renderFrame();
            var now = Environment.TickCount64;
            if (hasAudio)
            {
                _lastAudioTicks = now;
            }

            var idle = !hasAudio && now - _lastAudioTicks > IdleAfterMs;
            if (idle != _idle)
            {
                _idle = idle;
                var interval = idle ? IdleIntervalMs : ActiveIntervalMs;
                _timer.Change(interval, interval);
                Log.Info(_tag, idle ? "Silence — throttled to 2 Hz" : "Audio — back to 15 fps");
            }
        }
        catch (Exception ex)
        {
            // Also covers the benign race where Dispose wins between the guard above and
            // _timer.Change (ObjectDisposedException).
            Log.Error(_tag, "Render tick failed", ex);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        using var drained = new ManualResetEvent(false);
        if (_timer.Dispose(drained))
        {
            drained.WaitOne(1000);
        }
    }
}
