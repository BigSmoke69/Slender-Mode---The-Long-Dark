namespace SlenderMode
{
    /// <summary>Picked on the game-mode screen when a Slender game is started.</summary>
    internal enum SlenderDifficultyChoice
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
        TheArrival = 3,
    }

    /// <summary>Testing only: how many of the 8 pages a Slender game starts with already taken.</summary>
    internal enum StartPagesChoice
    {
        None = 0, One = 1, Two = 2, Three = 3, Four = 4,
        Five = 5, Six = 6, Seven = 7, All = 8,
    }

    /// <summary>Everything a difficulty decides. There are no per-knob settings any more.</summary>
    internal sealed class DifficultyLevel
    {
        public SlenderDifficultyChoice Id;
        public string Name;             // "Easy", shown beside the mode name
        public string Description;      // under it on the game-mode menu

        /// <summary>How long you can look at Slender before he takes you.</summary>
        public float StareSeconds;
        /// <summary>Standing by a burning fire ends his hunt.</summary>
        public bool FireRepels;
        /// <summary>How close the chaser has to be before the chase track comes in.</summary>
        public float ChaseMusicRange;
        /// <summary>The page you have to be hunting before the chaser starts turning up.</summary>
        public int ChaserFromPage;
        /// <summary>How fast the chaser runs. You sprint at about 4 m/s.</summary>
        public float ChaserSpeed;
        /// <summary>
        /// How long your light has to hold steady on the chaser's face to drive it off. This is
        /// what the chaser's difficulty actually comes down to: a cheap repel makes its speed
        /// irrelevant, because it never reaches you whatever speed it runs at.
        /// </summary>
        public float RepelHold;
        /// <summary>How far away the light still bites. Closer means letting it come to you first.</summary>
        public float RepelRange;

        /// <summary>Pages collected before Slender starts getting worse. Below this he stays calm.</summary>
        public int AggressionKnee;
        /// <summary>Overall multiplier on how fast he closes in.</summary>
        public float AggressionBase;
        /// <summary>Lowest pressure he is ever at, before any pages (The Arrival starts near the top).</summary>
        public float AggressionFloor;
    }

    internal static class Diff
    {
        /// <summary>What the mode is called in game.</summary>
        public const string ModeName = "SLENDER: THE EIGHT PAGES";

        public static readonly DifficultyLevel[] All =
        {
            new DifficultyLevel
            {
                Id = SlenderDifficultyChoice.Easy,
                Name = "Easy",
                Description = "He keeps his distance until the sixth page. You can stare for ten seconds, " +
                              "a fire still drives him off, and the chaser is slow enough to outrun.",
                StareSeconds = 10f, FireRepels = true, ChaseMusicRange = 40f,
                ChaserFromPage = 6, ChaserSpeed = 2.5f,
                RepelHold = 0.45f, RepelRange = 32f,
                AggressionKnee = 5, AggressionBase = 0.85f, AggressionFloor = 0f,
            },
            new DifficultyLevel
            {
                Id = SlenderDifficultyChoice.Normal,
                Name = "Normal",
                Description = "He turns on you from the fourth page. Eight seconds of staring, fire still " +
                              "works, and the chaser matches a hard run.",
                StareSeconds = 8f, FireRepels = true, ChaseMusicRange = 35f,
                ChaserFromPage = 4, ChaserSpeed = 2.9f,
                RepelHold = 0.8f, RepelRange = 28f,
                AggressionKnee = 3, AggressionBase = 1.0f, AggressionFloor = 0.15f,
            },
            new DifficultyLevel
            {
                Id = SlenderDifficultyChoice.Hard,
                Name = "Hard",
                Description = "Serious from the second page. Four seconds of staring, fire will not save you, " +
                              "and the chaser is faster than you are.",
                StareSeconds = 4f, FireRepels = false, ChaseMusicRange = 30f,
                ChaserFromPage = 2, ChaserSpeed = 3.3f,
                RepelHold = 1.4f, RepelRange = 22f,
                AggressionKnee = 1, AggressionBase = 1.15f, AggressionFloor = 0.40f,
            },
            new DifficultyLevel
            {
                Id = SlenderDifficultyChoice.TheArrival,
                Name = "The Arrival",
                Description = "No mercy from the first page. Three seconds of staring, no fire, and the chaser " +
                              "runs you down. Your light is the only thing that works.",
                StareSeconds = 3f, FireRepels = false, ChaseMusicRange = 25f,
                ChaserFromPage = 1, ChaserSpeed = 3.8f,
                RepelHold = 2.2f, RepelRange = 16f,
                AggressionKnee = 0, AggressionBase = 1.35f, AggressionFloor = 0.75f,
            },
        };

        public static DifficultyLevel Get(SlenderDifficultyChoice c)
        {
            int i = (int)c;
            return (i >= 0 && i < All.Length) ? All[i] : All[1];
        }

        public static DifficultyLevel Get(int i) => (i >= 0 && i < All.Length) ? All[i] : All[1];

        /// <summary>The level the game being played right now runs at.</summary>
        public static DifficultyLevel Current
        {
            get
            {
                try { return Get(SlenderGame.CurrentDifficulty); }
                catch { return All[1]; }
            }
        }

        // ---- fixed for every difficulty ----
        public const float AmbienceVolume = 1.0f;
        public const float ScareVolume = 1.0f;
        public const float KillVolume = 1.0f;
        public const float ChaseMusicVolume = 0.9f;
        public const float EffectsVolume = 0.8f;      // static + heartbeat
        public const float StaticStrength = 0.5f;
        public const float ChaserHeight = 1.75f;
        public const float ChaserYaw = 0f;
    }
}
