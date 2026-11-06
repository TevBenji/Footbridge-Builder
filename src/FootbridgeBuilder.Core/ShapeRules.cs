// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
//
// The game's net-shape rules (PRD 8.3), checked before the game sees a piece, so a bridge is never offered that would
// fold or be refused: a piece must outlast what the game cuts off it at each node (plus 0.2 m), a bend must be wider than
// the path's half width plus its node offset plus 1 m, no piece may be longer than the path allows, raised pieces stay at
// 12 m or less because the game re-derives their height between the ends, and no ramp is steeper than the path allows.
using System.Collections.Generic;
using Unity.Mathematics;

namespace FootbridgeBuilder.Core
{
    public static class ShapeRules
    {
        private const int Samples = 16;

        public static List<string> Problems(BridgePlan plan, PathSpec path)
        {
            var problems = new List<string>();
            float half = path.Width * 0.5f, offset = math.max(0f, path.NodeOffset);
            float minRadius = half + offset + 1f;
            var ends = new List<(int Piece, float2 Direction, float Grade)>[plan.Nodes.Count];
            for (int node = 0; node < ends.Length; node++) ends[node] = new List<(int, float2, float)>();
            for (int index = 0; index < plan.Pieces.Count; index++)
            {
                var piece = plan.Pieces[index];
                float3 a = plan.Position(piece.Start), d = plan.Position(piece.End);
                float run = math.max(1e-3f, Length(plan, piece));
                ends[piece.Start].Add((index, math.normalizesafe((piece.B - a).xz), (d.y - a.y) / run));
                ends[piece.End].Add((index, math.normalizesafe((piece.C - d).xz), (a.y - d.y) / run));
            }

            for (int index = 0; index < plan.Pieces.Count; index++)
            {
                var piece = plan.Pieces[index];
                float length = Length(plan, piece);
                float cut = Cut(ends[piece.Start], index, half, offset) + Cut(ends[piece.End], index, half, offset);
                Add(problems, length > path.MaxEdgeLength, "a piece is longer than the path allows");
                Add(problems, !piece.Deck && length > FootbridgePlanner.RaisedPieceLength + 0.05f, "a raised piece is longer than 12 m");
                Add(problems, length < cut + 0.2f, "a piece is too short and would fold");
                Add(problems, float.IsNaN(cut), "two pieces meet at too sharp an angle");
                Add(problems, TightestRadius(plan, piece) < minRadius - 0.05f, "a bend is too tight");
                float rise = math.abs(plan.Position(piece.End).y - plan.Position(piece.Start).y);
                Add(problems, rise / math.max(1e-3f, length) > path.MaxSlope + 1e-3f, "a ramp is steeper than the path allows");
            }
            return problems;
        }

        // What the game cuts off a piece at one node: the node offset at a dead end or straight on (plus the kink between
        // the two grades times the width), more at a corner, where the neighbour's half width also gets in the way.
        private static float Cut(List<(int Piece, float2 Direction, float Grade)> atNode, int piece, float half, float offset)
        {
            (int Piece, float2 Direction, float Grade) self = default;
            foreach (var end in atNode) if (end.Piece == piece) self = end;
            float cut = offset;
            foreach (var other in atNode)
            {
                if (other.Piece == piece) continue;
                float angle = math.acos(math.clamp(math.dot(self.Direction, other.Direction), -1f, 1f));
                if (angle >= math.radians(150f)) cut = math.max(cut, offset + math.abs(self.Grade + other.Grade) * half * 2f);
                else if (angle < math.radians(20f)) return float.NaN;
                else cut = math.max(cut, (half + offset + half * math.cos(angle)) / math.sin(angle));
            }
            return cut;
        }

        private static float3 Point(BridgePlan plan, PlannedPiece piece, float t)
        {
            float3 a = plan.Position(piece.Start), d = plan.Position(piece.End);
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * piece.B + 3f * u * t * t * piece.C + t * t * t * d;
        }

        public static float Length(BridgePlan plan, PlannedPiece piece)
        {
            float length = 0f;
            float2 previous = Point(plan, piece, 0f).xz;
            for (int step = 1; step <= Samples; step++)
            {
                float2 point = Point(plan, piece, step / (float)Samples).xz;
                length += math.distance(previous, point);
                previous = point;
            }
            return length;
        }

        private static float TightestRadius(BridgePlan plan, PlannedPiece piece)
        {
            var points = new List<float2>();
            for (int step = 0; step <= Samples; step++) points.Add(Point(plan, piece, step / (float)Samples).xz);
            return Geometry.TightestRadius(points, 1.5f, out _);
        }

        private static void Add(List<string> problems, bool failed, string problem)
        {
            if (failed && !problems.Contains(problem)) problems.Add(problem);
        }
    }
}
