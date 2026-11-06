// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
using System.Collections.Generic;
using Unity.Mathematics;

namespace FootbridgeBuilder.Core
{
    /// <summary>The road's centre line, measured by distance along it (its station).</summary>
    internal sealed class RoadLine
    {
        // Planning asks for the nearest station thousands of times, always within a few tens of metres of the road.
        private const float IndexReach = 40f;
        private readonly float3[] points;
        private readonly float[] stations;
        private readonly SpatialBuckets<int> segments = new SpatialBuckets<int>(16f);

        public RoadLine(float3[] points)
        {
            this.points = points;
            stations = new float[points.Length];
            for (int index = 1; index < points.Length; index++)
            {
                stations[index] = stations[index - 1] + math.distance(points[index - 1].xz, points[index].xz);
                float2 a = points[index - 1].xz, b = points[index].xz;
                segments.Add(math.min(a, b) - IndexReach, math.max(a, b) + IndexReach, index - 1);
            }
        }

        public float Length => stations.Length == 0 ? 0f : stations[stations.Length - 1];

        public float3 Point(float station)
        {
            int index = Segment(station, out float t);
            return math.lerp(points[index], points[index + 1], t);
        }

        /// <summary>The direction of travel at a station, averaged over a few metres so a sampled curve doesn't jitter.</summary>
        public float2 Tangent(float station)
        {
            float2 before = Point(math.max(0f, station - 2f)).xz, after = Point(math.min(Length, station + 2f)).xz;
            return math.normalizesafe(after - before, new float2(1f, 0f));
        }

        /// <summary>A point beside the road: positive offsets are on the left of the direction of travel.</summary>
        public float2 Offset(float station, float offset) => Point(station).xz + Geometry.Left(Tangent(station)) * offset;

        public float Nearest(float2 point)
        {
            float best = float.MaxValue, bestStation = 0f;
            void Try(int index)
            {
                float2 a = points[index].xz, b = points[index + 1].xz, ab = b - a;
                float t = math.saturate(math.dot(point - a, ab) / math.max(1e-6f, math.lengthsq(ab)));
                float distance = math.distancesq(point, a + ab * t);
                // Ties go to the earlier station, so the answer doesn't depend on the order segments were indexed.
                float station = math.lerp(stations[index], stations[index + 1], t);
                if (distance < best || (distance == best && station < bestStation)) { best = distance; bestStation = station; }
            }
            var near = segments.Near(point);
            foreach (int index in near) Try(index);
            // Only the segments within reach are indexed, so a far point (or one that found none in reach) scans them all.
            if (near.Count == 0 || best > IndexReach * IndexReach)
                for (int index = 0; index + 1 < points.Length; index++) Try(index);
            return bestStation;
        }

        private int Segment(float station, out float t)
        {
            if (points.Length < 2) { t = 0f; return 0; }
            station = math.clamp(station, 0f, Length);
            int low = 0, high = points.Length - 2;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (stations[middle] <= station) low = middle; else high = middle - 1;
            }
            float span = stations[low + 1] - stations[low];
            t = span > 1e-6f ? (station - stations[low]) / span : 0f;
            return low;
        }
    }

    /// <summary>A walking line in plan, sampled every half metre or so, with its length so far at each sample.</summary>
    internal sealed class Route
    {
        private readonly List<float2> points = new List<float2>();
        private readonly List<float> lengths = new List<float>();

        public float Length => lengths.Count == 0 ? 0f : lengths[lengths.Count - 1];

        public void Add(float2 point)
        {
            if (points.Count > 0)
            {
                float step = math.distance(points[points.Count - 1], point);
                if (step < 1e-4f) return;
                lengths.Add(Length + step);
            }
            else lengths.Add(0f);
            points.Add(point);
        }

        public float2 Position(float distance)
        {
            int index = Find(distance, out float t);
            return index + 1 < points.Count ? math.lerp(points[index], points[index + 1], t) : points[index];
        }

        public float2 Heading(float distance)
        {
            // A route cut short by the end of the road can be a single point: it has no heading of its own.
            if (points.Count < 2) return new float2(1f, 0f);
            int index = Find(distance, out _);
            if (index + 1 >= points.Count) index = points.Count - 2;
            return math.normalizesafe(points[index + 1] - points[index], new float2(1f, 0f));
        }

        private int Find(float distance, out float t)
        {
            t = 0f;
            if (points.Count < 2) return 0;
            distance = math.clamp(distance, 0f, Length);
            int low = 0, high = points.Count - 2;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (lengths[middle] <= distance) low = middle; else high = middle - 1;
            }
            float span = lengths[low + 1] - lengths[low];
            t = span > 1e-6f ? (distance - lengths[low]) / span : 0f;
            return low;
        }

        // ── builders ──

        /// <summary>Beside the road, following its curve, from a station in one direction until the road runs out.</summary>
        public static Route Along(RoadLine road, float station, float direction, float offset, float maxLength)
        {
            var route = new Route();
            for (float travelled = 0f; travelled <= maxLength; travelled += 1f)
            {
                float at = station + direction * travelled;
                if (at < 0f || at > road.Length) break;
                route.Add(road.Offset(at, offset));
            }
            return route;
        }

        public Route Straight(float2 heading, float length)
        {
            float2 from = points[points.Count - 1];
            for (float travelled = 0.5f; travelled < length; travelled += 0.5f) Add(from + heading * travelled);
            Add(from + heading * length);
            return this;
        }

        /// <summary>A circular turn from the current heading; positive angles turn left.</summary>
        public Route Arc(float2 heading, float radius, float angle)
        {
            float2 from = points[points.Count - 1];
            float side = math.sign(angle);
            float2 centre = from + Geometry.Left(heading) * radius * side;
            float2 spoke = from - centre;
            int steps = math.max(2, (int)math.ceil(math.abs(angle) * radius / 0.5f));
            for (int step = 1; step <= steps; step++)
            {
                float turn = angle * step / steps;
                math.sincos(turn, out float sin, out float cos);
                Add(centre + new float2(spoke.x * cos - spoke.y * sin, spoke.x * sin + spoke.y * cos));
            }
            return this;
        }

        /// <summary>The route up to <paramref name="distance"/>, as a new route.</summary>
        public Route Prefix(float distance)
        {
            var route = new Route();
            for (int index = 0; index < points.Count && lengths[index] < distance; index++) route.Add(points[index]);
            route.Add(Position(distance));
            return route;
        }

        public static Route From(float2 start)
        {
            var route = new Route();
            route.Add(start);
            return route;
        }

        public void Append(Route other, int skipFirst = 1)
        {
            for (int index = skipFirst; index < other.points.Count; index++) Add(other.points[index]);
        }
    }
}
