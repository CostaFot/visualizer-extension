using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VisualizerExtension.Properties;

namespace VisualizerExtension;

// The hub page (mirrors AgentsPanelExtension's UsagePage shape, minus the live state): the single
// top-level "Visualizer" command opens this static menu of the extension's destinations — the
// canvas visualizer, the v1 rows page, the volume mixer, and settings. Nothing here ticks, so
// there are no lifecycle hooks and no capture lease — the visualizer pages acquire their own when
// opened. Items are built once and returned forever (the stable-instance rule), and a plain
// ListPage is fine: typing just filters a four-row menu, there is nothing to scramble.
internal sealed partial class VisualizerHubPage : ListPage
{
    private readonly IListItem[] _items;

    public VisualizerHubPage(VisualizerCanvasPage canvasPage, VisualizerPage rowsPage)
    {
        Id = "com.costafotiadis.visualizer.page.hub";
        Title = Resources.Command_Visualizer;
        Icon = new IconInfo("\uE8D6"); // Segoe Audio glyph

        _items = [
            new ListItem(canvasPage) { Title = Resources.Hub_OpenVisualizer },
            new ListItem(rowsPage) { Title = Resources.Command_Visualizer_Rows },
            new ListItem(new OpenVolumeMixerCommand()) { Title = Resources.Action_OpenVolumeMixer },
            new ListItem(VisualizerSettingsManager.Instance.Settings.SettingsPage)
            {
                Title = Resources.Command_Settings,
                Subtitle = Resources.Command_Settings_Subtitle,
                Icon = new IconInfo("\uE713"), // Segoe Settings gear glyph
            },
        ];
    }

    public override IListItem[] GetItems() => _items;
}
