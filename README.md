# Footbridge Builder

<img align="right" src="src/FootbridgeBuilder/Properties/Thumbnail.png" alt="Footbridge Builder" width="240">

<img alt="Version" src="https://img.shields.io/badge/version-0.1.0-1f6feb">
<img alt="Game version" src="https://img.shields.io/badge/Cities%3A%20Skylines%20II-1.6-0e7490">
<a href="LICENSE"><img alt="Licence" src="https://img.shields.io/badge/licence-GPL--3.0-c2410c"></a>
<img alt="No other mods needed" src="https://img.shields.io/badge/needs-no%20other%20mods-2ea44f">

A Cities: Skylines II mod that puts a working pedestrian bridge over a road in one click.

Building a footbridge by hand takes three mods and a lot of fiddling, and then the cims cross at street level anyway.
**Footbridge Builder does it for you.** Hover a road and a real preview of the bridge appears, at the lowest height the game
allows, with ramps that fit beside the road and feet that join the sidewalk. Click, and it's built.

<a href="#features">Features</a> ·
<a href="#how-to-use">How to use</a> ·
<a href="#will-cims-use-it">Will cims use it?</a> ·
<a href="#how-it-works">How it works</a> ·
<a href="#building-from-source">Building from source</a> ·
<a href="#credits">Credits</a>

<br clear="right">

<p align="center">
<img src="src/FootbridgeBuilder/Properties/Screenshots/01.png" alt="Y-ramps over a six-lane road; the game's arch bridge carries the long deck" width="720">
</p>

<table>
<tr>
<td><img src="src/FootbridgeBuilder/Properties/Screenshots/02.png" alt="The preview, with the game's own price and the cim estimate"></td>
<td><img src="src/FootbridgeBuilder/Properties/Screenshots/03.png" alt="Beside a junction: the crosswalk across the road is gone"></td>
</tr>
<tr>
<td><img src="src/FootbridgeBuilder/Properties/Screenshots/04.png" alt="A spiral ramp where room is short"></td>
<td><img src="src/FootbridgeBuilder/Properties/Screenshots/05.png" alt="Over a highway"></td>
</tr>
</table>

## Features

- **One click, no other mods.** No Anarchy, no Move It. The bridge is the game's own footpath, raised on the game's own
  pillars, and you can bulldoze it like anything else.
- **A real preview.** The ghost is made of the game's temporary nets, so you see the pillars, the price and anything the game
  would refuse before you build.
- **As low as the game allows.** The deck clears the road by the game's own minimum (6 m over a normal road) plus half a
  metre, and stays level over the whole road.
- **Ramps that fit.** Y-ramps along the sidewalk, Z-ramps, switchbacks, spirals, a diagonal span or a snake deck. Auto tries
  them all, both ways, and keeps the one most cims will use. Every ramp is as steep as the path allows (cims don't mind
  climbing, only distance), and every piece passes the game's shape rules before the game sees it.
- **Feet that join the sidewalk.** Each ramp ends just short of the kerb, facing it, so the game joins it to the sidewalk
  without cutting the road or painting a new crossing.
- **Street crossings removed.** Marked crosswalks near the bridge lose their crosswalk, so crossing at street level costs
  cims more than the bridge.
- **Buildings left standing.** If a ramp would hit a building, the bridge tries other shapes, then moves along the road.
  Only when nothing fits does it work like the game's road tool, and the button says so: "Build and replace 2 buildings".
- **Wide roads.** A footpath spans about 36 m. Over a wider road the game's own arch or cable-stayed pedestrian bridge
  carries the deck, or Auto picks a spiral, whose deck is shorter.
- **Honest numbers.** The panel shows the game's own price, the deck height, and how many cims will use the bridge.

## Installation

Subscribe on Paradox Mods (link coming with the first release), or build it from source (below). It needs no other mods.

## How to use

1. **Open the tool**: the bridge button beside Zones in the toolbar, or <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd>.
2. **Hover a road.** The preview follows the cursor. Near a junction it snaps to the street crossing, where cims already
   cross.
3. **Turn it** with <kbd>[</kbd> and <kbd>]</kbd> (up to 30 degrees) if you like.
4. **Click** (or the green button) to build. The panel reports what was built: pieces, sidewalk joins, crossings removed
   and the cost.

<kbd>Esc</kbd> clears the preview; press it again to close the tool.

| Setting | What it does |
|---|---|
| Design | Auto, or a shape of your choice |
| Path | The footpath the bridge is built from |
| Remove crossings | Takes the crosswalk off street crossings near the bridge (on by default) |
| More settings | How far crossings are removed (20–150 m), extra height (0–3 m), and the turn |

## Will cims use it?

The panel says **Most**, **About half** or **Few**, worked out from the game's own pathfinding numbers. Cims pay for time,
not for climbing, so they take the bridge when its extra walk costs them less than crossing at street level. A crossing
whose crosswalk was removed still exists, but it costs a cim about as much as 17 to 270 m of walking, depending on how
much of a hurry it's in. So the mod keeps the extra walk short (low deck, steep ramps, feet on both sides) and removes the
crosswalks in reach. If a marked crosswalk still competes nearby, the panel says so.

## How it works

The planner runs off the game's main thread and never touches the world; it gets a copy of the road, the ground, and the
buildings, roads and water nearby. It places the deck square to the road (or turned), as low as the game's clearance rule
allows, checked against how the game will actually draw a raised path between two pillars. Each side then gets its ramps:
a ramp walks down a line beside the road at the steepest grade the path allows and stops where it meets the ground, so on
a slope an uphill ramp comes out shorter than a downhill one. A short bend on the ground turns each foot to face the
sidewalk. Every shape is checked against what stands nearby, and each design is scored by how many cims will use it.

The tool then hands the plan to the game as temporary nets: the same pieces the game's own net tool makes, each with its
own height. The game draws the preview, prices it, builds it on your click, and the mod checks what was built.

## Known limitations

- Version 1 bridges one road at a time. Junctions, roundabouts, tunnels, rail and water are planned for later.
- Squeezed between bridges already there, a plan can still overlap them in ways only the game sees. The preview turns red,
  the panel names the game's reason, and nothing is built.
- The game has no walkable stairs or lifts, so every design uses ramps.
- A crosswalk removed is still an unsafe crossing in the game, so some cims will run across. No mod setting changes
  city-wide pathfinding; if you want cims to never jaywalk, a mod like Stop Jaywalking does that.

## Building from source

Needs Windows, the game's Managed DLLs, the .NET SDK and Node 18 or newer.

```powershell
cd src/FootbridgeBuilder.UI; npm ci; cd ../..
dotnet build FootbridgeBuilder.sln -c Release
```

The game's DLL path is set in `build/GameReferences.props`; override it with `-p:ManagedPath="<...>\Cities2_Data\Managed"`.
Building never installs the mod.

## Credits

Inspired by kianzarrin's **Automatic Pedestrian Bridge Builder** for the first Cities: Skylines. Parts of the build code are
adapted from **Placemaker** by jackwrichards (GPL-3.0). Full credits are in [CREDITS.md](CREDITS.md).

## Licence

[GPL-3.0](LICENSE).
