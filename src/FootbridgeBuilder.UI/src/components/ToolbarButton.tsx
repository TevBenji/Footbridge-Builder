// Placemaker's button in the game's own tool bar, first in the group with Zones, Areas and Signature buildings (the user: not
// on the right with the other mods, no round black background, beside the zones). The vanilla ToolbarButtonStrip is extended;
// our button is rendered into its first group (a portal into the game's own element, re-attached if the game redraws it) with
// the strip's own button classes and the transparent-button theme its buttons use, so it looks and behaves like its neighbours.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: single Footbridge Builder toggle, original bridge icon and redraw observation; the slot only checks its parent, so it never fights another mod for first place.
import React, { useLayoutEffect, useRef, useState } from "react";
import { createPortal, findDOMNode } from "react-dom";
import { getModule, ModuleRegistryExtend } from "cs2/modding";
import * as ui from "cs2/ui";
import { act, useToolState } from "../lib/bindings";
import { ErrorBoundary } from "../lib/errorBoundary";
import { ToolbarTip } from "../lib/tips";
import { vanilla } from "../lib/vanilla";
import icon from "../assets/footbridge-builder.svg";

export const TOOLBAR_STRIP = "game-ui/game/components/toolbar/top/toolbar-button-strip/toolbar-button-strip.tsx";
function classes(path: string): Record<string, string> {
    try { return getModule(path, "classes") ?? {}; }
    catch { return {}; }
}
const strip = classes("game-ui/game/components/toolbar/top/toolbar-button-strip/toolbar-button-strip.module.scss");
const transparent = classes("game-ui/game/themes/transparent-button.module.scss");

function Toggle() {
    const state = useToolState();
    if (!ui.Button) return null;
    return <ToolbarTip><ui.Button src={icon} theme={transparent}
        className={[strip.buttonNew ?? strip.button, transparent.toggleStates].filter(Boolean).join(" ")}
        focusKey={vanilla.focusDisabled} selected={state.stage !== "Closed"}
        aria-label="Footbridge Builder" onSelect={() => act("toggleTool")} /></ToolbarTip>;
}

// A sibling marker would hide the real strip from other mods' extensions when several are loaded.
// ponytail: findDOMNode targets the game's React 18 runtime; use a forwarded host ref when the game exposes one or upgrades React.
class StripHost extends React.Component<{ component: React.ComponentType<any>; componentProps: any }> {
    render() { const Component = this.props.component; return <Component {...this.props.componentProps} />; }
}

function Slot({ host }: { host: React.RefObject<StripHost> }) {
    const own = useRef<HTMLDivElement | null>(null);
    const [slot, setSlot] = useState<HTMLElement | null>(null);
    useLayoutEffect(() => {
        let observer: MutationObserver | undefined;
        try {
            const stripElement = findDOMNode(host.current);
            if (!(stripElement instanceof HTMLElement)) return;
            const attach = () => {
                try {
                    const currentStrip = findDOMNode(host.current);
                    if (!(currentStrip instanceof HTMLElement)) return;
                    const group = currentStrip.firstElementChild;
                    if (!group) return;
                    if (!own.current) {
                        own.current = document.createElement("div");
                        own.current.style.display = "flex";
                        own.current.style.marginRight = "6rem";
                    }
                    // Only when it fell out: insisting on first place would make two mods doing this move each other forever.
                    if (own.current.parentElement !== group) group.insertBefore(own.current, group.firstElementChild);
                    setSlot(previous => previous === own.current ? previous : own.current);
                } catch (error) { console.error("[FootbridgeBuilder] Toolbar slot failed", error); }
            };
            attach();
            observer = new MutationObserver(attach);
            observer.observe(stripElement.parentElement ?? stripElement, { childList: true, subtree: true });
        } catch (error) { console.error("[FootbridgeBuilder] Toolbar attachment failed", error); }
        return () => observer?.disconnect();
    });
    useLayoutEffect(() => () => own.current?.remove(), []);
    return slot ? createPortal(<Toggle />, slot) : null;
}

export const withToolbarButton: ModuleRegistryExtend = Component => props => {
    const host = useRef<StripHost>(null);
    return <><StripHost ref={host} component={Component} componentProps={props ?? {}} /><ErrorBoundary name="toolbar"><Slot host={host} /></ErrorBoundary></>;
};
