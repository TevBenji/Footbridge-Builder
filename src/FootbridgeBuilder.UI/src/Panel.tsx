// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
// The layout follows Placemaker's panel (jackwrichards, GPL-3.0): docked on the right. The look is the game's own: its panel
// colours, its tick boxes and its green primary pill. Plain elements rather than cs2/ui components, so nothing depends on
// the vanilla themes.
import React, { useEffect, useState } from "react";
import { act, NumberTrigger, playSound, useToolState } from "./lib/bindings";
import { ErrorBoundary } from "./lib/errorBoundary";
import { buildLabel, canBuild, Design, statusLine, thousands, ToolState, walk } from "./lib/state";
import { SettingTip } from "./lib/tips";
import icon from "./assets/footbridge-builder.svg";
import closeIcon from "./assets/close.svg";
import checkIcon from "./assets/check.svg";
import chevronIcon from "./assets/chevron.svg";
import styles from "./Panel.module.css";

const classes = (...names: (string | false | undefined)[]) => names.filter(Boolean).join(" ");

const DESIGN_LABELS: [Design, string][] = [
    ["Auto", "Auto: the one cims use most"],
    ["YRamps", "Y-ramps along the sidewalk"],
    ["ZRamps", "Z-ramps"],
    ["Switchback", "Switchback"],
    ["SpiralRamp", "Spiral ramp"],
    ["DiagonalSpan", "Diagonal span"],
    ["SnakeDeck", "Snake deck"],
];

function Button({ children, onSelect, disabled = false, selected = false, className, label, sound = "select-item" }: React.PropsWithChildren<{
    onSelect: () => void; disabled?: boolean; selected?: boolean; className?: string; label?: string;
    sound?: "select-item" | "toggle-on" | "toggle-off";
}>) {
    return <button type="button" aria-label={label} aria-pressed={selected}
        className={classes(styles.button, className, selected && styles.selected, disabled && styles.disabled)}
        onMouseEnter={() => { if (!disabled) playSound("hover-item"); }}
        onClick={event => {
            event.stopPropagation();
            if (disabled) return;
            playSound(sound);
            onSelect();
        }}>{children}</button>;
}

function Row({ label, children }: React.PropsWithChildren<{ label: string }>) {
    return <SettingTip label={label}><div className={styles.row}>
        <div className={styles.label}>{label}</div>
        <div className={styles.editor}>{children}</div>
    </div></SettingTip>;
}

function Section({ title, children }: React.PropsWithChildren<{ title: string }>) {
    return <div className={styles.section}>
        <div className={styles.sectionHead}>
            <div className={styles.sectionTitle}>{title}</div>
            <div className={styles.sectionRule} />
        </div>
        {children}
    </div>;
}

/** A button that opens an inline list; the panel scrolls, and a popup would be clipped by it. */
function Picker({ label, options, value, disabled, onPick }: {
    label: string; options: [string, string][]; value: string; disabled: boolean; onPick: (name: string) => void;
}) {
    const [open, setOpen] = useState(false);
    useEffect(() => { if (disabled) setOpen(false); }, [disabled]);
    const current = options.find(([name]) => name === value)?.[1] ?? value;
    return <>
        <Row label={label}>
            <Button className={styles.picker} disabled={disabled} selected={open} onSelect={() => setOpen(!open)}>
                <div className={styles.pickerLabel}>{current}</div>
                <img className={classes(styles.chevron, open && styles.chevronOpen)} src={chevronIcon} />
            </Button>
        </Row>
        {open && !disabled ? <div className={styles.list}>
            {options.map(([name, text]) => <Button key={name} className={styles.listItem} selected={name === value}
                onSelect={() => { setOpen(false); if (name !== value) onPick(name); }}>{text}</Button>)}
        </div> : null}
    </>;
}

function Toggle({ label, value, disabled, onChange }: { label: string; value: boolean; disabled: boolean; onChange: (value: boolean) => void }) {
    return <Row label={label}>
        <Button className={styles.toggle} label={label} selected={value} disabled={disabled}
            sound={value ? "toggle-off" : "toggle-on"} onSelect={() => onChange(!value)}>
            <div className={classes(styles.toggleTrack, value && styles.toggleOn)}>
                {value ? <img className={styles.toggleTick} src={checkIcon} /> : null}
            </div>
        </Button>
    </Row>;
}

function Stepper({ label, value, minimum, maximum, step, format, disabled, trigger }: {
    label: string; value: number; minimum: number; maximum: number; step: number; format: (value: number) => string;
    disabled: boolean; trigger: NumberTrigger;
}) {
    const change = (next: number) => {
        const snapped = Number(Math.max(minimum, Math.min(maximum, next)).toFixed(4));
        if (!disabled && snapped !== value) act(trigger, snapped);
    };
    return <Row label={label}>
        <div className={styles.stepper}>
            <Button className={styles.stepButton} label={`Less ${label.toLowerCase()}`} disabled={disabled || value <= minimum}
                onSelect={() => change(value - step)}>-</Button>
            <div className={styles.value}>{format(value)}</div>
            <Button className={styles.stepButton} label={`More ${label.toLowerCase()}`} disabled={disabled || value >= maximum}
                onSelect={() => change(value + step)}>+</Button>
        </div>
    </Row>;
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: "good" | "warn" | "bad" }) {
    return <SettingTip label={label}><div className={styles.stat}>
        <div className={styles.statLabel}>{label}</div>
        <div className={classes(styles.statValue, tone === "good" && styles.good, tone === "warn" && styles.warn, tone === "bad" && styles.bad)}>{value}</div>
    </div></SettingTip>;
}

