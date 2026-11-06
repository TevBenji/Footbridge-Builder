// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using FootbridgeBuilder.Core;
using Game.Prefabs;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FootbridgeBuilder.Build
{
    /// <summary>A footpath the bridge can be built from, with what the game allows it.</summary>
    internal sealed class PathKind
    {
        public string Name;
        public string Label;
        public Entity Prefab;
        public PathSpec Spec;
        /// <summary>How far below its surface a raised piece reaches (negative), for the clearance over a road.</summary>
        public float UnderDeck;
        /// <summary>Not unlocked in this city yet: the game's own tools don't offer it either.</summary>
        public bool Locked;
        /// <summary>The longest unsupported span of this path's raised pieces.</summary>
        public float Span;
    }

    /// <summary>A pedestrian bridge the game ships, used as the deck where the road is wider than a path can span.</summary>
    internal sealed class DeckBridge
    {
        public string Name;
        public Entity Prefab;
        public float Shortest, Longest;
    }

    /// <summary>The game's footpaths that can be raised. Invisible paths are left out: one left as a dead end crashed the game.</summary>
    internal static class PathCatalog
    {
        public const string DefaultName = "Pavement Path";

        public static List<PathKind> List(EntityManager entities, PrefabSystem prefabs)
        {
            var result = new List<PathKind>();
            using (var query = entities.CreateEntityQuery(ComponentType.ReadOnly<PathwayData>(), ComponentType.ReadOnly<NetGeometryData>(),
                ComponentType.ReadOnly<PlaceableNetData>(), ComponentType.ReadOnly<PrefabData>()))
            using (var found = query.ToEntityArray(Allocator.Temp))
                foreach (var prefab in found)
                {
                    if (entities.HasComponent<BridgeData>(prefab)) continue;
                    string name;
                    try { name = prefabs.GetPrefab<PrefabBase>(prefab)?.name; } catch (Exception) { continue; }
                    // Bike-only paths carry no pedestrians, so they make no footbridge.
                    if (string.IsNullOrEmpty(name) || name.IndexOf("Invisible", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.EndsWith("BikePath", StringComparison.OrdinalIgnoreCase)) continue;
                    var geometry = entities.GetComponentData<NetGeometryData>(prefab);
                    var placeable = entities.GetComponentData<PlaceableNetData>(prefab);
                    // Only paths the game lets the player raise high enough to clear a road.
                    if (placeable.m_ElevationRange.max < 5f || geometry.m_DefaultWidth <= 0f || geometry.m_MaxSlopeSteepness <= 0f) continue;
                    result.Add(new PathKind
                    {
                        Name = name,
                        Label = Label(name),
                        Prefab = prefab,
                        // Locked is switched off when the city unlocks the path, not removed.
                        Locked = entities.HasComponent<Locked>(prefab) && entities.IsComponentEnabled<Locked>(prefab),
                        // Nine tenths of it: under a 40 m limit the game split a 39 m deck 2 m from its end and refused the
                        // pillars there (test city, 5 November 2026).
                        Span = geometry.m_ElevatedLength > 1f ? geometry.m_ElevatedLength * 0.9f : 1000f,
                        UnderDeck = math.min(0f, geometry.m_ElevatedHeightRange.min),
                        Spec = new PathSpec
                        {
                            Name = name,
                            Width = geometry.m_DefaultWidth,
                            MaxSlope = geometry.m_MaxSlopeSteepness,
                            NodeOffset = geometry.m_MinNodeOffset,
                            MaxEdgeLength = geometry.m_EdgeLengthRange.max > 1f ? geometry.m_EdgeLengthRange.max : 200f,
                            MaxElevation = placeable.m_ElevationRange.max,
                            MaxSpan = geometry.m_ElevatedLength > 1f ? geometry.m_ElevatedLength * 0.9f : 1000f,
                        },
                    });
                }
            result.Sort((a, b) => a.Spec.Width != b.Spec.Width ? a.Spec.Width.CompareTo(b.Spec.Width) : string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

        /// <summary>The game's unlocked pedestrian bridges (Arc, Cable Stayed, Covered), shortest reach first.</summary>
        public static List<DeckBridge> Bridges(EntityManager entities, PrefabSystem prefabs)
        {
            var result = new List<DeckBridge>();
            using (var query = entities.CreateEntityQuery(ComponentType.ReadOnly<PathwayData>(), ComponentType.ReadOnly<NetGeometryData>(),
                ComponentType.ReadOnly<BridgeData>(), ComponentType.ReadOnly<PrefabData>()))
            using (var found = query.ToEntityArray(Allocator.Temp))
                foreach (var prefab in found)
                {
                    if (entities.HasComponent<Locked>(prefab) && entities.IsComponentEnabled<Locked>(prefab)) continue;
                    string name;
                    try { name = prefabs.GetPrefab<PrefabBase>(prefab)?.name; } catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(name) || name.IndexOf("Pedestrian Bridge", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var geometry = entities.GetComponentData<NetGeometryData>(prefab);
                    result.Add(new DeckBridge { Name = name, Prefab = prefab, Shortest = geometry.m_EdgeLengthRange.min, Longest = math.min(geometry.m_EdgeLengthRange.max, geometry.m_ElevatedLength * 0.9f) });
                }
            result.Sort((a, b) => a.Longest != b.Longest ? a.Longest.CompareTo(b.Longest) : string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

        public static PathKind Find(List<PathKind> kinds, string name)
        {
            foreach (var kind in kinds) if (kind.Name == name) return kind;
            foreach (var kind in kinds) if (kind.Name == DefaultName) return kind;
            return kinds.Count > 0 ? kinds[0] : null;
        }

        private static string Label(string name)
        {
            try
            {
                var dictionary = GameManager.instance?.localizationManager?.activeDictionary;
                if (dictionary != null && dictionary.TryGetValue("Assets.NAME[" + name + "]", out var text) && !string.IsNullOrEmpty(text)) return text;
            }
            catch (Exception) { }
            return name;
        }
    }
}
