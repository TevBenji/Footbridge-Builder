// Bindings to Game/UI/PlacemakerUISystem (shapes: ../contract.ts, which is authoritative).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: one defensive JSON state binding and the Footbridge Builder triggers.
import { useMemo } from "react";
import { bindValue, trigger, useValue, ValueBinding } from "cs2/api";
import { readState } from "./state";

const GROUP = "FootbridgeBuilder";
const emptyBinding: ValueBinding<unknown> = {
    value: null,
    subscribe: () => ({ value: null, setChangeListener: () => undefined, dispose: () => undefined }),
    dispose: () => undefined,
};
const stateBinding = (() => {
    try { return bindValue<unknown>(GROUP, "state", null); }
    catch (error) { console.warn("[FootbridgeBuilder] State binding unavailable", error); return emptyBinding; }
})();

export function useToolState() {
    const raw = useValue(stateBinding);
    return useMemo(() => readState(raw), [raw]);
}

export type Action = "toggleTool" | "close" | "build";
export type TextTrigger = "setDesign" | "setPath" | "openLink";
export type NumberTrigger = "setReach" | "setExtraClearance" | "setTurn";
export type ToggleTrigger = "setRemoveCrossings" | "setPanelCollapsed";
export type PlaceTrigger = "setPanelPlace";
export function act(name: Action): void;
export function act(name: TextTrigger, value: string): void;
export function act(name: NumberTrigger, value: number): void;
export function act(name: ToggleTrigger, value: boolean): void;
export function act(name: PlaceTrigger, x: number, y: number): void;
export function act(name: Action | TextTrigger | NumberTrigger | ToggleTrigger | PlaceTrigger, value?: string | number | boolean, second?: number): void {
    try {
        if (value === undefined) trigger(GROUP, name);
        else if (second !== undefined) trigger(GROUP, name, value, second);
        else trigger(GROUP, name, value);
    } catch (error) { console.error(`[FootbridgeBuilder] Trigger ${name} failed`, error); }
}

/** The game's own UI sounds, through its audio binding; silent when the binding is missing. */
export function playSound(sound: "hover-item" | "select-item" | "toggle-on" | "toggle-off"): void {
    try { trigger("audio", "playSound", sound, 1); }
    catch { /* no audio binding */ }
}
