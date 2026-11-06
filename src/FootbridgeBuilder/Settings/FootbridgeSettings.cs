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

        public override void SetDefaults()
        {
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
