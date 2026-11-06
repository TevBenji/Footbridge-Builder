// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
namespace FootbridgeBuilder.Tool
{
    /// <summary>
    /// Closed: the tool is not active. Hovering: waiting for a road under the cursor. Planned: a bridge is shown and can be built.
    /// Building, Built, Failed: the build and its result.
    /// </summary>
    public enum ToolStage { Closed, Hovering, Planned, Building, Built, Failed }

    /// <summary>What the panel shows; the tool bumps <see cref="Version"/> whenever any of it changes.</summary>
    public sealed class ToolState
    {
        public ToolStage Stage;
        public string Summary = "";
        public string[] Messages = new string[0];

        /// <summary>Build progress, 0 to 1, while building.</summary>
        public float Progress;

        // The bridge on show.
        public bool Buildable;
        public string DesignName = "";
        public float DeckHeight;
        public string UseLabel = "";
        public float UseShare;
        public float ExtraWalk;
        /// <summary>What the game will charge, read from its own pricing of the ghost; -1 until it has priced it.</summary>
        public int Cost = -1;
        public int CrossingsRemoved;
        /// <summary>A marked crosswalk still competes after the build.</summary>
        public bool CompetingMarked;
        /// <summary>How far along the road the competing crossing is, in metres.</summary>
        public float CompetingDistance;
        /// <summary>The zoned buildings the game will replace, counted from its own warnings on the ghost.</summary>
        public int BuildingsReplaced;
        /// <summary>Pieces the game itself refuses (its red errors on the ghost).</summary>
        public int Refused;
        public float Turn;

        // The paths to choose from.
        public string[] PathNames = new string[0];
        public string[] PathLabels = new string[0];

        public int Version;
    }
}
