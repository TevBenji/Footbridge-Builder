// Placemaker — emitting CreationDefinitions for each plan element (GPL-3.0).
// Net courses follow ParkManager's CreateNetCourseDefinition and GridRoadGenerator's
// CreateGridDefinitions/MakeCoursePos (attach to an existing edge via m_Entity + m_SplitPosition,
// Upgraded composition flags on the definition). Credits: tomkpunkt, Ti4goc (GPL-3.0).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: one course per planned footbridge piece with each node's own elevation and the forced-elevation flags (Placemaker's Elevate), SubElevation so the game prices and charges the pieces itself, no attaching (ramp feet join the sidewalk on their own), and a crossing-removal upgrade on an existing road edge built the way the game's own upgrade tool builds it.
using System.Collections.Generic;
using Colossal.Mathematics;
using FootbridgeBuilder.Core;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

namespace FootbridgeBuilder.Build
{
    internal static class NetDefinitions
    {

        /// <summary>
        /// One definition per planned piece, all in one frame, each of the prefab <paramref name="prefabFor"/> gives it (a span
        /// too long for the path gets one of the game's pedestrian bridges). Every piece is its own course; pieces that meet
        /// share a node because their ends are the very same point. SubElevation is what the game's net tool sets: it makes the
        /// game price the pieces, show the cost and charge it on Apply.
        /// </summary>
        public static List<Entity> Emit(EntityManager entities, BridgePlan plan, System.Func<PlannedPiece, Entity> prefabFor, uint seed)
        {
            var definitions = new List<Entity>();
            var random = new Random(seed == 0 ? 1u : seed);
            foreach (var piece in plan.Pieces)
            {
                var start = plan.Nodes[piece.Start];
                var end = plan.Nodes[piece.End];
                var curve = new Bezier4x3(start.Position, piece.B, piece.C, end.Position);
                var definition = entities.CreateEntity();
                entities.AddComponentData(definition, new CreationDefinition
                {
                    m_Prefab = prefabFor(piece),
                    m_RandomSeed = random.NextInt(),
                    m_Flags = CreationFlags.SubElevation,
                });
                entities.AddComponent<Updated>(definition);
                entities.AddComponentData(definition, new NetCourse
                {
                    m_Curve = curve,
                    m_Length = MathUtils.Length(curve),
                    m_FixedIndex = -1,
                    m_Elevation = float2.zero,
                    m_StartPosition = Position(start, MathUtils.StartTangent(curve), 0f, CoursePosFlags.IsFirst, piece.ForceElevated),
                    m_EndPosition = Position(end, MathUtils.EndTangent(curve), 1f, CoursePosFlags.IsLast, piece.ForceElevated),
                });
                definitions.Add(definition);
            }
            return definitions;
        }

        // Each node carries its own height above the ground; a node clearly in the air, and both ends of a piece whose middle
        // is, are forced elevated, so the game builds the bridge at the planned heights instead of laying it on the ground.
        private static CoursePos Position(PlannedNode node, float3 tangent, float delta, CoursePosFlags flags, bool raisedPiece)
        {
            if (node.ForceElevated) flags |= CoursePosFlags.ForceElevatedNode;
            if (raisedPiece) flags |= CoursePosFlags.ForceElevatedEdge;
            return new CoursePos
            {
                m_Entity = Entity.Null,
                m_Position = node.Position,
                m_Rotation = NetUtils.GetNodeRotation(tangent),
                m_CourseDelta = delta,
                m_Elevation = new float2(node.Elevation),
                m_Flags = flags,
                m_ParentMesh = -1,
                m_SplitPosition = 0f,
            };
        }

