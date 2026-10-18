# Changelog

## 0.16.0 - first public release

Everything below is relative to the last internal build, for anyone who saw an earlier copy.

### Added
- **Four difficulties** - Easy, Normal, Hard and The Arrival - chosen in Mod Settings before
  starting a game and remembered per save. They set how long you can stare, whether fire drives
  him off, how early and how fast the chaser arrives, how hard it is to repel, and how
  aggressive Slender is from the first page.
- **The chaser** (a second pursuer) with its own model, run cycle and chase track. It ignores
  whether you are looking, runs straight at you and kills on contact. Hold a light on its face
  to drive it off; it returns faster each time.
- **Flashlight battery** - drains over about 7 minutes of light, refills over about 5 minutes
  switched off, and the beam gives out at empty.
- **Flashlight works without an aurora.** The game's flashlight is aurora-powered and a Slender
  game has no aurora, so the three methods that gate it are replaced inside Slender games only.
- **The far stare** - holding him in view from beyond 50 m for 7-15 seconds tears the picture
  and he is gone from where he stood. One time in five he reappears 15 m behind you.
- **"Start with pages collected"** testing setting, 0-8.
- Per-save difficulty, and a loading screen for Slender games.

### Changed
- Screen static now begins only inside 50 m and fades in rather than popping.
- Slender and the chaser hunt at the same time instead of taking turns; the ambience ducks
  under the chase track rather than restarting it.
- The chaser's run cycle was rebuilt with a real stride and swinging arms.

### Fixed
- Slender no longer hunts in Pilgrim or any other game mode. Save slot ids are reused by the
  game, so a slot that once held a Slender game could claim an ordinary one; identity is now
  checked three ways and stale records are cleared on sight.
- A test hunt or chase queued from the menus no longer fires in a brand-new game.
- 3D pages are no longer switched to the flat fallback (which draws through walls) by a single
  interrupted quit. The guard now needs two strikes, and pages clear the surface they are pinned
  to so they cannot sink into a tree trunk.
- The chaser used to stand still for several seconds instead of running away when it gave up.
- Shining a light at the chaser did nothing, because the check asked the game whether the
  flashlight was lit and that answer takes the aurora into account.
- A jumpscare's zoom could leave the camera's field of view permanently changed after a scene
  change.
- Slender's model no longer stays frozen on screen if the other one catches you mid-chase.