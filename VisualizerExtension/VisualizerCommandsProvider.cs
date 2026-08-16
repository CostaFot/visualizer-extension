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

    // The band is the product; the page is where a click lands (the big in-palette visualizer),
    // and it doubles as the extension's discoverable face in the palette root. Two bands while
    // the TODO #4 spike runs: same spectrum through the block renderer (8 bars x 8 levels) and
    // the braille renderer (22 bars x 4 levels) — pin both to compare readability at dock size;
    // the loser (or a style setting, TODO #13) retires one.
    private readonly VisualizerPage _page;
    private readonly VisualizerDockBand _band;
    private readonly VisualizerDockBand _brailleBand;

    private readonly ICommandItem[] _commands;
    private readonly ICommandItem[] _dockBands;

    public VisualizerCommandsProvider()
    {
        Id = "com.costafotiadis.visualizer";
        DisplayName = Resources.Extension_DisplayName;
        Icon = new IconInfo("\uE8D6"); // Segoe Audio glyph — replace with a PNG for the Store

        _page = new VisualizerPage(_source);
        _band = new VisualizerDockBand(
            _source,
            _page,
            new BlockBarsRenderer(),
            "com.costafotiadis.visualizer.dock.spectrum",
            Resources.Band_Title);
        _brailleBand = new VisualizerDockBand(
            _source,
            _page,
            new BrailleBarsRenderer(),
            "com.costafotiadis.visualizer.dock.spectrum.braille",
            Resources.Band_Title_Braille);

        _commands = [
            new CommandItem(_page)
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
        _page.Dispose();
        _source.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
