// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace FootbridgeBuilder.Core
{
    /// <summary>Small plan-view helpers: polylines, distances, and curves.</summary>
    internal static class Geometry
    {
        public static float DistanceToSegment(float2 point, float2 start, float2 end)
        {
            float2 direction = end - start;
            float lengthSquared = math.lengthsq(direction);
            float fraction = lengthSquared > 1e-9f ? math.saturate(math.dot(point - start, direction) / lengthSquared) : 0f;
            return math.distance(point, start + direction * fraction);
        }

        public static float Length(IReadOnlyList<float2> line)
        {
            float length = 0f;
            for (int index = 1; index < line.Count; index++) length += math.distance(line[index - 1], line[index]);
            return length;
        }

        /// <summary>Points every <paramref name="spacing"/> metres along the line (the last spacing stretched to end exactly).</summary>
        public static List<float2> Resample(IReadOnlyList<float2> line, float spacing)
        {
            var result = new List<float2>();
            if (line.Count == 0) return result;
            float length = Length(line);
            int segments = math.max(1, (int)math.round(length / spacing));
            float step = length / segments;
            result.Add(line[0]);
            int index = 1;
            float travelled = 0f;
            float2 previous = line[0];
            for (int segment = 1; segment < segments; segment++)
            {
                float target = segment * step;
                while (index < line.Count)
                {
                    float piece = math.distance(previous, line[index]);
                    if (travelled + piece >= target)
                    {
                        float fraction = piece > 1e-6f ? (target - travelled) / piece : 0f;
                        previous = math.lerp(previous, line[index], fraction);
                        travelled = target;
                        break;
                    }
                    travelled += piece;
                    previous = line[index];
                    index++;
                }
                result.Add(previous);
            }
            result.Add(line[line.Count - 1]);
            return result;
        }

        /// <summary>Radius of the circle through three points; infinite when they are in line.</summary>
        public static float Radius(float2 a, float2 b, float2 c)
        {
            float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (math.abs(cross) < 1e-6f) return float.PositiveInfinity;
            return math.distance(a, b) * math.distance(b, c) * math.distance(c, a) / (2f * math.abs(cross));
        }

        /// <summary>The tightest radius along a line, measured over points about <paramref name="span"/> metres either side.</summary>
        public static float TightestRadius(IReadOnlyList<float2> line, float span, out int at)
        {
            at = -1;
            float tightest = float.PositiveInfinity;
            if (line.Count < 3) return tightest;
            // Lines here are resampled evenly, so a fixed index offset spans the same distance everywhere.
            float spacing = Length(line) / (line.Count - 1);
            int offset = math.max(1, (int)math.round(span / math.max(0.01f, spacing)));
            for (int index = offset; index < line.Count - offset; index++)
            {
                float radius = Radius(line[index - offset], line[index], line[index + offset]);
                if (radius < tightest) { tightest = radius; at = index; }
            }
            return tightest;
        }

        /// <summary>Cubic Hermite curve from p0 (unit tangent t0) to p1 (unit tangent t1), resampled every spacing metres.</summary>
        public static List<float2> Hermite(float2 p0, float2 t0, float2 p1, float2 t1, float tangentScale, float spacing)
        {
            float chord = math.distance(p0, p1);
            float2 m0 = t0 * chord * tangentScale, m1 = t1 * chord * tangentScale;
            int steps = math.max(8, (int)(chord / 2f));
            var dense = new List<float2>(steps + 1);
            for (int step = 0; step <= steps; step++)
            {
                float t = step / (float)steps, t2 = t * t, t3 = t2 * t;
                dense.Add((2f * t3 - 3f * t2 + 1f) * p0 + (t3 - 2f * t2 + t) * m0 + (-2f * t3 + 3f * t2) * p1 + (t3 - t2) * m1);
            }
            return Resample(dense, spacing);
        }

        /// <summary>The left of a direction in the game's plan (x right, z forward).</summary>
        public static float2 Left(float2 direction) => new float2(-direction.y, direction.x);
    }

    /// <summary>A uniform grid of buckets over the plan, for "what is near this point" queries.</summary>
    public sealed class SpatialBuckets<T>
    {
        private readonly float bucketSize;
        private readonly Dictionary<long, List<T>> buckets = new Dictionary<long, List<T>>();

        public SpatialBuckets(float bucketSize) { this.bucketSize = bucketSize; }

        private long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        public void Add(float2 minimum, float2 maximum, T item)
        {
            int x0 = (int)math.floor(minimum.x / bucketSize), x1 = (int)math.floor(maximum.x / bucketSize);
            int y0 = (int)math.floor(minimum.y / bucketSize), y1 = (int)math.floor(maximum.y / bucketSize);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    long key = Key(x, y);
                    if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<T>();
                    list.Add(item);
                }
        }

        /// <summary>Items whose box touches the bucket holding the point (callers pad their boxes by the reach they query with).</summary>
        public List<T> Near(float2 point)
        {
            return buckets.TryGetValue(Key((int)math.floor(point.x / bucketSize), (int)math.floor(point.y / bucketSize)), out var list)
                ? list : Empty;
        }

        /// <summary>Items in every bucket that the square of <paramref name="reach"/> around the point touches; an item may repeat.</summary>
        public IEnumerable<T> Within(float2 point, float reach)
        {
            int x0 = (int)math.floor((point.x - reach) / bucketSize), x1 = (int)math.floor((point.x + reach) / bucketSize);
            int y0 = (int)math.floor((point.y - reach) / bucketSize), y1 = (int)math.floor((point.y + reach) / bucketSize);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (buckets.TryGetValue(Key(x, y), out var list))
                        foreach (var item in list) yield return item;
        }

        private static readonly List<T> Empty = new List<T>();
    }
}
