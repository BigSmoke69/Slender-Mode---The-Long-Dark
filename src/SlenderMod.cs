using System;
using System.IO;
using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using ModSettings;
using UnityEngine;

[assembly: MelonInfo(typeof(SlenderMode.SlenderMod), "Slender Mode", "0.16.0", "Danio")]
[assembly: MelonGame("Hinterland", "TheLongDark")]
[assembly: MelonAdditionalDependencies("ModSettings")]

namespace SlenderMode
{
    public sealed class SlenderMod : MelonMod
    {
        internal static SlenderMod Instance;
        internal static SlenderSettings Settings;
        static SlenderCustomModeSettings _customMode;

        enum State { Idle, Hunting, Killing, Waking, Ending }

        const float Deg = (float)(Math.PI / 180.0);
        const float KillReach = 9f;     // if it gets this close while you look away, it takes you
        const float GrabReach = 1.5f;   // get this close (about arm's length) and it grabs you instantly
        const float DangerHold = 6f;    // seconds before the music is allowed to calm down a level
        /// <summary>
        /// Inside this he is close enough to get into your head: the picture starts breaking up and
        /// holding your eyes on him fills the stare meter and ends with him taking you. Beyond it the
        /// screen stays clean and he will not be watched - see FarStareSeconds.
        /// Fixed for every difficulty; the difficulty decides how FAST the meter fills, not from where.
        /// </summary>
        const float StareRange = 50f;
        /// <summary>
        /// How long you can hold him in view from beyond StareRange before he is simply gone. Rolled
        /// fresh each time, so you can never count it out - sometimes he goes almost at once,
        /// sometimes he lets you look for a good while first.
        /// </summary>
        const float FarStareMin = 7f, FarStareMax = 15f;
        /// <summary>
        /// A branch, a bank of fog or the edge of the screen crossing him for an instant should not
        /// wipe the count - that is what made this unreliable at range. Sight has to be properly
        /// broken for this long before the count starts running back down.
        /// </summary>
        const float FarStareGrace = 0.45f;
        /// <summary>How often he answers a long look by appearing behind you instead of just leaving.</summary>
        const float BehindChance = 0.20f;
        const float BehindDist = 15f;
        /// <summary>
        /// How far one full chaser run cycle carries it, as a multiple of its height. Raising this
        /// slows the animation down for the same running speed - lower it and the legs start
        /// spinning, which reads as sprinting on the spot.
        /// </summary>
        const float ChaserStridePerHeight = 1.2f;

        string _root;
        SoundBank _sounds;
        AudioRig _audio;
        Visuals _vis;
        CreatureModel _model;
        CreatureModel _chaserModel;
        Chaser _chaser;
        readonly System.Random _rng = new System.Random();

        // the chaser
        bool _killByChaser, _chaserPending;
        bool _chaserModelProven, _chaserModelOff;   // _chaserModelOff: set by the crash guard only
        float _chaserModelShownAt = -1f;
        float _chaserPhase;            // where it is in its run cycle

        State _state = State.Idle;
        float _nextHuntAt, _huntEndsAt, _nextMoveAt, _stateT, _killLen;
        bool _forcedHunt, _triggerPending, _deathApplied, _playerLocked, _ambienceAssigned;
        Vector3 _pos;
        float _dist, _stare, _static;
        float _farStare;        // how long he has been watched from beyond StareRange
        float _farStareTarget;  // how long this particular look is allowed to last
        float _farStareLost;    // how long sight of him has actually been broken
        bool _lastActive;
        float _activeSince = -1f;
        bool _slenderScheduled;
        // jumpscare state
        float _scareReadyAt;          // shared cooldown so stingers never pile up
        bool _swellArmed = true;      // re-armed once he is far again
        bool _wasLooking;
        // Arrival-style jumpscare look: a glitch burst + camcorder zoom punch that decays
        float _burst, _burstZoom;     // burst 1 -> 0; zoom = how much narrower the view gets at the peak
        float _fovFactor = 1f, _fovBase;
        bool _fovActive, _fovBroken;
        // pages
        PageSystem _pages;
        float _finaleAt = -1f;            // after the last page: when he comes for you
        bool _finaleKill;                 // the current kill is the ending
        float _pagesCheckAt;
        bool _noSpotsWarned;
        readonly Vector3[] _smokeBase = new Vector3[PageSystem.PagesPerGame];
        readonly bool[] _smokeReady = new bool[PageSystem.PagesPerGame];
        readonly float[] _smokeVis = new float[PageSystem.PagesPerGame];
        readonly float[] _smokeTarget = new float[PageSystem.PagesPerGame];
        float _smokeCheckAt;
        int _smokeRound = -1;
        int _aimedPage = -1;
        bool _inputBroken;
        KeyCode _lastGuiKey; float _lastGuiKeyTime;
        string PagesGuardFile => Path.Combine(_root, "pages", "crashguard.txt");
        string FlatPagesFile => Path.Combine(_root, "pages", "flat_pages_on.txt");   // (older builds used flat_pages.txt: ignored now)
        float _traceUntil = -1f, _traceStart;
        /// <summary>Set to true to get the step-by-step trace log again (for bug hunting).</summary>
        internal const bool Diagnostics = false;
        const float TraceSeconds = Diagnostics ? 20f : 0f;

        /// <summary>Diagnostic: during the first seconds of a hunt, log before every call into the game.</summary>
        internal void T(string what)
        {
            float now = Time.unscaledTime;
            if (now < _traceUntil) LoggerInstance.Msg("[trace " + (now - _traceStart).ToString("0.000") + "] " + what);
        }
        bool _overlayDisabled, _overlayGuarded, _overlayGuardOpen;
        bool _modelProven;
        float _modelShownAt = -1f;
        string GuardFile => Path.Combine(_root, "crashguard.txt");
        bool _onScreen, _occluded, _looking;
        int _danger = -1;
        float _dangerDropAt;
        float _fadeBlack;
        string _caption;
        float _captionUntil;

        // =====================================================================
        // Setup
        // =====================================================================

        public override void OnInitializeMelon()
        {
            Instance = this;
            _root = Path.Combine(MelonEnvironment.ModsDirectory, "SlenderMode");
            Directory.CreateDirectory(Path.Combine(_root, "sounds", "kill"));
            Directory.CreateDirectory(Path.Combine(_root, "sounds", "ambience"));
            Directory.CreateDirectory(Path.Combine(_root, "model"));

            Settings = new SlenderSettings();
            Settings.AddToModSettings("Slender Mode", MenuType.Both);

            try
            {
                _customMode = new SlenderCustomModeSettings();
                _customMode.Enabled = Settings.Enabled;
                _customMode.RealDeath = Settings.RealDeath;
                _customMode.AddToCustomModeMenu(Position.BelowAll);
            }
            catch (Exception e)
            {
                LoggerInstance.Warning("Could not add to the Custom game menu (settings are still in Mod Settings): " + e.Message);
            }

            _sounds = new SoundBank(LoggerInstance);
            _sounds.LoadAll(Path.Combine(_root, "sounds"));
            _audio = new AudioRig();
            _vis = new Visuals();
            _model = new CreatureModel(LoggerInstance);
            _model.BeginLoad(Path.Combine(_root, "model"));
            _model.OnStep = step => { try { File.WriteAllText(GuardFile, step); } catch { } };
            _model.OnStepSettled = () => GuardEnd("model", true);

            // the chaser keeps its own model folder, so its textures never get mixed up with Slender's
            string chaserFolder = Path.Combine(_root, "model", "chaser");
            Directory.CreateDirectory(chaserFolder);
            _chaserModel = new CreatureModel(LoggerInstance) { Tag = "chaser" };
            _chaserModel.BeginLoad(chaserFolder);
            _chaserModel.OnStep = step => { try { File.WriteAllText(GuardFile, step); } catch { } };
            _chaserModel.OnStepSettled = () => GuardEnd("chaser", true);
            _chaser = new Chaser(LoggerInstance);

            CheckCrashGuard();
            SlenderGame.Init(LoggerInstance, _root);
            SlenderLoadingScreen.Init(_root);

            _pages = new PageSystem(LoggerInstance, _root);
            _pages.OnStep = step => { try { File.WriteAllText(PagesGuardFile, step); } catch { } };
            _pages.OnStepSettled = () => { try { File.Delete(PagesGuardFile); } catch { } };
            // Two strikes, the same way the loading image works. Quitting the game while the pages
            // are being built looks exactly like a crash from in here, and one of those should not
            // cost you real 3D pages for good: the flat fallback is drawn over the finished picture,
            // so it shows through walls and hillsides. It is a last resort, not somewhere to end up
            // by accident. Once 3D pages have run for a while the count is cleared again.
            _pages.OnProven = () => { try { File.Delete(FlatPagesFile); } catch { } };
            try
            {
                int strikes = 0;
                if (File.Exists(FlatPagesFile))
                {
                    // older builds wrote a sentence here rather than a count: that was one strike
                    if (!int.TryParse(File.ReadAllText(FlatPagesFile).Trim(), out strikes)) strikes = 1;
                }
                if (File.Exists(PagesGuardFile))
                {
                    string step = File.ReadAllText(PagesGuardFile).Trim();
                    File.Delete(PagesGuardFile);
                    strikes++;
                    File.WriteAllText(FlatPagesFile, strikes.ToString());
                    if (strikes >= 2)
                        LoggerInstance.Warning("The game closed twice while the 3D pages were being set up (" + step +
                            "). Pages are drawn flat from now on. Delete Mods\\SlenderMode\\pages\\flat_pages_on.txt to try 3D pages again.");
                    else
                        LoggerInstance.Msg("The game closed while the 3D pages were being set up (" + step +
                            "). If that was a crash and it happens again, pages fall back to flat ones.");
                }
                if (strikes >= 2) _pages.Disabled = true;
            }
            catch { }

            ScheduleNextHunt(0.5f, 1.0f);
            LoggerInstance.Msg("Slender: The Eight Pages v0.16.0 loaded. Settings: Options > Mod Settings > Slender Mode.");
        }

