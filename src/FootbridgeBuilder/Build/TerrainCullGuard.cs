// Guard against a native crash in TerrainSystem.CullForCascades after several builds in one game. The terrain's road cull
// list can be filled past its capacity when many new nets arrive at once, and the crash surfaces at its next resize.
//
// Before our nets are emitted (build and ghost), the list gets room for what is already there twice over plus enough for every
// net we add: the game still grows it by its own rule, we only make sure it never overflows. Every job that reads or writes the
// list is completed first, as the game does before it resizes.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: namespace changed; logging through the mod's logger.

using System;
using System.Collections;
using System.Reflection;
using Game.Simulation;
using Unity.Jobs;

namespace FootbridgeBuilder.Build
{
    internal static class TerrainCullGuard
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly FieldInfo CullFinished = typeof(TerrainSystem).GetField("m_CullFinished", Any);
        private static readonly FieldInfo LaneCull = typeof(TerrainSystem).GetField("m_LaneCull", Any);
        private static readonly FieldInfo ClipMapCull = typeof(TerrainSystem).GetField("m_ClipMapCull", Any);
        private static readonly FieldInfo Cascades = typeof(TerrainSystem).GetField("m_CascadeCulling", Any);
        private static FieldInfo _laneHandle;
        private static bool _warned;
        public static int Grown;

        /// <summary>Makes room in the terrain's road cull list for <paramref name="newNets"/> more nets. Main thread only.</summary>
        public static void Reserve(TerrainSystem terrain, int newNets)
        {
            if (terrain == null) return;
            try
            {
                Complete(CullFinished, terrain);
                Complete(LaneCull, terrain);
                Complete(ClipMapCull, terrain);
                if (Cascades?.GetValue(terrain) is IList list)
                    foreach (var info in list)
                    {
                        if (info == null) continue;
                        _laneHandle ??= info.GetType().GetField("m_LaneHandle", Any);
                        if (_laneHandle?.GetValue(info) is JobHandle h) h.Complete();
                    }
                var roads = terrain.GetRoads();   // completes the cull job too
                if (!roads.IsCreated) return;
                int want = roads.Length * 2 + Math.Max(0, newNets) * 18 + 2000;
                // the evidence: a list longer than its capacity has already been written past its end
                if (roads.Length > roads.Capacity) Mod.Logger.Warn($"Footbridge Builder terrain cull guard: the road cull list overflowed ({roads.Length} sections in a capacity of {roads.Capacity}).");
                if (roads.Capacity < want)
                {
                    int was = roads.Capacity;
                    roads.Capacity = want;
                    Grown++;
                    Mod.Logger.Info($"Footbridge Builder terrain cull guard: road cull list {roads.Length} sections, capacity {was} -> {want} (+{newNets} nets).");
                }
            }
            catch (Exception e)
            {
                if (_warned) return;
                _warned = true;
                Mod.Logger.Warn($"Footbridge Builder terrain cull guard: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Complete(FieldInfo f, object owner)
        {
            if (f?.GetValue(owner) is JobHandle h) h.Complete();
        }
    }
}
