// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using FootbridgeBuilder.Settings;
using Game;
using Game.Input;
using UnityEngine.Scripting;

namespace FootbridgeBuilder.Tool
{
    // ToolBaseSystem stops updating when another tool is active, so opening needs an always-running listener.
    public sealed partial class FootbridgeHotkeySystem : GameSystemBase
    {
        private ProxyAction hotkey;
        private FootbridgeToolSystem tool;
        private float nextErrorLog;

        [Preserve]
        protected override void OnUpdate()
        {
            try
            {
                if (hotkey == null)
                {
                    hotkey = Mod.Settings?.GetAction(FootbridgeSettings.ToggleToolAction);
                    if (hotkey != null) hotkey.shouldBeEnabled = true;
                }
                if (tool == null) tool = World.GetOrCreateSystemManaged<FootbridgeToolSystem>();
                var input = InputManager.instance;
                if (hotkey != null && hotkey.WasPerformedThisFrame() && input != null && !input.hasInputFieldFocus) tool.Toggle();
            }
            catch (Exception exception)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now < nextErrorLog) return;
                nextErrorLog = now + 5f;
                Mod.Logger.Error(exception, "Footbridge Builder: the tool hotkey failed");
            }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            try
            {
                if (hotkey != null) hotkey.shouldBeEnabled = false;
                base.OnDestroy();
            }
            catch (Exception exception) { Mod.Logger.Error(exception, "Footbridge Builder: removing the tool hotkey failed"); }
        }
    }
}
