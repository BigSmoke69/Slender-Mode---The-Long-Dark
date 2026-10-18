using ModSettings;

namespace SlenderMode
{
    /// <summary>Full settings page: Options > Mod Settings > Slender Mode (main menu and in-game).</summary>
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

        [Section("Hunts")]
        [Name("Daytime hunts")]
        [Description("Allow hunts during the day too, not only at night.")]
        public bool DayHunts = false;

        [Name("Minutes between hunts")]
        [Description("Average real-time minutes between hunts (it varies a bit each time).")]
        [Slider(2f, 60f, 59)]
        public float MinutesBetweenHunts = 12f;

        [Name("Hunt length (minutes)")]
        [Description("How long a hunt lasts if you survive it.")]
        [Slider(1f, 10f, 19)]
        public float HuntMinutes = 4f;

        [Name("Aggression")]
        [Description("How fast it closes in. 1 = normal, 2 = relentless.")]
        [Slider(0.5f, 2f, 16)]
        public float Aggression = 1f;

        [Name("Stare tolerance (seconds)")]
        [Description("How long you can look at it before it takes you. Shorter when it is close.")]
        [Slider(1f, 10f, 19)]
        public float StareSeconds = 4f;

        [Name("Fire keeps it away")]
        [Description("Standing next to a burning fire ends the hunt.")]
        public bool FireRepels = true;

        [Section("Sound and effects")]
        [Name("Ambience volume")]
        [Slider(0f, 1f, 21)]
        public float AmbienceVolume = 0.8f;

        [Name("Jumpscare volume")]
        [Description("Volume of the Slender: The Arrival stingers that play when he gets close.")]
        [Slider(0f, 1f, 21)]
        public float ScareVolume = 1f;

        [Name("Kill sound volume")]
        [Slider(0f, 1f, 21)]
        public float KillVolume = 1f;

        [Name("Static + heartbeat volume")]
        [Slider(0f, 1f, 21)]
        public float EffectsVolume = 0.7f;

        [Name("Screen static strength")]
        [Slider(0f, 1f, 21)]
        public float StaticStrength = 1f;

        [Name("Show distance")]
        [Description("Shows how far away it is, on the left of the screen, during a hunt.")]
        public bool ShowDistance = true;

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

        [Section("Testing")]
        [Name("Start a hunt now")]
        [Description("Each time you confirm with this on YES, a test hunt starts once you are outdoors in a game. Set back to NO when done testing.")]
        public bool TriggerNow = false;

        protected override void OnConfirm()
        {
            base.OnConfirm();
            SlenderMod.Instance?.OnSettingsConfirmed();
        }
    }

    /// <summary>Small section shown in Survival > Custom game setup.</summary>
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
