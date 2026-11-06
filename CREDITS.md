# Credits

Footbridge Builder is made by **[TevBenji](https://github.com/TevBenji)** and released under the
[GNU General Public License v3.0](LICENSE).

## Inspiration

**kianzarrin's [Automatic Pedestrian Bridge Builder](https://steamcommunity.com/sharedfiles/filedetails/?id=2030755273)** for the
first Cities: Skylines: one tool that puts a bridge over the road you point at, with ramps running both ways along the
sidewalk. Its source has no licence, so it was read for ideas only and none of its code is used here.

## Projects this mod builds on

| Project | Author | Licence | What it contributed |
|---|---|---|---|
| [Placemaker](https://github.com/jackwrichards/placemaker) (commit `e654b32`) | jackwrichards | GPL-3.0 | Net course definitions and raised pieces (each node's own elevation, forced elevation), the road-cull crash guard, the world snapshot pattern, and the panel's UI plumbing and look |
| ParkManager | tomkpunkt | GPL-3.0 | The net course definition pattern, credited through Placemaker |
| Grid Road Generator | Ti4goc | GPL-3.0 | Course positions, the height-grid idea and UI registration patterns, credited through Placemaker |
| [CS2-NetworkTools](https://github.com/lucarager/CS2-NetworkTools) | Luca Rager | MIT | The lookup of the game's own UI components, credited through Placemaker |
| [Stepped Streets](https://github.com/TevBenji/Stepped-Streets) | TevBenji | GPL-3.0 | The tool, panel, settings and publishing plumbing this mod started from |

## Adapted files

All from [Placemaker](https://github.com/jackwrichards/placemaker) by jackwrichards, GPL-3.0, commit `e654b32`, most of
them by way of Stepped Streets.

| File | Adapted from | What changed |
|---|---|---|
| `src/FootbridgeBuilder/Build/NetDefinitions.cs` | The net course path of `Game/Build/BuildService.Emit.cs`, including `Elevate` | One course per bridge piece with each node's own elevation and the forced-elevation flags; SubElevation so the game prices and charges the pieces; a crossing-removal upgrade built the way the game's own upgrade tool builds it |
| `src/FootbridgeBuilder/Build/TerrainCullGuard.cs` | `Game/Build/TerrainCullGuard.cs` | Copied; namespace and log lines changed |
| `src/FootbridgeBuilder/Tool/SiteSnapshot.cs` | `Game/Preview/SiteCapture.cs` | The hovered road followed through its straight-on nodes, its street crossings, and the buildings, nets and water round it as outlines; a cached height grid |
| `src/FootbridgeBuilder/Panel/PanelSystem.cs` | The JSON binding and trigger pattern of `Game/UI/PlacemakerUISystem.cs` | One state value and clamped setting triggers |
| `src/FootbridgeBuilder/FootbridgeBuilder.csproj` | `Placemaker.csproj` | Standalone game references and the UI bundle step |
| `src/FootbridgeBuilder.UI/src/components/ToolbarButton.tsx` | `UI/src/components/ToolbarButton.tsx` | Own icon and toggle; sits beside other mods' buttons without moving them |
| `src/FootbridgeBuilder.UI/src/lib/*` (`vanilla.ts`, `bindings.ts`, `errorBoundary.tsx`, `tips.tsx`), `src/index.tsx`, `types/*.d.ts`, `webpack.config.js`, `tools/css-lint.js`, `tools/css-presence.js` | The matching Placemaker UI files | Trimmed to this mod's panel; credits Grid Road Generator and CS2-NetworkTools |

Every adapted file keeps its original header and adds a line naming its source and what changed:
`Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, <date>: <changes>.`

## Original work

Original work by TevBenji (2026), GPL-3.0:

- **The footbridge planner** (`src/FootbridgeBuilder.Core/`): the deck placed as low as the game allows and checked against how
  the game re-derives a raised piece's height, the ramp shapes (Y, Z, switchback, spiral, diagonal and snake), the ramp
  walker that solves each ramp's length from the ground, the feet turned to face the sidewalk, building avoidance, the
  game's net-shape rules, and the estimate of how many cims will use the bridge, from the game's own pathfinding numbers.
- **The tool** (`src/FootbridgeBuilder/Tool/FootbridgeToolSystem.cs`): hover, the live preview made of real temporary nets,
  the build, the crossing removal and the check of what the game built.
- **The path catalogue, settings and panel**, and the icon.

## Game data

Cities: Skylines II, its assets and its modding API belong to Colossal Order and Paradox Interactive. Footbridge Builder is an
independent mod and is not made or endorsed by them.
