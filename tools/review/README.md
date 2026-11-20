# Terrain review

The Winter Isle is judged against the base game's island by a fresh agent reading matched screenshots.

- `review-shots.sh [orig|winter|both]` (Git Bash, with the game running: `bwm.ps1 play`) takes an aerial, a mid
  shot and eight eye-level shots of each island into `shots\review\`, and contact sheets into `shots\sheets\`.
  Eye-level shots are taken where the player lands near each point in `review-viewpoints.txt`, at the game
  camera's height, looking level along the planned heading; `pick-heading.py` turns the camera to the nearest
  open heading when the view is blocked within 6 m (the companion's `look` command). It pauses between
  teleports on purpose: see the CPU note in `CLAUDE.md`.
- The reviewer is a general-purpose agent given all 20 images and the same brief each round: score both islands
  1 to 10 on landform variety, ground detail at eye level, rocks and landmarks, vegetation, coastline, and paths
  and composition; a strict YES or NO on "similar quality"; the top five gaps tied to shot files; what to keep.
  It is told to ignore snow against summer, the base game's puzzle props, the HUD and falling snow.

Rounds so far (original / winter): round 3 ground 10/2, vegetation 10/4, coast 9/3; round 4 landform 9/6, ground
9/5, rocks 9/6, vegetation 9/4, coast 9/4, paths 8/6. Both NO.

Known weakness: a landing spot can be inside a fir's boughs, which have no collider, so the open-view rule
does not see them (winter-eye-1). Teleporting to the trail point at ground height instead of dropping from
150 m would fix it; reshoot both islands when changing the method.
