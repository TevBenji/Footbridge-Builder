// Placemaker — main-thread snapshot of the world around the selected area.
//
// Generators run on a worker thread (PreviewWorker) and must never touch ECS or the terrain
// height data (both main-thread only). This copies what they need into plain arrays: a height
// grid (idea from Grid Road Generator's HeightSnapshot), a coarser water-depth grid, nearby nets
// as Bézier curves with widths, building lots as obstacles, and the asset info map. The result
// (SiteContext) is immutable after capture and therefore safe to read from any thread.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: the hovered road followed through its straight-on nodes, its street crossings, and the buildings, nets and water round it as outlines for the footbridge planner; a cached height grid around the cursor; a fingerprint of what stands near the bridge.
using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using FootbridgeBuilder.Build;
using FootbridgeBuilder.Core;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FootbridgeBuilder.Tool
{
    /// <summary>A street crossing on the bridged road: its node and the road's edge ends there.</summary>
    internal sealed class CrossingNode
    {
        public Entity Node;
        /// <summary>Where it is: building the bridge renews the road's nodes and edges, so after that it's found again by place.</summary>
        public float3 Position;
        public bool Marked;
        public readonly List<(Entity Edge, bool AtStart)> Ends = new List<(Entity, bool)>();
    }

    /// <summary>Everything the planner needs about one hovered road, copied off the main thread's world.</summary>
    internal sealed class HoverSnapshot
    {
        public Entity Edge;
        public float3[] Road;
        public float RoadWidth;
        public bool Sidewalks;
        public float Clearance;
        public readonly List<Entity> Chain = new List<Entity>();
        public readonly Dictionary<long, CrossingNode> CrossingNodes = new Dictionary<long, CrossingNode>();
        public readonly List<Crossing> Crossings = new List<Crossing>();
        public readonly List<Obstacle> Obstacles = new List<Obstacle>();
        public Func<float2, float> Ground;
        public long Fingerprint;
    }

    /// <summary>
    /// The heights, buildings, nets and water round the cursor. Reading every building and net in a big city takes a while,
    /// so one capture serves while the cursor stays near it and for a few seconds.
    /// </summary>
    internal sealed class SiteArea
    {
        public const float Radius = 260f;
        private const float HeightSpacing = 3f;
        private const float WaterSpacing = 6f;

        public readonly float2 Centre;
        public readonly float Taken;
        private readonly float2 origin;
        private readonly int columns, rows;
        private readonly float[] heights;
        public readonly List<(Obstacle Outline, Entity Entity)> Buildings = new List<(Obstacle, Entity)>();
        public readonly List<(Entity Edge, float3[] Points, float Width, float2 Range)> Nets = new List<(Entity, float3[], float, float2)>();
        /// <summary>The pillars under raised nets already built: a ramp can't pass through one.</summary>
        public readonly List<Obstacle> Pillars = new List<Obstacle>();
        public readonly List<Obstacle> Water = new List<Obstacle>();
        /// <summary>Junctions (three roads or paths or more): their corners reach past the edges' own outlines.</summary>
        public readonly List<Obstacle> Junctions = new List<Obstacle>();

        public SiteArea(EntityManager entities, World world, float2 centre)
        {
            Centre = centre;
            Taken = UnityEngine.Time.realtimeSinceStartup;
            origin = centre - Radius;
            columns = rows = (int)math.ceil(2f * Radius / HeightSpacing) + 1;
            heights = new float[columns * rows];
            var heightData = world.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData();
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    float height = TerrainUtils.SampleHeight(ref heightData, new float3(origin.x + column * HeightSpacing, 0f, origin.y + row * HeightSpacing));
                    heights[row * columns + column] = math.isfinite(height) ? height : 0f;
                }

            var waterData = world.GetOrCreateSystemManaged<WaterSystem>().GetSurfacesData(out var dependencies);
            dependencies.Complete();
            for (float z = origin.y; z <= origin.y + 2f * Radius; z += WaterSpacing)
                for (float x = origin.x; x <= origin.x + 2f * Radius; x += WaterSpacing)
                {
                    float depth = WaterUtils.SampleDepth(ref waterData, new float3(x, 0f, z));
                    if (math.isfinite(depth) && depth <= 0.1f) continue;
                    float half = WaterSpacing * 0.5f;
                    Water.Add(new Obstacle { A = new float2(x - half, z - half), B = new float2(x + half, z - half), C = new float2(x + half, z + half), D = new float2(x - half, z + half), Kind = ObstacleKind.Water });
                }

            float2 low = origin, high = origin + 2f * Radius;
            using (var query = entities.CreateEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var found = query.ToEntityArray(Allocator.Temp))
                foreach (var building in found)
                {
                    Entity prefab = entities.GetComponentData<PrefabRef>(building).m_Prefab;
                    if (!entities.HasComponent<BuildingData>(prefab)) continue;
                    float2 size = (float2)entities.GetComponentData<BuildingData>(prefab).m_LotSize * 8f;
                    if (!math.all(size > 0f)) continue;
                    var transform = entities.GetComponentData<Game.Objects.Transform>(building);
                    if (math.any(transform.m_Position.xz < low - 100f) || math.any(transform.m_Position.xz > high + 100f)) continue;
                    var corners = Game.Objects.ObjectUtils.CalculateBaseCorners(transform.m_Position, transform.m_Rotation, size);
                    // Zoned buildings give way to a new net, as they do to the game's own road tool; the rest never do.
                    var kind = entities.HasComponent<SpawnableBuildingData>(prefab) ? ObstacleKind.Building : ObstacleKind.FixedBuilding;
                    Buildings.Add((new Obstacle { A = corners.a.xz, B = corners.b.xz, C = corners.c.xz, D = corners.d.xz, Kind = kind, Id = Pack(building) }, building));
                }

            using (var query = entities.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<Composition>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var found = query.ToEntityArray(Allocator.Temp))
                foreach (var edge in found)
                {
                    var curve = entities.GetComponentData<Curve>(edge);
                    var bounds = MathUtils.Bounds(curve.m_Bezier);
                    if (math.any(bounds.max.xz < low) || math.any(bounds.min.xz > high)) continue;
                    Entity composition = entities.GetComponentData<Composition>(edge).m_Edge;
                    if (!entities.HasComponent<NetCompositionData>(composition)) continue;
                    var data = entities.GetComponentData<NetCompositionData>(composition);
                    // Only nets the game would compare with a footpath: it skips those with no layer in common (tunnels,
                    // underground pipes and invisible markers).
                    var layers = NetUtils.GetCollisionMask(data, true);
                    if ((layers & (CollisionMask.OnGround | CollisionMask.Overground)) == 0 || data.m_Width <= 0f) continue;
                    int segments = math.max(1, (int)math.ceil(curve.m_Length / 4f));
                    var points = new float3[segments + 1];
                    for (int index = 0; index <= segments; index++) points[index] = MathUtils.Position(curve.m_Bezier, index / (float)segments);
                    Nets.Add((edge, points, data.m_Width, new float2(data.m_HeightRange.min, data.m_HeightRange.max)));
                }

            using (var query = entities.CreateEntityQuery(ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<ConnectedEdge>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var found = query.ToEntityArray(Allocator.Temp))
                foreach (var node in found)
                {
                    var position = entities.GetComponentData<Node>(node).m_Position;
                    if (math.any(position.xz < low) || math.any(position.xz > high)) continue;
                    var edges = entities.GetBuffer<ConnectedEdge>(node, true);
                    if (edges.Length < 3) continue;
                    float widest = 0f;
                    // The space the junction takes up: as low and as high as any road that meets there.
                    float2 range = new float2(float.MaxValue, float.MinValue);
                    float2 along = new float2(1f, 0f);
                    bool turned = false;
                    foreach (var connected in edges)
                    {
                        if (entities.HasComponent<Composition>(connected.m_Edge)
                            && entities.HasComponent<NetCompositionData>(entities.GetComponentData<Composition>(connected.m_Edge).m_Edge))
                        {
                            var data = entities.GetComponentData<NetCompositionData>(entities.GetComponentData<Composition>(connected.m_Edge).m_Edge);
                            widest = math.max(widest, data.m_Width);
                            range = new float2(math.min(range.x, data.m_HeightRange.min), math.max(range.y, data.m_HeightRange.max));
                        }
                        // Squared to the first road that meets here, so on a road at an angle its corners stay where the junction's are.
                        if (turned || !entities.HasComponent<Curve>(connected.m_Edge) || !entities.HasComponent<Edge>(connected.m_Edge)) continue;
                        var bezier = entities.GetComponentData<Curve>(connected.m_Edge).m_Bezier;
                        float2 tangent = entities.GetComponentData<Edge>(connected.m_Edge).m_Start == node ? (bezier.b - bezier.a).xz : (bezier.d - bezier.c).xz;
                        if (math.lengthsq(tangent) < 1e-4f) continue;
                        along = math.normalize(tangent);
                        turned = true;
                    }
                    float half = widest * 0.5f + 3f;
                    float2 at = position.xz, x = along * half, z = new float2(-along.y, along.x) * half;
                    bool known = range.x <= range.y;
                    Junctions.Add(new Obstacle { A = at - x - z, B = at + x - z, C = at + x + z, D = at - x + z,
                        Kind = ObstacleKind.Net, Surface = position.y, Below = known ? range.x : float.NaN, Above = known ? range.y : float.NaN, Id = Pack(node) });
                }

            // Pillars under raised nets: from the ground up to the net they hold.
            using (var query = entities.CreateEntityQuery(ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Owner>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var found = query.ToEntityArray(Allocator.Temp))
            using (var transforms = query.ToComponentDataArray<Game.Objects.Transform>(Allocator.Temp))
            using (var prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                for (int index = 0; index < found.Length; index++)
                {
                    var transform = transforms[index];
                    if (math.any(transform.m_Position.xz < low) || math.any(transform.m_Position.xz > high)) continue;
                    Entity prefab = prefabs[index].m_Prefab;
                    if (!entities.HasComponent<PillarData>(prefab)) continue;
                    float2 size = entities.HasComponent<ObjectGeometryData>(prefab) ? entities.GetComponentData<ObjectGeometryData>(prefab).m_Size.xz : float2.zero;
                    var corners = Game.Objects.ObjectUtils.CalculateBaseCorners(transform.m_Position, transform.m_Rotation, math.max(size, new float2(1f, 1f)));
                    // Its top: where it stands or the net it holds, whichever is higher.
                    float top = transform.m_Position.y;
                    Entity owner = entities.GetComponentData<Owner>(found[index]).m_Owner;
                    if (entities.HasComponent<Node>(owner)) top = math.max(top, entities.GetComponentData<Node>(owner).m_Position.y);
                    else if (entities.HasComponent<Curve>(owner))
                    {
                        var bezier = entities.GetComponentData<Curve>(owner).m_Bezier;
                        top = math.max(top, math.max(bezier.a.y, bezier.d.y));
                    }
                    float ground = Ground(transform.m_Position.xz);
                    Pillars.Add(new Obstacle { A = corners.a.xz, B = corners.b.xz, C = corners.c.xz, D = corners.d.xz, Kind = ObstacleKind.Net,
                        Surface = ground, Below = 0f, Above = math.max(0.5f, top - ground), Id = Pack(found[index]) });
                }
        }

        public float Ground(float2 point)
        {
            float2 coordinate = math.clamp((point - origin) / HeightSpacing, float2.zero, new float2(columns - 1, rows - 1));
            int column = math.min((int)coordinate.x, columns - 2), row = math.min((int)coordinate.y, rows - 2);
            float2 fraction = coordinate - new float2(column, row);
            return math.lerp(
                math.lerp(heights[row * columns + column], heights[row * columns + column + 1], fraction.x),
                math.lerp(heights[(row + 1) * columns + column], heights[(row + 1) * columns + column + 1], fraction.x), fraction.y);
        }

        public static long Pack(Entity entity) => ((long)entity.Version << 32) | (uint)entity.Index;
        public static Entity Unpack(long id) => new Entity { Index = (int)(id & 0xFFFFFFFF), Version = (int)(id >> 32) };
    }

    internal static class SiteSnapshot
    {
        /// <summary>How far the road is followed each way through its straight-on nodes.</summary>
        private const float Follow = 220f;

        /// <summary>Null when the edge isn't a road on the ground the bridge can cross.</summary>
        public static HoverSnapshot Capture(EntityManager entities, SiteArea area, Entity edge, float3 hit, PathKind path, out string problem)
        {
            problem = "";
            if (!IsGroundRoad(entities, edge, out var composition)) { problem = "Hover a road on the ground."; return null; }
            var snapshot = new HoverSnapshot { Edge = edge, RoadWidth = composition.m_Width };
            Entity prefab = entities.GetComponentData<PrefabRef>(edge).m_Prefab;
            snapshot.Sidewalks = HasPedestrianLanes(entities, edge);
            // The game's rule for a net over a net: the upper one's underside (its height range's low end) must stay above the
            // lower one's height range's top.
            snapshot.Clearance = math.max(2f, composition.m_HeightRange.max - path.UnderDeck);

            // The road ahead and behind, as long as it carries straight on as the same road.
            var chain = new List<(Entity Edge, bool Forward)> { (edge, true) };
            Extend(entities, chain, prefab, forwardEnd: true);
            Extend(entities, chain, prefab, forwardEnd: false);
            var points = new List<float3>();
            foreach (var (link, forward) in chain)
            {
                snapshot.Chain.Add(link);
                var curve = entities.GetComponentData<Curve>(link);
                int segments = math.max(1, (int)math.ceil(curve.m_Length / 2f));
                for (int index = points.Count == 0 ? 0 : 1; index <= segments; index++)
                    points.Add(MathUtils.Position(curve.m_Bezier, forward ? index / (float)segments : 1f - index / (float)segments));
            }
            snapshot.Road = points.ToArray();

            // Street crossings at every node along it: a marked crosswalk at a junction, an unsafe crossing at a plain node.
            for (int index = 0; index < chain.Count; index++)
            {
                var (link, forward) = chain[index];
                var ends = entities.GetComponentData<Edge>(link);
                foreach (bool atStart in new[] { true, false })
                {
                    Entity node = atStart ? ends.m_Start : ends.m_End;
                    long id = SiteArea.Pack(node);
                    if (!snapshot.CrossingNodes.TryGetValue(id, out var crossing))
                    {
                        int roads = RoadEdges(entities, node, out float widest, link);
                        if (roads < 2 || !snapshot.Sidewalks) continue;
                        crossing = new CrossingNode { Node = node, Position = entities.GetComponentData<Node>(node).m_Position };
                        snapshot.CrossingNodes[id] = crossing;
                        snapshot.Crossings.Add(new Crossing
                        {
                            Id = id,
                            Position = entities.GetComponentData<Node>(node).m_Position.xz,
                            ClearDistance = roads >= 3 ? widest * 0.5f + path.Spec.Width + 2f : path.Spec.Width + 5f,
                        });
                    }
                    crossing.Ends.Add((link, atStart));
                    if (MarkedCrossings(entities, node, crossing.Ends, snapshot.RoadWidth) > 0)
                    {
                        crossing.Marked = true;
                        snapshot.Crossings.Find(item => item.Id == id).Marked = true;
                    }
                }
            }

            // The road itself, as short outlines, so the planner keeps ramps off it.
            float half = snapshot.RoadWidth * 0.5f;
            for (int index = 0; index + 1 < points.Count; index++)
            {
                float2 a = points[index].xz, b = points[index + 1].xz;
                float2 side = math.normalizesafe(new float2(-(b - a).y, (b - a).x)) * half;
                snapshot.Obstacles.Add(new Obstacle { A = a + side, B = b + side, C = b - side, D = a - side, Kind = ObstacleKind.HostRoad, Surface = points[index].y });
            }
            var inChain = new HashSet<Entity>(snapshot.Chain);
            long fingerprint = 17;
            foreach (var (net, netPoints, width, range) in area.Nets)
            {
                if (inChain.Contains(net)) continue;
                for (int index = 0; index + 1 < netPoints.Length; index++)
                {
                    float2 a = netPoints[index].xz, b = netPoints[index + 1].xz;
                    if (math.distance(a, hit.xz) > Follow + 40f) continue;
                    float2 side = math.normalizesafe(new float2(-(b - a).y, (b - a).x)) * width * 0.5f;
                    snapshot.Obstacles.Add(new Obstacle { A = a + side, B = b + side, C = b - side, D = a - side, Kind = ObstacleKind.Net,
                        Surface = math.max(netPoints[index].y, netPoints[index + 1].y), Below = range.x, Above = range.y, Id = SiteArea.Pack(net) });
                    if (math.distance(a, hit.xz) < 120f) fingerprint += math.hash(new int4((int2)math.round(a), (int2)math.round(b)));
                }
            }
            foreach (var (outline, _) in area.Buildings)
            {
                if (math.distance(outline.A, hit.xz) > Follow + 60f) continue;
                snapshot.Obstacles.Add(outline);
                if (math.distance(outline.A, hit.xz) < 120f)
                    fingerprint += (long)math.hash(new int4((int2)math.round(outline.A), (int2)math.round(outline.C))) * 31;
            }
            foreach (var water in area.Water) if (math.distance(water.A, hit.xz) < Follow + 40f) snapshot.Obstacles.Add(water);
            foreach (var junction in area.Junctions) if (math.distance(junction.A, hit.xz) < Follow + 40f) snapshot.Obstacles.Add(junction);
            foreach (var pillar in area.Pillars) if (math.distance(pillar.A, hit.xz) < Follow + 40f) snapshot.Obstacles.Add(pillar);
            foreach (var link in snapshot.Chain) fingerprint = fingerprint * 31 + SiteArea.Pack(link);
            snapshot.Fingerprint = fingerprint;
            snapshot.Ground = area.Ground;
            return snapshot;
        }

        public static bool IsGroundRoad(EntityManager entities, Entity edge, out NetCompositionData composition)
        {
            composition = default;
            if (edge == Entity.Null || !entities.Exists(edge) || !entities.HasComponent<Road>(edge) || !entities.HasComponent<Edge>(edge)
                || !entities.HasComponent<Curve>(edge) || !entities.HasComponent<Composition>(edge) || entities.HasComponent<Deleted>(edge)) return false;
            Entity data = entities.GetComponentData<Composition>(edge).m_Edge;
            if (!entities.HasComponent<NetCompositionData>(data)) return false;
            composition = entities.GetComponentData<NetCompositionData>(data);
            return (composition.m_Flags.m_General & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) == 0;
        }

        // Follows the road through nodes where it carries straight on as the same road (the node joins exactly two roads).
        private static void Extend(EntityManager entities, List<(Entity Edge, bool Forward)> chain, Entity prefab, bool forwardEnd)
        {
            float travelled = 0f;
            var (current, forward) = forwardEnd ? chain[chain.Count - 1] : chain[0];
            var ends = entities.GetComponentData<Edge>(current);
            Entity node = forward == forwardEnd ? ends.m_End : ends.m_Start;
            while (travelled < Follow && chain.Count < 24)
            {
                if (RoadEdges(entities, node, out _, Entity.Null) != 2) return;
                Entity next = Entity.Null;
                foreach (var connected in entities.GetBuffer<ConnectedEdge>(node, true))
                    if (connected.m_Edge != current && entities.HasComponent<Road>(connected.m_Edge) && !entities.HasComponent<Temp>(connected.m_Edge)) next = connected.m_Edge;
                if (next == Entity.Null || !IsGroundRoad(entities, next, out _) || entities.GetComponentData<PrefabRef>(next).m_Prefab != prefab) return;
                var nextEnds = entities.GetComponentData<Edge>(next);
                // Oriented so the chain runs one way: forward when this edge starts where the last one ended.
                bool nextForward = forwardEnd ? nextEnds.m_Start == node : nextEnds.m_End == node;
                if (forwardEnd) chain.Add((next, nextForward)); else chain.Insert(0, (next, nextForward));
                travelled += entities.GetComponentData<Curve>(next).m_Length;
                node = forwardEnd ? (nextForward ? nextEnds.m_End : nextEnds.m_Start) : (nextForward ? nextEnds.m_Start : nextEnds.m_End);
                current = next;
            }
        }

        /// <summary>
        /// The marked crosswalks over our road at a node: the node's crossing lanes with a crosswalk (not unsafe) whose middle
        /// lies on one of our road's edges there. What the game draws, so it is also how a removal is checked.
        /// </summary>
        public static int MarkedCrossings(EntityManager entities, Entity node, List<(Entity Edge, bool AtStart)> ends, float roadWidth)
        {
            if (!entities.HasBuffer<Game.Net.SubLane>(node)) return 0;
            int count = 0;
            foreach (var lane in entities.GetBuffer<Game.Net.SubLane>(node, true))
            {
                var entity = lane.m_SubLane;
                if (!entities.HasComponent<Game.Net.PedestrianLane>(entity) || !entities.HasComponent<Curve>(entity)) continue;
                var flags = entities.GetComponentData<Game.Net.PedestrianLane>(entity).m_Flags;
                if ((flags & PedestrianLaneFlags.Crosswalk) == 0 || (flags & PedestrianLaneFlags.Unsafe) != 0) continue;
                float3 middle = MathUtils.Position(entities.GetComponentData<Curve>(entity).m_Bezier, 0.5f);
                foreach (var (edge, _) in ends)
                    if (entities.HasComponent<Curve>(edge)
                        && MathUtils.Distance(entities.GetComponentData<Curve>(edge).m_Bezier.xz, middle.xz, out _) < roadWidth * 0.25f) { count++; break; }
            }
            return count;
        }

        // Sidewalks show as the edge's pedestrian lanes; the composition's Sidewalk flag only marks an upgrade.
        private static bool HasPedestrianLanes(EntityManager entities, Entity edge)
        {
            if (!entities.HasBuffer<Game.Net.SubLane>(edge)) return false;
            foreach (var lane in entities.GetBuffer<Game.Net.SubLane>(edge, true))
                if (entities.HasComponent<Game.Net.PedestrianLane>(lane.m_SubLane)) return true;
            return false;
        }

        /// <summary>The roads meeting at a node, and the widest of them other than <paramref name="except"/>.</summary>
        private static int RoadEdges(EntityManager entities, Entity node, out float widest, Entity except)
        {
            widest = 0f;
            int count = 0;
            if (!entities.HasBuffer<ConnectedEdge>(node)) return 0;
            foreach (var connected in entities.GetBuffer<ConnectedEdge>(node, true))
            {
                var other = connected.m_Edge;
                if (!entities.HasComponent<Road>(other) || entities.HasComponent<Temp>(other) || entities.HasComponent<Deleted>(other)) continue;
                count++;
                if (other == except || !entities.HasComponent<Composition>(other)) continue;
                Entity data = entities.GetComponentData<Composition>(other).m_Edge;
                if (entities.HasComponent<NetCompositionData>(data)) widest = math.max(widest, entities.GetComponentData<NetCompositionData>(data).m_Width);
            }
            return count;
        }
    }
}
