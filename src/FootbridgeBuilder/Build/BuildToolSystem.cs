// Placemaker — invisible tool that owns the apply cadence during a build (GPL-3.0).
//
// Why a tool: CS2 applies or clears ALL temporary entities according to the ACTIVE tool's
// applyMode (ToolSystem.applyMode → ToolOutputSystem → ApplyTool/ClearTool phases). Another tool
// that sets ApplyMode.Clear every frame (the usual tool cadence) would wipe our temporaries, and
// no system outside the active tool can request Apply. So BuildService activates this tool for the
// few frames of a build and restores the previous tool afterwards. While it is active no other
// tool produces definitions, so every temporary entity belongs to our build.
//
// Phase: ToolUpdate (all tools update there; ToolOutputSystem reads applyMode right after).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: cut apply gates and audit fields; own activation, one startup Clear, quiet frames and previous-tool restoration.

using Game.Prefabs;
using Game.Tools;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace FootbridgeBuilder.Build
{
    public sealed partial class BuildToolSystem : ToolBaseSystem
    {
        private ToolBaseSystem _previousTool;
        private int _clearedFrame = -1;

        internal ApplyMode Requested = ApplyMode.None;
        internal int LastAppliedFrame { get; private set; } = -1;
        internal bool IsActive => m_ToolSystem.activeTool == this;
        // Clear the previous tool's pending preview once, before any zoning definitions exist, then let it settle.
        internal bool Ready => IsActive && _clearedFrame >= 0 && UnityEngine.Time.frameCount - _clearedFrame >= 2;

        public override string toolID => "Footbridge Builder Build";
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        internal void Activate()
        {
            _previousTool = m_ToolSystem.activeTool;
            Requested = ApplyMode.Clear;
            _clearedFrame = -1;
            LastAppliedFrame = -1;
            m_ToolSystem.activeTool = this;
        }

        internal void Restore()
        {
            Requested = ApplyMode.None;
            applyMode = ApplyMode.None;
            var previousTool = _previousTool;
            _previousTool = null;
            // A player who explicitly switched tools keeps that newer selection.
            if (IsActive)
                m_ToolSystem.activeTool = previousTool != null && previousTool != this && previousTool.World == World
                    ? previousTool : m_DefaultToolSystem;
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = Game.Common.TypeMask.None;
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDependencies)
        {
            applyMode = IsActive ? Requested : ApplyMode.None;
            if (applyMode == ApplyMode.Clear) _clearedFrame = UnityEngine.Time.frameCount;
            if (applyMode == ApplyMode.Apply) LastAppliedFrame = UnityEngine.Time.frameCount;
            Requested = ApplyMode.None;
            return inputDependencies;
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            applyMode = ApplyMode.None;
            Requested = ApplyMode.None;
            _clearedFrame = -1;
            base.OnStopRunning();
        }
    }
}
