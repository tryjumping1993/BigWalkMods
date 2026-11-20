# Winter Walk: plan for a Christmas edition of Big Walk

Written 2026-11-19, decisions added 2026-11-20. Goal: a snowy, Christmas-themed edition of Big Walk with 24 new
puzzles in a region about 60% the size of the original game's puzzle area, playable by the user and friends as
one modpack, by Christmas 2026.

## Decisions (from the user, 2026-11-20)

- **Region:** its own island, which players walk out to from the game's opening area. If new land turns out
  not to be possible, the fallback is the area beyond the wall near the end of the game, where there are few
  puzzles.
- **Target:** all 24 puzzles by Christmas.
- **Snow:** the snow and the falling-snow effects are to be made for this edition (own materials, shader work
  and particles), not produced by stripping or swapping the game's existing textures.
- **Players:** three is the minimum. Design for 3 and for 4+; two-player lobbies are not supported.
- **Audience:** friends only. No public release, so no store page, support or licence review beyond using
  assets we are allowed to use.
- **Decorations:** the snow area is dressed for Christmas, with strings of lights above all.
- **Snow you can build with:** if possible, players can move the looser snow about and build things with it,
  and some puzzles are based on that.
- **Blind mask:** six of the puzzles use the blinding mask.

## What "60% of the original" means

Measured from the game's own world (`world\world-index.tsv`):

- The island is four 1024 m terrains (2048 x 2048 m including sea).
- The base game's challenges sit in a band of roughly 700 x 540 m (about 0.38 km2): 29 any-player-count
  challenges plus 19 that differ by lobby size, around 40 puzzles in all.
- So the winter region is about 0.23 km2, for example 550 x 420 m, with 22 to 24 puzzles. That is close to the
  base game's own density (one puzzle per roughly 100 m of walking).

## What is already proven

