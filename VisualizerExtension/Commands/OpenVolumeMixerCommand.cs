using Microsoft.CommandPalette.Extensions.Toolkit;
using VisualizerExtension.Properties;

namespace VisualizerExtension;

// Click action for the visualizer dock button (and the top-level palette entry): open the system
// volume mixer. An InvokableCommand on a dock button executes in place — the palette window only
// opens for page commands — so this is the natural "do something useful" click for a band whose
// pixels are all spectrum bars.
internal sealed partial class OpenVolumeMixerCommand : InvokableCommand
{
    public OpenVolumeMixerCommand()
    {
        Name = Resources.Action_OpenVolumeMixer;
        Icon = new IconInfo("\uE767"); // Segoe Volume glyph
    }

    public override CommandResult Invoke()
    {
        _ = ShellHelpers.OpenInShell("ms-settings:apps-volume");
        return CommandResult.KeepOpen();
    }
}
