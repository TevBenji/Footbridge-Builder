// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
// The layout follows Placemaker's panel (jackwrichards, GPL-3.0): docked on the right. The look is the game's own: its panel
// colours and its green primary pill. Plain elements rather than cs2/ui components, so nothing depends on the vanilla themes.
import React from "react";
import { act, playSound, useToolState } from "./lib/bindings";
import { ErrorBoundary } from "./lib/errorBoundary";
import { statusLine, ToolState } from "./lib/state";
import icon from "./assets/footbridge-builder.svg";
import closeIcon from "./assets/close.svg";
import styles from "./Panel.module.css";

const classes = (...names: (string | false | undefined)[]) => names.filter(Boolean).join(" ");

function Button({ children, onSelect, disabled = false, className, label }: React.PropsWithChildren<{
    onSelect: () => void; disabled?: boolean; className?: string; label?: string;
}>) {
    return <button type="button" aria-label={label}
        className={classes(styles.button, className, disabled && styles.disabled)}
        onMouseEnter={() => { if (!disabled) playSound("hover-item"); }}
        onClick={event => {
            event.stopPropagation();
            if (disabled) return;
            playSound("select-item");
            onSelect();
        }}>{children}</button>;
}

function statusTone(state: ToolState) {
    if (state.stage === "Failed") return styles.toneBad;
    if (state.stage === "Building") return styles.toneBusy;
    if (state.stage === "Planned" || state.stage === "Built") return styles.toneGood;
    return styles.toneIdle;
}

function FootbridgePanel() {
    const state = useToolState();
    if (state.stage === "Closed") return null;
    const building = state.stage === "Building";
    // The tool also lists its summary among the messages; showing it twice reads as noise.
    const messages = state.messages.filter(message => message !== state.summary);

    return <div className={styles.dock}>
        <div className={styles.wrap}>
            <div className={styles.panel} onMouseDown={event => event.stopPropagation()} aria-busy={building}>
                <div className={styles.header}>
                    <div className={styles.accentBar} />
                    <img className={styles.headerIcon} src={icon} />
                    <div className={styles.title}>Footbridge Builder</div>
                    <Button className={styles.close} label="Close" disabled={building} onSelect={() => act("close")}>
                        <img className={styles.closeIcon} src={closeIcon} />
                    </Button>
                </div>

                <div className={styles.content}>
                    <div className={classes(styles.status, statusTone(state))} aria-live="polite">
                        <div className={styles.statusText}>{statusLine(state)}</div>
                        {building ? <div className={styles.progressTrack}>
                            <div className={styles.progress} style={{ width: `${100 * state.progress}%` }} />
                        </div> : <div className={styles.hints}>Esc to close</div>}
                    </div>
                    {messages.map((message, index) => <div key={index} className={styles.message}>
                        <div className={styles.messageDot} />
                        <div className={styles.messageText}>{message}</div>
                    </div>)}
                </div>

                <div className={styles.footer}>
                    {/* The tool can't plan a bridge yet, so there is nothing to build. */}
                    <Button className={styles.primary} disabled onSelect={() => undefined}><div>Build</div></Button>
                </div>
            </div>
        </div>
    </div>;
}

export function PanelRoot() {
    return <ErrorBoundary name="panel"><FootbridgePanel /></ErrorBoundary>;
}
