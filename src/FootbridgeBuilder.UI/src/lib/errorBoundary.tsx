// Crash guard: a render error in OUR tree must never take down the rest of the game UI.
// Adapted from Grid Road Generator (Ti4goc). In-game experience there: a React error #130 from a
// cs2/ui component missing at runtime (despite the typings) blanked the whole cohtml tree.
// React only offers render-error capture to class components, hence the class.
// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: null-only fallback for Footbridge Builder regions.
import React from "react";

export class ErrorBoundary extends React.Component<React.PropsWithChildren<{ name: string }>, { failed: boolean }> {
    state = { failed: false };
    static getDerivedStateFromError() { return { failed: true }; }
    componentDidCatch(error: unknown, info: unknown) {
        console.error(`[FootbridgeBuilder] UI region ${this.props.name} failed`, error, info);
    }
    render() { return this.state.failed ? null : this.props.children ?? null; }
}
