# Slender: The Eight Pages

A horror game mode for **The Long Dark**. You are put on Coastal Highway in a night that never
ends, looking for eight pages, while something tall stands at the edge of your light and only
moves when you are not looking at it. Later, something else starts running at you.

**Version 0.16.0** · The Long Dark 2.55 · MelonLoader 0.7.x · unofficial fan project

## Requirements

- The Long Dark 2.55 (Steam)
- [MelonLoader](https://github.com/LavaGang/MelonLoader) 0.7.x, started once so it generates
  its game files
- The **ModSettings** mod in your `Mods` folder

Nothing else. In particular you do **not** need Universal Tweaks or any other flashlight mod -
the aurora-free flashlight is handled inside this mod.

## Install

1. Copy `Mods\SlenderMode v.0.16.0.dll` into `TheLongDark\Mods\`.
2. Copy the `Mods\SlenderMode\` folder into `TheLongDark\Mods\` as well.
3. Start the game and choose -> SURVIVAL -> OPTIONS -> MODSETTINGS -> SLENDER MODE
4. Change anything to your liking. Make sure to DISABLE SLENDERMODE when entering your own saved games to be absolutely sure.
5. (OPTIONALLY) for testing purposes, use testing section.

Settings live in **Options > Mod Settings > Slender Mode**. Pick your difficulty there
*before* starting a game.

The mode only applies to games started from that menu entry. Pilgrim, Voyageur, Stalker,
Interloper, Misery, Custom, Challenges and Story are untouched.

## How it plays

**The night.** The clock loops between 22:00 and 04:30 so weather and hunger keep moving but
dawn never arrives. No aurora, no game music, low fog that never clears, and the moonlight
turned down to a faint cold blue. You start with two torches and a flashlight.

**The pages.** Eight of them, pinned to trees, rocks and walls, picked at random from the
recorded spots for the region. A thin column of smoke rises next to every page you have not
taken yet, visible from a long way off. Look at a page and press **E**. Every page you take
makes him worse. After the eighth, he comes for you.

**Slender.** He only moves while you are not looking at him. Inside 50 m the picture starts
breaking up, and holding your eyes on him fills a meter that ends with him taking you - how
fast depends on the difficulty. Beyond 50 m he will not be stared at: hold him in view for
somewhere between 7 and 15 seconds and the picture tears for an instant and he is no longer
where he stood. One time in five he is standing 15 m behind you instead. Walk into him and he
takes you on the spot.

**The chaser.** From a certain page onward - the sixth on Easy, the first on The Arrival - the
other one turns up. It does not care whether you are looking. It screams, runs straight at you,
and kills on contact. **Hold a light on its face** and it recoils and breaks off, but each time
you do that it comes back faster, and on the harder settings you have to let it get close and
hold the beam steady for longer. Going indoors ends a chase. Slender does not wait his turn, so
both can be on you at once.

**The flashlight.** It works without an aurora, which the game's own flashlight does not. The
battery gives about seven minutes of light and fills again over about five minutes switched off,
so it is a resource rather than a floodlight.

## Difficulty

Chosen in Mod Settings before you start a game, and remembered per save.

| | Stare | Fire drives him off | Chaser from page | Chaser speed | Light repel | Aggression at page 0 |
|---|---|---|---|---|---|---|
| **Easy** | 10 s | yes | 6 | 2.5 m/s | 0.45 s at 32 m | none |
| **Normal** | 8 s | yes | 4 | 2.9 m/s | 0.80 s at 28 m | slight |
| **Hard** | 4 s | no | 2 | 3.3 m/s | 1.40 s at 22 m | high |
| **The Arrival** | 3 s | no | 1 | 3.8 m/s | 2.20 s at 16 m | from the first second |

"Light repel" is how long you must hold a light on the chaser's face, and how close it has to
be before the light bites at all. A torch or flare works at a little over half the flashlight's
range.

## Settings

**Slender Mode** - turn the mod off, and **Real death**: YES means getting caught kills you for
real (a permadeath save is gone); NO means you black out and wake up badly hurt.

**Difficulty** - the table above.

**Slender game** - *Extra darkness*, how heavily the night grade sits over the world.

**Creature** - its height, and a rotation fix if
your model faces the wrong way.

**Pages** - the page spot editor (see [ASSETS.md](ASSETS.md)).

**Testing** - show the distance readout, start a hunt or a chase on demand, a repeating chaser
test mode, and **Start with pages collected** (0-8) so you can reach the later stages without
walking the whole set. Set these back to NO before playing properly.

## Build from source

Needs the .NET 6 SDK or newer, plus the game with MelonLoader and ModSettings installed.

```

## Notes for modding this game version

In The Long Dark 2.55 (Unity 6, MelonLoader 0.7.2) these crash the game or silently fail from a
mod, and the workarounds are worth knowing if you are writing your own:

- **Creating materials** - `new Material(...)` and `Instantiate(material)` both crash. Reuse an
  existing scene material and put your own textures on with a `MaterialPropertyBlock`.
- **`GUI.DrawTextureWithTexCoords`** crashes. Plain `GUI.DrawTexture` is fine.
- **Refreshing a ModSettings page that is not on screen** crashes. Only refresh from the main
  menu.
- **Unity's MP3 loading** is unavailable to mods. This mod decodes with NLayer on a background
  thread and builds the clip from raw samples.
- **The flashlight is aurora-gated in three separate places** - `GetNormalizedCharge`, `IsLit`
  and `EnableLights` - all inside native code, so writing the battery field from outside does
  nothing. All three are replaced, and only inside Slender games.
- **Save slot ids are reused.** A slot that held one game will be handed to the next, so a slot
  id alone never identifies a save.

## Licence and credits

The mod's code is MIT - see [LICENSE](LICENSE). Third-party notices and trademark information
are in [NOTICE.md](NOTICE.md).

- Code: SentinelDaniel, written with Claude :)
- MP3 decoding: [NLayer](https://github.com/naudio/NLayer), MIT
- The Slender Man character was created by Eric Knudsen ("Victor Surge")

This is an unofficial fan project, not affiliated with or endorsed by Eric Knudsen, Parsec
Productions, Blue Isle Studios, Midnight City, or Hinterland Studio. *The Long Dark* is a
trademark of Hinterland Studio Inc.
