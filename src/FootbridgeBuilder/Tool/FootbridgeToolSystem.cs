// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Linq;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Tools;
using Unity.Jobs;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;

namespace FootbridgeBuilder.Tool
{
    /// <summary>The footbridge tool. For now it only opens and closes with its panel; Esc closes it.</summary>
    public sealed partial class FootbridgeToolSystem : ToolBaseSystem
    {
        private float nextErrorLog;

        public ToolState State { get; } = new ToolState();
        public override string toolID => "Footbridge Builder";
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;
        internal bool IsActive => m_ToolSystem != null && m_ToolSystem.activeTool == this;

        [Preserve]
        protected override void OnCreate()
        {
            try { base.OnCreate(); }
            catch (Exception exception)
            {
                Fail(exception, "The footbridge tool could not be created.");
                Enabled = false;
            }
        }

        public void Toggle()
        {
            if (State.Stage == ToolStage.Building) { Message("Still building. Wait until it is done."); return; }
            if (IsActive) Close(); else Open();
        }

        public void Open()
        {
            try
            {
                if (State.Stage == ToolStage.Building) return;
                m_ToolSystem.activeTool = this;
                State.Stage = ToolStage.Hovering;
                Touch();
            }
            catch (Exception exception) { Fail(exception, "The footbridge tool could not open."); }
        }

        public void Close()
        {
            try
            {
                if (State.Stage == ToolStage.Building) { Message("Still building. Wait until it is done."); return; }
                State.Stage = ToolStage.Closed;
                if (IsActive) m_ToolSystem.activeTool = m_DefaultToolSystem;
                Touch();
            }
            catch (Exception exception) { Fail(exception, "The footbridge tool could not close."); }
        }

        [Preserve]
        protected override void OnStartRunning()
        {
            try
            {
                base.OnStartRunning();
                EnableActions(true);
                if (State.Stage == ToolStage.Closed) { State.Stage = ToolStage.Hovering; Touch(); }
            }
            catch (Exception exception) { Fail(exception, "The footbridge tool could not start."); }
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            try
            {
                applyMode = ApplyMode.None;
                EnableActions(false);
                // The invisible build tool briefly owns the tool slot while a build applies.
                if (State.Stage != ToolStage.Building) State.Stage = ToolStage.Closed;
                Touch();
                base.OnStopRunning();
            }
            catch (Exception exception) { Fail(exception, "The footbridge tool could not stop cleanly."); }
        }

        public override void InitializeRaycast()
        {
            try
            {
                base.InitializeRaycast();
                m_ToolRaycastSystem.typeMask = TypeMask.None;
            }
            catch (Exception exception) { LogUpdateError(exception, "Footbridge Builder: setting up the raycast failed"); }
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDependencies)
        {
            try
            {
                applyMode = ApplyMode.None;
                if (!IsActive || State.Stage == ToolStage.Building) return inputDependencies;
                var input = InputManager.instance;
                if (input == null || input.hasInputFieldFocus) return inputDependencies;
                var mouse = Mouse.current;
                var keyboard = Keyboard.current;
                // The game's cancel action also fires on the right button, which belongs to the camera.
                bool rightButton = mouse != null && (mouse.rightButton.isPressed || mouse.rightButton.wasPressedThisFrame);
                bool escape = (cancelAction != null && cancelAction.WasPressedThisFrame() && !rightButton)
                    || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame);
                if (escape) Close();
            }
            catch (Exception exception)
            {
                LogUpdateError(exception, "Footbridge Builder: tool update failed");
            }
            return inputDependencies;
        }

        [Preserve]
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            try
            {
                // Everything the tool holds belongs to the old city.
                State.Stage = ToolStage.Closed;
                State.Summary = "";
                State.Messages = new string[0];
                State.Progress = 0f;
                Touch();
                base.OnGamePreload(purpose, mode);
            }
            catch (Exception exception) { Fail(exception, "The footbridge tool could not reset for the next city."); }
        }

        private void EnableActions(bool enabled)
        {
            applyAction.shouldBeEnabled = enabled;
            secondaryApplyAction.shouldBeEnabled = enabled;
            cancelAction.shouldBeEnabled = enabled;
        }

        private void Message(string message)
        {
            if (State.Messages.Contains(message)) return;
            State.Messages = State.Messages.Concat(new[] { message }).ToArray();
            Touch();
        }

        private void Fail(Exception exception, string message)
        {
            Mod.Logger.Error(exception, "Footbridge Builder: " + message);
            State.Stage = ToolStage.Failed;
            State.Summary = message;
            Message(message);
            Touch();
        }

        private void LogUpdateError(Exception exception, string message)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextErrorLog) return;
            nextErrorLog = now + 5f;
            Mod.Logger.Error(exception, message);
        }

        private void Touch() => State.Version++;
    }
}
