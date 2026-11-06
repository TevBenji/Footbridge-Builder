# Footbridge Builder

A Cities: Skylines II code mod by [TevBenji](https://github.com/TevBenji). Hover a road and click: a working pedestrian bridge appears, at the right height, joined to the sidewalks, with no other mods needed.

**In development.** So far the tool opens and closes with its panel; it doesn't build bridges yet.

Press **Ctrl+Shift+F** or the toolbar button beside Zones to open it.

## Building from source

Needs Windows, the game's Managed DLLs, the .NET SDK and Node 18 or newer.

```powershell
cd src/FootbridgeBuilder.UI; npm ci; cd ../..
dotnet build FootbridgeBuilder.sln -c Release
```

The game's DLL path is set in `build/GameReferences.props`; override it with `-p:ManagedPath="<...>\Cities2_Data\Managed"`. Building never installs the mod.

## Licence

[GPL-3.0](LICENSE). Credits are in [CREDITS.md](CREDITS.md).