        public override void OnApplicationQuit()
        {
            // quitting normally is not a crash: close any open page setup step
            try { _pages?.Settle(); if (File.Exists(PagesGuardFile)) File.Delete(PagesGuardFile); } catch { }
            SlenderLoadingScreen.OnQuit();
        }

        internal void OnSettingsConfirmed()
        {
            try
            {
                if (_customMode != null)
                {
                    _customMode.Enabled = Settings.Enabled;
                    _customMode.RealDeath = Settings.RealDeath;
                    // The Custom game page only exists in the main menu. Refreshing it during a game
                    // touches destroyed menu objects and hard-crashes the game.
                    if (GameManager.IsMainMenuActive()) _customMode.RefreshGUI();
                }
            }
            catch { }

            // Every confirm with "Start a hunt now" on YES requests one test hunt.
            // (The setting is never changed from code: refreshing menus at the wrong time crashes the game.)
            if (Settings.TriggerNow)
            {
                _triggerPending = true;
                LoggerInstance.Msg("Test hunt requested (starts a few seconds after you are outdoors in a game).");
            }
            else if (_triggerPending)
            {
                _triggerPending = false;
                LoggerInstance.Msg("Test hunt cancelled.");
            }

            if (Settings.ChaserNow)
            {
                _chaserPending = true;
                LoggerInstance.Msg("Test chase requested (starts once you are outdoors in a Slender game).");
            }
            else if (_chaserPending)
            {
                _chaserPending = false;
                LoggerInstance.Msg("Test chase cancelled.");
            }

            if (Settings.ChaserTestMode)
            {
                _chaserPending = true;
                LoggerInstance.Msg("Chaser test mode ON: he spawns 50 m in front of you and keeps coming. Set it back to NO to play.");
            }

            // the difficulty picker applies to the game being played
            try { SlenderGame.SetCurrentDifficulty((int)Settings.Difficulty); } catch { }

            // confirming the page count while playing tops the current game up to it
            try
            {
                int head = (int)Settings.StartPages;
                if (head > 0 && SlenderGame.IsActive && _pages != null && _pages.HasPages)
                {
                    int before = _pages.Collected;
                    int got = _pages.GrantCollected(head);
                    if (got != before)
                    {
                        LoggerInstance.Msg("Testing: pages set to " + got + "/" + _pages.Total + ".");
                        Caption("Pages " + got + "/" + _pages.Total, 4f);
                    }
                }
            }
            catch { }

            if (!Settings.Enabled && _state == State.Hunting) EndHunt(false);
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            _slenderScheduled = false;
            if (_state == State.Hunting) EndHunt(true);
            // Ending counts too: left running, the ending would play itself out in the next scene.
            if (_state == State.Killing || _state == State.Waking || _state == State.Ending)
            {
                _finaleKill = false;
                _finaleAt = -1f;
                FinishSequence();
            }
            // The zoom punch holds the camera's own field of view to put back. That camera is gone
            // now, and writing its value onto the next scene's camera would change the view for good.
            _fovActive = false; _fovFactor = 1f; _burst = 0f; _burstZoom = 0f;
            SlenderGame.ForgetCurrentCheck();
            Patch_FL_NoFlicker.Forget();
            _noSpotsWarned = false;
            _model?.OnSceneChanged();
            _chaserModel?.OnSceneChanged();
            _chaser?.Stop();                 // it does not follow you through a door
            _pages?.OnSceneChanged();
            for (int i = 0; i < _smokeReady.Length; i++) { _smokeReady[i] = false; _smokeVis[i] = 0f; }
            _audio?.EnsureListener();
        }

        // ---- crash guard: a marker file exists only while a risky first-time step is running ----
        void CheckCrashGuard()
        {
            try
            {
                if (!File.Exists(GuardFile)) return;
                string step = File.ReadAllText(GuardFile).Trim();
                File.Delete(GuardFile);
                if (step.Length == 0) return;
                if (step.StartsWith("chaser"))
                {
                    LoggerInstance.Warning("Crash happened during chaser model step: " + step);
                    _chaserModelOff = true;
                    LoggerInstance.Warning("The game closed while the chaser's 3D model was being shown last time. " +
                        "For this session the chaser is a plain dark figure instead; restart the game to try the model again.");
                }
                else if (step.StartsWith("model"))
                {
                    LoggerInstance.Warning("Crash happened during model step: " + step);
                    Settings.UseCustomModel = false;
                    Settings.Save();
                    LoggerInstance.Warning("The game closed while the custom 3D model was being shown last time. " +
                        "Switched to the built-in creature. Set 'Use custom 3D model' to YES to try the model again.");
                }
                else if (step == "overlay")
                {
                    _overlayDisabled = true;
                    LoggerInstance.Warning("The game closed while the screen effects were being created last time. " +
                        "Screen static and the drawn creature are off for this session.");
                }
                else LoggerInstance.Warning("The game closed during step '" + step + "' last time.");
            }
            catch { }
        }

        void GuardBegin(string step)
        {
            try { File.WriteAllText(GuardFile, step); } catch { }
            if (Diagnostics) LoggerInstance.Msg("[step] begin " + step);
        }

        /// <summary>Clears the guard only if it still holds this step (or a sub-step of it).</summary>
        void GuardEnd(string step, bool quiet = false)
        {
            try
            {
                if (!File.Exists(GuardFile)) return;
                if (!File.ReadAllText(GuardFile).Trim().StartsWith(step)) return;
                File.Delete(GuardFile);
                if (!quiet && Diagnostics) LoggerInstance.Msg("[step] ok " + step);
            }
            catch { }
        }

