// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
using Unity.Mathematics;

namespace FootbridgeBuilder.Tool
{
    // Pure helpers are shared with the off-game tests; no live game data escapes a snapshot.
    internal static class SiteGeometry
    {
        internal static float Bilinear(float[] values, int columns, int rows, float2 origin, float spacing, float2 point)
        {
            float2 coordinate = math.clamp((point - origin) / spacing, float2.zero, new float2(columns - 1, rows - 1));
            int column = math.min((int)coordinate.x, columns - 2);
            int row = math.min((int)coordinate.y, rows - 2);
            float2 fraction = coordinate - new float2(column, row);
            return math.lerp(
                math.lerp(values[row * columns + column], values[row * columns + column + 1], fraction.x),
                math.lerp(values[(row + 1) * columns + column], values[(row + 1) * columns + column + 1], fraction.x), fraction.y);
        }

        internal static float DistanceSquared(float2 point, float2 start, float2 end)
        {
            float2 direction = end - start;
            float lengthSquared = math.lengthsq(direction);
            float fraction = lengthSquared > 0f ? math.saturate(math.dot(point - start, direction) / lengthSquared) : 0f;
            return math.distancesq(point, start + fraction * direction);
        }

        internal static bool Inside(float2 point, float2[] quad)
        {
            bool positive = false;
            bool negative = false;
            for (int index = 0; index < quad.Length; index++)
            {
                float2 edge = quad[(index + 1) % quad.Length] - quad[index];
                float2 offset = point - quad[index];
                float cross = edge.x * offset.y - edge.y * offset.x;
                positive |= cross > 0.001f;
                negative |= cross < -0.001f;
            }
            return !(positive && negative);
        }
    }
}
