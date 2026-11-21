# Big Walk Mods

Custom puzzles for the game Big Walk (House House; Unity 6000.3.17f1, IL2CPP), written as code and built
headless. Claude authors the puzzles; the user directs and playtests with friends. Puzzles are built on the
community [BigPuzzleSDK](https://github.com/markviews/BigPuzzleSDK) (MIT), pinned at 0.1.7.

The long-term goal is a Christmas edition: see `docs\winter-edition-plan.md`.

## Commands

All through `tools\bwm.ps1` (Windows PowerShell):

| Command | Does |
|---|---|
| `build <Puzzle>` | Builds one puzzle, renders previews, runs the SDK's checks, exports the pack. About 25 s. |
| `build <Puzzle> -Resite` | Same, but searches for a new site instead of reusing the remembered one. |
| `build-all` / `list` | All puzzle definitions. |
| `play` | Starts the game with mods if needed and hosts a solo walk on the "BWM Test" save, capped at 30 fps (`BWM_FPS` overrides, 0 = no cap). Unattended, about 80 s. |
| `game <command>` | Asks the running game's companion: `status`, `reload`, `'teleport?puzzle=X'`, `'shot?file=x&cx=..&cy=..&cz=..&tx=..&ty=..&tz=..'`, `'puzzle?name=X&do=start'` (or `stop`, `win`), `'time?hour=12'`, `'look?x=..&y=..&z=..'` (how far the level view is open in 12 directions), `'walk?x=..&z=..&speed=1.5'` (moves the player over the ground in a line), `'get?path=Type.member.member'` / `'set?path=...&value=v'` (read or change any game object's fields live), `quit`; `game help` lists all. |
| `companion` | Rebuilds the companion and Winter Walk plugins (the game must be closed). |
| `launch` | Starts the game with mods without hosting. A normal Steam launch stays vanilla. |
| `log` | The puzzle-related lines of the last game run. |
| `survey` | Renders `world\heightmap.png` (whole island) and `world\heightmap-zoom.png` (the coast by the opening area). Both include the Winter Isle. |
| `dump-world` / `part-metrics` | Rewrite `world\world-index.tsv` (every world object) / `world\part-metrics.tsv` (each SDK part's size about its pivot). |

Build output is only the framework's `[BWM]` lines; the full Unity log is `logs\unity-<command>.log`. Exit
code 0 and a final `[BWM] OK` mean success. Other build flags: `-NoPreview`, `-NoExport`, `-AllowIssues`.

## Layout

- `unity\` — Unity project. `Assets\Editor\Bwm\` is the framework, `Assets\Editor\Puzzles\` holds one
  `PuzzleDef` class per puzzle, `Assets\Puzzles\<Name>\` holds what a build generates for it (scene,
  `site.json`, materials), `Assets\BigPuzzleSDK\` is the imported SDK (do not edit).
- `companion\` — the test companion, a BepInEx plugin serving http://127.0.0.1:17361 inside the game.
  Dev profile only; it must never ship with a puzzle.
- `winterwalk\` — Winter Walk, the edition's own BepInEx plugin, which every player needs. It gives the island
  its sky and light: near the island's box (kept in step with `WinterIslePuzzle.cs` by hand) it blends the
  game's sky system (Enviro) towards winter for that player, and back on the mainland. It also dresses the island
  with the game's rocks and plants (`Dresser.cs`) and keeps the tracks players leave in the snow (`SnowTrails.cs`).
- `profile\` — the mod profile: BepInEx 6.0.788 (IL2CPP), the SDK mod, the companion and dev helpers
  (UnityExplorer hidden at startup, NoClip, SkipIntro). Builds export to `profile\BepInEx\plugins\BigPuzzleSDK\`.
- `previews\<Name>-*.png` — editor renders of the last build. `shots\*.png` — screenshots from the real game.
- `world\` — the world index and part metrics.
- `vendor\` — the SDK's `.unitypackage`, and `BigPuzzleSDK-src\` (its source: `Mod\` is the in-game code,
  `Unity\BigPuzzleSDK\Internal\` the editor stubs and checks, `docs\components.txt` the component reference).
- `docs\` — plans.

## Writing a puzzle

Add a class in `unity\Assets\Editor\Puzzles\`. `GlassBoothPuzzle.cs` is the model for a real puzzle (pad,
walls, soundproofing, wall-mounted parts, wiring); `TutorialPuzzle.cs` is the minimal smoke test.

- Coordinates are local metres from the puzzle's centre, `+z` forward. `height` is measured from the terrain
  under that point; pass `onGround: false` to measure from the root's level instead. For anything with walls,
  build one level pad (`b.Box`, sunk about 1.5 m) and place everything `onGround: false` on it.
- Call `b.ClearGround(sizeX, sizeZ)` for the whole working area. The island is covered in trees, shrubs and
  loose boulders that neither the SDK map nor the previews show.
- Parts face their local `+z`, and most are wall-mounted with the pivot at their centre: give them a height
  (boards about 1.3 m, displays about 1.6 to 2.3 m) and a wall behind them. `yaw` turns the facing: 90 faces
  `+x`, -90 faces `-x`, 180 faces `-z`. Sizes are in `world\part-metrics.tsv`.
- `Part(name, ...)` takes an SDK prefab's file name. `Set` / `Add` / `On` take the SDK component's type name
  and its serialized field name; enums are set by index. Field names and enum orders are in
  `vendor\BigPuzzleSDK-src\Unity\BigPuzzleSDK\Internal\Puzzle*.cs`.
- `b.MeshPiece(name, mesh, colour)` adds geometry built in code (land, ramps), with a collider. Override
  `PuzzleType => 2` for packs that are scenery or land, not a puzzle. Sea level is height 0; with `FixedSite`
  offshore the root sits on the sea floor, so subtract the root's position from world coordinates.
- **The Winter Isle** (`WinterIslePuzzle.cs`) is the island the winter edition is built on. Its build writes
  `unity\Land\WinterIsle.land`, a height grid the framework reads as ground, so puzzles can be sited on it and
  snap to it. Rebuild every island puzzle after changing the island's shape. To put a puzzle on the island,
  give it a `FixedSite` on the lowland, 70 m or more from the others (the island now marks its woods, rocks and
  paths as taken, so the site search finds nothing there, or a spot on the sea floor); the island's build levels
  and clears it, so build the puzzle, then `WinterIsle`, then the puzzle again. The editor previews do not show
  the island (puzzles there look as if they float over the sea); use game screenshots.
- **Island puzzles share `IsleKit.cs`:** the colours, `StartCorner` (start switch, live lamp, gourd vice, pillow,
  confetti), `BoardOnPosts`, `Lobby` (a Hider group), `Sensor` (a sized presence sensor), `BlindfoldHook` and
  `BlindGate`. `Sdk.SetFlags` sets a flags enum (a hook's `acceptedVanilla`), `Sdk.AddInts` an int list (a peg
  board's `answerFromFlap`, needed when its answers are Sound Peg Answers). Part sizes that caught me out: a Peg
  Board's pivot is at one end (it runs 2 m to its left), the Door is 1.24 m wide, a turnstile 2.12 m.
- The island's terrain is low shelves in the east and middle, a 73 m mountain in the west with two lower
  peaks, a ravine and a frozen lake (`Peaks`, `Lake` and the ravine constants in `WinterIslePuzzle.cs`). Its
  build levels every island puzzle's site, so a puzzle can be sited on a slope, and prints how much level
  ground is left.
- **The island's ground** is a 3 m height grid (`height[]`, which paths, sites and the dressing work on) drawn as a
  mesh twice as fine: `HeightAt` is a smooth curve through the grid with crags on steep ground, and the mesh and
  its collider follow it exactly. Use `HeightAt` for anything placed on the ground. On top of it the build lays
  scatter as merged meshes by district (drifts, tussocks, sticks, stones: section 7b), ice floes in the bays, and
  the dressing's game rocks and plants. Players' eyes are only 60 cm above the ground, so what lies on the snow
  matters more than the view from above suggests. The coast's line is `CoastFull` (points and inlets), sand lies
  only in bays, and dense woods are planted across the trails (`Land.Woods`).
- **How good is it?** `tools\review\README.md`: matched screenshots of both islands judged by a fresh agent. The
  user's goal is every one of its six scores "adequately close" to the base island's.
- **Sky:** the game copies some sky settings from its quality levels every frame (the flat cloud switch is
  one), so a value that will not stay set has to be changed in `Quality.Settings` instead. A screenshot's
  temporary camera does not move the sky: Winter Walk goes by the player's own camera, so `teleport` first.
- The island grows pine groves and boulders and treads paths from the causeway to the plaza and from the
  plaza to every puzzle on it, all clear of the puzzle sites, and marks that ground as taken so the site
  search keeps new puzzles off it. So after adding or moving a puzzle, rebuild `WinterIsle` too: that is what
  clears its trees and lays its path. `TheTreePuzzle.cs` is the gourd tower in the plaza
  (`WinterIslePuzzle.Plaza`), one plinth per puzzle.
- **The mine** (`WinterIsleMine.cs`, built by the island's pack): one low single-track line (2.9 m wide, 1.75 m
  high: a seated rider clears it, on foot it is a crouch) that runs under the whole island and surfaces at about
  thirteen stations, plus one big hall under the summit. The stations are placed and the line routed by the
  build (`PlaceStations`, `FindRoute`); where the ground is too thin the tunnel runs in a stone shed and the
  island's mesh leaves the ground out (`InCut`). The build writes the line and the lamps' places beside the
  Winter Walk plugin as `WinterIsle.mineline`, and the mine tower's place to `unity/Land/WinterIsle.mine`, which
  The Tree reads, so build WinterIsle before TheTree. In the game `winterwalk/MineDriver.cs` runs the twelve
  mantrips from the network clock, opens the gates, stops the snowfall underground, and gives the mine the game's
  own tunnel material (`Concrete_Mottle_Vertex`, tinted by our vertex colours) and prop material, with a small
  pool of real lights that follows the camera from lamp to lamp. So the mine's faces must wind the right way
  (`Q` takes the point a face is seen from) and carry texture coordinates in metres; `BWM/Mine` is only the
  fallback. `game 'set?path=WinterWalk.MineDriver.ForceOpen&value=true'` opens it for testing, as
  `GondolaDriver.ForceOpen` does the gondola; `game 'mat?x&y&z&r&name=Text'` lists what the game draws near a
  point (materials, textures, lights), which is how the game's tunnels were matched.
- **Skiing** (`winterwalk\SkiDriver.cs`): walking up to a rack (any object named "Ski Rack..." or "Board Rack..."
  beside the first) puts gear on; on anything whose collider is named "Piste Sheet" the player glides, steering by
  where they look; past a kicker's lip they fly (gravity, until the sheet comes back up). The island's sheet, its
  terrain park's kickers and Big Air's kicker all carry that name (give the mesh asset another name: `MeshPiece`
  saves meshes by piece name, and a second "Piste Sheet" asset replaces the first). The ski lift (lodge to the bench
  at the tree line) is a second `GondolaDriver`, set up by `ConfigureLift`, and opens with the gondola; its state is
  `GondolaDriver.LiftState`. Other players' gear is not shown.
- **Jetpacks** (`winterwalk\JetpackDriver.cs`): walking up to one of the opened presents under The Tree ("Jetpack
  Present...") straps a jetpack on and fills its 40 s tank; jump, let go, press jump again in the air and it fires
  while held. Jump is read from Rewired (the game's input) by the action named with "jump", else the space bar.
  `game 'set?path=WinterWalk.JetpackDriver.TestFire&value=true'` fires it without a controller; `Up`, `Back` and
  `Scale` move the worn model live. Other players' jetpacks are not shown.
- **Island Gear** (`IslandGearPuzzle.cs`, a scenery pack): five stations of the game's own tools (binoculars,
  walkie-talkies, megaphone, torches, GPS, map, compass, laser, night vision, a backpack and belt on hooks), at
  the landing, the plaza, the summit, the lodge and the mining yard. Items are not saved: a reload resets them.
- **Towers:** the SDK supports one tower button list, and shows one plinth per puzzle loaded, first in the list
  first. The Tree's list is its own five plinths (the gondola), then the mine tower's five (the mine), then the
  summit's twenty; the mine tower and the summit have their own hold buttons wired to `HoldToOpen`. So the mine
  opens only once ten puzzles exist. The SDK's plinths take only modded gourds, which is how "island gourds
  only" holds. An idle player falls asleep (red, then dark screen): keep that in mind when a test sits still.
- `Kit` (`Assets\Editor\Bwm\Kit.cs`) holds ready-made pieces: `Kit.LightString` / `Kit.LightRing` (Christmas
  lights between posts; every winter site should have some), `Kit.Ball` (snowballs), `Kit.Mound` (drifts), and
  flat-shaded building into a `MeshBuilder`: `Kit.AddBox` (posts, walls, roofs, plinths), `Kit.AddDisc`,
  `Kit.AddPine`, `Kit.AddRock`, `Kit.AddStar`. Build a puzzle's structures into one `MeshBuilder`, add it with
  `b.MeshPiece(name, mb.ToMesh(), b.ShaderMaterial("BWM/Snowy", "Snowy"))` and move it with
  `piece.Transform.position = b.WorldPoint(x, z)`. The Snowy shader takes the mesh's vertex colours and puts
  snow on whatever faces up; a material with `_SnowLine` 2 never takes snow. `LightTheTreePuzzle.cs` and
  `BellRingersPuzzle.cs` are the models for a winter puzzle; prefer this to `b.Box`, which uses the stock
  shader and takes no snow.
- **Snow surface maps** are generated, not painted: `tools\snowtex.py` (Python with numpy, scipy and Pillow) writes
  tileable height fields and the normal, cavity and mask maps made from them into `unity\Assets\Textures\Snow\`
  (PNGs not committed; `bwm.ps1 build` regenerates them when the script is newer). `SnowWind` is the wind-carved
  surface (2048 px over 12 m), `SnowGrain` the crust (1024 px over 1.6 m), `SnowTrack` trodden snow with boot
  prints (1024 px over 3 m). `Kit.SetSnowMaps(material)` gives them to a material; `BWM/Snow` (ground) and
  `BWM/SnowOverlay` (caps on the game's rocks) use them. Run it with `--preview <dir>` for shaded renders of each
  surface to look at before a build. Change the snow's look there, not with noise in the shader: shader value
  noise gave the blur and the evenly spaced bands the reviews complained of.
- **One snow everywhere:** `unity\Assets\Shaders\BwmSnowCommon.hlsl` holds the snow's shading (colour, blue shade,
  wrapped light, sheen, glints). The ground, `BWM/SnowOverlay`, `BWM/Fir` and `BWM/Snowy` all call it, so change the
  snow's look there and every surface follows. Fir and Snowy read the surface maps as globals (`_BwmWindNormal`,
  `_BwmWindHeight`, `_BwmGrainNormal`, `_BwmSnowMaps`) that Winter Walk sets from the island's materials, so snow
  on trees and props is smooth in the editor and on any pack loaded without the island.
- **Tracks in the snow** (`winterwalk\SnowTrails.cs`): the snow's surface is a height field in millimetres, kept only
  where disturbed. Each player's PC stamps it from where it sees every `PlayerCharacter` (a walk treads a trench
  with banks pushed up beside it, anything faster than 4.2 m/s a smooth groove), falling snow fills it by 1 mm
  every 7 s, and a 64 m window round the camera goes to the ground shader as `_BwmTrailTex`, which marches the
  eye's ray into the trench. Nothing is sent between players. The player's origin rides 0.75 m above the ground.
  Test without a controller: `game 'set?path=WinterWalk.SnowTrails.TestPattern&value=true'` draws tracks in front
  of the camera, `game 'walk?x=..&z=..'` makes the player leave a real one. Config `SnowTracks` turns it off.
- **Loose snow** is items: `b.Item(name, mesh, material, x, z, height, mass)` makes something players can pick
  up, carry and throw. A Presence Sensor with `type` 2 (Specific Item), the items in `acceptedCustom` and
  `requiredPlayers` set to how many are needed counts the ones inside it; wire its `onPressed` to
  `PuzzleSDK.Win` or to `GameObject.SetActive` (`piece.On(..., target.Go, "SetActive", true)`). `SnowmanPuzzle.cs`
  is the example. In the game each item's real prop is a root object named `VanillaItem`; the object with the
  item's own name is only a marker.
- Only Glass, Timer and Progress Bar can be scaled. Lamps start off. Ordinary walls do not block voices: put
  them under a parent with `PuzzleSoundProof`.
- A `Puzzle`-type puzzle needs one Gourd Vice (or Box), one Pillow and a Start Switch. The SDK's checks run
  on every build and fail it; they also reject wiring where start and stop do the same thing to a part.
- Custom runtime code goes in `unity\Assets\PuzzleScripts\` (assembly `WinterWalkScripts`); add a class to a
  piece with `b.AddCustomScript(piece, "ClassName")` and set its `[SerializeField]` fields with `Sdk.Set`. The
  SDK exports the folder as `WinterWalkScripts.dll` beside the packs, and the game loads it as a plugin.
  `SnowfallFollow.cs` is the working example. A changed script needs a game restart (`game quit`, then
  `play`), not a reload. Only the host should change shared state (the SDK's `Synced*` variables); that part
  is not exercised yet.
- Our own shaders live in `unity\Assets\Shaders\` and are used through `b.ShaderMaterial("BWM/Name", "File")`,
  which resets the material to the shader's defaults on every build. `BwmSnow.shader` (ground) and
  `BwmSnowflake.shader` (particles) are the examples. The lit pass must be tagged
  `"LightMode" = "UniversalForwardOnly"`: the game renders deferred, and a pass tagged `UniversalForward`
  draws nothing there. A build fails on shader compile errors.
- The first build finds the nearest flat, clear ground to the spawn rooms that is not near another puzzle and
  remembers it in `Assets\Puzzles\<Name>\site.json`; edit that file, or override `FixedSite` / `Near` / `Yaw`.

## Testing

1. `bwm.ps1 build <Puzzle>` and Read `previews\<Puzzle>-*.png`.
2. `bwm.ps1 play` (once), then after each rebuild `bwm.ps1 game reload`.
3. `bwm.ps1 game 'shot?file=...'` with a camera and target position, and Read the image in `shots\`. World
   position = site + local offset when yaw is 0. `time?hour=13` first for daylight.
4. `bwm.ps1 game 'puzzle?name=<Puzzle>&do=start'` then `do=win` exercises the start, deal and win paths.
5. Items: `game 'move?name=VanillaItem&nx=..&ny=..&nz=..&x=..&y=..&z=..'` moves the item nearest (nx, ny, nz).
   Put it a few metres above a zone and let it fall in: an item placed directly inside a zone is not counted,
   because the zone only notices things entering.

What this cannot do: pick things up or hang them on snap points, press buttons as a player, or anything
with two players. Those go to
the user and friends, who must all have the same packs and SDK version.

## Things that are easy to get wrong

- **This PC's CPU (2026-11-20):** one core of the i9-13900K (logical CPUs 4 and 5) logs machine-check errors, and
  the PC has frozen twice under bursty load (game + teleport-and-screenshot loops + Unity builds). `bwm.local.ps1`
  (not committed) sets `BWM_AFFINITY` so `bwm.ps1` keeps Unity and the game off that core. Never run a Unity build
  while the game is up (`game quit` first), keep the frame cap, and leave a few seconds between teleports.
- **Unity licence:** batch mode only finds the licence while Unity Hub is running and signed in. `bwm.ps1`
  starts the Hub if needed. Only editor 6000.3.17f1 (at `Z:\Unity\6000.3.17f1`) is wanted; never let the Hub
  install another version (it defaults to C:, which is nearly full).
- **Steam:** the game launches through Steam. If Steam shows "Refresh Sign In", nothing starts until the
  user signs in.
- **Shader variants:** never turn URP's variant stripping off to "be safe". The stock Lit shader alone becomes
  221,000 variants and the build runs for hours. `Cli.KeepShaderVariants` enables the lighting features in our
  pipeline asset instead, so stripping keeps the variants the game may ask for.
- **Do not `Start-Process -Wait` Unity:** it also waits for Unity's compiler server, which lingers for minutes.
- **Stripped engine methods:** the game is IL2CPP, so engine methods it never calls are missing. In the
  companion prefer the generic forms (`FindObjectOfType<T>()`), and test each new call live.
- **Puzzle ids:** the SDK ties a puzzle's permanent id to its root object in `Assets\Puzzles\<Name>\<Name>.unity`
  and records it in `unity\ProjectSettings\BigWalkPuzzleIds.json`. Builds reuse that root. Deleting the scene
  or that file gives the puzzle a new id, which breaks saves and multiplayer matching for it.
- **Saves:** the companion only ever hosts on the save slot named "BWM Test". Never select another slot.
- **SDK internals:** the framework and companion call the SDK's exporter, checks, map importer and reload by
  reflection. An SDK update can rename them; read the new source and adjust `Sdk.cs`, `Cli.cs`, `Plugin.cs`.
- **Game folder:** only `winhttp.dll`, `.doorstop_version` and `doorstop_config.ini` (enabled = false,
  absolute paths into `profile\`) were added to `G:\SteamLibrary\steamapps\common\Big Walk`.
- **Disk:** C: is nearly full. Unity, its caches and this repo are on Z:; keep it that way.

## Git

The repo is private on GitHub (`origin`). The user has given standing permission to commit and push whenever
the work forms a meaningful commit (a feature working in game, a spike answered, notes updated); do not ask
each time, and do not commit broken or half-built states.

## Known open problems

- Flat-colour materials on the Glass Booth's pad and walls show thin dark grid lines in the game. The same
  material on the test island and causeway does not. Cause unknown.
- `ClearGround` leaves dark flat patches where some boulders were.
- Packaging a puzzle for friends (Thunderstore zip) is not built.
- Nothing has been tested with two players.
