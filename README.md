# Visualizer for Command Palette

[![GitHub release](https://img.shields.io/github/v/release/CostaFot/visualizer-extension?style=flat-square&logo=github&label=release)](https://github.com/CostaFot/visualizer-extension/releases/latest)
[![GitHub downloads](https://img.shields.io/github/downloads/CostaFot/visualizer-extension/total?style=flat-square&logo=github&label=downloads)](https://github.com/CostaFot/visualizer-extension/releases)

<img src="listing/screenshot_dock.png" width="600"/>

A Windows 11 [Command Palette](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/overview) extension that turns whatever your PC is playing into a live spectrum visualizer on the Command Palette dock.

## Requirements
[PowerToys](https://github.com/microsoft/PowerToys) 0.100 or later, with Command Palette enabled

## Installation

<!-- TODO after Store approval: uncomment and fill in the Store ID.
### Microsoft Store

<a href="https://apps.microsoft.com/detail/STORE_ID_HERE" target="_self">
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>
-->

<!-- TODO after the WinGet submission is merged: uncomment.
### WinGet

```powershell
winget install --id CostaFotiadis.VisualizerForCommandPalette
```
-->

Download the `.msixbundle` from the [latest release](https://github.com/CostaFot/visualizer-extension/releases/latest). Store and winget when I get to it.

## Features

### Dock bands

Three pinnable styles — block bars, braille, and bars with a VU-colored dot.

<img src="listing/screenshot_bands.png" width="400"/>

### Canvas

Click a band for the full in-palette visualizer — bars with peak caps or an oscilloscope, switchable in settings.

<img src="listing/screenshot_canvas_bars.png" width="500"/>

<img src="listing/screenshot_oscilloscope.png" width="500"/>

### Test signal

**Test visualizer** plays a built-in tone sweep so you can see it work with nothing queued up.

<img src="listing/screenshot_hub.png" width="500"/>

## FAQ

**Does it use the microphone?**

No. Loopback capture of your default output device — it only hears what your PC is already playing. Read the source code.

**Some audio doesn't show up?**

Exclusive-mode and DRM audio never reaches loopback. The visualizer follows your default output device.

**Is there telemetry?**

No.

## License

[MIT](LICENSE)
