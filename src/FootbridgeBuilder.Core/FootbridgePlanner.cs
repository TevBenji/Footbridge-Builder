// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
//
// How a bridge is planned, for one road and one cursor:
//  1. The deck crosses square to the road (or turned), level, as low as the game allows, from a landing just past each
//     sidewalk. It is one piece, so no node and no pillar stands in the road. With a second road right beside the first
//     (the other half of a divided highway), the deck crosses both, resting on a pillar between them if it's too long.
//  2. Each side gets ramps ("legs") from its landing. A leg is a walking line in plan: along the sidewalk following the
//     road's curve, a switchback, a spiral, or the snake's S-bend. The walker goes down it at the steepest grade the path
//     allows and stops where the ramp meets the ground, so the ramp's length comes out of the ground rather than being
//     guessed: on a slope an uphill ramp is shorter than a downhill one. It drops a node at least every 12 m, because the
//     game re-derives a raised piece's height between its two end nodes.
//  3. Every leg is checked against what's on the ground. Roads, paths, water and service buildings rule it out. A zoned
//     building only counts against it: the game replaces it, as its own road tool does.
//  4. Each side keeps its best leg and each design is scored by how many cims will use it (CimEstimate). The order:
//     a design touching no building at the cursor, then the same a little along the road, and only when nothing is clear
//     anywhere the one that replaces fewest buildings.
// A junction or roundabout under the cursor gets a ring instead (FootbridgePlanner.Junction.cs).
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FootbridgeBuilder.Core
{
    public static partial class FootbridgePlanner
    {
        /// <summary>How far a ramp's round end stays off the road's edge, so the game joins it to the sidewalk instead of cutting the road.</summary>
        public const float KerbGap = 0.25f;
        public const float RaisedPieceLength = 12f;
        // Half a metre under the limit: a curved piece's cubic runs a little longer than the walking line it follows.
        private const float WalkStep = 11.5f;
        public const float ElevatedFrom = 0.75f;
        /// <summary>The game joins a path's dead end to a sidewalk this far past the road's edge (plus the path's half width).</summary>
        public const float JoinReach = 4f;
        /// <summary>The widest gap between two roads that one bridge goes over together.</summary>
        public const float BesideGap = 20f;
        private const float SnapReach = 30f;
        private const float GentleGrade = 1f / 12f;
        private const float SnakeBend = 18f;
        private const float LongestSpiralRadius = 20f;
        private const float MostTurnPerPiece = math.PI / 3f;
        /// <summary>How far the deck rises over the bridged road's own height to clear a street under it that sits higher (on a slope).</summary>
        private const float MostRise = 3f;
        private static readonly float[] Shifts = { 0f, 4f, -4f, 8f, -8f, 12f, -12f, 16f, -16f, 20f, -20f, 24f, -24f };

        private enum Leg { Both, Forward, Backward, SwitchbackForward, SwitchbackBackward, SpiralForward, SpiralBackward, SnakeForward, SnakeBackward }

        private static readonly Leg[] AnyLeg =
            { Leg.Both, Leg.Forward, Leg.Backward, Leg.SwitchbackForward, Leg.SwitchbackBackward, Leg.SpiralForward, Leg.SpiralBackward };

        private sealed class DeckOption
        {
            public Design Design;
            public float Lean;
            public Leg[] SideA, SideB;
        }

        /// <summary>A road the bridge crosses or lands beside, with the offsets its ramps keep from its centre line.</summary>
        private sealed class Way
        {
            public RoadLine Line;
            public float Width;
            public bool Sidewalks;
            /// <summary>The lowest deck over it, from its surface, extra clearance included.</summary>
            public float Clearance;
            /// <summary>A ramp's foot sits on the kerb line, a kerb gap off the road's edge.</summary>
            public float Kerb;
            /// <summary>A ramp along the road runs this far out, leaving room for the short bend that turns its foot to the road.</summary>
            public float Landing;
            public IReadOnlyList<Crossing> Crossings;
        }

        private sealed class Walk
        {
            public Route Route;
            public readonly List<float> Distances = new List<float>();
            public readonly List<float> Heights = new List<float>();
            public float Length3D;
            /// <summary>The foot's station along the road it joins.</summary>
            public float FootStation;
            public float2 Foot;
        }

        private sealed class Side
        {
            public bool Valid;
            public string Problem = "";
            public Leg Style;
            public Way Way;
            public float2 Landing;
            public readonly List<Walk> Walks = new List<Walk>();
            public readonly HashSet<long> Buildings = new HashSet<long>();
            public float Footprint;
            public float WalkScore;
        }

        private sealed class Candidate
        {
            public DeckOption Deck;
            public Side A, B;
            public float Station, DeckY, Shift;
            /// <summary>Over two roads with a deck too long for one span: the pillar between them.</summary>
            public float2? Median;
            public readonly HashSet<long> Buildings = new HashSet<long>();
            public CimEstimate.Result Use;
            public float Footprint;
        }

        public static BridgePlan Plan(BridgeRequest request, CancellationToken cancel = default)
        {
            var plan = new BridgePlan();
            if (request?.Road == null || request.Road.Length < 2 || request.Ground == null)
            {
                plan.Messages.Add("Hover a road.");
                return plan;
            }
            var context = new Context(request);
            context.Blockers = plan.Blockers;
            if (request.Junction != null) return context.PlanJunction(plan, cancel);
            float station = context.Snap(context.Road.Nearest(request.Cursor), plan.Messages);

            Candidate fallback = null;
            string reasonA = "", reasonB = "";
            foreach (float shift in Shifts)
            {
                cancel.ThrowIfCancellationRequested();
                float at = station + shift;
                if (at < 1f || at > context.Road.Length - 1f || context.DeckOverANode(at, 0f)) continue;
                Candidate best = null;
                foreach (var deck in context.DeckOptions())
                {
                    var candidate = context.Evaluate(deck, at, shift, out string whyA, out string whyB);
                    if (shift == 0f)
                    {
                        if (reasonA.Length == 0) reasonA = whyA;
                        if (reasonB.Length == 0) reasonB = whyB;
                    }
                    if (candidate == null) continue;
                    // Only a candidate about to win is checked against the game's shape rules (checking all would be slow), so
                    // a refused shape gives the next one its chance instead of ending the search.
                    if (candidate.Buildings.Count == 0) { if (Better(candidate, best) && context.Passes(candidate)) best = candidate; }
                    else if (Fewer(candidate, fallback) && context.Passes(candidate)) fallback = candidate;
                }
                if (best != null) return context.Assemble(best, plan);
            }
            if (fallback != null) return context.Assemble(fallback, plan);

            plan.Buildable = false;
            // The bridge as it would stand with nothing in the way, so the player sees its shape (in red) while looking for room.
            var preview = context.Preview(station);
            if (preview != null)
            {
                var shape = context.Assemble(preview, new BridgePlan());
                plan.Nodes.AddRange(shape.Nodes);
                plan.Pieces.AddRange(shape.Pieces);
                plan.Design = shape.Design;
                plan.DesignName = shape.DesignName;
                plan.DeckHeight = shape.DeckHeight;
            }
            if (reasonA.Length > 0) plan.Messages.Add($"No room for a ramp on the {context.SideName(-1, station)} side: {reasonA}.");
            if (reasonB.Length > 0) plan.Messages.Add($"No room for a ramp on the {context.SideName(+1, station)} side: {reasonB}.");
            if (reasonA.Length == 0 && reasonB.Length == 0) plan.Messages.Add("No bridge fits here.");
            return plan;
        }

        // Most cims first, then the shortest walk (C1), then the smaller footprint.
        private static bool Better(Candidate candidate, Candidate best)
        {
            if (best == null) return true;
            if (math.abs(candidate.Use.Share - best.Use.Share) > 1e-4f) return candidate.Use.Share > best.Use.Share;
            if (math.abs(candidate.Use.MeanWalk - best.Use.MeanWalk) > 0.5f) return candidate.Use.MeanWalk < best.Use.MeanWalk;
            return candidate.Footprint < best.Footprint - 1e-3f;
        }

        private static bool Fewer(Candidate candidate, Candidate best) =>
            best == null || candidate.Buildings.Count < best.Buildings.Count
            || (candidate.Buildings.Count == best.Buildings.Count && math.abs(candidate.Shift) < math.abs(best.Shift))
            || (candidate.Buildings.Count == best.Buildings.Count && math.abs(candidate.Shift) == math.abs(best.Shift) && Better(candidate, best));

        private sealed partial class Context
        {
            public readonly BridgeRequest Request;
            public readonly RoadLine Road;
            public List<string> Blockers;
            // While working out the preview shape: only the bridged road itself is in the way, and the span and height limits
            // that refused the plan are lifted, so the player still sees what they are placing.
            private bool ignoreObstacles;
            // Each outline with the circle around it: most outlines near a point are rejected by the circle alone.
            private readonly SpatialBuckets<(Obstacle Outline, float2 Centre, float Radius)> obstacles = new SpatialBuckets<(Obstacle, float2, float)>(16f);
            private readonly float half, grade, minRadius, bendRadius, spurRadius, minPiece, highest;
            private readonly float2 pathRange;
            private readonly Way main;
            // The road right beside the bridged one when the bridge goes over both (null otherwise), and which side it is on.
            private readonly Way beside;
            private readonly float besideSign;
            // Along-the-road walks for the side being planned: Both, Forward and Backward share them.
            private readonly Dictionary<float, (Walk Walk, string Problem, HashSet<long> Buildings)> alongWalks = new Dictionary<float, (Walk, string, HashSet<long>)>();

            public Context(BridgeRequest request)
            {
                Request = request;
                var path = request.Path ?? new PathSpec();
                half = math.max(0.5f, path.Width * 0.5f);
                grade = math.max(0.02f, path.MaxSlope * 0.95f);
                minRadius = half + math.max(0f, path.NodeOffset) + 1f;
                // A metre over the game's minimum, so the curves the pieces approximate them with stay clear of it.
                bendRadius = minRadius + 1f;
                minPiece = 2f * (half + math.max(0f, path.NodeOffset)) + 0.2f;
                // The game joins a path's dead end to the sidewalk only when the end faces the road (within 45 degrees). So a
                // ramp running along the road ends with a short ground bend of 60 degrees towards the kerb, and runs half that
                // bend's radius further out to leave room for it.
                spurRadius = math.max(bendRadius, (minPiece + 0.5f) * 3f / math.PI);
                highest = math.max(1f, path.MaxElevation);
                pathRange = path.HeightRange;
                main = MakeWay(request.Road, request.RoadWidth, request.RoadHasSidewalks, request.Clearance, request.Crossings);
                Road = main.Line;
                if (request.Junction == null) (beside, besideSign) = FindBeside();

                var besideIds = new HashSet<long>(beside != null ? request.Beside.Ids ?? Array.Empty<long>() : Array.Empty<long>());
                // Padded by the widest query, as SpatialBuckets expects.
                const float pad = 6f;
                foreach (var outline in request.Obstacles ?? Array.Empty<Obstacle>())
                {
                    if (outline == null) continue;
                    var obstacle = outline;
                    // Bridging both roads: the second one is crossed like the first, not stepped round.
                    if (obstacle.Kind == ObstacleKind.Net && besideIds.Contains(obstacle.Id))
                        obstacle = new Obstacle { A = outline.A, B = outline.B, C = outline.C, D = outline.D, Kind = ObstacleKind.HostRoad, Surface = outline.Surface, Id = outline.Id };
                    float2 low = math.min(math.min(obstacle.A, obstacle.B), math.min(obstacle.C, obstacle.D)) - pad;
                    float2 high = math.max(math.max(obstacle.A, obstacle.B), math.max(obstacle.C, obstacle.D)) + pad;
                    float2 centre = (obstacle.A + obstacle.B + obstacle.C + obstacle.D) * 0.25f;
                    float radius = math.sqrt(math.max(math.max(math.distancesq(centre, obstacle.A), math.distancesq(centre, obstacle.B)),
                        math.max(math.distancesq(centre, obstacle.C), math.distancesq(centre, obstacle.D))));
                    obstacles.Add(low, high, (obstacle, centre, radius));
                }
            }

            private Way MakeWay(float3[] line, float width, bool sidewalks, float clearance, IReadOnlyList<Crossing> crossings)
            {
                float kerb = width * 0.5f + half + KerbGap;
                return new Way
                {
                    Line = new RoadLine(line), Width = width, Sidewalks = sidewalks,
                    Clearance = math.max(0f, clearance) + math.max(0f, Request.ExtraClearance),
                    Kerb = kerb, Landing = kerb + spurRadius * 0.5f,
                    Crossings = crossings ?? Array.Empty<Crossing>(),
                };
            }

            /// <summary>
            /// The road beside the bridged one, when one bridge should cross both: running the same way within 25 degrees,
            /// its edge at most <see cref="BesideGap"/> from ours at the cursor, and no building between them (two streets of a
            /// neighbourhood are bridged one at a time).
            /// </summary>
            private (Way, float) FindBeside()
            {
                var street = Request.Beside;
                if (street?.Line == null || street.Line.Length < 2) return (null, 0f);
                float station = Road.Nearest(Request.Cursor);
                float2 at = Road.Point(station).xz, along = Road.Tangent(station);
                var line = new RoadLine(street.Line);
                float near = line.Nearest(at);
                float2 other = line.Point(near).xz;
                float facing = math.dot(along, line.Tangent(near));
                if (math.abs(facing) < math.cos(math.radians(25f))) return (null, 0f);
                // Square across from the cursor, not a road further along that bends close.
                if (math.abs(math.dot(other - at, along)) > 4f) return (null, 0f);
                float gap = math.distance(at, other) - (Request.RoadWidth + street.Width) * 0.5f;
                if (gap < -0.5f || gap > BesideGap) return (null, 0f);
                float2 across = math.normalizesafe(other - at);
                float2 from = at + across * Request.RoadWidth * 0.5f, to = other - across * street.Width * 0.5f;
                foreach (var obstacle in Request.Obstacles ?? Array.Empty<Obstacle>())
                {
                    if (obstacle == null || (obstacle.Kind != ObstacleKind.Building && obstacle.Kind != ObstacleKind.FixedBuilding)) continue;
                    for (float t = 0f; t <= 1f; t += 0.1f)
                        if (DistanceToQuad(math.lerp(from, to, t), obstacle) < half + 1f) return (null, 0f);
                }
                // Both run the same way, so a positive offset is the same side of each.
                var points = street.Line;
                if (facing < 0f)
                {
                    points = (float3[])street.Line.Clone();
                    Array.Reverse(points);
                }
                var way = MakeWay(points, street.Width, street.Sidewalks, street.Clearance, street.Crossings);
                return (way, math.dot(other - at, Geometry.Left(along)) >= 0f ? 1f : -1f);
            }

            private Way WayOn(float sign) => beside != null && sign == besideSign ? beside : main;

            /// <summary>Where a deck crossing the bridged road at <paramref name="station"/> meets the road on that side, as a station along it.</summary>
            private float WayStation(float sign, float station, float tan)
            {
                if (beside == null || sign != besideSign) return station;
                float2 at = Road.Point(station).xz;
                float distance = math.distance(at, beside.Line.Point(beside.Line.Nearest(at)).xz);
                return beside.Line.Nearest(Road.Offset(station + sign * distance * tan, sign * distance));
            }

            /// <summary>The landing on one side for a plain ramp, before any side has picked its shape.</summary>
            private float2 LandingAt(float sign, float station, float tan)
            {
                var way = WayOn(sign);
                float at = WayStation(sign, station, tan);
                return way.Line.Offset(at + sign * way.Landing * tan, sign * way.Landing);
            }

            private IEnumerable<Way> Ways()
            {
                yield return main;
                if (beside != null) yield return beside;
            }

            /// <summary>
            /// Too close to a crossing's node for the deck. Over a road node the game splits the deck at the road's edge and puts
            /// pillars there, which it then refuses (seen on a large road in the test city), so the deck keeps to one side.
            /// </summary>
            /// <param name="reach">How far along the road either side of the station the deck crosses it: a turned or leaning
            /// deck crosses the road over a stretch, and no node may lie under any of it.</param>
            private static bool OverANode(Way way, float station, float reach)
            {
                foreach (var crossing in way.Crossings)
                {
                    float at = way.Line.Nearest(crossing.Position);
                    if (math.distance(way.Line.Point(at).xz, crossing.Position) > way.Width * 0.5f + 4f) continue;
                    if (math.max(0f, math.abs(station - at) - reach) < crossing.ClearDistance - 0.01f) return true;
                }
                return false;
            }

            /// <summary>Whether a deck at <paramref name="station"/> passes over a node of the bridged road, or of the one beside it.</summary>
            public bool DeckOverANode(float station, float tan)
            {
                if (OverANode(main, station, main.Landing * math.abs(tan))) return true;
                return beside != null && OverANode(beside, WayStation(besideSign, station, tan), beside.Landing * math.abs(tan));
            }

            /// <summary>C2: a cursor near a street crossing snaps to it, so the bridge replaces the crossing cims already use.</summary>
            public float Snap(float station, List<string> messages)
            {
                float best = SnapReach;
                float snapped = station;
                foreach (var crossing in Request.Crossings ?? Array.Empty<Crossing>())
                {
                    float at = Road.Nearest(crossing.Position);
                    if (math.distance(Road.Point(at).xz, crossing.Position) > Request.RoadWidth * 0.5f + 4f) continue;
                    float distance = math.abs(at - station);
                    if (distance >= best) continue;
                    best = distance;
                    float side = station >= at ? 1f : -1f;
                    snapped = math.clamp(at + side * math.max(0f, crossing.ClearDistance), 0f, Road.Length);
                }
                if (snapped != station) messages.Add("Placed at the street crossing, where cims already cross.");
                return snapped;
            }

            public IEnumerable<DeckOption> DeckOptions()
            {
                float turn = math.clamp(Request.TurnDegrees, -30f, 30f);
                float diagonal = turn != 0f ? math.abs(turn) : 25f;
                Leg[] Only(Leg leg) => new[] { leg };
                switch (Request.Design)
                {
                    case Design.YRamps:
                        yield return new DeckOption { Design = Design.YRamps, Lean = turn, SideA = Only(Leg.Both), SideB = Only(Leg.Both) };
                        break;
                    case Design.ZRamps:
                        yield return new DeckOption { Design = Design.ZRamps, Lean = turn, SideA = Only(Leg.Backward), SideB = Only(Leg.Forward) };
                        yield return new DeckOption { Design = Design.ZRamps, Lean = turn, SideA = Only(Leg.Forward), SideB = Only(Leg.Backward) };
                        break;
                    case Design.Switchback:
                        var switchbacks = new[] { Leg.SwitchbackForward, Leg.SwitchbackBackward };
                        yield return new DeckOption { Design = Design.Switchback, Lean = turn, SideA = switchbacks, SideB = switchbacks };
                        break;
                    case Design.SpiralRamp:
                        var spirals = new[] { Leg.SpiralForward, Leg.SpiralBackward };
                        yield return new DeckOption { Design = Design.SpiralRamp, Lean = turn, SideA = spirals, SideB = spirals };
                        break;
                    case Design.DiagonalSpan:
                        yield return new DeckOption { Design = Design.DiagonalSpan, Lean = diagonal, SideA = Only(Leg.Backward), SideB = Only(Leg.Forward) };
                        yield return new DeckOption { Design = Design.DiagonalSpan, Lean = -diagonal, SideA = Only(Leg.Forward), SideB = Only(Leg.Backward) };
                        break;
                    case Design.SnakeDeck:
                        yield return new DeckOption { Design = Design.SnakeDeck, Lean = 50f, SideA = Only(Leg.SnakeBackward), SideB = Only(Leg.SnakeForward) };
                        yield return new DeckOption { Design = Design.SnakeDeck, Lean = -50f, SideA = Only(Leg.SnakeForward), SideB = Only(Leg.SnakeBackward) };
                        break;
                    default:
                        yield return new DeckOption { Design = Design.Auto, Lean = turn, SideA = AnyLeg, SideB = AnyLeg };
                        if (turn == 0f)
                        {
                            yield return new DeckOption { Design = Design.DiagonalSpan, Lean = diagonal, SideA = Only(Leg.Backward), SideB = Only(Leg.Forward) };
                            yield return new DeckOption { Design = Design.DiagonalSpan, Lean = -diagonal, SideA = Only(Leg.Forward), SideB = Only(Leg.Backward) };
                            yield return new DeckOption { Design = Design.SnakeDeck, Lean = 50f, SideA = Only(Leg.SnakeBackward), SideB = Only(Leg.SnakeForward) };
                            yield return new DeckOption { Design = Design.SnakeDeck, Lean = -50f, SideA = Only(Leg.SnakeForward), SideB = Only(Leg.SnakeBackward) };
                        }
                        break;
                }
            }

            public Candidate Evaluate(DeckOption deck, float station, float shift, out string whyA, out string whyB)
            {
                whyA = whyB = "";
                float tan = math.tan(math.radians(deck.Lean));
                float deckY = RoadTop(main, station, main.Landing * math.abs(tan)) + main.Clearance;
                if (beside != null) deckY = math.max(deckY, RoadTop(beside, WayStation(besideSign, station, tan), beside.Landing * math.abs(tan)) + beside.Clearance);
                float2 landingA = LandingAt(-1f, station, tan), landingB = LandingAt(+1f, station, tan);
                // A turned or leaning deck crosses the road over a stretch: the game splits it at any node under that stretch.
                if (!ignoreObstacles && tan != 0f && DeckOverANode(station, tan))
                {
                    whyA = whyB = "the deck would pass over a road node";
                    return null;
                }
                // A junction or street under the deck that sits a little higher than the road (on a slope): the deck rises to
                // clear it. Anything much higher (an elevated road) is left for the obstacle check to refuse.
                float under = DeckNeed(landingA, landingB) + math.max(0f, Request.ExtraClearance);
                if (under > deckY && under <= deckY + MostRise) deckY = under + 0.05f;
                float startY = deckY;
                if (!PickSides(deck.SideA, deck.SideB, station, tan, landingA, landingB, ref deckY, out var a, out var b, out var median, out whyA, out whyB))
                {
                    // Each side picks its own ramp, and a switchback lands further out than the others: a deck too long
                    // with one may still fit with plain ramps, on one side (the other may need its switchback) or both.
                    if (whyA != TooWide) return null;
                    var plainA = Array.FindAll(deck.SideA, leg => !Switchback(leg));
                    var plainB = Array.FindAll(deck.SideB, leg => !Switchback(leg));
                    bool fitted = false;
                    foreach (var (stylesA, stylesB) in new[] { (plainA, deck.SideB), (deck.SideA, plainB), (plainA, plainB) })
                    {
                        if (stylesA.Length == 0 || stylesB.Length == 0 || (stylesA.Length == deck.SideA.Length && stylesB.Length == deck.SideB.Length)) continue;
                        deckY = startY;
                        if (PickSides(stylesA, stylesB, station, tan, landingA, landingB, ref deckY, out a, out b, out median, out whyA, out whyB)) { fitted = true; break; }
                    }
                    // The span is what stopped it: say so, not why the fallbacks didn't fit either.
                    if (!fitted) { whyA = whyB = TooWide; return null; }
                }
                var candidate = new Candidate { Deck = deck, A = a, B = b, Station = station, DeckY = deckY, Shift = shift, Median = median };
                string deckProblem = CheckLine(a.Landing, deckY, b.Landing, deckY, deck: true, candidate.Buildings);
                if (deckProblem.Length > 0) { whyA = whyB = deckProblem; return null; }
                candidate.Buildings.UnionWith(a.Buildings);
                candidate.Buildings.UnionWith(b.Buildings);
                candidate.Footprint = a.Footprint + b.Footprint;
                candidate.Use = Estimate(candidate);
                return candidate;
            }

            private const string TooWide = "the road is too wide for one span of this path";

            private static bool Switchback(Leg leg) => leg == Leg.SwitchbackForward || leg == Leg.SwitchbackBackward;

            private float SpanLimit => (Request.Path ?? new PathSpec()).MaxSpan - 0.5f;

            /// <summary>
            /// Each side's best ramp, the deck raised until it clears the road between their landings, and the span checked;
            /// false with the reasons when it doesn't work. Over two roads a deck too long for one span rests on a pillar
            /// between them (<paramref name="median"/>).
            /// </summary>
            private bool PickSides(Leg[] stylesA, Leg[] stylesB, float station, float tan, float2 landingA, float2 landingB, ref float deckY,
                out Side a, out Side b, out float2? median, out string whyA, out string whyB)
            {
                whyA = whyB = "";
                a = b = null;
                median = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    a = BestSide(-1f, stylesA, station, tan, deckY, math.normalizesafe(landingA - landingB));
                    b = BestSide(+1f, stylesB, station, tan, deckY, math.normalizesafe(landingB - landingA));
                    if (!a.Valid || !b.Valid)
                    {
                        whyA = a.Valid ? "" : a.Problem;
                        whyB = b.Valid ? "" : b.Problem;
                        return false;
                    }
                    median = beside != null && math.distance(a.Landing, b.Landing) > SpanLimit ? Median(a.Landing, b.Landing) : null;
                    float shortfall = median.HasValue
                        ? math.max(DeckShortfall(a.Landing, median.Value, deckY, Ways()), DeckShortfall(median.Value, b.Landing, deckY, Ways()))
                        : DeckShortfall(a.Landing, b.Landing, deckY, Ways());
                    if (shortfall <= 0.01f) break;
                    if (attempt == 2)
                    {
                        // The preview keeps the last shape whose ramps match its deck.
                        if (ignoreObstacles) break;
                        whyA = whyB = "the deck can't clear the road here";
                        return false;
                    }
                    deckY += shortfall + 0.05f;
                }
                if (ignoreObstacles) return true;
                // Half a metre under the limit: a 40.0 m deck over a 32 m road was refused by a game whose limit is 40 m.
                bool fits = median.HasValue
                    ? math.distance(a.Landing, median.Value) <= SpanLimit && math.distance(median.Value, b.Landing) <= SpanLimit
                    : math.distance(a.Landing, b.Landing) <= SpanLimit;
                if (!fits)
                {
                    whyA = whyB = TooWide;
                    return false;
                }
                if (median.HasValue)
                {
                    string pillar = PillarProblem(median.Value, deckY);
                    if (pillar.Length > 0) { whyA = whyB = pillar; return false; }
                }
                return true;
            }

            /// <summary>
            /// Where a deck between two roads can rest on a pillar: the point on its line furthest from both roads, as long as a
            /// pillar has room there. Null when the gap is too narrow.
            /// </summary>
            private float2? Median(float2 from, float2 to)
            {
                float2? best = null;
                float bestRoom = half + 0.2f;
                int steps = math.max(2, (int)math.ceil(math.distance(from, to) * 2f));
                for (int step = 1; step < steps; step++)
                {
                    float2 point = math.lerp(from, to, step / (float)steps);
                    float room = float.MaxValue;
                    foreach (var way in Ways())
                        room = math.min(room, math.distance(point, way.Line.Point(way.Line.Nearest(point)).xz) - way.Width * 0.5f);
                    if (room > bestRoom) { bestRoom = room; best = point; }
                }
                return best;
            }

            private Side BestSide(float sign, Leg[] styles, float station, float tan, float deckY, float2 outward)
            {
                Side best = null, firstFailure = null;
                alongWalks.Clear();
                var way = WayOn(sign);
                float wayStation = WayStation(sign, station, tan);
                foreach (var style in styles)
                {
                    var side = BuildSide(way, sign, style, wayStation, tan, deckY, outward);
                    if (!side.Valid) { firstFailure ??= side; continue; }
                    side.WalkScore = (SideWalk(side, station) + SideWalk(side, station - CimEstimate.AlongTrip) + SideWalk(side, station + CimEstimate.AlongTrip)) / 3f;
                    if (best == null || side.Buildings.Count < best.Buildings.Count
                        || (side.Buildings.Count == best.Buildings.Count && side.WalkScore < best.WalkScore - 1e-3f)
                        || (side.Buildings.Count == best.Buildings.Count && math.abs(side.WalkScore - best.WalkScore) <= 1e-3f && side.Footprint < best.Footprint))
                        best = side;
                }
                return best ?? firstFailure ?? new Side { Problem = "no ramp shape is allowed here" };
            }

            /// <summary>
            /// One side's ramps of one shape, from its landing beside <paramref name="way"/> at <paramref name="station"/> along
            /// it (positive <paramref name="sign"/> is the road's left), down to feet that join its sidewalk.
            /// </summary>
            private Side BuildSide(Way way, float sign, Leg style, float station, float tan, float deckY, float2 outward)
            {
                bool switchback = Switchback(style);
                bool spiral = style == Leg.SpiralForward || style == Leg.SpiralBackward;
                // A spiral comes back square to the road on its own, so it starts at the kerb line.
                float offset = switchback ? way.Landing + 2f * bendRadius : spiral ? way.Kerb : way.Landing;
                float landingStation = station + sign * offset * tan;
                var road = way.Line;
                var side = new Side { Style = style, Way = way };
                if (landingStation < 0f || landingStation > road.Length) { side.Problem = "the road ends too soon"; return side; }
                side.Landing = road.Offset(landingStation, sign * offset);
                float height = deckY - Request.Ground(side.Landing);
                if (height < 1f) { side.Problem = "the ground is as high as the deck"; return side; }
                if (height > highest && !ignoreObstacles) { side.Problem = "the deck would be higher than the path can be raised"; return side; }
                // The pillar under the landing, whatever shape the ramps take from it.
                string pillar = PillarProblem(side.Landing, deckY);
                if (pillar.Length > 0) { side.Problem = pillar; return side; }

                float2 along = road.Tangent(landingStation);
                float legGrade = grade;
                var routes = new List<Route>();
                switch (style)
                {
                    case Leg.Both:
                    case Leg.Forward:
                    case Leg.Backward:
                    {
                        string landingIssue = CheckPoint(side.Landing, deckY, deck: false, side.Buildings);
                        if (landingIssue.Length > 0) { side.Problem = landingIssue; return side; }
                        foreach (float direction in style == Leg.Both ? new[] { 1f, -1f } : new[] { style == Leg.Forward ? 1f : -1f })
                        {
                            if (!alongWalks.TryGetValue(direction, out var reuse))
                            {
                                var buildings = new HashSet<long>();
                                var route = Route.Along(road, landingStation, direction, sign * offset, RouteLength(height, grade));
                                var found = WalkDown(route, deckY, grade);
                                reuse = (found, found == null ? NoGround : Finish(way, found, buildings), buildings);
                                alongWalks[direction] = reuse;
                            }
                            if (reuse.Problem.Length > 0) { side.Problem = reuse.Problem; return side; }
                            side.Walks.Add(reuse.Walk);
                            side.Buildings.UnionWith(reuse.Buildings);
                            side.Footprint += reuse.Walk.Distances[reuse.Walk.Distances.Count - 1];
                        }
                        side.Valid = true;
                        return side;
                    }
                    case Leg.SwitchbackForward:
                    case Leg.SwitchbackBackward:
                    {
                        float direction = style == Leg.SwitchbackForward ? 1f : -1f;
                        float radius = bendRadius;
                        // The outer leg goes far enough that the inner one lands about level with the deck.
                        float outer = math.max(0f, (height / grade - math.PI * radius) * 0.5f);
                        float2 heading = along * direction;
                        routes.Add(Route.From(side.Landing).Straight(heading, outer).Arc(heading, radius, math.PI * -sign * direction).Straight(-heading, 200f));
                        break;
                    }
                    case Leg.SpiralForward:
                    case Leg.SpiralBackward:
                    {
                        float direction = style == Leg.SpiralForward ? 1f : -1f;
                        // Three quarters of a turn away from the road, then straight back at it, so the foot faces the
                        // sidewalk beside the landing. The grade spreads the climb over the whole of that.
                        const float turns = 1.5f * math.PI;
                        float radius = math.max(bendRadius, (height / grade + 0.6f + minPiece) / (turns + 1f));
                        if (radius > LongestSpiralRadius) { side.Problem = "a spiral would be too big"; return side; }
                        // It lands a short piece before the kerb, so the foot ends on the ground facing the sidewalk.
                        legGrade = height / ((turns + 1f) * radius - 0.6f - minPiece);
                        float2 heading = along * direction;
                        routes.Add(Route.From(side.Landing).Arc(heading, radius, turns * sign * direction).Straight(-Geometry.Left(along) * sign, radius));
                        break;
                    }
                    case Leg.SnakeForward:
                    case Leg.SnakeBackward:
                    {
                        float direction = style == Leg.SnakeForward ? 1f : -1f;
                        float bendStation = landingStation + direction * SnakeBend;
                        if (bendStation < 0f || bendStation > road.Length) { side.Problem = "the road ends too soon"; return side; }
                        float2 bend = road.Offset(bendStation, sign * offset);
                        var curve = Geometry.Hermite(side.Landing, outward, bend, road.Tangent(bendStation) * direction, 1f, 0.5f);
                        if (Geometry.TightestRadius(curve, 2f, out _) < minRadius) { side.Problem = "the bend would be too tight"; return side; }
                        var route = Route.From(curve[0]);
                        for (int index = 1; index < curve.Count; index++) route.Add(curve[index]);
                        route.Append(Route.Along(road, bendStation, direction, sign * offset, RouteLength(height, GentleGrade)));
                        routes.Add(route);
                        // Gentle where the ground allows; on a falling slope a gentle ramp never lands, so it steepens.
                        legGrade = WalkDown(route, deckY, GentleGrade) != null ? math.min(grade, GentleGrade) : grade;
                        break;
                    }
                }

                string landingProblem = CheckPoint(side.Landing, deckY, deck: false, side.Buildings);
                if (landingProblem.Length > 0) { side.Problem = landingProblem; return side; }
                foreach (var route in routes)
                {
                    var walk = WalkDown(route, deckY, legGrade);
                    string problem = walk == null ? NoGround : Finish(way, walk, side.Buildings);
                    if (problem.Length > 0) { side.Problem = problem; return side; }
                    side.Walks.Add(walk);
                    side.Footprint += walk.Distances[walk.Distances.Count - 1];
                }
                side.Valid = true;
                return side;
            }

            private const string NoGround = "the ramp can't reach the ground in the room there is";

            // Long enough for any ramp that can land: a few times the plain run down, plus the bends.
            private static float RouteLength(float height, float legGrade) => math.min(300f, 4f * height / legGrade + 40f);

            /// <summary>
            /// Turns the foot to face the road with a short bend on the ground, then checks the foot's place by the sidewalk and
            /// everything the leg passes; "" when it's fine.
            /// </summary>
            private string Finish(Way way, Walk walk, HashSet<long> buildings)
            {
                var road = way.Line;
                float footDistance = walk.Distances[walk.Distances.Count - 1];
                float2 foot = walk.Route.Position(footDistance);
                float2 heading = walk.Route.Heading(math.max(0f, footDistance - 0.25f));
                float2 toRoad = math.normalizesafe(road.Point(road.Nearest(foot)).xz - foot);
                float facing = math.acos(math.clamp(math.dot(heading, toRoad), -1f, 1f));
                // A route that comes back at the road ends on the kerb line, a kerb gap off the road's edge: the foot goes there.
                float room = walk.Route.Length - footDistance;
                if (facing <= math.radians(40f) && room >= minPiece)
                {
                    // Already facing the road with room before the kerb: a short piece on the ground, straight on.
                    walk.Distances.Add(footDistance + room);
                    walk.Heights.Add(Request.Ground(walk.Route.Position(footDistance + room)));
                    walk.Length3D += room;
                    foot = walk.Route.Position(footDistance + room);
                }
                else if (facing <= math.radians(40f) && room > 0.01f
                    && footDistance + room - walk.Distances[walk.Distances.Count - 2] <= RaisedPieceLength)
                {
                    // Too little room for a piece of its own: the last piece runs on along the ground to the kerb line.
                    walk.Distances[walk.Distances.Count - 1] = footDistance + room;
                    walk.Heights[walk.Heights.Count - 1] = Request.Ground(walk.Route.Position(footDistance + room));
                    walk.Length3D += room;
                    foot = walk.Route.Position(footDistance + room);
                }
                else if (facing > math.radians(40f))
                {
                    float turn = facing - math.radians(30f);
                    if (turn > math.PI * 0.5f) return "the ramp's foot can't turn to face the sidewalk";
                    float radius = math.max(spurRadius, (minPiece + 0.3f) / turn);
                    bool left = heading.x * toRoad.y - heading.y * toRoad.x > 0f;
                    var route = walk.Route.Prefix(footDistance).Arc(heading, radius, left ? turn : -turn);
                    walk.Route = route;
                    walk.Distances.Add(route.Length);
                    walk.Heights.Add(Request.Ground(route.Position(route.Length)));
                    walk.Length3D += route.Length - footDistance;
                    foot = route.Position(route.Length);
                }
                walk.Foot = foot;
                walk.FootStation = road.Nearest(foot);
                if (way.Sidewalks && math.distance(foot, road.Point(walk.FootStation).xz) > way.Width * 0.5f + half + JoinReach - 0.5f)
                    return "the ramp's foot would land too far from the sidewalk";
                for (int piece = 1; piece < walk.Distances.Count; piece++)
                {
                    string problem = CheckWalk(walk, piece, buildings);
                    if (problem.Length > 0) return problem;
                }
                // The game stands a pillar under every raised node: none may land on another net.
                for (int node = 1; node + 1 < walk.Distances.Count; node++)
                {
                    float2 point = walk.Route.Position(walk.Distances[node]);
                    if (walk.Heights[node] - Request.Ground(point) <= ElevatedFrom) continue;
                    string problem = PillarProblem(point, walk.Heights[node]);
                    if (problem.Length > 0) return problem;
                }
                return "";
            }

            private string PillarProblem(float2 point, float height)
            {
                if (ignoreObstacles) return "";
                float reach = half + 0.2f;
                foreach (var (obstacle, centre, radius) in obstacles.Near(point))
                {
                    // Only a net below the node: a pillar reaches from the ground up to it.
                    if (obstacle.Kind != ObstacleKind.Net || obstacle.Surface >= height - 0.5f) continue;
                    float far = reach + radius;
                    if (math.distancesq(point, centre) > far * far || DistanceToQuad(point, obstacle) > reach) continue;
                    if (Blockers != null && Blockers.Count < 12)
                        Blockers.Add($"{obstacle.Kind} {obstacle.Id} (surface {obstacle.Surface:F1}) at ({point.x:F1}, {point.y:F1}), node at {height:F1}: a pillar would stand on it");
                    return "a pillar would stand on another road or path";
                }
                return "";
            }

            /// <summary>
            /// Down a route from the deck at the leg's grade, a node at least every 12 m (and every 60° of turn), until the
            /// ramp meets the ground. Null when the route runs out first.
            /// </summary>
            private Walk WalkDown(Route route, float startY, float legGrade)
            {
                // Near the end of the road a leg heading for it has no room at all: no ramp, so the next shape gets its turn.
                if (route.Length < minPiece) return null;
                var walk = new Walk { Route = route };
                walk.Distances.Add(0f);
                walk.Heights.Add(startY);
                float Above(float distance) => startY - legGrade * distance - Request.Ground(route.Position(distance));
                float at = 0f;
                while (true)
                {
                    float next = NextStop(route, at);
                    if (Above(next) <= 0f)
                    {
                        float low = at, high = next;
                        for (int step = 0; step < 32; step++)
                        {
                            float middle = (low + high) * 0.5f;
                            if (Above(middle) > 0f) low = middle; else high = middle;
                        }
                        float foot = math.max(high, minPiece);
                        if (foot - at < minPiece && walk.Distances.Count > 1)
                        {
                            // Too short a last piece folds in the game: share the last stretch out between two even pieces.
                            walk.Distances.RemoveAt(walk.Distances.Count - 1);
                            walk.Heights.RemoveAt(walk.Heights.Count - 1);
                            float previous = walk.Distances[walk.Distances.Count - 1];
                            if (foot - previous > WalkStep)
                            {
                                float middle = (previous + foot) * 0.5f;
                                walk.Distances.Add(middle);
                                walk.Heights.Add(startY - legGrade * middle);
                            }
                        }
                        walk.Distances.Add(foot);
                        walk.Heights.Add(Request.Ground(route.Position(foot)));
                        break;
                    }
                    if (next >= route.Length - 1e-3f) return null;
                    walk.Distances.Add(next);
                    walk.Heights.Add(startY - legGrade * next);
                    at = next;
                }
                for (int index = 1; index < walk.Distances.Count; index++)
                {
                    float run = walk.Distances[index] - walk.Distances[index - 1], rise = walk.Heights[index] - walk.Heights[index - 1];
                    walk.Length3D += math.sqrt(run * run + rise * rise);
                }
                return walk;
            }

            // The next node: 12 m on, sooner where the route turns more than 60°.
            private float NextStop(Route route, float at)
            {
                float end = math.min(route.Length, at + WalkStep);
                float2 heading = route.Heading(at);
                for (float probe = at + 0.5f; probe < end; probe += 0.5f)
                    if (math.acos(math.clamp(math.dot(heading, route.Heading(probe)), -1f, 1f)) > MostTurnPerPiece)
                        return math.max(at + minPiece, probe - 0.5f);
                return end;
            }

            private string CheckWalk(Walk walk, int piece, HashSet<long> buildings)
            {
                float from = walk.Distances[piece - 1], to = walk.Distances[piece];
                float fromY = walk.Heights[piece - 1], toY = walk.Heights[piece];
                int steps = math.max(1, (int)math.ceil((to - from) / 1f));
                for (int step = 0; step <= steps; step++)
                {
                    float t = step / (float)steps;
                    string problem = CheckPoint(walk.Route.Position(math.lerp(from, to, t)), math.lerp(fromY, toY, t), deck: false, buildings);
                    if (problem.Length > 0) return problem;
                }
                return "";
            }

            private string CheckLine(float2 from, float fromY, float2 to, float toY, bool deck, HashSet<long> buildings)
            {
                int steps = math.max(1, (int)math.ceil(math.distance(from, to)));
                for (int step = 0; step <= steps; step++)
                {
                    float t = step / (float)steps;
                    string problem = CheckPoint(math.lerp(from, to, t), math.lerp(fromY, toY, t), deck, buildings);
                    if (problem.Length > 0) return problem;
                }
                return "";
            }

            private string CheckPoint(float2 point, float height, bool deck, HashSet<long> buildings)
            {
                string problem = Blocked(point, height, deck, buildings, out var blocker);
                if (problem.Length > 0 && blocker != null && Blockers != null && Blockers.Count < 12)
                    Blockers.Add($"{blocker.Kind} {blocker.Id} (surface {blocker.Surface:F1}) at ({point.x:F1}, {point.y:F1}), path at {height:F1}: {problem}");
                return problem;
            }

            private string Blocked(float2 point, float height, bool deck, HashSet<long> buildings, out Obstacle blocker)
            {
                blocker = null;
                float reach = half + 0.2f;
                float above = height - Request.Ground(point);
                foreach (var (obstacle, centre, radius) in obstacles.Near(point))
                {
                    float far = reach + radius;
                    if (math.distancesq(point, centre) > far * far) continue;
                    if (ignoreObstacles && obstacle.Kind != ObstacleKind.HostRoad) continue;
                    float distance = DistanceToQuad(point, obstacle);
                    if (distance > reach) continue;
                    blocker = obstacle;
                    switch (obstacle.Kind)
                    {
                        case ObstacleKind.Building: buildings.Add(obstacle.Id); break;
                        case ObstacleKind.FixedBuilding: return "a service building is in the way";
                        case ObstacleKind.Water: if (above < 1.5f) return "water is in the way"; break;
                        case ObstacleKind.Net: if (!ClearOf(obstacle, height)) return "another road or path is in the way"; break;
                        // Ramps run a kerb gap off the road's edge by design, so only a real overlap counts.
                        case ObstacleKind.HostRoad: if (!deck && distance < half - 0.05f) return "the ramp would hang over the road"; break;
                    }
                }
                return "";
            }

            /// <summary>
            /// How much higher the deck must go to clear the roads under it everywhere, given that the game re-derives a raised
            /// piece's height as the ground below plus a straight blend of its two ends' elevations.
            /// </summary>
            private float DeckShortfall(float2 from, float2 to, float deckY, IEnumerable<Way> ways)
            {
                float groundFrom = Request.Ground(from), groundTo = Request.Ground(to);
                float shortfall = 0f;
                int steps = math.max(2, (int)math.ceil(math.distance(from, to)));
                for (int step = 0; step <= steps; step++)
                {
                    float t = step / (float)steps;
                    float2 point = math.lerp(from, to, t);
                    float deck = Request.Ground(point) + deckY - math.lerp(groundFrom, groundTo, t);
                    foreach (var way in ways)
                    {
                        float3 centre = way.Line.Point(way.Line.Nearest(point));
                        if (math.distance(point, centre.xz) > way.Width * 0.5f) continue;
                        shortfall = math.max(shortfall, centre.y + way.Clearance - deck);
                    }
                }
                return shortfall;
            }

            /// <summary>
            /// The game's rule for one net over another: the upper one's underside at or above the lower one's top. A piece at
            /// <paramref name="height"/> clears the net by passing over it or, where the net is raised, under it.
            /// </summary>
            private bool ClearOf(Obstacle net, float height)
            {
                if (height - net.Surface >= NeedOver(net)) return true;
                // Under a sloping net, its low end is what counts.
                float low = float.IsNaN(net.Lowest) ? net.Surface : math.min(net.Surface, net.Lowest);
                return !float.IsNaN(net.Below) && height + pathRange.y <= low + net.Below;
            }

            // How far above a net's surface a piece must be to pass over it. A net with no height range is taken as a road.
            private float NeedOver(Obstacle net) => float.IsNaN(net.Above) ? Request.Clearance : net.Above - pathRange.x;

            /// <summary>The height the deck's line needs to pass over every net under it (the bridged road aside); far below when none.</summary>
            private float DeckNeed(float2 from, float2 to)
            {
                float top = float.MinValue, reach = half + 0.2f;
                int steps = math.max(1, (int)math.ceil(math.distance(from, to)));
                for (int step = 0; step <= steps; step++)
                {
                    float2 point = math.lerp(from, to, step / (float)steps);
                    foreach (var (obstacle, centre, radius) in obstacles.Near(point))
                    {
                        if (obstacle.Kind != ObstacleKind.Net) continue;
                        float far = reach + radius;
                        if (math.distancesq(point, centre) > far * far || DistanceToQuad(point, obstacle) > reach) continue;
                        top = math.max(top, obstacle.Surface + NeedOver(obstacle));
                    }
                }
                return top;
            }

            /// <summary>The best bridge at <paramref name="station"/> with only the bridged road in the way: for showing, never for building.</summary>
            public Candidate Preview(float station)
            {
                var blockers = Blockers;
                Blockers = null;
                ignoreObstacles = true;
                try
                {
                    Candidate best = null;
                    foreach (var deck in DeckOptions())
                    {
                        var candidate = Evaluate(deck, station, 0f, out _, out _);
                        if (candidate != null && Better(candidate, best)) best = candidate;
                    }
                    return best;
                }
                finally
                {
                    ignoreObstacles = false;
                    Blockers = blockers;
                }
            }

            private static float RoadTop(Way way, float station, float reach)
            {
                float top = float.MinValue;
                for (float at = station - reach - 1f; at <= station + reach + 1f; at += 1f) top = math.max(top, way.Line.Point(at).y);
                return top;
            }

            // ── cims ──

            /// <summary>Walking from a sidewalk point at <paramref name="station"/> up this side to its landing, by the best leg.</summary>
            private float SideWalk(Side side, float station)
            {
                float best = float.MaxValue;
                // Trips are measured along the bridged road; a foot beside the other road is put on it by where it stands.
                foreach (var walk in side.Walks)
                    best = math.min(best, math.abs((side.Way == main ? walk.FootStation : Road.Nearest(walk.Foot)) - station) + walk.Length3D);
                return best;
            }

            private CimEstimate.Result Estimate(Candidate candidate)
            {
                var crossings = new List<CimEstimate.StreetOption>();
                float centre = candidate.Station;
                float2 middle = Road.Point(centre).xz;
                bool Kept(Crossing crossing) => crossing.Marked
                    && !(Request.CrossingControl == CrossingControl.RemoveInReach && math.distance(crossing.Position, middle) <= Request.CrossingReach);
                float Penalty(bool marked) => marked ? CimEstimate.MarkedPenalty : CimEstimate.UnsafePenalty;
                foreach (var crossing in Request.Crossings ?? Array.Empty<Crossing>())
                {
                    float at = Road.Nearest(crossing.Position);
                    if (math.distance(Road.Point(at).xz, crossing.Position) > Request.RoadWidth * 0.5f + 4f) continue;
                    var option = new CimEstimate.StreetOption { Id = crossing.Id, Station = at, Marked = Kept(crossing) };
                    if (beside != null)
                    {
                        // Over two roads a walk at street level crosses this one here and the other at its nearest crossing,
                        // paying both; with no crossing on the other road there's no way over at street level.
                        Crossing pair = null;
                        foreach (var other in beside.Crossings)
                            if (pair == null || math.distance(other.Position, crossing.Position) < math.distance(pair.Position, crossing.Position)) pair = other;
                        if (pair == null) continue;
                        bool pairKept = Kept(pair);
                        option.Penalty = Penalty(option.Marked) + Penalty(pairKept);
                        option.Detour = math.abs(Road.Nearest(pair.Position) - at);
                        option.Marked |= pairKept;
                    }
                    crossings.Add(option);
                }
                float deck = math.distance(candidate.A.Landing, candidate.B.Landing);
                // Over two roads the walk at street level goes kerb to kerb over both.
                float width = Request.RoadWidth;
                if (beside != null) width = math.distance(middle, beside.Line.Point(beside.Line.Nearest(middle)).xz) + (Request.RoadWidth + beside.Width) * 0.5f;
                return CimEstimate.Estimate(centre, width, crossings, (from, to) => SideWalk(candidate.A, from) + deck + SideWalk(candidate.B, to));
            }

            // ── output ──

            /// <summary>Its finished pieces pass the game's shape rules.</summary>
            public bool Passes(Candidate candidate)
            {
                var trial = Assemble(candidate, new BridgePlan());
                if (!trial.Buildable && Blockers != null && Blockers.Count < 12) Blockers.AddRange(trial.Messages);
                return trial.Buildable;
            }

            public BridgePlan Assemble(Candidate candidate, BridgePlan plan)
            {
                plan.Buildable = true;
                plan.Design = candidate.Deck.Design;
                plan.DesignName = DesignName(candidate) + (beside != null ? " over both roads" : "");
                plan.Shift = candidate.Shift;
                plan.DeckHeight = candidate.DeckY - Road.Point(candidate.Station).y;
                plan.ExtraWalk = candidate.Use.ExtraWalk;
                plan.UseShare = candidate.Use.Share;
                plan.UseLabel = CimEstimate.Label(candidate.Use.Share);
                plan.CompetingCrossing = candidate.Use.Competing;
                plan.CompetingMarked = candidate.Use.CompetingMarked;
                foreach (var crossing in Request.Crossings ?? Array.Empty<Crossing>())
                    if (crossing.Id == candidate.Use.Competing)
                        plan.CompetingDistance = math.abs(Road.Nearest(crossing.Position) - candidate.Station);
                plan.BuildingsToReplace.AddRange(Sorted(candidate.Buildings));
                if (Request.CrossingControl == CrossingControl.RemoveInReach)
                {
                    float2 middle = Road.Point(candidate.Station).xz;
                    foreach (var crossing in Request.Crossings ?? Array.Empty<Crossing>())
                        if (math.distance(crossing.Position, middle) <= Request.CrossingReach) plan.CrossingsToRemove.Add(crossing.Id);
                    if (beside != null)
                        foreach (var crossing in beside.Crossings)
                            if (math.distance(crossing.Position, middle) <= Request.CrossingReach && !plan.CrossingsToRemove.Contains(crossing.Id))
                                plan.CrossingsToRemove.Add(crossing.Id);
                }

                int a = AddNode(plan, candidate.A.Landing, candidate.DeckY, groundEnd: false);
                int b = AddNode(plan, candidate.B.Landing, candidate.DeckY, groundEnd: false);
                if (candidate.Median.HasValue)
                {
                    int median = AddNode(plan, candidate.Median.Value, candidate.DeckY, groundEnd: false);
                    AddDeck(plan, a, median, deck: true);
                    AddDeck(plan, median, b, deck: true);
                }
                else AddDeck(plan, a, b, deck: true);
                AddWalks(plan, candidate.A, a);
                AddWalks(plan, candidate.B, b);

                if (candidate.Shift != 0f) plan.Messages.Add($"Moved {math.abs(candidate.Shift):0} m along the road to find room.");
                AddReport(plan);
                if (!Request.RoadHasSidewalks || (beside != null && !beside.Sidewalks))
                    plan.Messages.Add("This road has no sidewalks, so the ramp feet can't join one. Link them to a path.");
                return Check(plan);
            }

            private void AddReport(BridgePlan plan)
            {
                if (plan.BuildingsToReplace.Count > 0)
                    plan.Messages.Add(plan.BuildingsToReplace.Count == 1 ? "No clear spot nearby: 1 building will be replaced."
                        : $"No clear spot nearby: {plan.BuildingsToReplace.Count} buildings will be replaced.");
            }

            private BridgePlan Check(BridgePlan plan)
            {
                foreach (string problem in ShapeRules.Problems(plan, Request.Path ?? new PathSpec()))
                {
                    plan.Buildable = false;
                    plan.Messages.Add("The game would refuse a piece: " + problem + ".");
                }
                return plan;
            }

            /// <summary>A straight level piece between two deck nodes.</summary>
            private void AddDeck(BridgePlan plan, int start, int end, bool deck)
            {
                float3 from = plan.Position(start), to = plan.Position(end);
                plan.Pieces.Add(new PlannedPiece
                {
                    Start = start, End = end, B = math.lerp(from, to, 1f / 3f), C = math.lerp(from, to, 2f / 3f), Deck = deck,
                    ForceElevated = math.lerp(from.y, to.y, 0.5f) - Request.Ground(math.lerp(from.xz, to.xz, 0.5f)) > ElevatedFrom,
                });
            }

            private void AddWalks(BridgePlan plan, Side side, int landingNode)
            {
                foreach (var walk in side.Walks)
                {
                    int previous = landingNode;
                    for (int index = 1; index < walk.Distances.Count; index++)
                    {
                        bool last = index == walk.Distances.Count - 1;
                        int node = AddNode(plan, walk.Route.Position(walk.Distances[index]), walk.Heights[index], last);
                        AddCurved(plan, walk, index, previous, node);
                        previous = node;
                    }
                }
            }

            /// <summary>The piece of a walk between its stops index-1 and index, as a cubic following the walking line.</summary>
            private void AddCurved(BridgePlan plan, Walk walk, int index, int startNode, int endNode)
            {
                float fromDistance = walk.Distances[index - 1], toDistance = walk.Distances[index];
                float3 start = plan.Position(startNode), end = plan.Position(endNode);
                float2 startHeading = walk.Route.Heading(fromDistance), endHeading = walk.Route.Heading(math.max(fromDistance, toDistance - 0.25f));
                // A circular arc's cubic handles are 4/3 tan(turn/4) of its radius long; a straight piece's a third of its length.
                float turn = math.acos(math.clamp(math.dot(startHeading, endHeading), -1f, 1f));
                float length = toDistance - fromDistance;
                float run = turn > 0.01f ? 4f / 3f * math.tan(turn / 4f) * length / turn : length / 3f, rise = (end.y - start.y) / 3f;
                float middleY = (start.y + end.y) * 0.5f;
                plan.Pieces.Add(new PlannedPiece
                {
                    Start = startNode, End = endNode,
                    B = start + new float3(startHeading.x * run, rise, startHeading.y * run),
                    C = end - new float3(endHeading.x * run, rise, endHeading.y * run),
                    ForceElevated = middleY - Request.Ground(walk.Route.Position((fromDistance + toDistance) * 0.5f)) > ElevatedFrom,
                });
            }

            private int AddNode(BridgePlan plan, float2 point, float height, bool groundEnd)
            {
                float elevation = math.max(0f, height - Request.Ground(point));
                plan.Nodes.Add(new PlannedNode
                {
                    Position = new float3(point.x, height, point.y),
                    Elevation = groundEnd ? 0f : elevation,
                    GroundEnd = groundEnd,
                    ForceElevated = !groundEnd && elevation > ElevatedFrom,
                });
                return plan.Nodes.Count - 1;
            }

            private static string DesignName(Candidate candidate)
            {
                switch (candidate.Deck.Design)
                {
                    case Design.DiagonalSpan: return "Diagonal span";
                    case Design.SnakeDeck: return "Snake deck";
                }
                string a = LegName(candidate.A.Style), b = LegName(candidate.B.Style);
                bool straightA = candidate.A.Style == Leg.Forward || candidate.A.Style == Leg.Backward;
                bool straightB = candidate.B.Style == Leg.Forward || candidate.B.Style == Leg.Backward;
                if (straightA && straightB) return candidate.A.Style != candidate.B.Style ? "Z-ramps" : "Ramps one way";
                if (a == b) return a;
                return a + " and " + b.ToLowerInvariant();
            }

            private static string LegName(Leg leg)
            {
                switch (leg)
                {
                    case Leg.Both: return "Y-ramps";
                    case Leg.SwitchbackForward:
                    case Leg.SwitchbackBackward: return "Switchback";
                    case Leg.SpiralForward:
                    case Leg.SpiralBackward: return "Spiral ramp";
                    case Leg.SnakeForward:
                    case Leg.SnakeBackward: return "Snake deck";
                    default: return "Ramp";
                }
            }

            public string SideName(float sign, float station) => Compass(Geometry.Left(Road.Tangent(station)) * sign);

            private static string Compass(float2 direction)
            {
                if (math.abs(direction.x) > math.abs(direction.y)) return direction.x > 0f ? "east" : "west";
                return direction.y > 0f ? "north" : "south";
            }

            private static List<long> Sorted(HashSet<long> set)
            {
                var list = new List<long>(set);
                list.Sort();
                return list;
            }

            private static float DistanceToQuad(float2 point, Obstacle quad)
            {
                if (Inside(point, quad.A, quad.B, quad.C, quad.D)) return 0f;
                return math.min(math.min(Geometry.DistanceToSegment(point, quad.A, quad.B), Geometry.DistanceToSegment(point, quad.B, quad.C)),
                    math.min(Geometry.DistanceToSegment(point, quad.C, quad.D), Geometry.DistanceToSegment(point, quad.D, quad.A)));
            }

            private static bool Inside(float2 p, float2 a, float2 b, float2 c, float2 d)
            {
                float Cross(float2 from, float2 to) => (to.x - from.x) * (p.y - from.y) - (to.y - from.y) * (p.x - from.x);
                float ab = Cross(a, b), bc = Cross(b, c), cd = Cross(c, d), da = Cross(d, a);
                return (ab >= 0f && bc >= 0f && cd >= 0f && da >= 0f) || (ab <= 0f && bc <= 0f && cd <= 0f && da <= 0f);
            }
        }
    }
}
