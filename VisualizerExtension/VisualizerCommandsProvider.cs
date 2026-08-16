using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VisualizerExtension.Properties;

namespace VisualizerExtension;

public partial class VisualizerCommandsProvider : CommandProvider
{
    // The band is the whole product; the top-level palette entry only exists so the extension has a
    // discoverable face (and a shortcut to the volume mixer).
    private readonly VisualizerDockBand _band = new();

    private readonly ICommandItem[] _commands;
    private readonly ICommandItem[] _dockBands;

    public VisualizerCommandsProvider()
    {
        Id = "com.costafotiadis.visualizer";
        DisplayName = Resources.Extension_DisplayName;
        Icon = new IconInfo("\uE8D6"); // Segoe Audio glyph — replace with a PNG for the Store

        _commands = [
            new CommandItem(new OpenVolumeMixerCommand())
            {
                Title = Resources.Command_Visualizer,
                Subtitle = Resources.Command_Visualizer_Subtitle,
            },
        ];

        _dockBands = [
            new CommandItem(_band) { Title = Resources.Band_Title },
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => _dockBands;

    public override void Dispose()
    {
        _band.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