        /// <summary>Shows the custom model (if enabled). Its setup runs in small logged steps; the crash
        /// guard holds the current step until the model has been on screen for 3 seconds.</summary>
        bool ShowModel(Vector3 feet, Vector3 lookAt)
        {
            if (!Settings.UseCustomModel || !_model.Ready) { _model.Show(false); return false; }
            bool shown = _model.Show(true);
            _model.Place(feet, lookAt, Settings.CreatureHeight, Settings.ModelYaw);
            if (shown)
            {
                // slow reach: arms rise and sink over ~7 s; the closer he is, the further he reaches
                float closeness = Mathf.Clamp01(1f - _dist / 40f);
                float wave = 0.5f - 0.5f * (float)Math.Cos(Time.unscaledTime * (2.0 * Math.PI / 7.0));
                float reach = 10f + (25f + 50f * closeness) * wave;          // degrees forward from hanging
                if (_state == State.Killing)                                  // caught: the hand comes up at your face
                    reach = 40f + 35f * Mathf.Clamp01(_stateT / 1.2f);
                _model.SetReach(reach);
            }
            if (shown && !_modelProven)
            {
                if (_modelShownAt < 0f) _modelShownAt = Time.unscaledTime;
                else if (Time.unscaledTime - _modelShownAt > 3f)
                {
                    _modelProven = true;
                    GuardEnd("model");
                }
            }
            return shown;
        }

        /// <summary>Shows the chaser's own model (its own folder, its own crash-guard step).</summary>
        bool ShowChaserModel(Vector3 feet, Vector3 lookAt)
        {
            if (_chaserModelOff || _chaserModel == null || !_chaserModel.Ready) { _chaserModel?.Show(false); return false; }
            bool shown = _chaserModel.Show(true);
            _chaserModel.Place(feet, lookAt, Diff.ChaserHeight, Diff.ChaserYaw);
            if (shown && !_chaserModelProven)
            {
                if (_chaserModelShownAt < 0f) _chaserModelShownAt = Time.unscaledTime;
                else if (Time.unscaledTime - _chaserModelShownAt > 3f)
                {
                    _chaserModelProven = true;
                    GuardEnd("chaser");
                }
            }
            return shown;
        }

        /// <summary>
        /// The chaser. Armed once you are far enough into the pages, then it comes on its own clock.
        /// Slender does not step back for it: both of them are on you at the same time, and the
        /// ambience is pinned at its worst section while the chase track plays over the top.
        /// </summary>
        void UpdateChaser(float now, float dt, Camera cam, Transform player)
        {
            if (_chaser == null) return;
            bool sg = SlenderGame.IsActive;
            bool test = Settings.ChaserTestMode;
            bool allowed = Settings.Enabled && !Settings.PageEditor
                           && _finaleAt < 0f && !_finaleKill
                           && (sg || test);               // test mode works in any game
            if (!allowed)
            {
                // Stopped whatever state it was in: armed but not yet visible still has to be called
                // off, or turning the mod back on would drop you straight into the chase it had queued.
                if (_chaser.State != Chaser.Mode.Off) { _chaser.Stop(); _chaserModel.Show(false); }
                return;
            }

            bool pagesReached = test || (_pages != null && Esc >= Math.Max(1, (int)Diff.Current.ChaserFromPage) - 1);

            if (_chaser.State == Chaser.Mode.Off)
            {
                _chaserModel.Show(false);
                if (_danger >= 0 && _state != State.Hunting) { _danger = -1; _audio.SetStage(-1, false); }
                if (test) _chaser.ArmIn(now, 12f);       // keep them coming while testing
                else if (pagesReached) _chaser.Arm(now, Pressure);
                bool outside = Outdoors() && (IsNight() || test);
                if (outside && (_chaserPending || (pagesReached && _chaser.WantsToStart(now))))
                {
                    _chaserPending = false;
                    _chaser.Start(now, player.position, cam, Pressure, Math.Max(1.5f, Diff.Current.ChaserSpeed), test, 50f);
                    OnChaserArrived(now);
                }
                return;
            }

            if (!Outdoors()) { _chaser.Stop(); _chaserModel.Show(false); return; }   // you got inside

            if (_chaser.Update(now, dt, cam, player, Pressure) == Chaser.Result.Caught)
            {
                _chaserModel.Show(false);
                BeginKill(cam, player, true);
                return;
            }

            if (_chaser.Visible)
            {
                // The cycle is driven by how far it actually moves rather than by a clock, so the
                // feet stay planted instead of skating. Stride is how far one full cycle (two
                // steps) carries it: the baked cycle swings the thighs about 36 degrees each way,
                // which on a figure this tall covers roughly 1.2 times its own height. Setting
                // this too short is what makes a run look like it is sprinting on the spot.
                float stride = Math.Max(0.5f, Diff.ChaserHeight * ChaserStridePerHeight);
                _chaserPhase += _chaser.CurrentSpeed * dt / stride;
                if (_chaserPhase > 100000f) _chaserPhase -= 100000f;
                ShowChaserModel(_chaser.Pos, player.position);
                if (_chaserModel.HasAnimation) _chaserModel.SetAnimPhase(_chaserPhase);
                // One owner for the ambience: the hunt. The chaser just pins it at its worst
                // section and the chase track plays over the top, so the two run together instead
                // of restarting each other every frame.
                SetDanger(3, now);
            }
            else _chaserModel.Show(false);
        }

        void OnChaserArrived(float now)
        {
            // Slender carries on with his own hunt: both of them are on you at once.
            if (!_overlayDisabled) { try { _vis.Ensure(); } catch { } }
            _audio.Ensure();
            _audio.StopScares();
            // only its own sound: Slender's jumpscare stingers are not used for the chaser,
            // and nothing breaks the picture up - the chase track carries it.
            var grp = _sounds.Chaser;
            if (grp.Count > 0) _audio.PlayScare(grp[_rng.Next(grp.Count)], Diff.ScareVolume);
            if (!_audio.HasChaseMusic) SetDanger(3, now);
            // no text: it arrives without explaining itself
        }

        void ScheduleNextHunt(float minFactor, float maxFactor)
        {
            // Slender games: he never stays away for long - under two minutes early on, well under
            // one once you are deep into the pages.
            if (SlenderGame.IsActive) { _nextHuntAt = Time.unscaledTime + Mathf.Lerp(90f, 25f, Pressure) * Rand(0.75f, 1.25f); return; }
            float mins = 12f;   // only reachable outside a Slender game
            _nextHuntAt = Time.unscaledTime + mins * 60f * Rand(minFactor, maxFactor);
        }

        // =====================================================================
        // Game state helpers
        // =====================================================================

        bool GameplayActive(out Camera cam, out Transform player)
        {
            cam = null; player = null;
            try
            {
                if (GameManager.IsMainMenuActive()) return false;
                if (GameManager.m_IsPaused) return false;
                var pm = GameManager.GetPlayerManagerComponent();
                if (pm == null || pm.PlayerIsDead()) return false;
                var rest = GameManager.GetRestComponent();
                if (rest != null && rest.IsSleeping()) return false;
                player = GameManager.GetPlayerTransform();
                cam = GameManager.GetMainCamera();
                return player != null && cam != null;
            }
            catch { return false; }
        }

        static bool Outdoors()
        {
            Instance?.T("weather: indoor check");
            try
            {
                var w = GameManager.GetWeatherComponent();
                return w != null && !w.IsIndoorScene() && !w.IsIndoorEnvironment();
            }
            catch { return false; }
        }

        static bool IsNight()
        {
            Instance?.T("time of day: night check");
            try { return GameManager.GetTimeOfDayComponent().IsNight(); } catch { return true; }
        }

        static bool NearFire(Vector3 p)
        {
            Instance?.T("fire manager: near fire check");
            try { return GameManager.GetFireManagerComponent().PointInRadiusOfBurningFire(p); } catch { return false; }
        }

        float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        void Caption(string text, float seconds)
        {
            _caption = text;
            _captionUntil = Time.unscaledTime + seconds;
        }

        // =====================================================================
        // Main loop
        // =====================================================================

