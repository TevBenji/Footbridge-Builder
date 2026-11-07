// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
//
// Where a deck rests on pillars. The deck's line is sampled every metre or so; at each sample a pillar may stand or not (never
// in a road, on a track or on another net). Of all the ways to put pillars at allowed samples so that no span is longer than
// the deck can carry, this keeps the one with the fewest pillars, then the shortest longest span, then the most even spans:
// a dynamic programme over the samples, each reached from the best way to any sample within one span behind it.
using System.Collections.Generic;
using Unity.Mathematics;

namespace FootbridgeBuilder.Core
{
    internal static class Spans
    {
        /// <summary>
        /// The samples to stand pillars on, between the first and last (which are the deck's ends and always supported), in
        /// order. Null when some stretch is longer than <paramref name="longest"/> with nowhere for a pillar:
        /// <paramref name="gapFrom"/> and <paramref name="gapTo"/> then say where (as distances along the line).
        /// </summary>
        /// <param name="distances">Each sample's distance along the line, rising.</param>
        /// <param name="allowed">Whether a pillar may stand at each sample (the two ends are supported whatever it says).</param>
        /// <param name="shortest">No span shorter than this: a piece shorter than the game's cuts at its two nodes folds.</param>
        public static List<int> Place(IReadOnlyList<float> distances, IReadOnlyList<bool> allowed, float longest, out float gapFrom, out float gapTo, float shortest = 0f)
        {
            gapFrom = gapTo = 0f;
            int count = distances.Count;
            if (count < 2) return new List<int>();
            // Per sample: the fewest pillars so far, the longest span so far and the sum of squared spans, and where from.
            var pillars = new int[count];
            var longestSoFar = new float[count];
            var squares = new float[count];
            var from = new int[count];
            for (int index = 0; index < count; index++) { pillars[index] = int.MaxValue; from[index] = -1; }
            pillars[0] = 0;
            for (int index = 1; index < count; index++)
            {
                bool last = index == count - 1;
                if (!last && !allowed[index]) continue;
                for (int back = index - 1; back >= 0; back--)
                {
                    float span = distances[index] - distances[back];
                    if (span > longest) break;
                    if (span < shortest || pillars[back] == int.MaxValue) continue;
                    int candidate = pillars[back] + (last ? 0 : 1);
                    float worst = math.max(longestSoFar[back], span), square = squares[back] + span * span;
                    if (candidate < pillars[index]
                        || (candidate == pillars[index] && (worst < longestSoFar[index] - 1e-3f
                            || (math.abs(worst - longestSoFar[index]) <= 1e-3f && square < squares[index] - 1e-3f))))
                    {
                        pillars[index] = candidate;
                        longestSoFar[index] = worst;
                        squares[index] = square;
                        from[index] = back;
                    }
                }
            }
            if (pillars[count - 1] == int.MaxValue)
            {
                // The stretch that can't be crossed: from the farthest support reachable to the next allowed one after it.
                int reached = 0;
                for (int index = 0; index < count; index++) if (pillars[index] != int.MaxValue) reached = index;
                int next = count - 1;
                for (int index = reached + 1; index < count; index++) if (allowed[index]) { next = index; break; }
                gapFrom = distances[reached];
                gapTo = distances[next];
                return null;
            }
            var chosen = new List<int>();
            for (int index = from[count - 1]; index > 0; index = from[index]) chosen.Add(index);
            chosen.Reverse();
            return chosen;
        }
    }
}
