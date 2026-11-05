// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
using Unity.Mathematics;

namespace FootbridgeBuilder.Tool
{
    internal sealed class RightClickGesture
    {
        private bool down;
        private float downAt, moved;

        internal void Reset() => down = false;

        internal bool Update(bool pressed, bool held, float2 movement, bool overWorld, float now)
        {
            if (pressed)
            {
                down = held;
                downAt = now;
                moved = math.length(movement);
                return !held && overWorld;
            }
            if (!down) return false;
            moved += math.length(movement);
            if (held) return false;
            down = false;
            return overWorld && moved < 8f && now - downAt < 0.5f;
        }
    }
}