        public override void OnUpdate()
        {
            T("frame: update begin");
            _sounds.Poll();
            _model.Poll();
            _chaserModel.Poll();
            if (!_ambienceAssigned && !_sounds.IsLoading)
            {
                _ambienceAssigned = true;
                _audio.Ensure();
                _audio.SetAmbienceClip(_sounds.Ambience.Count > 0 ? _sounds.Ambience[_rng.Next(_sounds.Ambience.Count)] : null);
                _audio.SetChaseClip(_sounds.ChaseMusic.Count > 0 ? _sounds.ChaseMusic[0] : null);
                if (_sounds.ChaseMusic.Count > 0) LoggerInstance.Msg("Chase music loaded.");
                if (_sounds.Ambience.Count == 0) LoggerInstance.Warning("No ambience sounds found in " + Path.Combine(_root, "sounds", "ambience"));
                if (_sounds.Kill.Count == 0) LoggerInstance.Warning("No kill sounds found in " + Path.Combine(_root, "sounds", "kill"));
            }

            float now = Time.unscaledTime;
            float dt = Math.Min(Time.unscaledDeltaTime, 0.1f);

            // jumpscare burst fades out over ~0.7 s; the zoom punch eases back with it
            _burst = Math.Max(0f, _burst - dt * 1.4f);
            float fov = 1f - _burstZoom * _burst * _burst;
            if (_state == State.Killing && !_deathApplied)
                fov = Math.Min(fov, 0.72f - 0.27f * Mathf.Clamp01(_stateT / Math.Max(0.5f, _killLen))); // pulls in on his face
            else if (_state != State.Hunting) fov = 1f;
            _fovFactor = fov;

            T("game: gameplay-active check");
            bool active = GameplayActive(out Camera cam, out Transform player);
            _lastActive = active;
            if (!active) _activeSince = -1f; else if (_activeSince < 0f) _activeSince = now;
            bool sequence = _state == State.Killing || _state == State.Waking || _state == State.Ending;
            if (active) UpdatePages(now, dt, cam, player);
            T("audio: pause state");
            _audio.SetPaused(!active && !sequence);

            if (!active && !sequence)
            {
                // freeze the clocks while paused / in menus / asleep
                _nextHuntAt += dt; _huntEndsAt += dt; _nextMoveAt += dt; if (_finaleAt > 0f) _finaleAt += dt;
                _chaser?.ShiftClock(dt);
                if (_state == State.Hunting) _model.Show(false);
                // The chaser's model is left alone: hiding it on pause made the drawing below
                // fall back to the flat stand-in figure behind the pause menu.
                return;
            }

            switch (_state)
            {
                case State.Idle: UpdateIdle(now, player); break;
                case State.Hunting: UpdateHunt(now, dt, cam, player); break;
                case State.Killing: UpdateKill(dt, cam); break;
                case State.Waking: UpdateWake(dt); break;
                case State.Ending: UpdateEnding(dt); break;
            }

            if (_state == State.Idle || _state == State.Hunting) UpdateChaser(now, dt, cam, player);

            // --- audio mix ---
            float closeness = _state == State.Hunting ? Mathf.Clamp01(1f - _dist / 45f) : 0f;
            float fx = Diff.EffectsVolume;
            float staticVol = _state == State.Hunting || _state == State.Killing ? _static * 0.6f * fx : 0f;
            float heartVol = _state == State.Hunting ? closeness * 0.9f * fx : 0f;
            float heartPitch = 0.85f + closeness * 0.9f + _stare * 0.4f;
            // The chaser adds no static or heartbeat of its own - it gets the chase track instead.
            // Anything still sounding here is Slender's, and he is allowed to be heard over it.
            float chaseMusic = 0f;
            if (_chaser != null && _chaser.Active && _chaser.Distance <= Diff.Current.ChaseMusicRange)
                chaseMusic = Diff.ChaseMusicVolume;
            // the ambience ducks under the chase track rather than stopping, so both are heard
            float ambVol = Diff.AmbienceVolume;
            if (chaseMusic > 0.01f) ambVol *= 0.45f;
            T("audio: mix (static " + staticVol.ToString("0.00") + ", heart " + heartVol.ToString("0.00") + ", stage " + _audio.Stage + ")");
            _audio.Update(dt, ambVol, staticVol, heartVol, heartPitch);
            _audio.UpdateChase(dt, chaseMusic);
            T("frame: update end");
        }

        // ---------------------------------------------------------------- Idle

        void UpdateIdle(float now, Transform player)
        {
            if (_activeSince < 0f || now - _activeSince < 5f) return; // let the game finish loading first

            // A new game started from the Slender menu entry becomes a Slender game.
            if (SlenderGame.TryClaimNewGame())
            {
                SlenderWorld.OnNewGame();
                _pages?.ForgetCurrentGame();     // reused save slot: the pages start at 0
                _chaser?.ResetForNewGame();
                // A test hunt or chase asked for on the menus was meant for the game you were in,
                // not the one you are starting now. Left queued it fires the moment you step
                // outside, which looks exactly like him hunting you from the first second.
                if (_triggerPending || _chaserPending)
                {
                    _triggerPending = false;
                    _chaserPending = false;
                    LoggerInstance.Msg("New Slender game: the queued test hunt/chase was dropped so it starts quiet. " +
                                       "Confirm the setting again to run one here.");
                }
                Caption(Diff.ModeName + "  -  Coastal Highway", 6f);
                _nextHuntAt = now + Rand(30f, 60f);          // first hunt soon after you arrive
            }
            if (SlenderGame.IsActive) SlenderWorld.Update(now);

            // after the last page: silence, then he comes for you (no normal hunts any more)
            if (_finaleAt > 0f) { if (now >= _finaleAt) StartFinale(); return; }

            if (!Settings.Enabled) return;

            if (_triggerPending)
            {
                if (Outdoors())
                {
                    _triggerPending = false;
                    StartHunt(now, player, true);
                }
                else if (_captionUntil < now) Caption("(Slender Mode: the test hunt starts when you go outside)", 4f);
                return;
            }

            // Scheduled hunts only happen in Slender games; normal Survival and Story games are untouched.
            // ("Start a hunt now" above still works anywhere, for testing.)
            if (!SlenderGame.IsActive) return;
            if (!_slenderScheduled) { _slenderScheduled = true; _nextHuntAt = Math.Min(_nextHuntAt, now + Rand(30f, 60f)); }
            if (now < _nextHuntAt) return;

            bool allowed = Outdoors() && !(Diff.Current.FireRepels && NearFire(player.position));
            if (allowed) StartHunt(now, player, false);
            else _nextHuntAt = now + 20f; // check again shortly
        }

        void StartHunt(float now, Transform player, bool forced)
        {
            _traceStart = Time.unscaledTime;
            _traceUntil = _traceStart + TraceSeconds;
            T("hunt start");
            _audio.Ensure();
            if (!_overlayDisabled && !_overlayGuarded)
            {
                GuardBegin("overlay");
                _overlayGuardOpen = true;
                try { _vis.Ensure(); } catch (Exception e) { _overlayDisabled = true; LoggerInstance.Error("Screen effects disabled: " + e.Message); }
                _overlayGuarded = true; // finished after the first drawn frame (OnGUI)
            }
            _state = State.Hunting;
            _forcedHunt = forced;
            // Slender games: the hunt never times out (he haunts you non-stop). Normal games: hunt length setting.
            _huntEndsAt = SlenderGame.IsActive ? float.MaxValue : now + 240f;   // Slender games never end a hunt; 4 min elsewhere
            _stare = 0f; _static = 0f; _danger = -1;
            _farStare = 0f; _farStareLost = 0f; _farStareTarget = Rand(FarStareMin, FarStareMax);
            _swellArmed = true; _wasLooking = false; _scareReadyAt = 0f;
            var cam = GameManager.GetMainCamera();
            T("place creature");
            PlaceAround(player.position, cam, SlenderGame.IsActive ? Mathf.Lerp(85f, 55f, Pressure) + Rand(-8f, 8f) : Rand(55f, 75f), true);
            _nextMoveAt = now + Rand(7f, 11f) / Agg;
            SetDanger(0, now);
            LoggerInstance.Msg(forced ? "Test hunt started." : "A hunt has started.");
        }

        void EndHunt(bool quiet)
        {
            if (!_modelProven) GuardEnd("model", true);
            _state = State.Idle;
            _model.Show(false);
            _audio.SetStage(-1, false);
            _danger = -1;
            _stare = 0f; _static = 0f; _farStare = 0f; _farStareLost = 0f;
            _forcedHunt = false; _burst = 0f;
            ScheduleNextHunt(0.6f, 1.4f);
            if (!quiet) LoggerInstance.Msg("The hunt is over.");
        }

        // ---------------------------------------------------------------- Hunting

