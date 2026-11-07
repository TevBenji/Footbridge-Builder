// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
//
// A bridge round a junction or a roundabout: one span over every leg, just past the junction's own surface (the game
// refuses a deck over a node), joined at the corners between neighbouring legs, so the deck makes a ring. Where two legs
// carry straight on, the corner between them stays open and the ring becomes a U. Each corner gets ramps along its legs'
// sidewalks, the same walking ramps a bridge over one road has, so every foot joins a road's sidewalk. A roundabout gets the
// same ring just outside it: the game joins a ramp's foot to an approach road's sidewalk, never to the roundabout's own ring.
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FootbridgeBuilder.Core
{
    public static partial class FootbridgePlanner
    {
        /// <summary>How much further out than the junction's surface the ring is tried when it doesn't fit nearer.</summary>
        private static readonly float[] RingPushes = { 0f, 4f, 8f, 12f };
        /// <summary>Two legs this far apart or more (a road carrying straight on) leave the corner between them open.</summary>
        private const float OpenCorner = 150f * math.PI / 180f;
        /// <summary>The farthest out along a leg a span may cross it.</summary>
        private const float FarthestSpan = 80f;
        /// <summary>The trips a ring is scored by start and end this far out along the legs.</summary>
        private const float RingTrip = 30f;

        private sealed partial class Context
        {
            private sealed class RingLeg
            {
                public Way Way;
                /// <summary>Outward from the junction, where the span crosses.</summary>
                public float2 Direction;
                public float Angle;
                /// <summary>The nearest station along the leg its span may cross at: past the junction's own surface.</summary>
                public float Clear;
                public float Station;
                /// <summary>The span's ends: on the leg's right (towards the previous leg) and its left (towards the next).</summary>
                public float2 Right, Left;
            }

            /// <summary>The corner between a leg and the next one counterclockwise.</summary>
            private sealed class Corner
            {
                public bool Joined;
                /// <summary>The corner's two ends: the first leg's left end and the next leg's right end.</summary>
                public float2 First, Next;
                /// <summary>The two ends are one node.</summary>
                public bool Shared;
                /// <summary>A joined corner whose ends are apart: the deck between them.</summary>
                public Route Link;
                /// <summary>Ramps from each end, along that end's leg; null where the end has none.</summary>
                public Side FromFirst, FromNext;
                public float Gap;
            }

            private sealed class Ring
            {
                public readonly List<RingLeg> Legs = new List<RingLeg>();
                public readonly List<Corner> Corners = new List<Corner>();
                public float DeckY;
                public readonly HashSet<long> Buildings = new HashSet<long>();
                public float Share, ExtraWalk;
            }

            public BridgePlan PlanJunction(BridgePlan plan, CancellationToken cancel)
            {
                var site = Request.Junction;
                var legs = RingLegs(site);
                if (legs.Count < (site.Roundabout ? 2 : 3))
                {
                    plan.Messages.Add("Hover the middle of a junction where three roads or more meet.");
                    return plan;
                }
                Ring fallback = null;
                string reason = "";
                foreach (float push in RingPushes)
                {
                    cancel.ThrowIfCancellationRequested();
                    var ring = BuildRing(site, legs, push, out string why);
                    if (ring == null) { if (reason.Length == 0) reason = why; continue; }
                    var trial = AssembleRing(site, ring, new BridgePlan());
                    if (!trial.Buildable)
                    {
                        if (Blockers != null && Blockers.Count < 12) Blockers.AddRange(trial.Messages);
                        if (reason.Length == 0) reason = "the game would refuse a piece";
                        continue;
                    }
                    if (ring.Buildings.Count == 0) return AssembleRing(site, ring, plan);
                    if (fallback == null || ring.Buildings.Count < fallback.Buildings.Count) fallback = ring;
                }
                if (fallback != null) return AssembleRing(site, fallback, plan);

                plan.Buildable = false;
                // The ring as it would stand with nothing in the way, so the player sees its shape (in red).
                var blockers = Blockers;
                Blockers = null;
                ignoreObstacles = true;
                try
                {
                    var shape = BuildRing(site, legs, 0f, out _);
                    if (shape != null)
                    {
                        var preview = AssembleRing(site, shape, new BridgePlan());
                        plan.Nodes.AddRange(preview.Nodes);
                        plan.Pieces.AddRange(preview.Pieces);
                        plan.DesignName = preview.DesignName;
                        plan.DeckHeight = preview.DeckHeight;
                    }
                }
                finally
                {
                    ignoreObstacles = false;
                    Blockers = blockers;
                }
                string place = site.Roundabout ? "roundabout" : "junction";
                plan.Messages.Add(reason.Length > 0 ? $"No ring fits round this {place}: {reason}." : $"No ring fits round this {place}.");
                return plan;
            }

            /// <summary>The junction's legs, counterclockwise from east.</summary>
            private List<RingLeg> RingLegs(JunctionSite site)
            {
                var legs = new List<RingLeg>();
                foreach (var street in site.Legs ?? Array.Empty<Street>())
                {
                    if (street?.Line == null || street.Line.Length < 2) continue;
                    var way = MakeWay(street.Line, street.Width, street.Sidewalks, street.Clearance, street.Crossings);
                    // The span's own width and a metre more past the junction's surface.
                    float clear = math.max(0f, street.Clear) + half + 1f;
                    float2 direction = math.normalizesafe(way.Line.Point(math.min(way.Line.Length, clear)).xz - site.Centre.xz, way.Line.Tangent(0f));
                    legs.Add(new RingLeg { Way = way, Direction = direction, Angle = math.atan2(direction.y, direction.x), Clear = clear });
                }
                legs.Sort((x, y) => x.Angle.CompareTo(y.Angle));
                return legs;
            }

            /// <summary>The ring with its spans <paramref name="push"/> metres further out than they need be; null with the reason when it doesn't fit.</summary>
            private Ring BuildRing(JunctionSite site, List<RingLeg> bases, float push, out string why)
            {
                why = "";
                int count = bases.Count;
                var ring = new Ring();
                foreach (var leg in bases)
                    ring.Legs.Add(new RingLeg { Way = leg.Way, Direction = leg.Direction, Angle = leg.Angle, Clear = leg.Clear, Station = leg.Clear + push });
                var legs = ring.Legs;
                RingLeg At(int index) => legs[(index % count + count) % count];

                var gaps = new float[count];
                for (int k = 0; k < count; k++)
                {
                    float gap = At(k + 1).Angle - legs[k].Angle;
                    while (gap <= 1e-4f) gap += 2f * math.PI;
                    gaps[k] = gap;
                }
                // A joined corner's two ends meet where its legs' ramp lines cross; the spans go out that far when the
                // crossing lies past both legs' junction surface, so the corner is one node.
                var least = new float[count];
                for (int k = 0; k < count; k++) least[k] = legs[k].Station;
                for (int k = 0; k < count; k++)
                {
                    if (gaps[k] >= OpenCorner) continue;
                    RingLeg first = legs[k], next = At(k + 1);
                    float sin = math.sin(gaps[k]), cos = math.cos(gaps[k]);
                    float along = (next.Way.Landing + first.Way.Landing * cos) / sin;
                    float alongNext = along * cos + first.Way.Landing * sin;
                    if (along < least[k] - 0.01f || alongNext < least[(k + 1) % count] - 0.01f) continue;
                    first.Station = math.max(first.Station, along);
                    next.Station = math.max(next.Station, alongNext);
                }
                // Ends that nearly meet but not quite would leave a piece too short to build: move both spans out until the
                // deck between them is long enough.
                for (int fix = 0; fix < 16; fix++)
                {
                    foreach (var leg in legs)
                    {
                        leg.Right = leg.Way.Line.Offset(leg.Station, -leg.Way.Landing);
                        leg.Left = leg.Way.Line.Offset(leg.Station, leg.Way.Landing);
                    }
                    bool moved = false;
                    for (int k = 0; k < count; k++)
                    {
                        if (gaps[k] >= OpenCorner) continue;
                        float apart = math.distance(legs[k].Left, At(k + 1).Right);
                        if (apart < 0.75f || apart >= minPiece + 0.3f) continue;
                        legs[k].Station += 0.5f;
                        At(k + 1).Station += 0.5f;
                        moved = true;
                    }
                    if (!moved) break;
                }
                foreach (var leg in legs)
                {
                    if (leg.Station > leg.Way.Line.Length - 2f) { why = "a road at the junction ends too soon"; return null; }
                    if (leg.Station > FarthestSpan) { why = "the roads meet at too sharp an angle"; return null; }
                }

                for (int k = 0; k < count; k++)
                {
                    var corner = new Corner { Joined = gaps[k] < OpenCorner, Gap = gaps[k], First = legs[k].Left, Next = At(k + 1).Right };
                    if (corner.Joined)
                    {
                        if (math.distance(corner.First, corner.Next) < 0.75f)
                        {
                            corner.Shared = true;
                            corner.Next = corner.First;
                            At(k + 1).Right = corner.First;
                        }
                        else
                        {
                            corner.Link = LinkRoute(site, corner.First, corner.Next, out string problem);
                            if (corner.Link == null) { why = problem; return null; }
                        }
                    }
                    ring.Corners.Add(corner);
                }

                // Level all round, as low as the highest leg allows, and over anything else under the deck.
                float deckY = float.MinValue, under = float.MinValue;
                foreach (var leg in legs)
                {
                    deckY = math.max(deckY, RoadTop(leg.Way, leg.Station, 0f) + leg.Way.Clearance);
                    under = math.max(under, DeckNeed(leg.Right, leg.Left));
                }
                foreach (var corner in ring.Corners)
                    if (corner.Link != null)
                        for (float at = 0f; at < corner.Link.Length; at += 2f)
                            under = math.max(under, DeckNeed(corner.Link.Position(at), corner.Link.Position(math.min(corner.Link.Length, at + 2f))));
                under += math.max(0f, Request.ExtraClearance);
                if (under > deckY && under <= deckY + MostRise) deckY = under + 0.05f;
                var ways = new List<Way>();
                foreach (var leg in legs) ways.Add(leg.Way);
                for (int attempt = 0; ; attempt++)
                {
                    float shortfall = 0f;
                    foreach (var leg in legs) shortfall = math.max(shortfall, DeckShortfall(leg.Right, leg.Left, deckY, ways));
                    if (shortfall <= 0.01f) break;
                    if (attempt == 2)
                    {
                        if (ignoreObstacles) break;
                        why = "the deck can't clear the roads here";
                        return null;
                    }
                    deckY += shortfall + 0.05f;
                }
                ring.DeckY = deckY;

                // Every deck node stands on a pillar, at a height the path can be raised to.
                var deckNodes = new List<float2>();
                foreach (var leg in legs) { deckNodes.Add(leg.Right); deckNodes.Add(leg.Left); }
                foreach (var corner in ring.Corners)
                    if (corner.Link != null)
                        foreach (float stop in LinkStops(corner.Link)) deckNodes.Add(corner.Link.Position(stop));
                foreach (var point in deckNodes)
                {
                    float height = deckY - Request.Ground(point);
                    if (height < 1f) { why = "the ground is as high as the deck"; return null; }
                    if (height > highest && !ignoreObstacles) { why = "the deck would be higher than the path can be raised"; return null; }
                    string pillar = PillarProblem(point, deckY);
                    if (pillar.Length > 0) { why = pillar; return null; }
                }
                foreach (var leg in legs)
                {
                    if (!ignoreObstacles && math.distance(leg.Right, leg.Left) > SpanLimit) { why = "a road at the junction is too wide for one span of this path"; return null; }
                    if (!ignoreObstacles && OverANode(leg.Way, leg.Station, 0f)) { why = "the deck would pass over another road node"; return null; }
                    string problem = CheckLine(leg.Right, deckY, leg.Left, deckY, deck: true, ring.Buildings);
                    if (problem.Length > 0) { why = problem; return null; }
                }
                foreach (var corner in ring.Corners)
                {
                    if (corner.Link == null) continue;
                    for (float at = 0f; at <= corner.Link.Length; at += 1f)
                    {
                        string problem = CheckPoint(corner.Link.Position(at), deckY, deck: false, ring.Buildings);
                        if (problem.Length > 0) { why = problem; return null; }
                    }
                }

                // Ramps at every corner: along both legs where they fit, else along one. An open corner needs one at each end.
                for (int k = 0; k < count; k++)
                {
                    var corner = ring.Corners[k];
                    RingLeg first = legs[k], next = At(k + 1);
                    var fromFirst = Ramp(first, +1f, deckY);
                    var fromNext = Ramp(next, -1f, deckY);
                    string where = Compass(new float2(math.cos(first.Angle + corner.Gap * 0.5f), math.sin(first.Angle + corner.Gap * 0.5f)));
                    if (!corner.Joined)
                    {
                        var failed = !fromFirst.Valid ? fromFirst : !fromNext.Valid ? fromNext : null;
                        if (failed != null) { why = $"no room for a ramp on the {where} side ({failed.Problem})"; return null; }
                        corner.FromFirst = fromFirst;
                        corner.FromNext = fromNext;
                    }
                    else
                    {
                        Side bestFirst = null, bestNext = null;
                        bool found = false;
                        int fewest = 0, bestOneWay = 0;
                        float smallest = 0f;
                        // Fewest buildings, then ramps along both legs (walkers from either one climb straight up), then the
                        // smaller footprint.
                        foreach (var (a, b) in new (Side, Side)[] { (fromFirst, fromNext), (fromFirst, null), (null, fromNext) })
                        {
                            if ((a != null && !a.Valid) || (b != null && !b.Valid)) continue;
                            var buildings = new HashSet<long>();
                            if (a != null) buildings.UnionWith(a.Buildings);
                            if (b != null) buildings.UnionWith(b.Buildings);
                            float footprint = (a?.Footprint ?? 0f) + (b?.Footprint ?? 0f);
                            int oneWay = a != null && b != null ? 0 : 1;
                            if (found && (buildings.Count > fewest
                                || (buildings.Count == fewest && (oneWay > bestOneWay || (oneWay == bestOneWay && footprint >= smallest))))) continue;
                            found = true;
                            bestFirst = a;
                            bestNext = b;
                            fewest = buildings.Count;
                            bestOneWay = oneWay;
                            smallest = footprint;
                        }
                        if (!found)
                        {
                            var failed = !fromFirst.Valid ? fromFirst : fromNext;
                            why = $"no room for a ramp at the {where} corner ({failed.Problem})";
                            return null;
                        }
                        corner.FromFirst = bestFirst;
                        corner.FromNext = bestNext;
                    }
                    if (corner.FromFirst != null) ring.Buildings.UnionWith(corner.FromFirst.Buildings);
                    if (corner.FromNext != null) ring.Buildings.UnionWith(corner.FromNext.Buildings);
                }
                EstimateRing(site, ring);
                return ring;
            }

            /// <summary>Plain ramps from one of a leg's span ends, along its sidewalk away from the junction.</summary>
            private Side Ramp(RingLeg leg, float sign, float deckY)
            {
                alongWalks.Clear();
                return BuildSide(leg.Way, sign, Leg.Forward, leg.Station, 0f, deckY, leg.Direction);
            }

            /// <summary>
            /// The deck between a corner's two ends: straight across the corner, or round the junction's surface (a roundabout's
            /// ring) where the straight line would cut over it. Null when it can't keep clear.
            /// </summary>
            private Route LinkRoute(JunctionSite site, float2 from, float2 to, out string problem)
            {
                problem = "";
                float keepOut = math.max(0f, site.Reach) + half + 1f;
                float2 centre = site.Centre.xz;
                var route = Route.From(from);
                if (Geometry.DistanceToSegment(centre, from, to) >= keepOut) return route.Straight(math.normalizesafe(to - from), math.distance(from, to));
                float2 a = from - centre, b = to - centre;
                float radiusA = math.length(a), radiusB = math.length(b);
                if (math.min(radiusA, radiusB) < keepOut)
                {
                    problem = "the deck can't get round the junction";
                    return null;
                }
                // Counterclockwise, as the corner runs from its first leg to the next.
                float start = math.atan2(a.y, a.x), turn = math.atan2(b.y, b.x) - start;
                while (turn <= 0f) turn += 2f * math.PI;
                int steps = math.max(4, (int)math.ceil(turn * math.max(radiusA, radiusB)));
                for (int step = 1; step <= steps; step++)
                {
                    float t = step / (float)steps, angle = start + turn * t;
                    route.Add(centre + new float2(math.cos(angle), math.sin(angle)) * math.lerp(radiusA, radiusB, t));
                }
                return route;
            }

            /// <summary>The nodes along a corner's deck, its ends left out: evenly spaced, no piece over 12 m.</summary>
            private static List<float> LinkStops(Route link)
            {
                var stops = new List<float>();
                int pieces = math.max(1, (int)math.ceil(link.Length / WalkStep));
                for (int piece = 1; piece < pieces; piece++) stops.Add(link.Length * piece / pieces);
                return stops;
            }

            /// <summary>
            /// How many cims take the ring, against crossing at the junction itself. For each leg, as for a bridge over one
            /// road, three trips across it: straight over (<see cref="RingTrip"/> out either side) and twice the trip round the
            /// junction, from the leg before it to the leg after it (<see cref="CimEstimate.AlongTrip"/> out each), which passes
            /// the ramps on its way.
            /// </summary>
            private void EstimateRing(JunctionSite site, Ring ring)
            {
                int count = ring.Legs.Count;
                bool kept = Request.CrossingControl == CrossingControl.Off && site.CrossingMarked;
                float penalty = kept ? CimEstimate.MarkedPenalty : CimEstimate.UnsafePenalty;
                float share = 0f, extra = 0f;
                for (int k = 0; k < count; k++)
                {
                    var leg = ring.Legs[k];
                    Corner before = ring.Corners[(k - 1 + count) % count], after = ring.Corners[k];
                    float span = math.distance(leg.Right, leg.Left);
                    float straight = RingAccess(before, toNext: true, fromNextLeg: true, RingTrip) + span
                        + RingAccess(after, toNext: false, fromNextLeg: false, RingTrip) - (2f * RingTrip + leg.Way.Width);
                    float round = RingAccess(before, toNext: true, fromNextLeg: false, CimEstimate.AlongTrip) + span
                        + RingAccess(after, toNext: false, fromNextLeg: true, CimEstimate.AlongTrip) - (2f * CimEstimate.AlongTrip + leg.Way.Width);
                    share += (CimEstimate.Share(straight, penalty) + 2f * CimEstimate.Share(round, penalty)) / 3f;
                    extra += straight;
                }
                ring.Share = share / count;
                ring.ExtraWalk = extra / count;
            }

            /// <summary>
            /// From a sidewalk <paramref name="trip"/> metres out along one of a corner's two legs (its next leg, or its first)
            /// up to one of the corner's deck ends: by a ramp at that end, or one at the other end and along the deck between.
            /// </summary>
            private static float RingAccess(Corner corner, bool toNext, bool fromNextLeg, float trip)
            {
                float best = float.MaxValue;
                foreach (var (side, atNext) in new[] { (corner.FromFirst, false), (corner.FromNext, true) })
                {
                    if (side == null) continue;
                    // An open corner's two ends aren't joined: only a ramp at the end itself reaches it.
                    if (atNext != toNext && !corner.Joined) continue;
                    float deck = atNext == toNext ? 0f : corner.Link?.Length ?? 0f;
                    foreach (var walk in side.Walks)
                    {
                        // A ramp along the trip's own leg is reached along its sidewalk; one along the other leg, round the corner.
                        float ground = atNext == fromNextLeg ? math.abs(walk.FootStation - trip) : trip + walk.FootStation;
                        best = math.min(best, ground + walk.Length3D + deck);
                    }
                }
                return best == float.MaxValue ? 0f : best;
            }

            private BridgePlan AssembleRing(JunctionSite site, Ring ring, BridgePlan plan)
            {
                int count = ring.Legs.Count;
                plan.Buildable = true;
                plan.Design = Design.Auto;
                int open = 0;
                foreach (var corner in ring.Corners) if (!corner.Joined) open++;
                plan.DesignName = site.Roundabout ? "Ring round the roundabout"
                    : open == 0 ? "Ring round the junction" : open == 1 ? "U round the junction" : "Bridges round the junction";
                plan.DeckHeight = ring.DeckY - site.Centre.y;
                plan.ExtraWalk = ring.ExtraWalk;
                plan.UseShare = ring.Share;
                plan.UseLabel = CimEstimate.Label(ring.Share);
                plan.BuildingsToReplace.AddRange(Sorted(ring.Buildings));

                // The junction's own crossings go with the crossings in reach along its legs. What still competes is the
                // junction's marked crosswalk when crossings are kept, else the nearest crossing left along a leg.
                bool remove = Request.CrossingControl == CrossingControl.RemoveInReach;
                if (remove && site.CrossingId != 0) plan.CrossingsToRemove.Add(site.CrossingId);
                bool junctionCompetes = !remove && site.CrossingMarked && site.CrossingId != 0;
                if (junctionCompetes)
                {
                    plan.CompetingCrossing = site.CrossingId;
                    plan.CompetingMarked = true;
                }
                float nearest = float.MaxValue;
                foreach (var leg in ring.Legs)
                    foreach (var crossing in leg.Way.Crossings)
                    {
                        float distance = math.distance(crossing.Position, site.Centre.xz);
                        if (remove && distance <= Request.CrossingReach)
                        {
                            if (!plan.CrossingsToRemove.Contains(crossing.Id)) plan.CrossingsToRemove.Add(crossing.Id);
                            continue;
                        }
                        if (junctionCompetes || distance >= nearest) continue;
                        nearest = distance;
                        plan.CompetingCrossing = crossing.Id;
                        plan.CompetingMarked = crossing.Marked;
                        plan.CompetingDistance = distance;
                    }

                var firstNode = new int[count];
                var nextNode = new int[count];
                for (int k = 0; k < count; k++)
                {
                    var corner = ring.Corners[k];
                    firstNode[k] = AddNode(plan, corner.First, ring.DeckY, groundEnd: false);
                    nextNode[k] = corner.Shared ? firstNode[k] : AddNode(plan, corner.Next, ring.DeckY, groundEnd: false);
                }
                // Leg k's span runs from the corner before it (that corner's next end) to the corner after it (its first end).
                for (int k = 0; k < count; k++) AddDeck(plan, nextNode[(k - 1 + count) % count], firstNode[k], deck: true);
                for (int k = 0; k < count; k++)
                {
                    var corner = ring.Corners[k];
                    if (corner.Link == null) continue;
                    var link = new Walk { Route = corner.Link };
                    link.Distances.Add(0f);
                    link.Heights.Add(ring.DeckY);
                    foreach (float stop in LinkStops(corner.Link)) { link.Distances.Add(stop); link.Heights.Add(ring.DeckY); }
                    link.Distances.Add(corner.Link.Length);
                    link.Heights.Add(ring.DeckY);
                    int previous = firstNode[k];
                    for (int index = 1; index < link.Distances.Count; index++)
                    {
                        int node = index == link.Distances.Count - 1 ? nextNode[k]
                            : AddNode(plan, corner.Link.Position(link.Distances[index]), ring.DeckY, groundEnd: false);
                        AddCurved(plan, link, index, previous, node);
                        previous = node;
                    }
                }
                for (int k = 0; k < count; k++)
                {
                    var corner = ring.Corners[k];
                    if (corner.FromFirst != null) AddWalks(plan, corner.FromFirst, firstNode[k]);
                    if (corner.FromNext != null) AddWalks(plan, corner.FromNext, nextNode[k]);
                }

                AddReport(plan);
                foreach (var leg in ring.Legs)
                    if (!leg.Way.Sidewalks)
                    {
                        plan.Messages.Add("A road here has no sidewalks, so a ramp foot beside it can't join one. Link it to a path.");
                        break;
                    }
                return Check(plan);
            }
        }
    }
}
