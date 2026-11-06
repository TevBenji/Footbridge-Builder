// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
export type ToolStage = "Closed" | "Hovering" | "Planned" | "Building" | "Built" | "Failed";
const STAGES: readonly ToolStage[] = ["Closed", "Hovering", "Planned", "Building", "Built", "Failed"];

export const DESIGNS = ["Auto", "YRamps", "ZRamps", "Switchback", "SpiralRamp", "DiagonalSpan", "SnakeDeck"] as const;
export type Design = typeof DESIGNS[number];

export interface PathOption { name: string; label: string; }

export interface Settings {
    design: Design;
    path: string;
    removeCrossings: boolean;
    /** 20-150 m along the road. */
    reach: number;
    /** 0-3 m above the lowest deck the game allows. */
    extraClearance: number;
}

export interface ToolState {
    stage: ToolStage;
    summary: string;
    messages: string[];
    /** 0-1 while Building. */
    progress: number;
    buildable: boolean;
    designName: string;
    deckHeight: number;
    useLabel: string;
    useShare: number;
    extraWalk: number;
    /** -1 until the game has priced the ghost. */
    cost: number;
    crossingsRemoved: number;
    competingMarked: boolean;
    buildingsReplaced: number;
    refused: number;
    turn: number;
    paths: PathOption[];
    settings: Settings;
}

const object = (value: unknown): Record<string, unknown> =>
    value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
const text = (value: unknown): string => typeof value === "string" ? value : "";
const flag = (value: unknown, fallback: boolean): boolean => typeof value === "boolean" ? value : fallback;
const number = (value: unknown, fallback: number, minimum: number, maximum: number): number =>
    typeof value === "number" && Number.isFinite(value) ? Math.max(minimum, Math.min(maximum, value)) : fallback;
const whole = (value: unknown, fallback: number, minimum: number, maximum: number): number =>
    Math.round(number(value, fallback, minimum, maximum));

/** Never throws: a missing binding or broken JSON reads as a closed tool, so the panel can't take the game UI down. */
export function readState(raw: unknown): ToolState {
    let parsed = raw;
    try { if (typeof raw === "string") parsed = JSON.parse(raw); }
    catch { parsed = null; }
    const state = object(parsed);
    const settings = object(state.settings);
    const paths = Array.isArray(state.paths) ? state.paths.map(object)
        .filter(path => typeof path.name === "string" && path.name.length > 0)
        .map(path => ({ name: path.name as string, label: text(path.label) || path.name as string })) : [];
    return {
        stage: STAGES.includes(state.stage as ToolStage) ? state.stage as ToolStage : "Closed",
        summary: text(state.summary),
        messages: Array.isArray(state.messages) ? state.messages.filter((value): value is string => typeof value === "string") : [],
        progress: number(state.progress, 0, 0, 1),
        buildable: flag(state.buildable, false),
        designName: text(state.designName),
        deckHeight: number(state.deckHeight, 0, 0, 100),
        useLabel: text(state.useLabel),
        useShare: number(state.useShare, 0, 0, 1),
        extraWalk: number(state.extraWalk, 0, -1000, 1000),
        cost: whole(state.cost, -1, -1, 2147483647),
        crossingsRemoved: whole(state.crossingsRemoved, 0, 0, 1000),
        competingMarked: flag(state.competingMarked, false),
        buildingsReplaced: whole(state.buildingsReplaced, 0, 0, 10000),
        refused: whole(state.refused, 0, 0, 10000),
        turn: number(state.turn, 0, -30, 30),
        paths,
        settings: {
            design: DESIGNS.includes(settings.design as Design) ? settings.design as Design : "Auto",
            path: text(settings.path),
            removeCrossings: flag(settings.removeCrossings, true),
            reach: whole(settings.reach, 60, 20, 150),
            extraClearance: number(settings.extraClearance, 0.5, 0, 3),
        },
    };
}

export function statusLine(state: ToolState): string {
    switch (state.stage) {
        case "Hovering": return state.summary || "Hover a road.";
        case "Planned": return state.buildable ? state.designName || "Ready to build" : "No bridge fits here.";
        case "Building": return "Building...";
        case "Built": return state.summary || "Built";
        case "Failed": return state.summary || "Failed";
        default: return "";
    }
}

/** The green button's words: the game replaces the buildings a ramp would stand on, as its own road tool does. */
export function buildLabel(state: ToolState): string {
    const count = state.buildingsReplaced;
    if (count <= 0) return "Build";
    return count === 1 ? "Build and replace 1 building" : `Build and replace ${count} buildings`;
}

export function canBuild(state: ToolState): boolean {
    return state.stage === "Planned" && state.buildable && state.refused === 0 && state.cost >= 0;
}

export function thousands(value: number): string {
    return Math.round(value).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}

/** "+12 m walk" or "12 m shorter", for the use estimate. */
export function walk(extra: number): string {
    const metres = Math.round(Math.abs(extra));
    if (metres === 0) return "no extra walk";
    return extra > 0 ? `+${metres} m walk` : `${metres} m shorter`;
}