        void UpdateHunt(float now, float dt, Camera cam, Transform player)
        {
            Vector3 pp = player.position;
            Vector3 cp = cam.transform.position;

            if (!Settings.Enabled || !Outdoors()) { EndHunt(false); return; }
            if (Diff.Current.FireRepels && NearFire(pp))
            {
                EndHunt(false);
                Caption("The fire keeps it at bay.", 4f);
                return;
            }
            if (!_forcedHunt && !IsNight()) { EndHunt(false); return; }
            if (now >= _huntEndsAt) { EndHunt(false); return; }

            _dist = Flat(pp - _pos);
            if (_dist > 160f) PlaceAround(pp, cam, Rand(60f, 75f), true); // you ran / travelled far: it follows
            // walk right up to him and he grabs you, looking or not
            if (_dist <= GrabReach) { T("grab: player walked into it"); BeginKill(cam, player); return; }

            float h = Settings.CreatureHeight;
            T("visibility (dist " + _dist.ToString("0") + ")");
            ComputeVisibility(cam, cp, h);
            // Jumpscares only when he is in front of you: once each time he comes into view, then a long cooldown.
            if (_looking && !_wasLooking && _dist < 35f)
            {
                if (_dist < 20f)
                {
                    // he's right there: the sting, and he blinks in at you while the camera breaks up
                    if (Scare(_sounds.ScareBig, now, 12f))
                    {
                        float nd = Math.Max(KillReach + 2.5f, _dist * 0.55f);
                        bool lunge = !SlenderGame.IsActive || Esc >= 2;     // early pages: the sting, but he stays put
                        if (lunge && nd < _dist - 1f) { PlaceAround(pp, cam, nd, false); _dist = Flat(pp - _pos); }
                        Burst(1f, 0.32f);
                    }
                }
                else if (Scare(_sounds.ScareSwell, now, 12f)) Burst(0.55f, 0.15f); // he's in view and close
            }
            _wasLooking = _looking;
            T("visibility done: onScreen " + _onScreen + ", occluded " + _occluded + ", looking " + _looking + ", stare " + _stare.ToString("0.00"));

            // --- staring ---
            // Within StareRange the meter fills and ends with him taking you. Beyond it he does not
            // close in on a stare: hold him in view for FarStareSeconds and the picture tears for an
            // instant and he is no longer standing where he was.
            if (_looking && _dist <= StareRange)
            {
                _farStare = 0f; _farStareLost = 0f;
                float closeBoost = 1f + Mathf.Clamp01((30f - _dist) / 30f);
                _stare += dt / Math.Max(0.5f, Diff.Current.StareSeconds * (SlenderGame.IsActive ? Mathf.Lerp(1.6f, 0.75f, Pressure) : 1f)) * closeBoost;
            }
            else if (_looking)
            {
                _stare = Math.Max(0f, _stare - dt * 0.3f);
                _farStareLost = 0f;
                if (_farStare <= 0f) _farStareTarget = Rand(FarStareMin, FarStareMax);
                _farStare += dt;
                if (_farStare >= _farStareTarget) { VanishFromView(cam, pp); return; }
            }
            else
            {
                _stare = Math.Max(0f, _stare - dt * 0.3f);
                // only once sight is properly broken, not for a flicker through branches or fog
                _farStareLost += dt;
                if (_farStareLost > FarStareGrace) _farStare = Math.Max(0f, _farStare - dt * 0.5f);
            }
            if (_stare >= 1f) { BeginKill(cam, player); return; }

            // --- it only moves while you are not looking ---
            if (now >= _nextMoveAt)
            {
                _nextMoveAt = now + Rand(5f, 9f) / Agg;
                if (!_looking)
                {
                    if (_dist <= KillReach) { BeginKill(cam, player); return; }
                    float step = Rand(7f, 13f) * Agg;
                    float nd = Math.Max(KillReach - 1f, _dist - step);
                    bool inFront = _rng.NextDouble() < (SlenderGame.IsActive ? Mathf.Lerp(0.10f, 0.35f, Pressure) : 0.22f) && nd > 18f; // sometimes it waits where you will see it
                    T("move: " + (inFront ? "in front" : "behind") + " at " + nd.ToString("0") + " m");
                    PlaceAround(pp, cam, nd, !inFront);
                    _dist = Flat(pp - _pos);
                    // (no sound while he's behind you: jumpscares only play when you can see him)
                }
            }

            // --- crossing into close range: a rising swell (re-armed when he is far again) ---
            // he appears in front of you after moving closer -> short sharp hit (only if you're looking)
            if (_swellArmed && _looking && _dist < 30f) { _swellArmed = false; if (Scare(_sounds.ScareHit, now, 12f)) Burst(0.8f, 0.22f); }
            else if (!_swellArmed && !_looking) _swellArmed = true;

            // --- danger level drives the ambience sections ---
            int d = _dist > 45f ? 0 : _dist > 25f ? 1 : _dist > 12f ? 2 : 3;
            d = Math.Max(d, Esc >= 6 ? 2 : Esc >= 3 ? 1 : 0);   // more pages, scarier music
            if (_stare > 0.55f) d = 3;
            else if (_stare > 0.2f) d = Math.Max(d, 2);
            else if (_looking) d = Math.Max(d, 1);
            T("danger level " + d);
            SetDanger(d, now);

            // --- picture interference ---
            // The screen only starts breaking up once he is inside StareRange, and then only as much
            // as his closeness warrants: nothing at 50 m, everything at 10. The stare meter is a
            // separate thing on top - that is the warning he is about to take you. Both ease in
            // rather than snapping on: slowly at the edge of the range, almost instantly up close.
            // Further out than that the picture is clean, and the only interference you will ever
            // see is the single flash when he decides he has been watched long enough.
            float near = Mathf.Clamp01((StareRange - _dist) / 40f);          // 0 at 50 m, 1 at 10 m
            float inRange = Mathf.Clamp01((StareRange + 4f - _dist) / 8f);   // gone by ~54 m, no hard edge
            float want = (_looking ? 0.14f * near + 0.86f * _stare : _stare * 0.5f) * inRange;
            float fade = want > _static ? Mathf.Lerp(2.4f, 0.18f, near) : 1.1f;
            _static = Mathf.MoveTowards(_static, want, dt / Math.Max(0.05f, fade));

            // --- the 3D model, if there is one ---
            T("model show/hide");
            ShowModel(_pos, pp);
        }

        /// <summary>
        /// Watched from beyond StareRange for FarStareSeconds: the picture tears for a moment and
        /// when it settles he is not there any more. No kill and no warning - at that distance he
        /// simply will not be looked at, and you are left unsure whether you saw him at all.
        /// </summary>
        void VanishFromView(Camera cam, Vector3 playerPos)
        {
            _farStare = 0f;
            _farStareLost = 0f;
            _farStareTarget = Rand(FarStareMin, FarStareMax);   // a fresh count for the next look
            _stare = 0f;
            Burst(0.75f, 0.05f);                      // the flash itself
            _static = Math.Max(_static, 0.55f);       // which dies away over the next half second
            _wasLooking = false;
            _swellArmed = true;

            // Usually he just gives up the spot and goes - somewhere out of sight, no closer than
            // he was. Now and then he answers the staring instead, and when the picture clears he
            // is standing right behind you.
            bool behind = _rng.NextDouble() < BehindChance;
            if (behind)
            {
                PlaceBehind(playerPos, cam, BehindDist);
                _nextMoveAt = Time.unscaledTime + Rand(6f, 10f) / Agg;   // and he waits there
            }
            else
            {
                PlaceAround(playerPos, cam, Math.Max(StareRange + 12f, _dist), true);
                _nextMoveAt = Time.unscaledTime + Rand(4f, 7f) / Agg;
            }
            _dist = Flat(playerPos - _pos);
            T(behind ? "far stare: moved in behind you" : "far stare: moved out of view");
        }

        /// <summary>Puts him squarely behind you. Turn around and he is simply there.</summary>
        void PlaceBehind(Vector3 playerPos, Camera cam, float dist)
        {
            Vector3 f = cam != null ? cam.transform.forward : Vector3.forward;
            f.y = 0f;
            float baseYaw = (float)Math.Atan2(f.x, f.z) / Deg;
            float yaw = (baseYaw + 180f + Rand(-18f, 18f)) * Deg;   // behind, not surgically so
            Vector3 p = playerPos + new Vector3((float)Math.Sin(yaw), 0f, (float)Math.Cos(yaw)) * dist;
            T("physics: ground raycast");
            p.y = GroundY(p, playerPos.y);
            _pos = p;
        }