function statusTone(state: ToolState) {
    if (state.stage === "Failed" || (state.stage === "Planned" && (!state.buildable || state.refused > 0))) return styles.toneBad;
    if (state.stage === "Building") return styles.toneBusy;
    if (state.stage === "Planned" || state.stage === "Built") return styles.toneGood;
    return styles.toneIdle;
}

function toneFor(share: number): "good" | "warn" | "bad" {
    return share >= 0.7 ? "good" : share >= 0.35 ? "warn" : "bad";
}

function Facts({ state }: { state: ToolState }) {
    const tone = toneFor(state.useShare);
    return <div className={styles.card}>
        <Stat label="Will cims use it?" value={`${state.useLabel} · ${walk(state.extraWalk)}`} tone={tone} />
        <Stat label="Deck height" value={`${state.deckHeight.toFixed(1)} m`} />
        <Stat label="Crossings removed" value={state.crossingsRemoved > 0 ? `${state.crossingsRemoved}` : "None in reach"} />
        <Stat label="Cost" value={state.cost >= 0 ? thousands(state.cost) : "Pricing..."} />
        {state.buildingsReplaced > 0 ? <Stat label="Buildings" tone="warn"
            value={state.buildingsReplaced === 1 ? "1 will be replaced" : `${state.buildingsReplaced} will be replaced`} /> : null}
        {state.competingMarked ? <div className={styles.warning}>{state.competingDistance > 0
            ? `A marked crosswalk ${state.competingDistance} m along the road still competes, so fewer cims will use the bridge.`
            : "A marked crosswalk nearby still competes, so fewer cims will use the bridge."}</div> : null}
    </div>;
}

function FootbridgePanel() {
    const state = useToolState();
    const [more, setMore] = useState(false);
    if (state.stage === "Closed") return null;
    const building = state.stage === "Building";
    const planned = state.stage === "Planned" && state.buildable;
    const settings = state.settings;
    // The tool also lists its summary among the messages; showing it twice reads as noise.
    const messages = state.messages.filter(message => message !== state.summary && message !== statusLine(state));
    const paths: [string, string][] = state.paths.map(path => [path.name, path.label]);

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
                        </div> : <div className={styles.hints}>{planned ? "Click to build · [ ] to turn · Esc to clear" : "Esc to close"}</div>}
                    </div>
                    {messages.map((message, index) => <div key={index} className={styles.message}>
                        <div className={styles.messageDot} />
                        <div className={styles.messageText}>{message}</div>
                    </div>)}

                    {planned ? <Section title="This bridge"><Facts state={state} /></Section> : null}

                    <Section title="Settings">
                        <Picker label="Design" options={DESIGN_LABELS} value={settings.design} disabled={building}
                            onPick={design => act("setDesign", design)} />
                        {paths.length > 1 ? <Picker label="Path" options={paths} value={settings.path} disabled={building}
                            onPick={path => act("setPath", path)} /> : null}
                        <Toggle label="Remove crossings" value={settings.removeCrossings} disabled={building}
                            onChange={value => act("setRemoveCrossings", value)} />
                        <Button className={styles.moreButton} selected={more} onSelect={() => setMore(!more)}>
                            <div>More settings</div>
                            <img className={classes(styles.chevron, more && styles.chevronOpen)} src={chevronIcon} />
                        </Button>
                        {more ? <>
                            <Stepper label="Crossing reach" value={settings.reach} minimum={20} maximum={150} step={10}
                                format={value => `${value} m`} disabled={building || !settings.removeCrossings} trigger="setReach" />
                            <Stepper label="Extra clearance" value={settings.extraClearance} minimum={0.5} maximum={3} step={0.25}
                                format={value => `${value.toFixed(2)} m`} disabled={building} trigger="setExtraClearance" />
                            <Stepper label="Turn" value={state.turn} minimum={-30} maximum={30} step={5}
                                format={value => `${value}°`} disabled={building} trigger="setTurn" />
                        </> : null}
                    </Section>
                </div>

                <div className={styles.footer}>
                    <Button className={styles.primary} disabled={!canBuild(state)} onSelect={() => act("build")}>
                        <div>{buildLabel(state)}</div>
                    </Button>
                </div>
            </div>
        </div>
    </div>;
}

export function PanelRoot() {
    return <ErrorBoundary name="panel"><FootbridgePanel /></ErrorBoundary>;
}
