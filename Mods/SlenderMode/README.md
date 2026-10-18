# Slender Mode v0.2.0

Something hunts you in The Long Dark.

## How a hunt goes

1. At night (or anytime, if you allow daytime hunts), a hunt can start while you're outdoors.
   The ambience fades in quietly. That's your warning.
2. It appears far away, between the trees. **It only moves when you're not looking.**
   Each time you look away, it gets closer.
3. Looking at it fills the screen with static, and the closer it is, the faster that builds.
   Stare too long and it takes you.
4. If it reaches you while your back is turned, it takes you.
5. **Ways out:** go indoors, stand by a burning fire, or survive until the hunt ends.

The ambience follows the danger: the calm section while it's far away, rising through the
harsher sections as it closes in or as you stare, and easing back down when you get away.
A heartbeat gets louder and faster the closer it is. Its distance shows on the left of the
screen, turning red as it gets close.

**Being caught:** your kill sound plays, it's right in your face, then black.
- Real death OFF (default): you wake up in the snow with 25% condition.
- Real death ON: the game's normal death. Permadeath saves are lost.

## Settings

- **Options → Mod Settings → Slender Mode** (main menu and pause menu): everything, including
  "Start a hunt now" for testing. Every confirm with it on YES starts one test hunt, so set it back
  to NO when you're done.
- **Survival → Custom game:** a Slender Mode section to switch it on or off and choose real death.

## Files

| Where | What |
|---|---|
| `Mods\SlenderMode v.0.1.dll` | The mod |
| `Mods\SlenderMode\sounds\ambience\` | Hunt ambience (mp3 or wav). Tracks 2 minutes or longer are split into 4 sections, calm → scary. |
| `Mods\SlenderMode\sounds\kill\` | Kill sounds (mp3 or wav). A random one plays each time. |
| `Mods\SlenderMode\model\` | The creature: one `.obj` plus its color textures (`.jpg`/`.png`). Without it, the built-in creature is used. |
| `Mods\SlenderMode\SlenderMode.json` | Saved settings |

To remove the mod, delete `SlenderMode v.0.1.dll` and the `SlenderMode` folder.

## Safety net

If the game ever closes while the 3D model is being set up, the next launch notices, switches to
the built-in creature, and says so in `MelonLoader\Latest.log`. Set "Use custom 3D model" back to
YES to try again.

## Notes for modding this game version (found the hard way)

In this version of The Long Dark (Unity 6, MelonLoader 0.7.2), these crash the game or fail when
called from a mod:
- **Creating a material** (`new Material(...)` or `Instantiate(material)`): instant hard crash.
  The mod reuses an existing scene material and applies its textures with a `MaterialPropertyBlock`.
- **`GUI.DrawTextureWithTexCoords`**: hard crash. Use `GUI.DrawTexture` with an offset rect instead.
- **Refreshing a ModSettings page that isn't on screen** (e.g. the Custom game page during a game): hard crash.
- **Unity's MP3 loading** (`InternalCreateAudioClipUsingDH`, `DownloadHandlerAudioClip`): broken
  ("Method not found"). The mod decodes MP3s itself with the bundled NLayer decoder (MIT license).

To get the step-by-step trace log back for bug hunting, set `Diagnostics = true` in `SlenderMod.cs`
and rebuild.