        /// <summary>Plays a random jumpscare from the group, respecting a shared cooldown.</summary>
        bool Scare(System.Collections.Generic.List<AudioClip> group, float now, float cooldown)
        {
            if (group == null || group.Count == 0 || now < _scareReadyAt) return false;
            _scareReadyAt = now + cooldown;
            T("audio: jumpscare");
            _audio.PlayScare(group[_rng.Next(group.Count)], Diff.ScareVolume);
            return true;
        }

        /// <summary>Starts the visual side of a jumpscare: camcorder breakup + a quick zoom punch.</summary>
        void Burst(float strength, float zoom)
        {
            if (strength >= _burst) { _burst = strength; _burstZoom = zoom; }
        }

        /// <summary>Applies the zoom punch to the game camera after the game has positioned it.</summary>
        public override void OnLateUpdate()
        {
            if (_fovBroken || (_fovFactor > 0.999f && !_fovActive)) return;
            // menus and the main screen have their own camera: leave it alone
            if (!_lastActive && !_fovActive) return;
            try
            {
                var cam = GameManager.GetMainCamera();
                if (cam == null) return;
                if (_fovFactor < 0.999f)
                {
                    if (!_fovActive) { _fovBase = cam.fieldOfView; _fovActive = true; }
                    cam.fieldOfView = _fovBase * _fovFactor;
                }
                else
                {
                    cam.fieldOfView = _fovBase;
                    _fovActive = false;
                }
            }
            catch (Exception e) { _fovBroken = true; _fovActive = false; LoggerInstance.Warning("Camera zoom disabled: " + e.Message); }
        }

        void SetDanger(int d, float now)
        {
            if (d > _danger)
            {
                _danger = d;
                _dangerDropAt = now + DangerHold;
                _audio.SetStage(d, true);                       // spike: start at the section's opening hit
            }
            else if (d < _danger)
            {
                if (now >= _dangerDropAt)
                {
                    _danger--;
                    _dangerDropAt = now + DangerHold;
                    _audio.SetStage(_danger, false);            // ease down, skipping the hit
                }
            }
            else _dangerDropAt = now + DangerHold;
        }

        void ComputeVisibility(Camera cam, Vector3 cp, float h)
        {
            Vector3 chest = _pos + Vector3.up * (h * 0.6f);
            Vector3 head = _pos + Vector3.up * (h * 0.9f);
            T("camera: world to viewport");
            Vector3 vp = cam.WorldToViewportPoint(chest);
            _onScreen = vp.z > 0f && vp.x > 0.08f && vp.x < 0.92f && vp.y > -0.05f && vp.y < 1.05f;
            T("physics: raycasts to creature");
            _occluded = Blocked(cp, chest) && Blocked(cp, head);
            _looking = _onScreen && !_occluded && _dist < 130f;
        }

