# Visualizer for Command Palette

[![GitHub release](https://img.shields.io/github/v/release/CostaFot/visualizer-extension?style=flat-square&logo=github&label=release)](https://github.com/CostaFot/visualizer-extension/releases/latest)
[![GitHub downloads](https://img.shields.io/github/downloads/CostaFot/visualizer-extension/total?style=flat-square&logo=github&label=downloads)](https://github.com/CostaFot/visualizer-extension/releases)

<img src="listing/visualizer_main_listing.png" width="600"/>

A Windows 11 [Command Palette](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/overview) extension that adds a cool little visualizer on the Command Palette dock.

## Requirements
[PowerToys](https://github.com/microsoft/PowerToys) 0.100 or later, with Command Palette enabled

## Installation

### Microsoft Store

<a href="https://apps.microsoft.com/detail/9P2R1MXQP49Z" target="_self">
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>

<!-- TODO after the WinGet submission is merged: uncomment.
### WinGet

```powershell
winget install --id CostaFotiadis.VisualizerForCommandPalette
```
-->

..or download the `.msixbundle` from the [latest release](https://github.com/CostaFot/visualizer-extension/releases/latest). winget when I get to it.

## Features

### Dock bands

Three pinnable styles — block bars, braille, and bars with a VU-colored dot.

<img src="listing/screenshot_dock_all.png" width="400"/>
<img src="listing/screenshot_dock_braille.png" width="400"/>
<img src="listing/screenshot_dock_default.png" width="400"/>

### Canvas

Click a band for the full in-palette visualizer — switchable in settings.

<img src="listing/screenshot_canvas_bars.png" width="500"/>

<img src="listing/screenshot_oscilloscope.png" width="500"/>

## FAQ

**Do you listen/record my microphone while I talk to my mom?**

Thrilling. No.

**Some audio doesn't show up?**

Exclusive-mode and DRM audio never reaches loopback.

**Is there telemetry?**

No.

## License

[MIT](LICENSE)
