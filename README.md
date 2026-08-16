# Visualizer for Command Palette

A PowerToys Command Palette extension that renders a live music/audio spectrum visualizer as a
pinnable **Dock band**. It captures whatever the machine is playing (WASAPI loopback on the
default render endpoint — no microphone, no per-app hooks) and draws ~15 fps spectrum bars as
block glyphs in a dock button.

- 8 log-spaced frequency bands (40 Hz–16 kHz), hand-rolled FFT, no audio dependencies
- Three dock styles, picked by pinning the band you like: block bars (8×8), braille (22×4), and
  blocks with a VU-colored dot icon
- Click a band for the full in-palette canvas visualizer — vertical bars with peak caps or a
  Winamp-style oscilloscope (switchable in settings); right-click for the volume mixer and a
  built-in test signal
- Runs only while a surface is visible; throttles to 2 Hz after a few seconds of silence

The project is feature-complete as of 2026-08-16 — maintenance only, no roadmap (see AGENTS.md).

Build: `dotnet build VisualizerExtension.sln -p:Platform=x64` (the platform flag matters — see
AGENTS.md). Deployment is done from Rider.
