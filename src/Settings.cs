using ModSettings;

namespace SlenderMode
{
    /// <summary>
    /// Options &gt; Mod Settings &gt; Slender Mode.
    /// The hunt, sound and chaser knobs are gone: a difficulty decides all of them now
    /// (see Difficulty.cs), and you pick it on the game-mode screen when you start a Slender game.
    /// </summary>
    internal sealed class SlenderSettings : JsonModSettings
    {
        public SlenderSettings() : base("SlenderMode/SlenderMode") { }

        [Section("Slender Mode")]
        [Name("Slender Mode")]
        [Description("Turns the creature hunts on or off.")]
        public bool Enabled = true;

        [Name("Real death")]
        [Description("YES: getting caught kills you for real (permadeath saves are lost). NO: you black out and wake up badly hurt.")]
        public bool RealDeath = false;

        [Section("Difficulty")]
        [Name("Difficulty")]
        [Description("Set when you start a Slender game, from the game-mode screen. Changing it here " +
                     "applies to the game you are playing: how soon he turns on you, how long you can " +
                     "stare, whether fire helps, and how early and how fast the chaser comes.")]
        [Choice(new string[] { "Easy", "Normal", "Hard", "The Arrival" })]
        public SlenderDifficultyChoice Difficulty = SlenderDifficultyChoice.Normal;

        [Section("Slender game")]
        [Name("Extra darkness")]
        [Description("Slender games: the Slender: The Arrival night look on top of the foggy night (cold blue darkness, dark lens edges, grain). Higher = darker. 0 = off.")]
        [Slider(0f, 0.8f, 17)]
        public float ExtraDarkness = 0.35f;

        [Section("Creature")]
        [Name("Use custom 3D model")]
        [Description("Use the .obj model in Mods/SlenderMode/model if there is one. NO = built-in creature.")]
        public bool UseCustomModel = true;

        [Name("Creature height (m)")]
        [Slider(2f, 5f, 31)]
        public float CreatureHeight = 3.0f;

        [Name("Model rotation fix")]
        [Description("If a custom model faces the wrong way, turn it here (degrees).")]
        [Slider(-180f, 180f, 9)]
        public float ModelYaw = 0f;

        [Section("Pages")]
        [Name("Page spot editor")]
        [Description("YES: walk around (any game in this region), aim at a tree, rock or wall and press F8 to pin a page spot there; F9 removes the nearest. Each Slender game picks 8 of your spots. Set back to NO to play.")]
        public bool PageEditor = false;

        [Section("Testing")]
        [Name("Show distance")]
        [Description("Shows how far away it is, on the left of the screen.")]
        public bool ShowDistance = true;

        [Name("Start a hunt now")]
        [Description("Each time you confirm with this on YES, a test hunt starts once you are outdoors in a game. Set back to NO when done testing.")]
        public bool TriggerNow = false;

        [Name("Send the chaser now")]
        [Description("Each time you confirm with this on YES, a chase starts once you are outdoors in a Slender game, whatever page you are on. Set back to NO when done testing.")]
        public bool ChaserNow = false;

        // A slider here showed the right handle position but always printed "0" beside it: the
        // number format is applied as a template rather than as a format specifier, so a plain
        // "0" came out as the literal character. A choice row has nothing to format and shows
        // exactly the label it is given.
        [Name("Start with pages collected")]
        [Description("Testing: a new Slender game begins with this many of its 8 pages already taken, so you can get to the later stages without finding them all. Confirming this during a Slender game also tops that game up to this many - it never takes pages back off you. 0 = off.")]
        [Choice(new string[] { "0", "1", "2", "3", "4", "5", "6", "7", "8" })]
        public StartPagesChoice StartPages = StartPagesChoice.None;

        [Name("Chaser test mode")]
        [Description("YES: he spawns 50 m straight in front of you and runs at you, over and over (a new one about 12 seconds after each chase ends). Works in any game, day or night, at any page. Set back to NO to play properly.")]
        public bool ChaserTestMode = false;

        protected override void OnConfirm()
        {
            base.OnConfirm();
            SlenderMod.Instance?.OnSettingsConfirmed();
        }
    }

    /// <summary>Small section shown in Survival &gt; Custom game setup.</summary>
    internal sealed class SlenderCustomModeSettings : ModSettingsBase
    {
        [Section("Slender Mode")]
        [Name("Slender Mode")]
        [Description("Something hunts you at night. All settings: Options > Mod Settings > Slender Mode.")]
        public bool Enabled = true;

        [Name("Real death")]
        [Description("YES: getting caught kills you for real. NO: you wake up badly hurt.")]
        public bool RealDeath = false;

        protected override void OnConfirm()
        {
            var s = SlenderMod.Settings;
            if (s == null) return;
            s.Enabled = Enabled;
            s.RealDeath = RealDeath;
            s.Save();
            // Only refresh the Mod Settings page while in the main menu, where it is guaranteed to exist.
            try { if (Il2Cpp.GameManager.IsMainMenuActive()) s.RefreshGUI(); } catch { }
        }
    }
}
