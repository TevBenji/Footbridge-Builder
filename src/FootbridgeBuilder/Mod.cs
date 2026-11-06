// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using FootbridgeBuilder.Settings;
using FootbridgeBuilder.Tool;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace FootbridgeBuilder
{
    public sealed class Mod : IMod
    {
        internal static readonly ILog Logger = LogManager.GetLogger(nameof(FootbridgeBuilder)).SetShowsErrorsInUI(false);

        internal static FootbridgeSettings Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Logger.Info("Footbridge Builder loading");

            // Each area registers inside its own try/catch, so a broken one is logged and never takes the game down.
            Register("settings", () =>
            {
                Settings = new FootbridgeSettings(this);
                AssetDatabase.global.LoadSettings(nameof(FootbridgeBuilder), Settings, new FootbridgeSettings(this));
                Settings.RegisterInOptionsUI();
                // Before any GetAction: the actions come from the binding properties.
                Settings.RegisterKeyBindings();
                GameManager.instance.localizationManager.AddSource("en-US", Settings.Labels());
            });
            Register("tool", () => updateSystem.UpdateAt<FootbridgeToolSystem>(SystemUpdatePhase.ToolUpdate));
            Register("tool hotkey", () => updateSystem.UpdateAt<FootbridgeHotkeySystem>(SystemUpdatePhase.ToolUpdate));
            Register("panel bindings", () => updateSystem.UpdateAt<Panel.PanelSystem>(SystemUpdatePhase.UIUpdate));
#if FB_DEV
            Register("dev harness", Dev.DevHarness.StartIfAsked);
#endif
        }

        public void OnDispose()
        {
            Register("removing the settings", () => Settings?.UnregisterInOptionsUI());
            Settings = null;
        }

        private static void Register(string area, Action action)
        {
            try { action(); }
            catch (Exception exception) { Logger.Error(exception, "Footbridge Builder: " + area + " failed"); }
        }
    }
}
