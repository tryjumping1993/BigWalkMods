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
| `game <command>` | Asks the running game's companion: `status`, `reload`, `'teleport?puzzle=X'`, `'shot?file=x&cx=..&cy=..&cz=..&tx=..&ty=..&tz=..'`, `'puzzle?name=X&do=start'` (or `stop`, `win`), `'time?hour=12'`, `'look?x=..&y=..&z=..'` (how far the level view is open in 12 directions), `'walk?x=..&z=..&speed=1.5'` (moves the player over the ground in a line), `'get?path=Type.member.member'` / `'set?path=...&value=v'` (read or change any game object's fields live), `'press?action=jump'` / `'press?action=crouch,moveY&ms=1500'` (pretends the game's input, Rewired, has those actions held: the test player jumps, crouches and walks as a player would), `player` (the local player's ground, grip, pose, jump and crouch state), `quit`; `game help` lists all. |
| `companion` | Rebuilds the companion and Winter Walk plugins (the game must be closed). |
| `launch` | Starts the game with mods without hosting. A normal Steam launch stays vanilla. |
| `log` | The puzzle-related lines of the last game run. |
| `logs [n] [-Full]` | Winter Walk's debug log of the latest session (n = 2 the one before): the header, what became of the session before, warnings, snapshots, marks, slow frames, the last heartbeat. |
| `package [<version>]` | The players' download: `Z:\Temp\claude\BigWalkMods\dist\BigChristmasWalk-<date>-<commit>.zip` (about 0.9 GB) with `Install.cmd` / `Uninstall.cmd` and the players' `README.txt` (sources in `tools\install\`). Build the packs and plugins first. |
| `survey` | Renders `world\heightmap.png` (whole island) and `world\heightmap-zoom.png` (the coast by the opening area). Both include the Winter Isle. |
| (trailer) | `tools\trailer\plan.py` plans a fly-over (`frames.csv`), `capture.py --hide GameOverlay` shoots it from the running game into `shots\trailer\` (resumable), `encode.ps1` makes the MP4s in `Z:\Temp\claude\BigWalkMods\trailer\` (`-Music <wav> -MusicStart 2.4` lays the game's menu theme under it; `menu_theme.py` extracts that from the game). `GameOverlay` is the HUD (the arms reticle) and the sleep fade. |
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
  Present...") straps a jetpack on (it never runs out of fuel); jump, let go, press jump again in the air and it fires
  while held. Jump is read from Rewired (the game's input) by the action named with "jump", else the space bar.
  `game 'set?path=WinterWalk.JetpackDriver.TestFire&value=true'` fires it without a controller; `Up`, `Back` and
  `Scale` move the worn model live. Other players' jetpacks are not shown.
- **Island Gear** (`IslandGearPuzzle.cs`, a scenery pack): five stations of the game's own tools (binoculars,
  walkie-talkies, megaphone, torches, GPS, map, compass, laser, night vision, a backpack and belt on hooks), at
  the landing, the plaza, the summit, the lodge and the mining yard. Items are not saved: a reload resets them.
- **Weather and the fog veil:** `winterwalk\WeatherDriver.cs` works out mist and cloud from the hour and blizzards
  from the network clock (Poisson, mean `BlizzardMeanMinutes` of real play, default 90; a game day is only 15 real
  minutes); `SkyDriver` blends them into the sky, and a blizzard switches on the pack's "Blizzard Snowfall".
  `game 'set?path=WinterWalk.WeatherDriver.ForceBlizzard&value=1'` holds one (-1 lets go). The island's pack rings
  itself with `BWM/Veil` ribbons (clear near the camera) and hangs `BWM/VeilGlow` blooms over its lit places. The
  game's fog does not draw for a screenshot's own camera: use `shot?file=x` with no camera for the player's view,
  and `walk` first, or the idle player is asleep and the view blurred.
- **Towers:** the SDK supports one tower button list, and shows one plinth per puzzle loaded, first in the list
  first. The Tree's list is its own five plinths (the gondola), then the mine tower's five (the mine), the airport
  tower's five (the biplanes), the train tower's five (the railway), then the summit's twenty; every tower but the
  tree's has its own hold button wired to `HoldToOpen`. So the mine opens once ten puzzles exist, the airport at
  fifteen, the railway at twenty. A save stores how many gourds, and fills the list from the first. The SDK's
  plinths take only modded gourds, which is how "island gourds only" holds. An idle player falls asleep (red, then
  dark screen): keep that in mind when a test sits still.
- **The railway** (`WinterIsleRail.cs`, built by the island's pack) is a network in `unity\Land\WinterIsle.railnet`
  (`LINE name closed|open`, `P x y z` every 4 m in running order, `S index name`): a single-track **loop** round the
  island, run clockwise, with nine stations by the puzzles (Plaza, East Lowland, Airport, South Shore, Seal Point, West
  Cliffs, Cirque Gate, North Shore, Northwest), and the double-track **summit spur** from Cirque Gate up to the summit
  (High Shelf, Summit). `tools\railnet\graph\route.py` designed the loop (`tools\railnet\spline\route.py` is an
  alternative design); without the network file the old single line (`unity\Land\WinterIsle.rail`, from `tools\railroute.py`)
  is built. The build sorts each line into ground (benched: island step 3c), tunnel (stone portals with hoods over the
  cut, `InCut`) and trestle, lays the stations, about six trains on the loop and two on the spur (a locomotive and two
  coaches with eight `PuzzleSeat`s each, a throttle lever in every car), and writes `WinterIsle.railline` beside the
  Winter Walk plugin (`LINE`, `P`, `S`, `TRAIN`, `L` lamp lines). `winterwalk\TrainDriver.cs` runs them at 9 m/s (the
  game's train runs at 3), five minutes at each station, once "Train Plinth 1".."5" hold gourds. **Only the host
  decides:** it plans each train's moves (accelerate, run, brake to a stop) and publishes them through the pack's
  `RailControl` script (`unity\Assets\PuzzleScripts\RailControl.cs`, an SDK `SyncedString`); every PC places the cars
  from those plans and the network clock. A train never plans to stop closer than 70 m behind where the train ahead
  will stop (moving block), creeps up when held, and sets off at once when a player pushes a throttle lever (a
  `Button Sim` alone in its group, whose success calls `RailControl.Throttle`, which runs on the host). On the spur the
  locomotive is always downhill. For tests: `TrainDriver.ForceOpen`, `TestThrottle` (a train's number: as if its lever
  were pushed), `State`. The tower (a water tower) stands beside the Plaza station
  (`WinterIsleRail.TowerSpot`). The summit lift (a second `Chairlift` in the island's step 11b, a third
  `GondolaDriver` in Winter Walk) climbs from beyond the spur's buffers to the summit.
- **The airport** (`WinterIsleAirport.cs`, the island's pack): a runway on the south-east shelf, a hangar with three
  biplanes behind doors ("Airport Gates"), a fourth on the apron, the control tower (The Tree's, with "Airport
  Plinth 1".."5"), and the summit's airstrip, a deck on stilts out north-west from the crest with two more.
  `winterwalk\PlaneDriver.cs`: walking up to a parked plane's cockpit boards it; forward is the throttle, the plane
  turns and climbs towards where the camera looks, and jump climbs out once stopped on the ground. For tests:
  `PlaneDriver.ForceOpen`, `TestBoard`, `TestThrottle`, `TestYaw`, `TestPitch`, `TestLeave`. Other players see the
  pilot fly but not the plane. `game actions` lists the game's input actions (Rewired) with their live values.
- **Cast Iron Pizza** (`CastIronPizzaPuzzle.cs`, a scenery pack): the user's own restaurant, from their photos and
  logo, on a terrace built out level from the summit over the south-west face (a stone bastion near the summit, a
  braced trestle further out), at about the real shop's proportions (9 x 14 m, one door, two windows). Coordinates in
  the code are the shop's own (x right looking out of the door, z out towards the summit). It is drawn in three room
  zones (dining warm, kitchen and hall cool), each with its own `BWM/Picture` material and renderer, a low room light
  and shading baked into vertex colours (`Occlusion`), plus real lamps: empty objects named
  `Room Light <n> <hex colour> <range> <intensity>` that `winterwalk\RoomLights.cs` turns into point lights within
  70 m of the camera. Pictures and surfaces (brick, oak, beadboard, steel, the signs, cats, chalkboards, ovens...) are
  one 4096 atlas with a normal map and gloss in alpha, `tools\pizzeria_art.py` (layout and each surface's real repeat
  size in `CastIronAtlas.json`); the user's logo (`toolsrt\cast-iron-logo.png`) is also a decal centred on its
  lettering, `tools\pizzeria_logo.py`. Those PNGs are not committed: `bwm.ps1 build` regenerates them when a script is
  newer. Glass and the window's logo use `BWM/Picture` with `_AlphaMode` 1 (alpha is opacity, transparent queue).
  The outer walls stop 2 cm short of the rooms' wall planes: coplanar faces z-fight in the game. Seats at the bar,
  booths, tables and on the terrace; three skillet pizzas to carry. "Indoors Pizzeria" (an empty object sized to the
  shop) stops the snowfall inside (MineDriver). Vertex colours are read as linear: for a colour to look as written,
  use `new Color(...).linear` (the pizzeria's `B(r, g, b)` does). **Summit Disc Golf** (`SummitDiscGolfPuzzle.cs`)
  puts two carryable baskets and six frisbees on its terrace.
- **Where puzzles go:** `tools\sites\sitecheck.py` checks a layout (`{"sites": {"Name": [x, z]}}`) against
  everything the island has (railway bench and track, pistes, airport, lifts, lake, plaza, coast, other sites 70 m apart,
  how much levelling) with the island's geometry ported exactly (`islegeom.py`); `--current` checks today's sites,
  `--map` draws where free ground is. A site on a slope is cut in as a terrace (the blend round its flat is
  clamp(1.5 x drop, 10, 36) m; run the checker with `--set cut_fail=18 --set ring_fail=24` for terraces). The puzzles
  were spread out from the plaza on 2026-11-21 (four stay there as the hub); `WinterIslePuzzle.FormerSites` keeps their
  old places as coast guards, so the coast (and the railway beside it) did not move. Every island puzzle has a FixedSite.
- **Dinosaurs** (`DinosaursPuzzle.cs`, a scenery pack; `winterwalk\DinoDriver.cs`; `unity\Assets\PuzzleScripts\DinoControl.cs`):
  eight friendly dinosaurs (long-necks, plated, horned; Christmas hats and scarves) wander round homes (`Homes`), only at
  night (`NightFrom` 19 h to `NightTo` 6 h; by day they keep out of sight, showing or hiding only with no player within
  60 m, and a chase ends at dawn; `ForceNight` for tests). Each PC
  measures its own player's noise (voice loudness from the game's lips, x3 through a megaphone; sprinting, jumping,
  waving, a cowbell) and reports it to the host through DinoControl; past `Threshold` of heard noise (fading to 40 m,
  settling with time) a dinosaur chases the noisiest player at 95 % of the game's sprint speed (`tunings.forwardSprintSpeed`,
  3.5 m/s). The caught player's own PC drops everything (hand and pockets) and wakes them at the last tower they came near
  (The Tree's, the mine's, airport's, train's or summit's button post); flying, skiing fast, jetpacking or a ghost is out
  of reach. Calm walks come from the network clock (nothing sent); chases are the host's orders. Tests: `DinoDriver.TestNoise`
  (noise a second), `Peaceful`, `State`, `Last`, `LastReport`, `ChaseSpeed`. Sitting is not
  counted as out of reach: the game reports a teleported test player as sitting.
- **Puzzles' far parts:** a `PuzzleDef` can list `ExtraSites` (world x, z, radius) that the island levels and keeps
  clear like its site (`LookoutsPuzzle.cs`, whose four lookouts are kilometres apart). No paths are drawn to sites
  west of x = -2300; the train, the mine and the planes reach them.
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
  every 7 s, tracks more than 100 m from the camera are forgotten (all of them off the isle), and a 64 m window round
  the camera goes to the ground shader as `_BwmTrailTex`, which marches the
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

## The debug log (players' and ours)

`winterwalk\Diag.cs` writes one file per game start to `LocalLow\House House\Big Walk\WinterWalk-logs\session-*.log`
(12 kept), on every player's PC: all of BepInEx's log with times (written through at once; heartbeats and snapshots
forced to disk for a PC that freezes), a header (PC, versions, every pack with a fingerprint to compare between
friends), what became of the session before (closed normally or not, Windows' crash/hang records and CPU hardware
errors in its time, a crash dump), a heartbeat every 10 s (fps, Winter Walk's own ms per frame by driver, memory, the
player's state), walk events, "<driver> started/stopped moving the player" (drivers call `Diag.Touched` where they
write the player's body; `using var diag = Diag.Time("name")` times a driver), snapshots when the player is off the
ground but not falling for 2 s, or jump/crouch does nothing (the game's movement components, what is below), slow
frames, a count of what is drawn by owner and distance when the fps stays low, memory past each 2 GB step (biggest
textures and meshes), a shader check 25 s into a walk (for "see-through ground"), and the Dresser's check of its
copies (floating, buried, over nothing). F8 (`MarkKey`) marks a moment with a screenshot and both counts;
`game 'set?path=WinterWalk.DiagDriver.TestMark&value=true'` does it without a keyboard. Players send
`Send-Logs.cmd` (in the zip) output: one zip on their Desktop. Config `[Debug]` in `bwm.winterwalk.cfg`.

## Testing

1. `bwm.ps1 build <Puzzle>` and Read `previews\<Puzzle>-*.png`.
2. `bwm.ps1 play` (once), then after each rebuild `bwm.ps1 game reload`.
3. `bwm.ps1 game 'shot?file=...'` with a camera and target position, and Read the image in `shots\`. World
   position = site + local offset when yaw is 0. `time?hour=13` first for daylight.
4. `bwm.ps1 game 'puzzle?name=<Puzzle>&do=start'` then `do=win` exercises the start, deal and win paths.
5. Items: `game 'move?name=VanillaItem&nx=..&ny=..&nz=..&x=..&y=..&z=..'` moves the item nearest (nx, ny, nz).
   Put it a few metres above a zone and let it fall in: an item placed directly inside a zone is not counted,
   because the zone only notices things entering.

An idle test player falls asleep about 12 s after the walk loads (red screen); asleep it reports no ground and ignores
`press`, so movement tests must start within those seconds of `play`.

What this cannot do: pick things up or hang them on snap points, press buttons as a player, or anything
with two players. Those go to
the user and friends, who must all have the same packs and SDK version.

## Things that are easy to get wrong

- **This PC's CPU (2026-11-20):** one core of the i9-13900K (logical CPUs 4 and 5) logs machine-check errors, and
  the PC has frozen twice under bursty load (game + teleport-and-screenshot loops + Unity builds). `bwm.local.ps1`
  (not committed) sets `BWM_AFFINITY` so `bwm.ps1` keeps Unity and the game off that core (`play` and `launch`), and
  `CpuAffinity = FFFFFFCF` in the profile's `bwm.winterwalk.cfg` makes Winter Walk do it in the game itself, however
  it starts (a players' install on this PC needs the same line in the game folder's config). On 2026-11-21 the game
  crashed three times on that core (a code address one bit wrong, garbage pointers) a minute or less after Windows
  logged corrected errors on APIC 16. Never run a Unity build
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
- **The tutorial puzzle** (`TutorialPuzzle.cs`, on the mainland) is a smoke test only: its pack is kept out of the
  profile (its gourd would count towards the island's towers). `build-all` re-exports it; delete
  `profile\BepInEx\plugins\BigPuzzleSDK\BwmTutorial.puzzlepack` afterwards.
- **Puzzle ids:** the SDK ties a puzzle's permanent id to its root object in `Assets\Puzzles\<Name>\<Name>.unity`
  and records it in `unity\ProjectSettings\BigWalkPuzzleIds.json`. Builds reuse that root. Deleting the scene
  or that file gives the puzzle a new id, which breaks saves and multiplayer matching for it.
- **Saves:** the companion only ever hosts on the save slot named "BWM Test". Never select another slot.
- **Players' install** (`tools\install\`): `Install.cmd` finds Big Walk through Steam's library list and copies
  `payload\` into the game folder: Doorstop enabled with paths relative to the game, BepInEx's core and .NET, the
  SDK mod, the packs in `plugins\BigChristmasWalk\` (never the SDK's `BigPuzzleSDK` folder: packs there switch on its
  creator keys) and Winter Walk. It records what it copied in `BepInEx\BigChristmasWalk.installed.txt` and backs up
  what it replaced; a newer version updates in place; `Uninstall.cmd` removes it and restores the backup. It refuses
  a game folder whose Doorstop points elsewhere (this PC's dev setup) unless `-Force`. Tested 2026-11-21 on a fake
  folder and on this PC's game (installed with `-Force`, booted through Steam with 35 packs, uninstalled: the dev
  files came back byte-identical). The first start generates BepInEx's interop (needs the internet once).
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
- Releases are private (user, 2026-11-21): the repo stays private, and the players' zip goes in the user's Google Drive
  (`D:\My Drive\Big Christmas Walk\`, synced by Drive for desktop); the user shares it by link. Never make the repo
  public.
- Nothing has been tested with two players.
- Found by the debug log on 2026-11-21, not yet fixed:
  - The "BWM Test" save loads the player into the game's spawn room at (-242, 37, -485) with no collider beneath (not
    even terrain within 500 m) and holds them there, off the ground, unable to jump. Suspect the dev profile's
    SkipIntro (players' installs do not have it): try without it.
  - Memory: 16 GB private, 11-12 GB in RAM in a walk; 55 million mesh vertices, most in the island's "Grass N"
    scatter meshes (about a million vertices each, readable, so kept twice; `WinterIslePuzzle.cs` section 7b).
  - Drawing: the island's dressing is ~201,000 renderers (rocks, firs, plants; 196,000 cast shadows); seen from the
    summit, 162,000 of them are over 1 km away. Unity reports every renderer in the world as visible (`isVisible` is
    no guide), so how many are really drawn is not measured. The user finds it slow looking at the mountain (not
    reproduced at the 30 fps cap on this PC) and suggests fog that hides it until within about 50 m.
  - Dressing: 27 copies float 0.6 m or more over what is under them (some over the sea or a tunnel cut), 25 stand
    over open sea with nothing under them (near -1817, -2497), 2,749 rocks lie wholly under the ground.
  - Jagged spikes at the rail tunnels' mouths (a tester's screenshot, 2026-11-21): snow-shaded, one-sided triangles
    beside the track at a portal, seen from outside and gone looking back: slivers of the island's ground mesh left
    standing where the ground is cut away over the tunnel (`InCut`), not dressing. Separately, 72 dressing rocks reach
    into the rail tunnels or the mine (the biggest 47-59 m granite cliffs through "Mine Walls", e.g. -940, 13, -1158;
    by their boxes, so some may be near misses).
  - Seeing through our shaders (the snow most) on quality settings above Low, with a large slowdown (the user,
    2026-11-21). Every level renders Deferred+ (no depth priming), so the difference is another per-level setting:
    the debug log writes the full pipeline settings and renderer features at each walk and on every change of
    graphics settings; compare a Low session with a Medium one. Slowness only shows uncapped (not at the 30 fps cap).