        static bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1.5f) return false;
            Vector3 dir = d / len;
            try
            {
                return Physics.Raycast(from + dir * 0.7f, dir, len - 1.4f, -1, QueryTriggerInteraction.Ignore);
            }
            catch { return false; }
        }

        void PlaceAround(Vector3 playerPos, Camera cam, float dist, bool outOfView)
        {
            Vector3 f = cam != null ? cam.transform.forward : Vector3.forward;
            f.y = 0f;
            float baseYaw = (float)Math.Atan2(f.x, f.z) / Deg;
            float off = outOfView ? Rand(110f, 250f) : Rand(-25f, 25f);
            float yaw = (baseYaw + off) * Deg;
            Vector3 p = playerPos + new Vector3((float)Math.Sin(yaw), 0f, (float)Math.Cos(yaw)) * dist;
            T("physics: ground raycast");
            p.y = GroundY(p, playerPos.y);
            _pos = p;
        }

        /// <summary>Finds the terrain under p (skips tree tops and rocks where it can).</summary>
        internal static float GroundY(Vector3 p, float refY)
        {
            float y = refY;
            try
            {
                Vector3 origin = new Vector3(p.x, refY + 60f, p.z);
                for (int i = 0; i < 5; i++)
                {
                    if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f, -1, QueryTriggerInteraction.Ignore)) break;
                    y = hit.point.y;
                    var col = hit.collider;
                    if (col != null && col.TryCast<TerrainCollider>() != null) break;
                    origin = hit.point + Vector3.down * 0.05f; // look underneath whatever we hit
                }
            }
            catch { }
            return y;
        }

        static float Flat(Vector3 v) => (float)Math.Sqrt(v.x * v.x + v.z * v.z);

        // ---------------------------------------------------------------- Kill

        void BeginKill(Camera cam, Transform player, bool byChaser = false)
        {
            _killByChaser = byChaser;
            _chaser?.Stop();        // whoever got you, the chase is over
            // If Slender got you while the chaser was still running at you, the chaser's model would
            // otherwise stand frozen in the middle of the shot for the whole kill.
            if (!byChaser) _chaserModel?.Show(false);
            _state = State.Killing;
            _stateT = 0f;
            _deathApplied = false;
            _fadeBlack = 0f;
            _static = 1f;
            Burst(1f, 0f);

            AudioClip clip = _sounds.Kill.Count > 0 ? _sounds.Kill[_rng.Next(_sounds.Kill.Count)] : null;
            _killLen = clip != null ? Mathf.Clamp(clip.length, 2.5f, 9f) : 3f;
            _audio.SetStage(-1, false);
            _audio.StopScares();
            _audio.PlayKill(clip, Diff.KillVolume);

            // it is suddenly right in front of you
            Vector3 cp = cam.transform.position;
            Vector3 f = cam.transform.forward; f.y = 0f;
            float fl = Flat(f); if (fl < 0.01f) f = Vector3.forward; else f = f / fl;
            float h = byChaser ? Diff.ChaserHeight : Settings.CreatureHeight;
            _pos = new Vector3(cp.x, cp.y - h * 0.9f, cp.z) + f * (byChaser ? 0.9f : 1.3f);

            LockPlayer(true);
            LoggerInstance.Msg(byChaser ? "The chaser got you." : "Caught.");
        }

        void UpdateKill(float dt, Camera cam)
        {
            _stateT += dt;
            float t = _stateT;

            if (cam != null)
            {
                if (_killByChaser) ShowChaserModel(_pos, cam.transform.position);
                else ShowModel(_pos, cam.transform.position);
            }

            // the camcorder breaks up more and more, then the signal dies: full static, hard cut to black
            float p = Mathf.Clamp01(t / _killLen);
            _static = Math.Min(1f, 0.45f + 0.45f * p * p + 0.15f * (float)Math.Abs(Math.Sin(t * 17f)));
            if (t > _killLen - 0.45f) _static = 1f;
            _fadeBlack = t >= _killLen - 0.1f ? 1f : 0f;

            if (t >= _killLen && !_deathApplied)
            {
                _deathApplied = true;
                _model.Show(false);
                _chaserModel.Show(false);
                _audio.StopKill();
                if (_finaleKill) { _state = State.Ending; _stateT = 0f; _fadeBlack = 1f; _static = 0f; return; }
                ApplyOutcome();
            }
        }

        void ApplyOutcome()
        {
            Condition c = null;
            try { c = GameManager.GetConditionComponent(); } catch { }

            if (Settings.RealDeath)
            {
                LockPlayer(false);
                _state = State.Idle;
                _fadeBlack = 0f;
                _static = 0f;
                try { c?.KillPlayer(DamageSource.Fear, "Taken in the dark"); }
                catch
                {
                    try { c?.AddHealth(-(c.m_CurrentHP + 100f), DamageSource.Fear); } catch { }
                }
                ScheduleNextHunt(1.0f, 1.5f);
                return;
            }

            // scare-only: you black out and wake up badly hurt
            try
            {
                if (c != null)
                {
                    float target = Math.Max(1f, c.m_MaxHP * 0.25f);
                    if (c.m_CurrentHP > target) c.AddHealthWithNoHudNotification(target - c.m_CurrentHP, DamageSource.Fear);
                }
            }
            catch { }
            _state = State.Waking;
            _stateT = 0f;
            _static = 0f;
            Caption("You wake up in the snow. It let you go... this time.", 7f);
        }

        // ---------------------------------------------------------------- Pages

        /// <summary>Pages taken in this Slender game: each one makes him worse.</summary>
        int Esc => SlenderGame.IsActive && _pages != null ? _pages.Collected : 0;
        /// <summary>
        /// 0 = barely interested, 1 = everything he has. The difficulty decides how many pages you
        /// get before the climb even starts (the knee), and how high it begins: The Arrival opens
        /// near the top, Easy leaves him calm until the sixth page.
        /// </summary>
        float Pressure
        {
            get
            {
                var d = Diff.Current;
                float span = Math.Max(1f, 7f - d.AggressionKnee);
                float p = Mathf.Clamp01((Esc - d.AggressionKnee) / span);
                return Mathf.Clamp01(Math.Max(d.AggressionFloor, p));
            }
        }
        /// <summary>Speed multiplier. Slender game: slow distant stalking at first (0.4x), 1.3x by page 7.</summary>
        float Agg => Diff.Current.AggressionBase * (SlenderGame.IsActive ? Mathf.Lerp(0.4f, 1.3f, Pressure * Pressure * 0.4f + Pressure * 0.6f) : 1f);

        bool _keyE, _keyF8, _keyF9;   // from the GUI-event fallback

        void UpdatePages(float now, float dt, Camera cam, Transform player)
        {
            bool sg = SlenderGame.IsActive;
            bool editor = Settings.PageEditor;
            bool settled = _activeSince >= 0f && now - _activeSince > 5f;

            // a Slender save gets its pages once spots exist for this region
            if (sg && settled && !_pages.HasPages && now >= _pagesCheckAt)
            {
                _pagesCheckAt = now + 5f;
                string msg = _pages.EnsureForCurrentGame();
                // testing: hand the new game however many pages the settings ask for
                int head = (int)Settings.StartPages;
                if (head > 0 && _pages.HasPages)
                {
                    int got = _pages.GrantCollected(head);
                    if (got > 0) LoggerInstance.Msg("Testing: started this game with " + got + "/" + _pages.Total + " pages.");
                }
                if (msg != null) Caption(msg, 8f);
                else if (_pages.HasPages) Caption("Find the " + _pages.Total + " pages.", 6f);
                else if (!_noSpotsWarned && !editor)
                {
                    _noSpotsWarned = true;
                    Caption("No page spots recorded for this region yet. Turn on the page spot editor in Mod Settings.", 9f);
                }
            }

            if (settled) _pages.Update(sg, editor);

            // all pages taken (also after loading a save): he comes for you
            if (sg && _pages.AllCollected && _finaleAt < 0f && !_finaleKill && _state == State.Idle && !editor)
                _finaleAt = now + Rand(9f, 14f);

            // keys
            bool e = false, f8 = false, f9 = false;
            if (!_inputBroken)
            {
                try { e = Input.GetKeyDown(KeyCode.E); f8 = Input.GetKeyDown(KeyCode.F8); f9 = Input.GetKeyDown(KeyCode.F9); }
                catch (Exception ex) { _inputBroken = true; LoggerInstance.Warning("Legacy input unavailable (" + ex.Message + "), reading keys from GUI events."); }
            }
            e |= _keyE; f8 |= _keyF8; f9 |= _keyF9;
            _keyE = _keyF8 = _keyF9 = false;

            if (editor)
            {
                _aimedPage = -1;
                if (f8) Caption(_pages.AddSpot(cam), 4f);
                if (f9) Caption(_pages.RemoveNearestSpot(player.position), 4f);
                return;
            }

            bool canTake = sg && _pages.HasPages && _finaleAt < 0f && (_state == State.Idle || _state == State.Hunting);
            _aimedPage = canTake ? _pages.Aimed(cam) : -1;
            if (canTake && e && _aimedPage >= 0)
            {
                int got = _pages.TryTake(cam);
                if (got > 0) OnPageTaken(now, got);
            }

            if (sg) UpdateSmoke(now, dt, cam);
        }

        void OnPageTaken(float now, int got)
        {
            _aimedPage = -1;
            int total = _pages.Total;
            LoggerInstance.Msg("Page taken: " + got + "/" + total);
            Caption("Pages " + got + "/" + total, 4f);
            Burst(0.35f, 0f);
            if (got >= total)
            {
                // the last page: everything goes quiet... then he takes you
                if (_state == State.Hunting) EndHunt(true);
                _audio.StopScares();
                _audio.SetStage(-1, false);
                _finaleAt = now + Rand(9f, 14f);
            }
            else if (_state == State.Idle)
                _nextHuntAt = Math.Min(_nextHuntAt, now + Rand(8f, 16f));   // he noticed
        }

        void StartFinale()
        {
            _finaleAt = -1f;
            Camera cam = null; Transform player = null;
            if (!GameplayActive(out cam, out player)) { _finaleAt = Time.unscaledTime + 3f; return; }
            LoggerInstance.Msg("All pages taken. The ending.");
            _finaleKill = true;
            _audio.Ensure();
            if (!_overlayDisabled) { try { _vis.Ensure(); } catch { } }
            BeginKill(cam, player);
        }

        void UpdateSmoke(float now, float dt, Camera cam)
        {
            if (_pages.Round != _smokeRound) { _smokeRound = _pages.Round; for (int i = 0; i < _smokeReady.Length; i++) _smokeReady[i] = false; }
            bool check = now >= _smokeCheckAt;
            if (check) _smokeCheckAt = now + 0.3f;
            Vector3 cp = cam.transform.position;
            foreach (var kv in _pages.OpenPages())
            {
                int i = kv.Key; if (i >= _smokeReady.Length) continue;
                var s = kv.Value;
                if (!_smokeReady[i])
                {
                    // next to the page, out from the tree/wall, on the ground
                    Vector3 n = s.Normal; n.y = 0f;
                    n = n.sqrMagnitude > 0.01f ? n.normalized : Vector3.forward;
                    Vector3 side = Vector3.Cross(Vector3.up, n);
                    Vector3 b = s.Pos + n * 1.6f + side * 1.8f;
                    b.y = GroundY(b, s.Pos.y);
                    _smokeBase[i] = b; _smokeReady[i] = true;
                    _smokeVis[i] = 0f; _smokeTarget[i] = 1f;
                }
                if (check)
                {
                    Vector3 bs = _smokeBase[i];
                    bool seen = !Blocked(cp, bs + Vector3.up * 3f) || !Blocked(cp, bs + Vector3.up * 9f) || !Blocked(cp, bs + Vector3.up * 15f);
                    _smokeTarget[i] = seen ? 1f : 0f;
                }
                _smokeVis[i] = Mathf.MoveTowards(_smokeVis[i], _smokeTarget[i], dt * 1.5f);
            }
        }

        /// <summary>Smoke over the pages, flat pages (fallback), editor labels.</summary>
        void DrawWorldLayer(Camera cam)
        {
            try
            {
                bool editor = Settings.PageEditor;
                if (editor)
                {
                    Vector3 cp = cam.transform.position;
                    int k = 0;
                    foreach (var s in _pages.Spots)
                    {
                        k++;
                        if (s.Scene != PageSystem.Scene()) continue;
                        _vis.DrawSpotLabel(cam, s.Pos, "page spot " + k + "  (" + Vector3.Distance(cp, s.Pos).ToString("0") + " m)");
                    }
                    _vis.DrawHint("PAGE SPOT EDITOR   F8: pin a spot where you aim   F9: remove nearest   (" + _pages.CountInScene() + " here)");
                }
                else if (SlenderGame.IsActive)
                {
                    foreach (var kv in _pages.OpenPages())
                    {
                        int i = kv.Key;
                        if (i < _smokeReady.Length && _smokeReady[i]) _vis.DrawSmoke(cam, _smokeBase[i], i, _smokeVis[i]);
                    }
                }
                // The flat fallback is painted over the finished picture, so on its own it shows
                // straight through walls and hillsides. Only draw one you could actually see.
                Vector3 eye = cam.transform.position;
                foreach (var s in _pages.FlatPages(editor))
                {
                    if (Blocked(eye, s.Pos + Vector3.up * 0.08f)) continue;
                    _vis.DrawPageSprite(cam, s.Pos, _pages.PageTexture(s.Art));
                }
            }
            catch (Exception e) { LoggerInstance.Warning("Page drawing error: " + e.Message); }
        }

        void HandleGuiKeyEvent()
        {
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.KeyDown) return;
            KeyCode k = ev.keyCode;
            if (k != KeyCode.E && k != KeyCode.F8 && k != KeyCode.F9) return;
            if (k == _lastGuiKey && Time.unscaledTime - _lastGuiKeyTime < 0.3f) return;
            _lastGuiKey = k; _lastGuiKeyTime = Time.unscaledTime;
            if (k == KeyCode.E) _keyE = true; else if (k == KeyCode.F8) _keyF8 = true; else _keyF9 = true;
        }

        /// <summary>The ending card after the last page, then the outcome and a fresh set of pages.</summary>
        void UpdateEnding(float dt)
        {
            _stateT += dt;
            _fadeBlack = 1f;
            if (_stateT < 9f) return;
            _finaleKill = false;
            _finaleAt = -1f;
            _pages.NewRound();
            for (int i = 0; i < _smokeReady.Length; i++) { _smokeReady[i] = false; _smokeVis[i] = 0f; }
            ApplyOutcome();
            if (_state == State.Waking) Caption("You wake up in the snow. The pages are back. He's waiting.", 8f);
        }

        void UpdateWake(float dt)
        {
            _stateT += dt;
            _fadeBlack = 1f - Mathf.Clamp01((_stateT - 1.5f) / 3.5f);
            if (_stateT >= 5f) FinishSequence();
        }

        void FinishSequence()
        {
            LockPlayer(false);
            _model.Show(false);
            _chaserModel.Show(false);
            _killByChaser = false;
            _state = State.Idle;
            _fadeBlack = 0f;
            _static = 0f;
            _stare = 0f;
            ScheduleNextHunt(1.2f, 1.8f);
        }

        void LockPlayer(bool locked)
        {
            try
            {
                var pm = GameManager.GetPlayerManagerComponent();
                if (pm == null) return;
                if (locked)
                {
                    if (pm.GetControlMode() == PlayerControlMode.Normal)
                    {
                        pm.SetControlMode(PlayerControlMode.Locked);
                        _playerLocked = true;
                    }
                }
                else if (_playerLocked)
                {
                    _playerLocked = false;
                    if (pm.GetControlMode() == PlayerControlMode.Locked) pm.SetControlMode(PlayerControlMode.Normal);
                }
            }
            catch { }
        }

        // =====================================================================
        // Drawing
        // =====================================================================

        public override void OnGUI()
        {
            if (_inputBroken) HandleGuiKeyEvent();
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (_state == State.Hunting && !_lastActive) return; // paused / menus: draw nothing
            bool worldLayer = _lastActive && (_state == State.Idle || _state == State.Hunting)
                              && (SlenderGame.IsActive || Settings.PageEditor);
            bool chaserOut = _chaser != null && _chaser.Visible;
            if (_state == State.Idle && !worldLayer && !chaserOut && _fadeBlack <= 0f && (string.IsNullOrEmpty(_caption) || Time.unscaledTime > _captionUntil)) return;

            if (_overlayDisabled) return;
            T("gui: begin");
            _vis.Ensure();
            if (_overlayGuardOpen)
            {
                _overlayGuardOpen = false;
                GuardEnd("overlay");
            }
            Camera cam = null;
            try { cam = GameManager.GetMainCamera(); } catch { }

            bool modelShowing = _model.IsShowing;
            if (worldLayer && cam != null) DrawWorldLayer(cam);
            // Slender games: Arrival-style night grade over the world (smoke and pages included)
            if (_lastActive && SlenderGame.IsActive && Settings.ExtraDarkness > 0.01f && (_state == State.Idle || _state == State.Hunting))
                _vis.DrawNightGrade(Settings.ExtraDarkness);   // Arrival-style night: navy dark, lens edges, grain

            if (_state == State.Hunting && cam != null && !modelShowing && _onScreen && !_occluded)
            {
                float alpha = Mathf.Clamp01(1.25f - _dist / 120f);
                float jitter = _static > 0.25f ? _static * 6f : 0f;
                T("gui: draw creature");
                _vis.DrawCreature(cam, _pos, Settings.CreatureHeight, alpha, jitter);
            }

            // --- the chaser ---
            if (_chaser != null && _chaser.Visible && cam != null && _lastActive
                && (_state == State.Idle || _state == State.Hunting))
            {
                if (!_chaserModel.IsShowing)
                {
                    // no model (or it is off): a plain dark figure, so the chase still reads
                    Vector3 chest = _chaser.Pos + Vector3.up * (Diff.ChaserHeight * 0.6f);
                    Vector3 vp = cam.WorldToViewportPoint(chest);
                    if (vp.z > 0f && vp.x > -0.15f && vp.x < 1.15f && vp.y > -0.15f && vp.y < 1.15f)
                        _vis.DrawCreature(cam, _chaser.Pos, Diff.ChaserHeight,
                                          Mathf.Clamp01(1.3f - _chaser.Distance / 90f), _chaser.Intensity * 5f);
                }
                _vis.DrawChaseEdge(_chaser.LightHold);
            }

            if (_state == State.Killing && !modelShowing)
            {
                float zoom = 1f + Mathf.Clamp01(_stateT / _killLen) * 0.35f;
                _vis.DrawCloseUp(zoom, Mathf.Clamp01(_stateT * 6f), 4f + _static * 10f);
            }

            float strength = Diff.StaticStrength;
            float staticLevel = Math.Max(_static, _burst * 0.85f) * strength;
            // (the chaser adds no static of its own; what is left here is Slender's stare)
            if (_state == State.Killing) staticLevel = Math.Max(staticLevel * 0.85f, 0.25f);
            bool signalLost = _state == State.Killing && _stateT > _killLen - 0.45f;
            if (staticLevel > 0.01f) T("gui: draw static " + staticLevel.ToString("0.00"));
            _vis.DrawStatic(signalLost ? 1f : staticLevel);

            // camcorder breakup: tearing strips, colour fringes, dropouts
            float glitch = _burst;
            if (_state == State.Killing) glitch = Math.Max(glitch, 0.35f + 0.65f * Mathf.Clamp01(_stateT / Math.Max(0.5f, _killLen)));
            _vis.DrawGlitch(glitch * Math.Max(0.35f, strength));

            if (_state == State.Killing && _stateT < 0.25f)
                _vis.DrawFade(new Color(0.5f, 0f, 0f), 0.6f * (1f - _stateT / 0.25f)); // red flash

            _vis.DrawFade(Color.black, _fadeBlack);

            if (Settings.ShowDistance && _chaser != null && _chaser.Active)
            {
                _vis.DrawDistance(_chaser.Distance);   // during a chase, its distance is the one that matters
            }
            else if (_state == State.Hunting && Settings.ShowDistance)
            {
                T("gui: distance");
                _vis.DrawDistance(_dist);
            }
            if (_lastActive && SlenderGame.IsActive && _pages.HasPages && !Settings.PageEditor
                && (_state == State.Idle || _state == State.Hunting))
            {
                _vis.DrawPageCounter(_pages.Collected, _pages.Total);
                if (_aimedPage >= 0 && _finaleAt < 0f) _vis.DrawHint("E  -  take the page");
            }
            if (_state == State.Ending)
                _vis.DrawEndCard(_pages.Total + " / " + _pages.Total + " PAGES", "He was always there.", Mathf.Clamp01((_stateT - 2.5f) / 2f));
            T("gui: end");

            if (!string.IsNullOrEmpty(_caption) && Time.unscaledTime < _captionUntil)
            {
                float left = _captionUntil - Time.unscaledTime;
                _vis.DrawCaption(_caption, Mathf.Clamp01(left / 1.5f));
            }
        }
    }
}
