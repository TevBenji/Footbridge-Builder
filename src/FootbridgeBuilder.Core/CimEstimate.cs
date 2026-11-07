// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
//
// How many cims will take the bridge, from the game's own pathfinding numbers (docs: cim-crossing-behaviour.md, PRD 5.5):
// a cim pays for time (walk length / 1.67 m/s times its time weight, spread from 1.25 to 20 over cims) and a street
// crossing adds a fixed penalty (about 10 marked, about 200 unsafe). Climbing costs nothing but its extra length. So a cim
// takes the bridge when its extra walk costs less than the crossing's penalty, which happens below a time weight of
// 1.67 x penalty / extra walk; the share is the part of the weight spread under that line.
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace FootbridgeBuilder.Core
{
    public static class CimEstimate
    {
        /// <summary>The along-the-road trips start and end this far from the bridge.</summary>
        public const float AlongTrip = 60f;
        private const float WalkSpeed = 1.67f, LowWeight = 1.25f, HighWeight = 20f;
        public const float UnsafePenalty = 200f, MarkedPenalty = 10f;

        public struct StreetOption
        {
            public long Id;
            /// <summary>The crossing's station along the bridged road.</summary>
            public float Station;
            public bool Marked;
            /// <summary>
            /// What the walk at street level pays, when it isn't this one crossing's own (over a pair of roads: both roads'
            /// crossings together); 0 for the crossing's own.
            /// </summary>
            public float Penalty;
            /// <summary>Walking at street level beyond the crossing itself (between a pair of roads' two crossings).</summary>
            public float Detour;
        }

        public struct Result
        {
            public float Share;
            public float ExtraWalk;
            /// <summary>The walk over the bridge, averaged over the three trips: the tie-break when shares are equal.</summary>
            public float MeanWalk;
            public long Competing;
            public bool CompetingMarked;
        }

        /// <summary>The share of cims taking a route that is <paramref name="extraWalk"/> metres longer instead of paying a crossing penalty.</summary>
        public static float Share(float extraWalk, float penalty)
        {
            if (extraWalk <= 0f) return 1f;
            float weight = WalkSpeed * penalty / extraWalk;
            return math.saturate((weight - LowWeight) / (HighWeight - LowWeight));
        }

        public static string Label(float share) => share >= 0.7f ? "Most" : share >= 0.35f ? "About half" : "Few";

        /// <summary>
        /// Averages three trips: straight over at the bridge, and along the road each way. <paramref name="bridgeWalk"/> gives
        /// the walk over the bridge from a sidewalk station on one side to one on the other.
        /// </summary>
        public static Result Estimate(float station, float roadWidth, IReadOnlyList<StreetOption> options, Func<float, float, float> bridgeWalk)
        {
            var result = new Result { Share = 0f };
            var trips = new[] { (station, station), (station - AlongTrip, station + AlongTrip), (station + AlongTrip, station - AlongTrip) };
            for (int trip = 0; trip < trips.Length; trip++)
            {
                var (from, to) = trips[trip];
                float bridge = bridgeWalk(from, to);
                result.MeanWalk += bridge / trips.Length;
                float share = 1f, shortestStreet = float.MaxValue;
                long competing = 0;
                bool competingMarked = false;
                foreach (var option in options)
                {
                    float street = math.abs(option.Station - from) + roadWidth + math.abs(option.Station - to) + option.Detour;
                    shortestStreet = math.min(shortestStreet, street);
                    float optionShare = Share(bridge - street, option.Penalty > 0f ? option.Penalty : option.Marked ? MarkedPenalty : UnsafePenalty);
                    if (optionShare < share || (competing == 0 && optionShare <= share))
                    {
                        share = optionShare;
                        competing = option.Id;
                        competingMarked = option.Marked;
                    }
                }
                result.Share += share / trips.Length;
                if (trip == 0)
                {
                    result.ExtraWalk = bridge - (options.Count > 0 ? shortestStreet : roadWidth);
                    result.Competing = competing;
                    result.CompetingMarked = competingMarked;
                }
            }
            return result;
        }
    }
}
