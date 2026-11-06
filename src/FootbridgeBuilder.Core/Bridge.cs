// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace FootbridgeBuilder.Core
{
    /// <summary>The bridge shapes the planner can make. Auto picks per side and keeps the one most cims will use.</summary>
    public enum Design { Auto, YRamps, ZRamps, Switchback, SpiralRamp, DiagonalSpan, SnakeDeck }

    public enum CrossingControl { Off, RemoveInReach }

    public enum ObstacleKind
    {
        /// <summary>A zoned building: the game replaces it when a net overlaps it, as its own road tool does.</summary>
        Building,
        /// <summary>A service or signature building: never replaced.</summary>
        FixedBuilding,
        /// <summary>Another road or path: a piece may pass over it only with room to spare.</summary>
        Net,
        /// <summary>The road being bridged: only the deck may cross it.</summary>
        HostRoad,
        Water,
    }

    /// <summary>The footpath the bridge is built from, read from the game's prefab.</summary>
    public sealed class PathSpec
    {
        public string Name = "Pavement Path";
        public float Width = 3f;
        /// <summary>The steepest the game lets this path climb, as rise over run.</summary>
        public float MaxSlope = 0.25f;
        /// <summary>How far the game cuts a piece back from a node it shares.</summary>
        public float NodeOffset = 0.5f;
        public float MaxEdgeLength = 200f;
        /// <summary>The highest a node may be raised above the ground.</summary>
        public float MaxElevation = 30f;
    }

    /// <summary>Something on the ground the bridge must respect, as a four-cornered outline.</summary>
    public sealed class Obstacle
    {
        public float2 A, B, C, D;
        public ObstacleKind Kind;
        /// <summary>For nets: the height of the net's surface.</summary>
        public float Surface;
        public long Id;
    }

    /// <summary>A road node where cims can cross at street level: marked crosswalks, and the unsafe crossings at plain nodes.</summary>
    public sealed class Crossing
    {
        public long Id;
        public float2 Position;
        public bool Marked;
        /// <summary>How far from the node a deck must stay to clear the other roads that meet there.</summary>
        public float ClearDistance;
    }

    public sealed class BridgeRequest
    {
        /// <summary>The road's centre line, about 2 m apart, running on through its straight-on neighbours.</summary>
        public float3[] Road = Array.Empty<float3>();
        /// <summary>The road's full width, sidewalks included.</summary>
        public float RoadWidth = 12f;
        public bool RoadHasSidewalks = true;
        public float2 Cursor;
        public float TurnDegrees;
        /// <summary>The lowest deck the game accepts over this road, measured from the road surface.</summary>
        public float Clearance = 5.5f;
        public float ExtraClearance = 0.5f;
        public Design Design = Design.Auto;
        public CrossingControl CrossingControl = CrossingControl.RemoveInReach;
        public float CrossingReach = 60f;
        public PathSpec Path = new PathSpec();
        public Func<float2, float> Ground = _ => 0f;
        public IReadOnlyList<Obstacle> Obstacles = Array.Empty<Obstacle>();
        public IReadOnlyList<Crossing> Crossings = Array.Empty<Crossing>();
    }

    public struct PlannedNode
    {
        public float3 Position;
        /// <summary>Height above the ground, which the game wants on each node.</summary>
        public float Elevation;
        public bool ForceElevated;
        /// <summary>A ramp's foot: a dead end on the ground that the game joins to the sidewalk.</summary>
        public bool GroundEnd;
    }

    /// <summary>One net piece between two nodes, as a cubic curve (the nodes are its ends).</summary>
    public struct PlannedPiece
    {
        public int Start, End;
        public float3 B, C;
        public bool ForceElevated;
        public bool Deck;
    }

    public sealed class BridgePlan
    {
        /// <summary>False when nothing fits: the ghost turns red and <see cref="Messages"/> says why.</summary>
        public bool Buildable;
        public Design Design;
        public string DesignName = "";
        public readonly List<PlannedNode> Nodes = new List<PlannedNode>();
        public readonly List<PlannedPiece> Pieces = new List<PlannedPiece>();
        /// <summary>The deck's height above the road surface.</summary>
        public float DeckHeight;
        /// <summary>How far the bridge moved along the road from the cursor to find room.</summary>
        public float Shift;
        /// <summary>The extra walk for a cim crossing straight over, against the best street crossing; negative is shorter.</summary>
        public float ExtraWalk;
        /// <summary>The expected share of cims crossing here who take the bridge, 0 to 1.</summary>
        public float UseShare;
        public string UseLabel = "";
        public readonly List<long> CrossingsToRemove = new List<long>();
        /// <summary>The street crossing that competes hardest after the build, 0 when none.</summary>
        public long CompetingCrossing;
        public bool CompetingMarked;
        public readonly List<long> BuildingsToReplace = new List<long>();
        public readonly List<string> Messages = new List<string>();

        public float3 Position(int node) => Nodes[node].Position;
    }
}