| Capability | State |
|---|---|
| Puzzles written as code, built headless in about 25 s | Working (`tools\bwm.ps1 build`) |
| Seeing and driving the game without a person | Working: companion plugin hosts a test walk, reloads puzzles, teleports, takes screenshots, starts and wins puzzles |
| A real puzzle from the game's parts plus blockout geometry | Working: Glass Booth loads, deals its answer, and wins |
| Snow on the ground | Proven live: swapping the terrain's 12 ground textures turns the whole island white (`shots\winter-after.png`) |
| Clearing trees, shrubs and boulders from a site | Working (the SDK's Terrain Hole) |
| New land in the sea | Built 2026-11-20: the Winter Isle, 0.21 km2 above the sea, with a 56 m causeway to the mainland shore. It loads in game, a player stands on its summit and on the causeway, and the Glass Booth is sited on it (`WinterIslePuzzle.cs`, `shots\winterisle-oblique.png`) |

## What is not proven (each is a spike in phase 0)

1. **Custom script logic.** Answered 2026-11-20 for local code: a script of ours (the snowfall follower) is
   exported, loaded by the game and runs. Host-driven shared state through the SDK's synced variables, which
   the code-heavy puzzles need, is still untried.
2. **Custom models, textures and sounds.** Only cubes and the game's own parts so far.
3. **Material artifact.** Flat-colour materials show thin dark grid lines in the game. Cause unknown.
4. **Snow on everything that is not terrain.** Paths stay red-brown, grass and shrubs stay orange, trees olive,
   rocks brown. Each needs its own treatment (tint, texture swap, or hiding).
5. **Falling snow, cold light, fog.** Falling snow is answered 2026-11-20: our own particle effect and flake
   shader follow each player's camera over the island (`shots\snowfall-player.png`). Sky and light are answered
   too: the Winter Walk plugin (`winterwalk\`) blends the game's sky system towards winter for each player as
   they near the island (`shots\sky-isle-day.png`, `-dusk`, `-night`, `sky-partway.png`, `sky-mainland.png`).
6. **Multiplayer.** Nothing has been played by two people. State sync is the SDK's job, but untested here.
7. **Installing on a friend's PC.** No packaging script yet; nobody else has installed any of this.
8. **Walking on new land.** Standing is proven on the full-size island; nobody has walked the causeway or the
   ramps between shelves yet, which needs a person at the controls.

## Design

Big Walk's puzzles are about communicating across distance and barriers. The winter edition keeps that and
changes the dressing: the gourd tower becomes a Christmas tree, gourds become its ornaments, and each puzzle is
a small winter scene. Three rules for every puzzle:

- Needs at least three players and works for 3 and 4+ (the SDK's Hider shows different layouts per lobby size).
- Nobody can get stuck; everything resets when the puzzle stops.
- Readable in snow: strong colour on puzzle structures, lamps on anything used at night.

### The island

A new landmass of about 0.23 km2 off the coast by the opening area (sea level is height 0, and the shore is
about 150 m from the spawn rooms). It ships as geometry in a puzzle pack: a terrain mesh with colliders, a
causeway or bridge out to it, and its own trees, rocks and buildings. Because every surface is ours, the look
is ours too:

- Snow ground: our own material (soft blue shadows, sparkle, packed-snow paths), not the game's textures.
- Winter trees, rocks, ice and frozen shoreline: our own models and materials.
- Falling snow: our own particle effect that follows each player's camera, with wind and gusts.
- Light and sky over the island: overcast and cold through the game's sky system, blended in as players cross
  the causeway, so the mainland keeps its normal look.

The island as built:

- It fills the open sea south-west of the opening area, keeping a 50 m channel from the mainland and staying
  inside the game's terrain square (the user's rule for its shape). That gives 0.21 km2, about 55% of the base
  game's puzzle area. Land past the square's edge also held a player in a test, so the island can grow west or
  south later if the 24 puzzles need more room.
- The causeway lands on the mainland shore at about (-468, -700), roughly 290 m from the spawn rooms.
- Its terrain is varied. The east and middle are low shelves 2 m apart joined by gentle ramps, where most
  puzzles sit. A mountain rises in the west to 73 m in cliff bands, with two lower peaks north and south, a
  ravine 9 m deep north of the plaza and a frozen lake in the south-west. About a quarter of the land is level
  enough to build on; steep ground shows bare rock. Every puzzle site is levelled, and paths are routed around
  slopes too steep to walk.
- Sea level is height 0. Players dropped in the sea float and are not killed, so water is not a wall: puzzles
  cannot rely on the shoreline to keep players in or out.
- Its ground uses our own snow shader (drift relief, blue shadows, glints) and it snows there.
- About 340 snow-laden pines stand in groves, with boulders, all generated and all using our own snow-capped
  shader. Packed-snow paths run from the causeway's landing to a levelled central plaza and from there to
  every puzzle. The Tree stands in the plaza.
- Sky and light: within about 170 m of the island each player's sky turns paler with haze on the horizon, a
  softer sun, brighter light from the sky and a thin layer of high cloud, fading back to the game's own on the
  mainland. This is the Winter Walk plugin, which every player needs; without it the island still works under
  the game's sunny sky. Its config has an off switch (`WinterSky`).
- Still to make: a frozen shoreline, cabins and other buildings, puzzles up on the mountain, and an art pass
  on all of it.

Fallback if new land fails later: the north-west of the island, beyond the big wall near the ending (few base-game
puzzles there), dressed with the same bespoke snow materials laid over the ground as meshes.

### Decorations

- **Strings of lights** are the island's signature: sagging wires of coloured bulbs between posts, along the
  causeway, around every puzzle site, over the main paths and wound round The Tree. They are our own geometry
  and shader (a kit call in a puzzle definition), they twinkle, and they glow at night. Built 2026-11-20.
- Real lamps (the game's own, which light the snow) are used sparingly where a puzzle needs to be read at night.
- Later: wreaths, candy-striped posts, presents, lanterns, a decorated gate at the causeway.

### Snow you can build with

The game cannot deform ground, but it can do carry-and-place very well, so loose snow is made of things:

- **Snow lumps** are items of ours (the SDK's Custom Item) that sit in drifts. Players pick them up, carry
  them, throw them, and put them down, and every player sees the same thing because the game syncs items.
- **Building** is placing lumps where something should be. A zone that counts the lumps inside it (the SDK's
  Presence Sensor in its item mode) makes the built thing appear piece by piece: a snowman, a wall, a bridge
  span. Snap points (invisible hooks) hold a lump exactly in place where shape matters.
- **Throwing** counts too: the same zones register a lump passing through, which makes targets.
- Not possible: free sculpting, digging holes, or unlimited snow. Each drift holds a fixed number of lumps,
  which go back to their drift when a puzzle resets.

First test site built 2026-11-20 (`SnowmanPuzzle.cs`). Checked in game without hands: the three snowballs
are real items, they fall and roll, the zone counts them when they drop in, the puzzle is won and the
snowman's hat, nose and arms appear. Not checked: how they handle when carried and thrown, and whether the
snap points take them, which is what would stack the three into a body. Until a person has tried that, the
finished snowman is three loose snowballs with trimmings floating above them.

### Blind mask

The game has both a wearable blindfold and the blindfold challenge's own turnstile, and the SDK offers both
(Vanilla Item: Blindfold; No Items Gate with Blind Mask on). Scripts can also read what a player wears on
their head. Six puzzles below are built round a player who cannot see.

### Puzzle list (24; the first is built)

"Code" says whether it needs custom C#. "Risk" is my estimate of how likely it is to need redesign.
**B** marks the six blind-mask puzzles, **S** the snow-building ones.

| # | Name | Idea | Main SDK parts | Code | Risk |
|---|---|---|---|---|---|
| 1 | Glass Booth | Reader in a soundproof booth points out tiles for the others to hang | Display, Peg Board, Glass, Door | No | Built |
| 2 | Snow Bridge **S** | A gap too wide to jump; carry lumps from drifts on both banks to build the spans, and the far bank's drift can only be reached by the first span | Custom Item, Presence Sensor | Yes (spans) | Medium |
| 3 | Light the Tree | Six posts ring a tree; one lamp lights at a time and its button must be pressed, but each button's lamp is on the far side, so the others must call it | Press In Order, Light, Poke Button, Progress Bar | No | Built |
| 4 | Bell Ringers | Bell stands far apart, each with a lever; all must be pulled within about a second. A fourth stand appears for four or more players | Sim Press Switch, Hider | No | Built |
| 5 | Hold the Star | Everyone holds a button at once to raise the star; one must let go to fetch the gourd | N-Hold Button, Box | No | Low |
| 6 | Reindeer Count | Count reindeer cut-outs scattered on a hillside from two lookouts that each see some | Count Input, Count Validator, Telescope | Yes (sets the answer) | Medium |
| 7 | Bauble Hunt | Coloured baubles buried in drifts go on a round board | Circle Peg, Circle Peg Board | No | Low |
| 8 | Santa's List | A list only readable through one telescope; a second player hangs what is read out | Telescope (invisible ink), Peg Tile, Peg Board | No | Low |
| 9 | Blind Carols **B** | The masked player sorts tune-playing tiles by ear; a partner who cannot hear them reads where each belongs | Sound Peg, Sound Peg Answer, Peg Board, blind mask gate, Soundproof | No (custom clips) | Medium |
| 10 | Whispering Chimney | One player hears a distant room through a chimney microphone and relays what happens | Directional Mic, Listening Chair, Intercom | No | Medium |
| 11 | Blizzard Walk **B** | A masked player is talked across a narrow walkway over a drop | Blind mask gate, Soundproof | No | Medium |
| 12 | Radio Silence | A valley where walkie-talkies fail; messages go by runner or signal | Radio Dead Zone, Display, Peg Board | No | Low |
| 13 | Stocking Sort | Hang the right gift on each named stocking; names are across the valley | Custom Item, Hook, Sign | Yes (checks hooks) | Medium |
| 14 | Build a Snowman **S** | Three snowballs of different sizes and the trimmings, assembled to a blueprint only a far player sees | Custom Item, Hook, Presence Sensor, Display | Yes | Medium (test site built) |
| 15 | Blind Sculptor **B S** | The builder wears the mask; the others talk them to the right lumps and the right places | Custom Item, Presence Sensor, blind mask gate | Yes | Medium |
| 16 | Footprints | Step on hidden pads in an order that only a lookout can see | Presence Sensor, Press In Order, Light | No | Low |
| 17 | Sleigh Run | Carry a present from start to goal before the clock runs out, through gates others hold open | Timer, Timer Spawner, Timer Goal, Door, Hold Button | Maybe | Medium |
| 18 | Whiteout Maze **B** | Masked players walk a walled maze; a lookout on a tower sees it all and calls the turns | Blind mask gate, Presence Sensor, Light | No | Medium |
| 19 | Snow Globe | A room in a glass dome; inside and outside each hold half of a switch sequence | Glass (soundproof), Switch, Validator | Yes (sequence) | Medium |
| 20 | Fairy Lights | A string of coloured lamps; set each switch so the pattern matches a picture seen elsewhere | Switch, Light, Display | Yes | Medium |
| 21 | Thin Ice **B** | A masked player crosses a frozen lake on the safe tiles; the caller on the shore sees which are safe | Presence Sensor, Light, Timer, blind mask gate | Yes | Medium |
| 22 | Snowball Range **B S** | A masked thrower, aimed by voice, must put snowballs through the hoops the spotter calls | Custom Item, Presence Sensor, Light, blind mask gate | Yes (hit order) | Medium |
| 23 | Advent Trail | 24 numbered signs across the whole island; a final board asks about them | Sign, Peg Tile, Peg Board | No | Low (but needs every other site placed first) |
| 24 | Midnight Finale | With every ornament on the tree, everyone triggers the fireworks together | Firework Launcher, Sim Press Switch, Light | Yes | Medium (the launcher has a known particle bug) |
| T | The Tree | The modded gourd tower as a Christmas tree; each won puzzle adds an ornament | Gourd Plinth x24, Tower Button | No | Built |

Built so far (2026-11-20): 1, 3, 4, The Tree, and a first version of 14. "Built" means it loads in the game,
starts, and looks right in screenshots; none has been played, and the levers, buttons and lamps have not
been operated by a player.

Eleven need no custom code. Blind mask: 9, 11, 15, 18, 21, 22. Snow building: 2, 14, 15, 22 (and 7 uses the
drifts). Each blind-mask puzzle has to be safe for someone who cannot see: railings or soft resets, never a
fall into the sea, and the mask comes off the moment the puzzle stops.

### Art and audio

- Geometry: code-built blockout first, then free low-poly holiday models (CC0 packs) for trees, presents,
  cabins, sleigh, fences. Game assets are never copied into a pack; the SDK reuses them from each player's
  own install.
- Peg tile pictures: custom sheets of Christmas icons (the SDK takes a 4 x 4 sheet, eight images each).
- Sound: carols must be public-domain compositions in recordings we are allowed to use (CC0 or self-made).
- Snow lumps, snowballs, snowman parts and the light strings are generated in code, like the island.

## Framework work this needs

- Building kit: walls with door and window openings, rooms, stairs, ramps, fences, so a booth is one call.
- Model, texture and audio import by path, with the correct shader assigned.
- A scripted-puzzle template (host-only logic, synced variables), and its DLL in the export.
- Lobby-size variants (Hider) expressed in a definition.
- Region planner: all sites on one map, spacing rules, a rendered overview image.
- Automated smoke test: for every puzzle, host, teleport, start, win, screenshot, and check the log.
- Packaging: one command to produce the Thunderstore-format modpack (SDK, WinterWalk plugin, all packs).

## Phases

| Phase | Work | Done when |
|---|---|---|
| 0. Spikes | The eight unknowns above, smallest test of each | Each has a yes/no answer and a fallback; a friend has installed a two-puzzle pack and played it with the user |
| 1. World | WinterWalk plugin (ground, vegetation, sky, snowfall, off switch); region survey and choice; travel into the region | A walk through the region looks like winter in screenshots at noon, dusk and night, on two PCs |
| 2. Vertical slice | The Tree, puzzles 1 to 5, building kit, smoke test, packaging | A group plays all five start to finish and places ornaments; their notes are in the repo |
| 3. Production | Puzzles 6 to 24 in batches of four or five, each batch followed by a playtest | 20 or more puzzles pass a playtest with no blocker |
| 4. Release | Polish pass, night lighting, Advent Trail, finale, README, modpack | One-step install from a profile code; full run by a fresh group |

Rough size: phase 0 is two or three working sessions; phase 1 about three; each puzzle half a session to a
full one plus a playtest; so 25 to 35 sessions in all, limited mainly by how often a group can playtest.

## Timing

The target is all 24 by Christmas, five weeks from the plan's date. A schedule that fits:

| Week | Work |
|---|---|
| 1 (to 26 Nov) | Phase 0 spikes, the island and causeway, bespoke snow and snowfall, building kit, packaging; first friend install |
| 2 (to 3 Dec) | The Tree and puzzles 1 to 8; first group playtest |
| 3 (to 10 Dec) | Puzzles 9 to 16; second playtest |
| 4 (to 17 Dec) | Puzzles 17 to 24; third playtest |
| 5 (to 24 Dec) | Fixes from playtests, night lighting, Advent Trail and finale tuning, final pack |

That is eight puzzles a week, which only works if playtests happen weekly and the code-heavy puzzles (about a
third) do not stall. My own estimate is that this is tight; the order keeps whatever exists playable, so a
slip costs puzzles at the end, not the edition.

## Risks

- **SDK and game updates.** The SDK is at 0.1.7 with weekly releases, and a game patch can break the mod
  loader. Pin versions for the whole group until release; expect one or two forced fixes.
- **Everyone must install the same thing.** Console players cannot join. A mismatch means nothing syncs.
- **Three-player minimum.** Every playtest needs three people at once.
- **Solo testing proves little about multiplayer.** Budget a short playtest after every batch.
- **Performance.** Recolouring 47,000 trees and adding snowfall must be measured on the weakest PC in the group.
- **Scope.** 24 distinct puzzles is a large design job; variety will suffer before quality should.

## Mountain, towers and gondola (2026-11-20)

What the user asked for: the large mountain; a Christmas tower that takes the first five gourds; a secret area
that takes twenty more; a high-speed gondola that unlocks once the tower is complete; slopes made for skiing;
and finished assets for the puzzles.

### What the SDK allows (read from its source)

- Only modded gourds go in plinths, one gourd per modded puzzle.
- Only one Tower-type pack has its gourds put back from a save. A gourd in a plinth of any other pack is saved
  as "lost" and returns to its puzzle's pillow. So both gourd places live in one pack: **The Tree**.
- A Tower Button shows only as many plinths as there are modded puzzles loaded, first plinths first. With
  fewer than 25 puzzles the last summit plinths simply are not there yet.
- `onGourdPlaced` does not fire when a save puts gourds back, so anything that depends on the tower polls
  `HasGourd` instead of counting events.

### The plan

- **The Tree** (plaza) keeps its scene root and id. Its Tower Button's list is 25 plinths: the first 5 ring the
  tree, the next 20 stand in the summit sanctuary. The tree's hold button opens the next of its five; a
  second hold button at the summit opens the summit plinths while held.
- **Gondola**: a bottom station beside the plaza and a top station on the summit plateau, pylons and a cable
  between, cabins with two chairlift seats each (the SDK's Custom Chair with the game's chairlift poses, the
  way the base game's own chairlift carries players). Winter Walk moves the cabins: each one's place on the
  loop is worked out from the network clock (`Mirror.NetworkTime.time`), so every player sees the same cabin
  in the same place with nothing sent. Cabins slow through the stations and run fast on the line. Until the
  tree's five plinths hold gourds the cabins stand still in the bottom station.
- **Mountain**: the summit plateau is ringed by ground too steep to climb and has no trail, so the gondola is
  the only way up. A corridor under the cable is kept clear of trees and rocks.
- **Skiing**: there are no skis in the game. Pistes are wide, even runs from the top station down to the
  bottom station, on their own low-friction collider, clear of rocks and scatter, ending in a run-out, so
  sliding down is the way back. Nobody can be trapped: every piste ends where walking works again.

### Open questions for the user

- Is the summit's 20 on top of the tree's 5 (25 puzzles in all), or 20 in all?
- "Complete assets for the puzzles": finish the look of the four built puzzles, or build the unbuilt ones in
  the list above?
