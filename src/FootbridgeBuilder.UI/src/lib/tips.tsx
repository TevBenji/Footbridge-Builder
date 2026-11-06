// Native hover tooltips. Same bubble as the game's tool tooltips (cs2/ui Tooltip) with the vanilla DescriptionTooltip
// title/description styling.
// Adapted from Grid Road Generator (Ti4goc). Note: a plain HTML title attribute shows nothing in
// Gameface, so every hint must go through a Tooltip.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: one toolbar tooltip with a hotkey footer, using cs2/ui.
import React from "react";
import * as ui from "cs2/ui";
import styles from "../Panel.module.css";

export function ToolbarTip({ children }: { children: React.ReactElement }) {
    if (!ui.Tooltip) return children;
    return <ui.Tooltip direction="up" delayTime={250} tooltip={<div className={styles.tip}>
        <strong>Footbridge Builder</strong>
        <div>Hover a road and click to put a footbridge over it.</div>
        <div className={styles.hotkey}>Ctrl+Shift+F</div>
    </div>}>{children}</ui.Tooltip>;
}
