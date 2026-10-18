using System;
using System.IO;
using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using ModSettings;
using UnityEngine;

[assembly: MelonInfo(typeof(SlenderMode.SlenderMod), "Slender Mode", "0.4.0", "Danio")]
[assembly: MelonGame("Hinterland", "TheLongDark")]
[assembly: MelonAdditionalDependencies("ModSettings")]

namespace SlenderMode
{
    public sealed class SlenderMod : MelonMod
    {
        internal static SlenderMod Instance;
        internal static SlenderSettings Settings;
        static SlenderCustomModeSettings _customMode;

        enum State { Idle, Hunting, Killing, Waking }

        const float Deg = (float)(Math.PI / 180.0);
        const float KillReach = 9f;     // if it gets this close while you look away, it takes you
        const float DangerHold = 6f;    // seconds before the music is allowed to calm down a level

        string _root;
        SoundBank _sounds;
        AudioRig _audio;
        Visuals _vis;
        CreatureModel _model;
        readonly System.Random _rng = new System.Random();

        State _state = State.Idle;
        float _nextHuntAt, _huntEndsAt, _nextMoveAt, _stateT, _killLen;
        bool _forcedHunt, _triggerPending, _deathApplied, _playerLocked, _ambienceAssigned;
        Vector3 _pos;
        float _dist, _stare, _static;
        bool _lastActive;
        float _activeSince = -1f;
        bool _slenderScheduled;
        // jumpscare state
        float _scareReadyAt;          // shared cooldown so stingers never pile up
        bool _swellArmed = true;      // re-armed once he is far again
        bool _wasLooking;
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
        bool _modelGuardActive, _modelProven;
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

            CheckCrashGuard();
            SlenderGame.Init(LoggerInstance, _root);

            ScheduleNextHunt(0.5f, 1.0f);
            LoggerInstance.Msg("Slender Mode v0.4.0 loaded. Settings: Options > Mod Settings > Slender Mode.");
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

            if (!Settings.Enabled && _state == State.Hunting) EndHunt(false);
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            _slenderScheduled = false;
            if (_state == State.Hunting) EndHunt(true);
            if (_state == State.Killing || _state == State.Waking) FinishSequence();
            _model?.OnSceneChanged();
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
                if (step.StartsWith("model"))
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

        void ScheduleNextHunt(float minFactor, float maxFactor)
        {
            // Slender games: he never stays away for long (1.5-3 minutes between hunts).
            if (SlenderGame.IsActive) { _nextHuntAt = Time.unscaledTime + Rand(90f, 180f); return; }
            float mins = Math.Max(1f, Settings.MinutesBetweenHunts);
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
            if (!_ambienceAssigned && !_sounds.IsLoading)
            {
                _ambienceAssigned = true;
                _audio.Ensure();
                _audio.SetAmbienceClip(_sounds.Ambience.Count > 0 ? _sounds.Ambience[_rng.Next(_sounds.Ambience.Count)] : null);
                if (_sounds.Ambience.Count == 0) LoggerInstance.Warning("No ambience sounds found in " + Path.Combine(_root, "sounds", "ambience"));
                if (_sounds.Kill.Count == 0) LoggerInstance.Warning("No kill sounds found in " + Path.Combine(_root, "sounds", "kill"));
            }

            float now = Time.unscaledTime;
            float dt = Math.Min(Time.unscaledDeltaTime, 0.1f);

            T("game: gameplay-active check");
            bool active = GameplayActive(out Camera cam, out Transform player);
            _lastActive = active;
            if (!active) _activeSince = -1f; else if (_activeSince < 0f) _activeSince = now;
            bool sequence = _state == State.Killing || _state == State.Waking;
            T("audio: pause state");
            _audio.SetPaused(!active && !sequence);

            if (!active && !sequence)
            {
                // freeze the clocks while paused / in menus / asleep
                _nextHuntAt += dt; _huntEndsAt += dt; _nextMoveAt += dt;
                if (_state == State.Hunting) _model.Show(false);
                return;
            }

            switch (_state)
            {
                case State.Idle: UpdateIdle(now, player); break;
                case State.Hunting: UpdateHunt(now, dt, cam, player); break;
                case State.Killing: UpdateKill(dt, cam); break;
                case State.Waking: UpdateWake(dt); break;
            }

            // --- audio mix ---
            float closeness = _state == State.Hunting ? Mathf.Clamp01(1f - _dist / 45f) : 0f;
            float fx = Settings.EffectsVolume;
            float staticVol = _state == State.Hunting || _state == State.Killing ? _static * 0.6f * fx : 0f;
            float heartVol = _state == State.Hunting ? closeness * 0.9f * fx : 0f;
            float heartPitch = 0.85f + closeness * 0.9f + _stare * 0.4f;
            T("audio: mix (static " + staticVol.ToString("0.00") + ", heart " + heartVol.ToString("0.00") + ", stage " + _audio.Stage + ")");
            _audio.Update(dt, Settings.AmbienceVolume, staticVol, heartVol, heartPitch);
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
                Caption("SLENDER  -  Coastal Highway", 6f);
                _nextHuntAt = now + Rand(30f, 60f);          // first hunt soon after you arrive
            }
            if (SlenderGame.IsActive) SlenderWorld.Update(now);

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

