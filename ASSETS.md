All paths are inside `TheLongDark\Mods\SlenderMode\`.

## Sounds

Each folder takes `.mp3` or `.wav` files (**not** `.ogg`) and picks at random from whatever
is in it. Leave a folder empty and that sound simply never plays.

| Folder | What plays there | Suggested length |
|---|---|---|
| `sounds\ambience\` | The background track. A long one is split into 4 escalating sections. | 2-10 min |
| `sounds\kill\` | Plays when something catches you. | 3-9 s |
| `sounds\scare_big\` | Sting for when you turn and he is close. | 3-8 s |
| `sounds\scare_hit\` | Shorter, sharper hit. | 2-5 s |
| `sounds\scare_swell\` | Rising swell as he crosses into close range. | 3-8 s |
| `sounds\chaser\` | The chaser's scream as it starts running at you. | 2-6 s |
| `sounds\chase_music\` | Loops while the chaser is closing in. | 30 s - 2 min |

## Models

| Folder | Figure |
|---|---|
| `model\` | Slender. The newest `.obj` in the folder wins. |
| `model\chaser\` | The chaser. The newest `.obj` in that folder. |

**The chaser model ships with this repository** - it is CC BY 4.0, so redistributing it is
allowed as long as the credit travels with it. See `model\chaser\CREDITS.txt`.

The mod can play a baked vertex animation if it finds a `.anim` file next to the chaser's
`.obj`. There is no editor for this - it is a custom binary format produced offline from a
rigged model. Without one the chaser slides rather than runs, which still reads fine at
distance.

## Pages

`pages\page_1.jpg` through `page_8.jpg` are the eight page images. Anything roughly A4 and
readable at arm's length works; hand-drawn scans look best. Missing images fall back to a
blank sheet, so the mod still plays.

## Loading screen

`loading.jpg` replaces the background of the loading screen for Slender games only. Any
widescreen image works. If the file is absent the game's own loading art is used, and only
the intro text changes.

## Page spots

`pages\spots.txt` ships with 15 recorded spots on Coastal Highway, which is enough to play.
To record your own: **Mod Settings > Slender Mode > Page spot editor: YES**, then aim at a
tree, rock or wall within 4 m and press **F8** (**F9** removes the nearest). Each game picks
8 of them at random. Set the editor back to NO to play.
