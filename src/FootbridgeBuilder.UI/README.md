# Footbridge Builder UI

This is the game's React panel and bottom toolbar button. The game supplies React and the `cs2/*` APIs; the bundle doesn't ship a second React runtime. Toolchain versions are pinned in `package.json` and `package-lock.json`.

From this directory:

```powershell
npm ci
npm run build
npm run typecheck
npm test
npm run lint:css
```

The output is `build/FootbridgeBuilder.mjs`, `build/FootbridgeBuilder.css` and hashed icons under `build/images/footbridgebuilder/`. CSS lint runs during webpack and rejects what the game's UI engine doesn't support. Dimensions use the game's `rem` units.

After `npm ci`, `dotnet build FootbridgeBuilder.sln -c Release` also runs webpack with `FOOTBRIDGE_BUILDER_UI_OUT` set to the C# project's output folder. Neither build installs anything. Without `node_modules`, or with `-p:SkipUI=true`, the C# build skips the bundle with a warning.

After a Release build, `pwsh -File tools/check-panel-json.ps1` checks the compiled C# JSON writers without starting the game.

## Binding contract

Group: `FootbridgeBuilder`. Value: `state`, a `ValueBinding<string>` carrying JSON, written by `src/FootbridgeBuilder/Panel/PanelSystem.cs` and read by `src/lib/state.ts`. `readState` never throws and clamps every field, so a missing binding or broken JSON reads as a closed tool.

| Field | Type | Meaning |
|---|---|---|
| `stage` | string | `Closed`, `Hovering`, `Planned`, `Building`, `Built` or `Failed` |
| `summary` | string | The tool's result in plain words |
| `messages` | string[] | Things the player should know |
| `progress` | number | 0 to 1 while building |
| `buildable` | boolean | A bridge fits here |
| `designName` | string | The shape on show, such as `Y-ramps` |
| `deckHeight` | number | Metres above the road |
| `useLabel`, `useShare`, `extraWalk` | string, number 0-1, number | The cim estimate: Most, About half or Few; the share; the extra walk in metres (negative is shorter) |
| `cost` | number | The game's own price for the ghost; -1 until it has priced it |
| `crossingsRemoved` | number | Marked crosswalks that lose their crosswalk on build |
| `competingMarked` | boolean | A marked crosswalk in reach still competes |
| `buildingsReplaced` | number | Zoned buildings the game will replace (its own warnings on the ghost) |
| `refused` | number | Temporaries the game marks with an error |
| `turn` | number | Degrees, -30 to 30 |
| `pinned` | boolean | The bridge is held in place for the panel; only then can it be built |
| `discord` | boolean | A Discord link is set, so its buttons show |
| `paths` | `{name, label}[]` | Footpaths that can be raised |
| `settings` | object | `design`, `path`, `removeCrossings`, `reach` (20-150), `extraClearance` (0.5-3), `panelX` and `panelY` (the panel's top-left corner as a fraction of the screen, below 0 for its usual place), `panelCollapsed` |

| Trigger | Argument | Does |
|---|---|---|
| `toggleTool` | none | Opens or closes the tool |
| `close` | none | Closes the tool |
| `build` | none | Builds the pinned bridge; does nothing without a pin |
| `setDesign` | string | `Auto`, `YRamps`, `ZRamps`, `Switchback`, `SpiralRamp`, `DiagonalSpan` or `SnakeDeck` |
| `setPath` | string | A path's prefab name from `paths` |
| `setRemoveCrossings` | boolean | Remove crossings in reach |
| `setReach` | number | Clamped to 20-150 m, saved |
| `setExtraClearance` | number | Clamped to 0.5-3 m in 0.25 steps, saved |
| `setTurn` | number | Clamped to -30..30 in 5 degree steps, not saved |
| `setPanelPlace` | number, number | The panel's corner as fractions of the screen, clamped to 0-1, saved; below 0 puts it back in its usual place. Never re-plans |
| `setPanelCollapsed` | boolean | Folds the panel to its header, saved. Never re-plans |
| `openLink` | string | `bug` or `discord`: C# maps the key to a fixed address and opens it. An unknown key does nothing |

Numeric triggers clamp in C#, save with `ApplyAndSave()` and then tell the tool, which plans again.
