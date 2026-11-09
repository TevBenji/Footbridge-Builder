// Native hover tooltips. Same bubble as the game's tool tooltips (cs2/ui Tooltip) with the vanilla DescriptionTooltip
// title/description styling.
// Adapted from Grid Road Generator (Ti4goc). Note: a plain HTML title attribute shows nothing in
// Gameface, so every hint must go through a Tooltip.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: the toolbar tooltip with a hotkey footer and a description for every setting.
import React from "react";
import * as ui from "cs2/ui";
import styles from "../Panel.module.css";

/** What each row does, shown when the pointer rests on it. Keyed by the row's label. */
export const SETTING_TIPS: Record<string, string> = {
    "Design": "The shape of the ramps. Auto tries every shape that fits and keeps the one most cims will use.",
    "Path": "The footpath the bridge is built from. Wider paths need more room.",
    "Remove crossings": "Takes the crosswalk off street crossings near the bridge, so cims prefer it. Cims can still run across where a crosswalk was, so the bridge has to save them walking.",
    "Crossing reach": "How far along the road crossings are removed.",
    "Extra clearance": "Height above the lowest deck the game allows. Every metre up is walking that loses cims.",
    "Turn": "Turns the bridge across the road. [ and ] turn it too.",
    "Will cims use it?": "From the game's own pathfinding numbers: cims compare the extra walk with the cost of crossing at street level. Climbing costs them nothing; only distance counts.",
    "Deck height": "The deck's height above the road: the lowest the game allows plus the extra clearance.",
    "Cost": "What the game charges for these pieces, read from its own price for the preview.",
    "Crossings removed": "Marked crosswalks in reach that lose their crosswalk when you build.",
    "Report a bug": "Opens a new issue on GitHub. Add a screenshot and your FootbridgeBuilder.log.",
    "Discord": "Questions, ideas and news.",
};

export function SettingTip({ label, children }: { label?: string; children: React.ReactElement }) {
    const text = label ? SETTING_TIPS[label] : undefined;
    if (!text || !ui.Tooltip) return children;
    return <ui.Tooltip direction="right" delayTime={400} hideOnInteraction tooltip={<div className={styles.tip}>
        <strong>{label}</strong>
        <div>{text}</div>
    </div>}>{children}</ui.Tooltip>;
}

export function ToolbarTip({ children }: { children: React.ReactElement }) {
    if (!ui.Tooltip) return children;
    return <ui.Tooltip direction="up" delayTime={250} tooltip={<div className={styles.tip}>
        <strong>Footbridge Builder</strong>
        <div>Hover a road and click to pin a footbridge there, then press Build in the panel. Hover the middle of a junction or roundabout for a ring over every road there.</div>
        <div className={styles.hotkey}>Ctrl+Shift+F</div>
    </div>}>{children}</ui.Tooltip>;
}
