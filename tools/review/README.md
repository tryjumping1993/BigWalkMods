# Terrain review

The Winter Isle is judged against the base game's island by a fresh agent reading matched screenshots.

- `review-shots.sh [orig|winter|both]` (Git Bash, with the game running: `bwm.ps1 play`) takes an aerial, a mid
  shot, eight eye-level shots and one shore shot of each island into `shots\review\`, and contact sheets into
  `shots\sheets\`. Eye-level shots: the player is set down on the ground at each point (the base island's points
  are in `review-viewpoints.txt`; the Winter Isle's are twelve spread through `world\winter-viewpoints.tsv`, which
  its build writes along the trails), and the shot is taken from the game camera's own position, looking level
  along the planned heading. `pick-heading.py` turns the camera to the nearest open heading when the view is
  blocked within 6 m (the companion's `look`). Where nothing to stand on is loaded yet (much of the base island
  streams in), the player is left to come down where they will. Shore shot: due south from the even eye points
  to the sea, taken at the first place a player can stand within 6 m of sea level, looking east.
  It pauses between teleports on purpose: see the CPU note in `CLAUDE.md`.
- The reviewer is a general-purpose agent given all 22 images and the same brief each round: score both islands
  1 to 10 on landform variety, ground detail at eye level, rocks and landmarks, vegetation, coastline, and paths
  and composition, and say for each whether the Winter Isle is adequately close; a strict YES or NO overall; the
  top five gaps tied to shot files; what to keep. It is told to ignore snow against summer, the base game's
  puzzle props, the HUD and falling snow, and that the players' eyes are 60 cm above the ground.
- The goal (set by the user, 2026-11-20): keep going until the reviewer judges every one of the six adequately
  close.

Rounds so far (original / winter):

| Round | Landform | Ground | Rocks | Vegetation | Coast | Paths | Close on |
|---|---|---|---|---|---|---|---|
| 3 | 9/6 | 10/2 | 10/5 | 10/4 | 9/3 | 8/5 | (not asked) |
| 4 | 9/6 | 9/5 | 9/6 | 9/4 | 9/4 | 8/6 | (not asked) |
| 5 | 9/6 | 9/5 | 9/7 | 10/6 | 9/3 | 7/7 | rocks, paths |
| 6 | 9/7 | 9/5 | 9/6 | 10/5 | 9/5 | 8/7 | landform, paths |
| 7 | 9/7 | 9/5 | 9/7 | 9/5 | 9/6 | 8/6 | landform, rocks, paths |
| 8 | 9/7 | 9/5 | 9/7 | 9/5 | 8/6 | 7/6 | landform, rocks, paths |
| 9 | 9/7 | 9/5 | 9/6 | 10/5 | 8/7 | 7/6 | landform, coast, paths |
| 10 | 9/7 | 9/5 | 9/7 | 9/5 | 8/6 | 7/6 | landform, rocks, paths |
| 11 | 9/7 | 9/6 | 9/6 | 10/5 | 8/6 | 8/7 | landform, rocks (just), paths |
| 12 | 9/7 | 9/5 | 9/6 | 9/6 | 9/6 | 8/7 | landform, rocks, coast (both narrowly), paths |
| 13 | 9/7 | 9/5 | 9/6 | 9/6 | 8/6 | 7/7 | landform, rocks, coast (both narrowly), paths |
| 14 | 9/7 | 9/6 | 9/6 | 9/6 | 8/6.5 | 8/7.5 | landform, coast (marginal), paths |
| 15 | 9/7 | 9/6 | 9/7 | 9/5 | 8/7 | 7/8 | landform, rocks, coast, paths (ground a near miss) |
| 16 | 9/6 | 9/5 | 9/5 | 9/6 | 9/6 | 8/7 | paths (after the new firs, the game's ground layers, cirques and strata; the strata read as contour terraces) |
| 17 | 8/7 | 9/5 | 9/6 | 9/7 | 8/5 | 7/7 | landform, vegetation (first time), paths |
| 18 | 9/7 | 9/5 | 9/5 | 9/6 | 9/5 | 8/7 | landform, paths |
| 19 | 8/6 | 9/5 | 9/5 | 9/6 | 9/4 | 7/6 | landform (just), paths (after the crag blocks: they read as stacked boxes) |
| 20 | 8/6 | 9/5 | 9/5 | 9/6 | 9/4 | 7/7 | paths (after faceting the mountain and removing the crag blocks; landform nearest) |

What each round's gaps led to is in the git log. Things the reviewer has asked for more than once: clutter lying
on the snow, dense woods a walker is inside of, a shore that is not raw terrain, peaks that are not smooth cones.

Rounds 6 to 10 changed a great deal on the island and moved ground and vegetation not at all (5 and 5 every
round). What the reviewers name each time is a difference of kind, not amount: our conifers are flat-shaded cards
with untextured trunks beside the game's textured trees, the snow is one material where the base island's ground
changes every few metres, and every winter eye shot is under open sky where the base island's are inside woods.
More scatter does not move those scores. Rocks and coast flip between 6 and 7 with the reviewer.

Round 11 (after the great mountain, 28 woods and bark on the trunks) named: card boughs with sawtooth edges and no
understorey in the woods; bare-ground patches that are a blurred texture under a hard mask; a flat, untextured beach
with a straight waterline; no walker-sized rock inland; the same recipe everywhere.

## The mountain

The great mountain has its own review, since the base game has no ski mountain to set beside it: `mountain-shots.sh`
takes twelve fixed views into `shots/mountain/` (run it a minute or more after `play`, so the dressing has loaded),
and a fresh agent follows `mountain-brief.md`: seven scores and a strict yes or no on "does it look like a realistic
ski slope". The terrain reviewer's brief is `brief.md`.

| Round | Shape | Ski face | Pistes | Forest | Rock | Furniture | Snow | Verdict |
|---|---|---|---|---|---|---|---|---|
| 1 | 4 | 3 | 4 | 4 | 5 | 3 | 5 | NO |
| 2 | 5 | 4 | 5 | 6 | 4 | 3 | 3 | NO |
| 3 | 6 | 5 | 5 | 5 | 4 | 5 | 7 | NO |
| 4 | 6 | 5 | 4 | 5 | 5 | 5 | 5 | NO |
| 5 | 5 | 5 | 5 | 5 | 3 | 5 | 5 | NO |
| 6 | 4 | 3 | 4 | 5 | 3 | 5 | 6 | NO |
| 7 | 5 | 4 | 4 | 4 | 3 | 5 | 6 | NO |
| 8 | 5 | 3 | 4 | 5 | 3 | 5 | 5 | NO |
| 9 | 5 | 4 | 5 | 5 | 2 | 6 | 5 | NO |
| 10 | 5 | 4 | 6 | 6 | 3 | 5 | 6 | NO |
| 11 | 4 | 4 | 6 | 6 | 3 | 6 | 6 | NO |
| 12 | 5 | 5 | 6 | 6 | 3 | 6 | 5 | NO |

Rounds 10 and 11 pulled opposite ways on the lesser peaks: round 10 asked for them lower, and round 11, with them
lowered, called the mountain a symmetrical pyramid and asked for a distinct second summit at three quarters to four
fifths of the main one. Round 11 also showed that a bench followed by a steeper pitch hides the whole face below it
from the bowl and the summit (both reviewers want to see the runs drop away), so the profile is now concave with only
a short, gentle step; the build prints the slope down the middle run, which is the check to make before shooting.

Round 4 lost ground on pistes and snow: the fir walls planted along the runs' edges put three cameras inside
branches, the berm along each edge showed as a dark brown outline from the air, and bare patches and shrubs still
sat on the runs.

Round 2's snow score fell because the corduroy added after round 1 rang into moire at distance (it now follows
lines from the summit and fades once a pixel cannot hold it). The same rings cost the terrain review's ground score
in round 12, where two eye shots were also on open piste; the viewpoints now skip the pistes.
