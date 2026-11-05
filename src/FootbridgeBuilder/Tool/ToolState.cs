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

        public int Version;
    }
}
