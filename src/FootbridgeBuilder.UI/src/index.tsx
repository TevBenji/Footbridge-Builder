// Placemaker UI entry. Registration pattern adapted from Grid Road Generator (Ti4goc) and
// CS2-NetworkTools (Luca Rager, MIT). Each hook-up is isolated in its own try/catch: one failing
// registration (e.g. a game update renaming a module) must not cost the others.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: register only Footbridge Builder' panel and bottom toolbar button.
import { ModRegistrar } from "cs2/modding";
import { PanelRoot } from "./Panel";
import { initializeVanilla } from "./lib/vanilla";
import { TOOLBAR_STRIP, withToolbarButton } from "./components/ToolbarButton";

const register: ModRegistrar = registry => {
    try { initializeVanilla(registry); }
    catch (error) { console.error("[FootbridgeBuilder] Vanilla lookup failed", error); }
    try { registry.append("Game", PanelRoot); }
    catch (error) { console.error("[FootbridgeBuilder] Panel registration failed", error); }
    try { registry.extend(TOOLBAR_STRIP, "ToolbarButtonStrip", withToolbarButton); }
    catch (error) { console.error("[FootbridgeBuilder] Toolbar registration failed", error); }
};
export default register;
