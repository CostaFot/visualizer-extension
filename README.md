# Visualizer for Command Palette

A PowerToys Command Palette extension that renders a live music/audio spectrum visualizer as a
pinnable **Dock band**. It captures whatever the machine is playing (WASAPI loopback on the
default render endpoint — no microphone, no per-app hooks) and draws ~15 fps spectrum bars as
block glyphs in a dock button.

- 10 log-spaced frequency bands (40 Hz–16 kHz), hand-rolled FFT, no audio dependencies
- Runs only while the band is visible; throttles to 2 Hz after a few seconds of silence
- Click the band to open the system volume mixer

Build: `dotnet build VisualizerExtension.sln -p:Platform=x64` (the platform flag matters — see
AGENTS.md). Deployment is done from Rider.
