using System;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VisualizerExtension.Properties;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace VisualizerExtension;

// TODO #11: "Test visualizer" — plays the built-in tone ladder + sweep (Audio/TestSignal.cs)
// through the default output so users can watch every bar respond without hunting for music.
// Playback goes through the WinRT MediaPlayer: dependency-free, no UI affinity, and it renders to
// the same default endpoint the loopback capture listens to, so the visualizer sees the signal by
// construction. One process-wide player behind a gate — invoking again mid-signal restarts from
// the top instead of layering a second signal (sites may create as many command instances as
// they like).
internal sealed partial class PlayTestSignalCommand : InvokableCommand
{
    private static readonly object _gate = new();
    private static MediaPlayer? _player;

    public PlayTestSignalCommand()
    {
        Name = Resources.Action_PlayTestSignal;
        Icon = new IconInfo("\uE768"); // Segoe Play glyph
    }

    public override CommandResult Invoke()
    {
        try
        {
            lock (_gate)
            {
                _player?.Dispose();
                _player = new MediaPlayer
                {
                    Source = MediaSource.CreateFromStream(CreateStream(TestSignal.Wav), "audio/wav"),
                };
                _player.Play();
            }
        }
        catch (Exception ex)
        {
            // Degrade-and-log: a broken media pipeline must not take the host's Invoke call down.
            Log.Error("TestSignal", $"Playback failed: {ex.Message}");
        }

        return CommandResult.KeepOpen();
    }

    private static InMemoryRandomAccessStream CreateStream(byte[] wav)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(wav);
            // Completes on the thread pool (in-memory, no UI affinity) — safe to block once here.
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.DetachStream();
        }

        return stream;
    }
}
