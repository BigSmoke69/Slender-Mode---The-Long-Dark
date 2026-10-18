using System;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SlenderMode
{
    /// <summary>
    /// The chaser (Charlie Matheson): the other thing out there. Unlike Slender, it does not care
    /// whether you are looking - it runs straight at you and kills on contact. It only turns up once
    /// you have a few pages. Shine a light in its face and it recoils like an animal and breaks off.
    /// </summary>
    internal sealed class Chaser
    {
        internal enum Mode { Off, Incoming, Chasing, Recoiling, Leaving }
        internal enum Result { None, Caught }

        // --- tuning ---
        const float SpawnMin = 30f, SpawnMax = 44f;   // where it appears, behind you
        const float CatchReach = 1.7f;                // this close and it has you
        const float GiveUpDist = 95f;                 // you outran it
        const float MaxChaseSeconds = 55f;            // it does not follow forever
        const float WindUp = 0.9f;                    // the scream, before it really goes
        const float RecoilSeconds = 2.6f;             // it backs off, shielding its face
        const float BeamAngle = 30f;                  // half-angle of "you are shining it at me"

        /// <summary>
        /// How long the light has to hold on it, and from how far, both come from the difficulty:
        /// a repel that is instant and works from across the field makes the chaser harmless
        /// however fast it runs.
        /// </summary>
        static float RepelSeconds { get { try { return Math.Max(0.1f, Diff.Current.RepelHold); } catch { return 0.55f; } } }
        static float FlashlightRange { get { try { return Math.Max(4f, Diff.Current.RepelRange); } catch { return 32f; } } }
        /// <summary>A torch or flare is a poorer weapon than the beam: it has to be closer still.</summary>
        static float TorchRange => FlashlightRange * 0.55f;

        readonly MelonLogger.Instance _log;
        readonly System.Random _rng = new System.Random();

        Mode _mode = Mode.Off;
        Vector3 _pos, _recoilDir, _lastPlayerPos;
        float _dist = 999f, _modeT, _chaseT, _lightHold, _nextAt, _speed, _curSpeed;
        int _repelsThisGame;
        // the way around an obstacle is worked out a few times a second, not every frame:
        // picking it costs up to seven raycasts
        Vector3 _steer;
        float _steerUntil;

        public Chaser(MelonLogger.Instance log) { _log = log; }

        public Mode State => _mode;
        public Vector3 Pos => _pos;
        public float Distance => _dist;
        /// <summary>How fast it is actually moving right now (drives the run cycle).</summary>
        public float CurrentSpeed => _mode == Mode.Off ? 0f : _curSpeed;
        /// <summary>True while it is out there: Slender steps back and the mod draws it.</summary>
        public bool Active => _mode == Mode.Incoming || _mode == Mode.Chasing || _mode == Mode.Recoiling;
        /// <summary>True while it should be drawn at all (includes running away).</summary>
        public bool Visible => _mode != Mode.Off;
        /// <summary>0..1: how much pressure the chase puts on the screen and the mix.</summary>
        public float Intensity
        {
            get
            {
                if (_mode == Mode.Off) return 0f;
                float near = Mathf.Clamp01(1f - _dist / 35f);
                if (_mode == Mode.Incoming) return Mathf.Clamp01(_modeT / WindUp) * Math.Max(0.45f, near);
                if (_mode == Mode.Recoiling) return near * 0.5f;
                if (_mode == Mode.Leaving) return near * 0.25f;
                return Math.Max(0.35f, near);
            }
        }
        /// <summary>0..1 while the light is working on it (drives the on-screen cue).</summary>
        public float LightHold => Mathf.Clamp01(_lightHold / RepelSeconds);

        public void ResetForNewGame()
        {
            _mode = Mode.Off;
            _dist = 999f;
            _lightHold = 0f;
            _chaseT = 0f;
            _repelsThisGame = 0;
            _nextAt = 0f;
            _steerUntil = 0f;
        }

        /// <summary>Called when a chase is cut short (a door, a death, a scene change). Always leaves a
        /// gap before the next one, so stepping back outside does not start a chase on the spot.</summary>
        public void Stop()
        {
            _mode = Mode.Off;
            _lightHold = 0f;
            _chaseT = 0f;
            _steerUntil = 0f;
            if (_nextAt > 0f)
            {
                float soonest = UnityEngine.Time.unscaledTime + 45f;
                if (_nextAt < soonest) _nextAt = soonest;
            }
        }

        /// <summary>Pushes the next possible chase out by this many seconds (used while paused).</summary>
        public void ShiftClock(float dt) { if (_nextAt > 0f) _nextAt += dt; }

        /// <summary>Arms the first chase once you are deep enough into the pages.</summary>
        public void Arm(float now, float pressure)
        {
            if (_nextAt > 0f) return;
            // the first one comes fairly soon after you reach the page it unlocks at
            _nextAt = now + Mathf.Lerp(55f, 30f, pressure) * Rand(0.85f, 1.2f);
            _log.Msg("Chaser armed: first chase in about " + (_nextAt - now).ToString("0") + " s.");
        }

        public bool WantsToStart(float now) => _mode == Mode.Off && _nextAt > 0f && now >= _nextAt;

        /// <summary>Test mode: pull the next chase in to this many seconds from now.</summary>
        public void ArmIn(float now, float seconds)
        {
            if (_nextAt <= 0f || _nextAt > now + seconds) _nextAt = now + seconds;
        }

        /// <summary>inFront + fixedDist: the test spawn, straight ahead where you can watch him come.</summary>
        public void Start(float now, Vector3 playerPos, Camera cam, float pressure, float baseSpeed,
                          bool inFront = false, float fixedDist = 0f)
        {
            float yaw;
            Vector3 f = cam != null ? cam.transform.forward : Vector3.forward;
            f.y = 0f;
            float baseYaw = (float)Math.Atan2(f.x, f.z);
            // normally behind you and off to one side; in front when testing
            yaw = inFront ? baseYaw + Rand(-0.12f, 0.12f) : baseYaw + (float)(Math.PI) + Rand(-1.0f, 1.0f);
            float d = fixedDist > 1f ? fixedDist : Rand(SpawnMin, SpawnMax);
            Vector3 p = playerPos + new Vector3((float)Math.Sin(yaw), 0f, (float)Math.Cos(yaw)) * d;
            p.y = SlenderMod.GroundY(p, playerPos.y);
            _pos = p;
            _dist = d;
            _lastPlayerPos = playerPos;
            _mode = Mode.Incoming;
            _modeT = 0f;
            _chaseT = 0f;
            _lightHold = 0f;
            _steerUntil = 0f;
            _recoilDir = Vector3.zero;
            // it gets a little faster the further in you are, and after each time you drove it off
            // Each time you drive it off it comes back faster, so leaning on the light is a
            // delaying tactic rather than an answer.
            _speed = baseSpeed * Mathf.Lerp(1f, 1.18f, pressure) * (1f + 0.09f * _repelsThisGame);
            _log.Msg("Chaser incoming at " + d.ToString("0") + " m " + (inFront ? "in front of you" : "behind you") +
                     " (speed " + _speed.ToString("0.0") + " m/s).");
        }

        /// <summary>Per frame while a game is running. Returns Caught when it reaches you.</summary>
        public Result Update(float now, float dt, Camera cam, Transform player, float pressure)
        {
            if (_mode == Mode.Off || cam == null || player == null) return Result.None;

            Vector3 pp = player.position;
            Vector3 cp = cam.transform.position;
            _lastPlayerPos = pp;
            _modeT += dt;

            Vector3 to = pp - _pos; to.y = 0f;
            _dist = to.magnitude;
            Vector3 dir = _dist > 0.01f ? to / _dist : Vector3.forward;

            switch (_mode)
            {
                case Mode.Incoming:
                    // the scream, then it comes: a short wind-up at half speed
                    _curSpeed = _speed * 0.45f;
                    Advance(dir, _speed * 0.45f * dt, pp);
                    UpdateLight(dt, cam, cp, pressure);
                    if (_modeT >= WindUp) { _mode = Mode.Chasing; _modeT = 0f; }
                    break;

                case Mode.Chasing:
                    _chaseT += dt;
                    _curSpeed = _speed;
                    if (_dist <= CatchReach) { _log.Msg("The chaser caught you."); return Result.Caught; }
                    Advance(dir, _speed * dt, pp);
                    UpdateLight(dt, cam, cp, pressure);
                    if (_chaseT > MaxChaseSeconds || _dist > GiveUpDist)
                    {
                        _log.Msg("Chaser gave up (" + (_dist > GiveUpDist ? "you outran it" : "it lost interest") + ").");
                        Leave(now, pressure, false);
                    }
                    break;

                case Mode.Recoiling:
                    // backing away from you, fast, with its arm over its face
                    _curSpeed = _speed * 1.5f;
                    Advance(_recoilDir, _speed * 1.5f * dt, pp);
                    if (_modeT >= RecoilSeconds) Leave(now, pressure, true);
                    break;

                case Mode.Leaving:
                    _curSpeed = _speed * 1.25f;
                    Advance(_recoilDir, _speed * 1.25f * dt, pp);
                    if (_modeT >= 3.5f || _dist > 70f) { _mode = Mode.Off; _lightHold = 0f; }
                    break;
            }
            return Result.None;
        }

        void Leave(float now, float pressure, bool repelled)
        {
            _mode = Mode.Leaving;
            _modeT = 0f;
            _lightHold = 0f;
            // Giving up has no recoil direction of its own, and without one it would stand rooted to
            // the spot for the whole of Leaving instead of running off. Head away from you.
            if (!repelled || _recoilDir.sqrMagnitude < 0.01f)
            {
                Vector3 away = _pos - _lastPlayerPos; away.y = 0f;
                _recoilDir = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            }
            if (repelled)
            {
                _repelsThisGame++;
                // driven off: it comes back sooner than if it simply lost you
                _nextAt = now + Mathf.Lerp(95f, 50f, pressure) * Rand(0.85f, 1.15f);
            }
            else
            {
                _nextAt = now + Mathf.Lerp(170f, 85f, pressure) * Rand(0.85f, 1.2f);
            }
        }

        static readonly float[] Turns = { 35f, -35f, 65f, -65f, 100f, -100f };

        /// <summary>
        /// Walks it along dir, stepping around whatever is in the way, and keeps it on the ground.
        /// Finding a way around costs up to seven raycasts, so the chosen heading is kept for a
        /// fifth of a second instead of being worked out again every frame.
        /// </summary>
        void Advance(Vector3 dir, float step, Vector3 playerPos)
        {
            if (step <= 0f) return;
            Vector3 use = dir;
            float now = UnityEngine.Time.unscaledTime;
            try
            {
                if (now < _steerUntil)
                {
                    // still heading around the same obstacle, as long as that still points our way
                    if (Vector3.Dot(_steer, dir) > 0f) use = _steer;
                    else _steerUntil = 0f;
                }
                if (now >= _steerUntil)
                {
                    _steerUntil = now + 0.2f;
                    _steer = dir;
                    Vector3 chest = _pos + Vector3.up * 1.1f;
                    if (BlockedAhead(chest, dir, 2.2f))
                    {
                        // try to slip around: small turns first, then wide ones
                        for (int i = 0; i < Turns.Length; i++)
                        {
                            Vector3 alt = Rotate(dir, Turns[i]);
                            if (!BlockedAhead(chest, alt, 2.2f)) { _steer = alt; break; }
                        }
                    }
                    use = _steer;
                }
            }
            catch { }

            Vector3 next = _pos + use * step;
            next.y = SlenderMod.GroundY(next, playerPos.y);
            // never let it teleport up a cliff face in one step
            if (Math.Abs(next.y - _pos.y) > step * 3f + 1.5f) next.y = _pos.y;
            _pos = next;
        }

        static Vector3 Rotate(Vector3 v, float degrees)
        {
            float a = degrees * (float)(Math.PI / 180.0);
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            return new Vector3(v.x * c + v.z * s, 0f, -v.x * s + v.z * c);
        }

        static bool BlockedAhead(Vector3 from, Vector3 dir, float len)
        {
            try { return Physics.Raycast(from, dir, len, -1, QueryTriggerInteraction.Ignore); }
            catch { return false; }
        }

        // ------------------------------------------------------------ the light

        /// <summary>
        /// 0 none, 1 torch/flare/lantern, 2 flashlight.
        ///
        /// The flashlight is read off the item itself rather than through the game's
        /// IsLitFlashlight(), because that answer takes the aurora into account and a Slender game
        /// has no aurora - the light is burning in your hand but the game would say it is not lit.
        /// </summary>
        internal static int HeldLight()
        {
            try
            {
                var pm = GameManager.GetPlayerManagerComponent();
                if (pm == null) return 0;
                var gear = pm.m_ItemInHands;
                if (gear == null) return 0;

                try
                {
                    var fl = gear.m_FlashlightItem;
                    if (fl != null && fl.IsOn()) return 2;
                }
                catch { }
                if (gear.IsLitFlashlight()) return 2;

                if (gear.IsLitTorch()) return 1;
                if (gear.IsLitLightsource()) return 1;   // flares, lanterns
                return 0;
            }
            catch { return 0; }
        }

        void UpdateLight(float dt, Camera cam, Vector3 camPos, float pressure)
        {
            bool onIt = false;
            try
            {
                int light = HeldLight();
                if (light > 0)
                {
                    float range = light == 2 ? FlashlightRange : TorchRange;
                    Vector3 chest = _pos + Vector3.up * 1.0f;
                    Vector3 d = chest - camPos;
                    float len = d.magnitude;
                    if (len < range && len > 0.01f)
                    {
                        float dot = Vector3.Dot(cam.transform.forward, d / len);
                        if (dot > (float)Math.Cos(BeamAngle * Math.PI / 180.0))
                            onIt = !BlockedAhead(camPos + (d / len) * 0.6f, d / len, len - 1.2f);
                    }
                }
            }
            catch { }

            if (onIt) _lightHold += dt;
            else _lightHold = Math.Max(0f, _lightHold - dt * 1.6f);

            if (_lightHold >= RepelSeconds)
            {
                Vector3 away = _pos - camPos; away.y = 0f;
                _recoilDir = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
                _mode = Mode.Recoiling;
                _modeT = 0f;
                _lightHold = 0f;
                _log.Msg("Chaser driven off by the light.");
            }
        }

        float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
    }
}
