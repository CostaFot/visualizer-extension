using System.Collections.Generic;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VisualizerExtension.Properties;

namespace VisualizerExtension;

// How VisualizerCanvasPage fills its character canvas. The dock deliberately has NO style
// setting — each dock style is its own registered band and the user picks by pinning.
internal enum PageStyle
{
    VerticalBars,
    Oscilloscope,
}

// Extension settings, surfaced in Command Palette's Settings UI and persisted to
// visualizer.settings.json under the CmdPal settings folder. Singleton, modeled on
// AgentsPanelExtension's UsageSettingsManager. Wired into the host via
// VisualizerCommandsProvider (Settings = VisualizerSettingsManager.Instance.Settings).
//
// ⚠️ Toolkit quirk (same as the reference repo): the adaptive-card field's visible label comes
// from **Description** (ToDictionary maps Description → the card's `label`); Label is only the
// semantic name. Put the user-visible text in Description.
//
// Values are read PULL-STYLE on every render tick — a style change applies on the next frame,
// no restart needed.
internal sealed class VisualizerSettingsManager : JsonSettingsManager
{
    public static readonly VisualizerSettingsManager Instance = new();

    private const string PageStyleBars = "bars";
    private const string PageStyleOscilloscope = "oscilloscope";

    // First choice is the default (ChoiceSetSetting takes choices[0].Value as its default).
    private readonly ChoiceSetSetting _pageStyle = new(
        "pageStyle",
        [
            new ChoiceSetSetting.Choice(Resources.Settings_PageStyle_Bars, PageStyleBars),
            new ChoiceSetSetting.Choice(Resources.Settings_PageStyle_Oscilloscope, PageStyleOscilloscope),
        ])
    {
        Label = Resources.Settings_PageStyle_Label,
        Description = Resources.Settings_PageStyle_Desc,
        IgnoreUnknownValue = true,
    };

    // The canvas fill style; unknown/legacy persisted values (e.g. the removed "spectrogram")
    // fall back to bars.
    public PageStyle PageStyle => _pageStyle.Value switch
    {
        PageStyleOscilloscope => PageStyle.Oscilloscope,
        _ => PageStyle.VerticalBars,
    };

    private VisualizerSettingsManager()
    {
        FilePath = Path.Combine(Utilities.BaseSettingsPath("Microsoft.CmdPal"), "visualizer.settings.json");
        Settings.Add(_pageStyle);
        LoadSettings();
        Settings.SettingsChanged += (_, _) => SaveSettings();
    }
}
