using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VisualizerExtension.Properties;

namespace VisualizerExtension;

public partial class VisualizerCommandsProvider : CommandProvider
{
    // One shared capture behind both surfaces (the dock is always visible, so band + page being
    // live at once is the normal case) — see SpectrumSource.
    private readonly SpectrumSource _source = new();

    // The band is the product; the canvas page is where a dock click lands (the big in-palette
    // vertical visualizer, TODO #12). The extension's discoverable face in the palette root is
    // the hub page, which lists the canvas, the v1 rows page, the volume mixer, and settings.
    // BOTH dock bands are permanent (TODO #4 verdict):
    // the same spectrum through the block renderer (8 bars x 8 levels) and the braille renderer
    // (22 bars x 4 levels) — the user picks the dock style by pinning one, the other, or both
    // from the Dock's band manager, so the dock needs no style setting.
    private readonly VisualizerCanvasPage _canvasPage;
    private readonly VisualizerPage _rowsPage;
    private readonly VisualizerHubPage _hubPage;
    private readonly VisualizerDockBand _band;
    private readonly VisualizerDockBand _brailleBand;

    private readonly ICommandItem[] _commands;
    private readonly ICommandItem[] _dockBands;

    public VisualizerCommandsProvider()
    {
        Id = "com.costafotiadis.visualizer";
        DisplayName = Resources.Extension_DisplayName;
        Icon = new IconInfo("\uE8D6"); // Segoe Audio glyph — replace with a PNG for the Store

        // Surface the extension's settings (page style — TODO #13) in the Command Palette
        // Settings UI. See Settings/VisualizerSettingsManager.cs.
        Settings = VisualizerSettingsManager.Instance.Settings;

        _canvasPage = new VisualizerCanvasPage(_source);
        _rowsPage = new VisualizerPage(_source);
        _band = new VisualizerDockBand(
            _source,
            _canvasPage,
            new BlockBarsRenderer(),
            "com.costafotiadis.visualizer.dock.spectrum",
            Resources.Band_Title);
        _brailleBand = new VisualizerDockBand(
            _source,
            _canvasPage,
            new BrailleBarsRenderer(),
            "com.costafotiadis.visualizer.dock.spectrum.braille",
            Resources.Band_Title_Braille);

        _hubPage = new VisualizerHubPage(_canvasPage, _rowsPage);

        // The palette face is the hub (mirrors AgentsPanelExtension): one top-level command
        // opening the menu of destinations — canvas, rows, volume mixer, settings. Dock clicks
        // still land straight on the canvas page.
        _commands = [
            new CommandItem(_hubPage)
            {
                Title = Resources.Command_Visualizer,
                Subtitle = Resources.Command_Visualizer_Subtitle,
            },
        ];

        _dockBands = [
            new CommandItem(_band) { Title = Resources.Band_Title },
            new CommandItem(_brailleBand) { Title = Resources.Band_Title_Braille },
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => _dockBands;

    public override void Dispose()
    {
        _band.Dispose();
        _brailleBand.Dispose();
        _canvasPage.Dispose();
        _rowsPage.Dispose();
        _source.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
