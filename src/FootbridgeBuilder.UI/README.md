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

| Trigger | Argument | Does |
|---|---|---|
| `toggleTool` | none | Opens or closes the tool |
| `close` | none | Closes the tool |
