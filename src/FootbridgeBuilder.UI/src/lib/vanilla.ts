// Vanilla game UI components resolved from the cohtml module registry.
// Adapted from Grid Road Generator (Ti4goc); vanilla.ts pattern from CS2-NetworkTools
// (Luca Rager, MIT, https://github.com/lucarager/CS2-NetworkTools).
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: resolve only the focus key used by Footbridge Builder.
import { FocusKey } from "cs2/ui";
import { ModuleRegistry } from "cs2/modding";

export const vanilla: { focusDisabled?: FocusKey } = {};
export function initializeVanilla(registry: ModuleRegistry) {
    try { vanilla.focusDisabled = registry.get("game-ui/common/focus/focus-key.ts", "FOCUS_DISABLED"); }
    catch (error) { console.warn("[FootbridgeBuilder] Focus key unavailable", error); }
}
