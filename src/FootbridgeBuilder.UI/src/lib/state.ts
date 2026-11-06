// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
export type ToolStage = "Closed" | "Hovering" | "Planned" | "Building" | "Built" | "Failed";
const STAGES: readonly ToolStage[] = ["Closed", "Hovering", "Planned", "Building", "Built", "Failed"];

export interface ToolState {
    stage: ToolStage;
    summary: string;
    messages: string[];
    /** 0-1 while Building. */
    progress: number;
}

const object = (value: unknown): Record<string, unknown> =>
    value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
const text = (value: unknown): string => typeof value === "string" ? value : "";
const number = (value: unknown, fallback: number, minimum: number, maximum: number): number =>
    typeof value === "number" && Number.isFinite(value) ? Math.max(minimum, Math.min(maximum, value)) : fallback;

/** Never throws: a missing binding or broken JSON reads as a closed tool, so the panel can't take the game UI down. */
export function readState(raw: unknown): ToolState {
    let parsed = raw;
    try { if (typeof raw === "string") parsed = JSON.parse(raw); }
    catch { parsed = null; }
    const state = object(parsed);
    return {
        stage: STAGES.includes(state.stage as ToolStage) ? state.stage as ToolStage : "Closed",
        summary: text(state.summary),
        messages: Array.isArray(state.messages) ? state.messages.filter((value): value is string => typeof value === "string") : [],
        progress: number(state.progress, 0, 0, 1),
    };
}

export function statusLine(state: ToolState): string {
    switch (state.stage) {
        case "Hovering": return "Hover a road.";
        case "Planned": return "Ready to build";
        case "Building": return "Building...";
        case "Built": return state.summary || "Built";
        case "Failed": return state.summary || "Failed";
        default: return "";
    }
}