        /// <summary>
        /// Takes the crosswalk off one or both ends of a road edge, the way the game's upgrade tool does: a definition naming
        /// the edge as its original, the edge's full new flag set, its own curve, and the Upgrade flag. The game keeps the edge
        /// and changes its flags. Never written straight onto the edge: that crashed the game in Placemaker's tests.
        /// Null when there's nothing to change.
        /// </summary>
        public static Entity? RemoveCrosswalk(EntityManager entities, Entity edge, bool atStart, bool atEnd)
        {
            if (!entities.Exists(edge) || entities.HasComponent<Deleted>(edge) || !entities.HasComponent<Composition>(edge)) return null;
            var composition = entities.GetComponentData<Composition>(edge);
            var flags = entities.HasComponent<Upgraded>(edge) ? entities.GetComponentData<Upgraded>(edge).m_Flags : default;
            var before = flags;
            foreach (bool start in new[] { true, false })
            {
                if (start ? !atStart : !atEnd) continue;
                Entity nodeData = start ? composition.m_StartNode : composition.m_EndNode;
                if (!entities.HasComponent<NetCompositionData>(nodeData)) continue;
                // The game reads an edge end's crosswalk from its left flags where the node's composition is inverted (the
                // start, in right-hand traffic) and from its right flags otherwise.
                bool left = (entities.GetComponentData<NetCompositionData>(nodeData).m_Flags.m_General & CompositionFlags.General.Invert) != 0;
                var side = ((left ? flags.m_Left : flags.m_Right) | CompositionFlags.Side.RemoveCrosswalk) & ~CompositionFlags.Side.AddCrosswalk;
                if (left) flags.m_Left = side; else flags.m_Right = side;
            }
            if (flags.Equals(before)) return null;

            var ends = entities.GetComponentData<Edge>(edge);
            var curve = entities.GetComponentData<Curve>(edge).m_Bezier;
            var definition = entities.CreateEntity();
            entities.AddComponentData(definition, new CreationDefinition
            {
                m_Original = edge,
                m_Prefab = entities.GetComponentData<PrefabRef>(edge).m_Prefab,
                m_Flags = CreationFlags.Align | CreationFlags.SubElevation | CreationFlags.Upgrade | CreationFlags.Parent,
            });
            entities.AddComponentData(definition, new Upgraded { m_Flags = flags });
            var replacements = entities.AddBuffer<SubReplacement>(definition);
            if (entities.HasBuffer<SubReplacement>(edge))
                foreach (var replacement in entities.GetBuffer<SubReplacement>(edge, true)) replacements.Add(replacement);
            entities.AddComponentData(definition, new NetCourse
            {
                m_Curve = curve,
                m_Length = MathUtils.Length(curve),
                m_FixedIndex = entities.HasComponent<Fixed>(edge) ? entities.GetComponentData<Fixed>(edge).m_Index : -1,
                m_StartPosition = new CoursePos
                {
                    m_Entity = ends.m_Start, m_Position = curve.a, m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve)),
                    m_CourseDelta = 0f, m_Flags = CoursePosFlags.IsFirst, m_ParentMesh = -1,
                },
                m_EndPosition = new CoursePos
                {
                    m_Entity = ends.m_End, m_Position = curve.d, m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve)),
                    m_CourseDelta = 1f, m_Flags = CoursePosFlags.IsLast, m_ParentMesh = -1,
                },
            });
            entities.AddComponent<Updated>(definition);
            return definition;
        }

        /// <summary>Whether an edge end still carries a marked crosswalk.</summary>
        public static bool HasCrosswalk(EntityManager entities, Entity edge, bool atStart)
        {
            if (!entities.Exists(edge) || !entities.HasComponent<Composition>(edge)) return false;
            var composition = entities.GetComponentData<Composition>(edge);
            Entity nodeData = atStart ? composition.m_StartNode : composition.m_EndNode;
            return entities.HasComponent<NetCompositionData>(nodeData)
                && (entities.GetComponentData<NetCompositionData>(nodeData).m_Flags.m_General & CompositionFlags.General.Crosswalk) != 0;
        }

        public static void Destroy(EntityManager entities, List<Entity> definitions)
        {
            foreach (var definition in definitions)
                if (entities.Exists(definition) && !entities.HasComponent<Deleted>(definition)) entities.AddComponent<Deleted>(definition);
            definitions.Clear();
        }
    }
}
