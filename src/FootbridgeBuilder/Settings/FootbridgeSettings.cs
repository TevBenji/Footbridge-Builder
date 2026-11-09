// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Game.Input;
using Game.Modding;
using Game.Settings;

namespace FootbridgeBuilder.Settings
{
    /// <summary>Saved settings and the Options page. Property names are save format: name them once and keep them.</summary>
    [FileLocation("ModsSettings/FootbridgeBuilder/FootbridgeBuilder")]
    [SettingsUIKeyboardAction(ToggleToolAction, ActionType.Button)]
    public sealed class FootbridgeSettings : ModSetting
    {
        public const string ToggleToolAction = "ToggleTool";

        private const string Section = "Main";
        private const string Group = "Footbridge";

        public FootbridgeSettings(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        [SettingsUIKeyboardBinding(BindingKeyboard.F, ToggleToolAction, ctrl: true, shift: true)]
        [SettingsUISection(Section, Group)]
        public ProxyBinding ToggleToolBinding { get; set; }

        // Set from the panel, so they stay off the Options page.
        [SettingsUIHidden] public string Design { get; set; }
        [SettingsUIHidden] public string PathName { get; set; }
        [SettingsUIHidden] public bool RemoveCrossings { get; set; }
        [SettingsUIHidden] public int CrossingReach { get; set; }
        [SettingsUIHidden] public float ExtraClearance { get; set; }
        // The panel's top-left corner as a fraction of the screen, so a new resolution can't lose it; below 0 is its usual place.
        [SettingsUIHidden] public float PanelX { get; set; }
        [SettingsUIHidden] public float PanelY { get; set; }
        [SettingsUIHidden] public bool PanelCollapsed { get; set; }

        public override void SetDefaults()
        {
            Design = "Auto";
            PathName = "Pavement Path";
            RemoveCrossings = true;
            CrossingReach = 60;
            ExtraClearance = 0.5f;
            PanelX = PanelY = -1f;
            PanelCollapsed = false;
        }

        /// <summary>
        /// English labels for the Options page. The game builds its label ids from type and property names, so without these
        /// the page shows the raw ids.
        /// </summary>
        public MemorySource Labels()
        {
            return new MemorySource(new Dictionary<string, string>
            {
                [GetSettingsLocaleID()] = "Footbridge Builder",
                [GetOptionTabLocaleID(Section)] = "Footbridge Builder",
                [GetOptionGroupLocaleID(Group)] = "Footbridge Builder",
                [GetBindingMapLocaleID()] = "Footbridge Builder",
                [GetBindingKeyLocaleID(ToggleToolAction)] = "Open Footbridge Builder",
                [GetOptionLabelLocaleID(nameof(ToggleToolBinding))] = "Open Footbridge Builder",
                [GetOptionDescLocaleID(nameof(ToggleToolBinding))] = "Opens or closes the Footbridge Builder tool.",
            });
        }
    }
}