            bool allowed = Outdoors() && (IsNight() || Settings.DayHunts) && !(Settings.FireRepels && NearFire(player.position));
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
            _huntEndsAt = now + Math.Max(1f, Settings.HuntMinutes) * 60f;
            _stare = 0f; _static = 0f; _danger = -1;
            _swellArmed = true; _wasLooking = false; _scareReadyAt = 0f;
            var cam = GameManager.GetMainCamera();
            T("place creature");
            PlaceAround(player.position, cam, Rand(55f, 75f), true);
            _nextMoveAt = now + Rand(7f, 11f) / Settings.Aggression;
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
            _stare = 0f; _static = 0f;
            _forcedHunt = false;
            ScheduleNextHunt(0.6f, 1.4f);
            if (!quiet) LoggerInstance.Msg("The hunt is over.");
        }

        // ---------------------------------------------------------------- Hunting

        void UpdateHunt(float now, float dt, Camera cam, Transform player)
        {
            Vector3 pp = player.position;
            Vector3 cp = cam.transform.position;

            if (!Settings.Enabled || !Outdoors()) { EndHunt(false); return; }
            if (Settings.FireRepels && NearFire(pp))
            {
                EndHunt(false);
                Caption("The fire keeps it at bay.", 4f);
                return;
            }
            if (!_forcedHunt && !Settings.DayHunts && !IsNight()) { EndHunt(false); return; }
            if (now >= _huntEndsAt) { EndHunt(false); return; }

            _dist = Flat(pp - _pos);
            if (_dist > 160f) PlaceAround(pp, cam, Rand(60f, 75f), true); // you ran / travelled far: it follows

            float h = Settings.CreatureHeight;
            T("visibility (dist " + _dist.ToString("0") + ")");
            ComputeVisibility(cam, cp, h);
            if (_looking && !_wasLooking && _dist < 25f) Scare(_sounds.ScareBig, now, 10f);   // you turn and he's right there
            _wasLooking = _looking;
            T("visibility done: onScreen " + _onScreen + ", occluded " + _occluded + ", looking " + _looking + ", stare " + _stare.ToString("0.00"));

            // --- staring ---
            if (_looking)
            {
                float closeBoost = 1f + Mathf.Clamp01((30f - _dist) / 30f);
                _stare += dt / Math.Max(0.5f, Settings.StareSeconds) * closeBoost;
            }
            else
            {
                _stare = Math.Max(0f, _stare - dt * 0.3f);
            }
            if (_stare >= 1f) { BeginKill(cam, player); return; }

            // --- it only moves while you are not looking ---
            if (now >= _nextMoveAt)
            {
                _nextMoveAt = now + Rand(5f, 9f) / Settings.Aggression;
                if (!_looking)
                {
                    if (_dist <= KillReach) { BeginKill(cam, player); return; }
                    float step = Rand(7f, 13f) * Settings.Aggression;
                    float nd = Math.Max(KillReach - 1f, _dist - step);
                    bool inFront = _rng.NextDouble() < 0.22 && nd > 18f; // sometimes it waits where you will see it
                    T("move: " + (inFront ? "in front" : "behind") + " at " + nd.ToString("0") + " m");
                    PlaceAround(pp, cam, nd, !inFront);
                    _dist = Flat(pp - _pos);
                    if (_dist < 30f && !inFront) Scare(_sounds.ScareHit, now, 3f);                   // he got closer while you looked away
                }
            }

            // --- crossing into close range: a rising swell (re-armed when he is far again) ---
            if (_swellArmed && _dist < 35f) { _swellArmed = false; Scare(_sounds.ScareSwell, now, 4f); }
            else if (!_swellArmed && _dist > 45f) _swellArmed = true;

            // --- danger level drives the ambience sections ---
            int d = _dist > 45f ? 0 : _dist > 25f ? 1 : _dist > 12f ? 2 : 3;
            if (_stare > 0.55f) d = 3;
            else if (_stare > 0.2f) d = Math.Max(d, 2);
            else if (_looking) d = Math.Max(d, 1);
            T("danger level " + d);
            SetDanger(d, now);

            _static = _looking ? 0.12f + 0.88f * _stare : _stare * 0.6f;

            // --- the 3D model, if there is one ---
            T("model show/hide");
            ShowModel(_pos, pp);
        }

        /// <summary>Plays a random jumpscare from the group, respecting a shared cooldown.</summary>
        void Scare(System.Collections.Generic.List<AudioClip> group, float now, float cooldown)
        {
            if (group == null || group.Count == 0 || now < _scareReadyAt) return;
            _scareReadyAt = now + cooldown;
            T("audio: jumpscare");
            _audio.PlayScare(group[_rng.Next(group.Count)], Settings.ScareVolume);
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
        static float GroundY(Vector3 p, float refY)
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

        void BeginKill(Camera cam, Transform player)
        {
            _state = State.Killing;
            _stateT = 0f;
            _deathApplied = false;
            _fadeBlack = 0f;
            _static = 1f;

            AudioClip clip = _sounds.Kill.Count > 0 ? _sounds.Kill[_rng.Next(_sounds.Kill.Count)] : null;
            _killLen = clip != null ? Mathf.Clamp(clip.length, 2.5f, 9f) : 3f;
            _audio.SetStage(-1, false);
            _audio.StopScares();
            _audio.PlayKill(clip, Settings.KillVolume);

            // it is suddenly right in front of you
            Vector3 cp = cam.transform.position;
            Vector3 f = cam.transform.forward; f.y = 0f;
            float fl = Flat(f); if (fl < 0.01f) f = Vector3.forward; else f = f / fl;
            float h = Settings.CreatureHeight;
            _pos = new Vector3(cp.x, cp.y - h * 0.9f, cp.z) + f * 1.3f;

            LockPlayer(true);
            LoggerInstance.Msg("Caught.");
        }

        void UpdateKill(float dt, Camera cam)
        {
            _stateT += dt;
            float t = _stateT;

            if (cam != null) ShowModel(_pos, cam.transform.position);

            _static = Math.Min(1f, 0.6f + 0.4f * (float)Math.Abs(Math.Sin(t * 13f)));
            _fadeBlack = Mathf.Clamp01((t - (_killLen - 1.1f)) / 0.9f);

            if (t >= _killLen && !_deathApplied)
            {
                _deathApplied = true;
                _model.Show(false);
                _audio.StopKill();
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
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (_state == State.Hunting && !_lastActive) return; // paused / menus: draw nothing
            if (_state == State.Idle && _fadeBlack <= 0f && (string.IsNullOrEmpty(_caption) || Time.unscaledTime > _captionUntil)) return;

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

            if (_state == State.Hunting && cam != null && !modelShowing && _onScreen && !_occluded)
            {
                float alpha = Mathf.Clamp01(1.25f - _dist / 120f);
                float jitter = _static > 0.25f ? _static * 6f : 0f;
                T("gui: draw creature");
                _vis.DrawCreature(cam, _pos, Settings.CreatureHeight, alpha, jitter);
            }

            if (_state == State.Killing && !modelShowing)
            {
                float zoom = 1f + Mathf.Clamp01(_stateT / _killLen) * 0.35f;
                _vis.DrawCloseUp(zoom, Mathf.Clamp01(_stateT * 6f), 4f + _static * 10f);
            }

            float staticLevel = _static * Settings.StaticStrength;
            if (_state == State.Killing) staticLevel = Math.Max(staticLevel * 0.85f, 0.25f);
            if (staticLevel > 0.01f) T("gui: draw static " + staticLevel.ToString("0.00"));
            _vis.DrawStatic(staticLevel);

            if (_state == State.Killing && _stateT < 0.25f)
                _vis.DrawFade(new Color(0.5f, 0f, 0f), 0.6f * (1f - _stateT / 0.25f)); // red flash

            _vis.DrawFade(Color.black, _fadeBlack);

            if (_state == State.Hunting && Settings.ShowDistance)
            {
                T("gui: distance");
                _vis.DrawDistance(_dist);
            }
            T("gui: end");

            if (!string.IsNullOrEmpty(_caption) && Time.unscaledTime < _captionUntil)
            {
                float left = _captionUntil - Time.unscaledTime;
                _vis.DrawCaption(_caption, Mathf.Clamp01(left / 1.5f));
            }
        }
    }
}
