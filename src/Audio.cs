using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using System.Threading.Tasks;

namespace SlenderMode
{
    /// <summary>
    /// Loads mp3/wav files. Decoding runs on a background thread in managed code (Unity's own
    /// decoder can't be called from mods in this game version); clips are then created on the
    /// main thread from the raw samples.
    /// </summary>
    internal sealed class SoundBank
    {
        public readonly List<AudioClip> Kill = new List<AudioClip>();
        public readonly List<AudioClip> Ambience = new List<AudioClip>();
        // Jumpscares for when he's close (split from "Slender: The Arrival - All Slenderman Sounds"):
        public readonly List<AudioClip> ScareBig = new List<AudioClip>();    // instant full hit: you turn and see him close
        public readonly List<AudioClip> ScareHit = new List<AudioClip>();    // short sharp hit: he moved closer behind you
        public readonly List<AudioClip> ScareSwell = new List<AudioClip>();  // rising swell: he crossed into close range
        public readonly List<AudioClip> Chaser = new List<AudioClip>();      // the chaser's scream as it comes for you
        public readonly List<AudioClip> ChaseMusic = new List<AudioClip>();  // the loop that plays while it is on you

        sealed class Pending { public Task<DecodedSound> Job; public List<AudioClip> Target; public string Name; }
        readonly List<Pending> _pending = new List<Pending>();
        readonly MelonLogger.Instance _log;
        const int Chunk = 1 << 18; // samples per SetData call

        public SoundBank(MelonLogger.Instance log) { _log = log; }

        public bool IsLoading => _pending.Count > 0;

        public void LoadAll(string soundsRoot)
        {
            Kill.Clear();
            Ambience.Clear();
            Queue(Path.Combine(soundsRoot, "kill"), Kill);
            Queue(Path.Combine(soundsRoot, "ambience"), Ambience);
            ScareBig.Clear(); ScareHit.Clear(); ScareSwell.Clear();
            Queue(Path.Combine(soundsRoot, "scare_big"), ScareBig);
            Queue(Path.Combine(soundsRoot, "scare_hit"), ScareHit);
            Queue(Path.Combine(soundsRoot, "scare_swell"), ScareSwell);
            Chaser.Clear();
            Queue(Path.Combine(soundsRoot, "chaser"), Chaser);
            ChaseMusic.Clear();
            Queue(Path.Combine(soundsRoot, "chase_music"), ChaseMusic);
        }

        void Queue(string dir, List<AudioClip> target)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string file in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".ogg") { _log.Warning("Skipping " + Path.GetFileName(file) + ": .ogg is not supported, use .mp3 or .wav"); continue; }
                if (ext != ".mp3" && ext != ".wav") continue;
                string path = file;
                _pending.Add(new Pending { Job = Task.Run(() => SoundDecoder.Decode(path)), Target = target, Name = Path.GetFileName(file) });
            }
        }

        /// <summary>Call every frame from the main thread.</summary>
        public void Poll()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (!p.Job.IsCompleted) continue;
                _pending.RemoveAt(i);
                if (p.Job.IsFaulted)
                {
                    _log.Error("Could not decode " + p.Name + ": " + p.Job.Exception?.GetBaseException().Message);
                    continue;
                }
                try
                {
                    var d = p.Job.Result;
                    int frames = d.Samples.Length / d.Channels;
                    var clip = AudioClip.Create("SlenderMode_" + d.Name, frames, d.Channels, d.Frequency, false);
                    // upload in chunks so we never build one giant interop array
                    var buf = new float[Chunk - Chunk % d.Channels];
                    for (int pos = 0; pos < d.Samples.Length; pos += buf.Length)
                    {
                        int n = Math.Min(buf.Length, d.Samples.Length - pos);
                        float[] part = n == buf.Length ? buf : new float[n];
                        Array.Copy(d.Samples, pos, part, 0, n);
                        Il2CppStructArray<float> arr = part;
                        clip.SetData(arr, pos / d.Channels);
                    }
                    clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    p.Target.Add(clip);
                    _log.Msg("Loaded sound: " + d.Name + " (" + (frames / (float)d.Frequency).ToString("0.0") + " s)");
                }
                catch (Exception e)
                {
                    _log.Error("Error creating sound " + p.Name + ": " + e.Message);
                }
            }
        }
    }

    /// <summary>
    /// All of the mod's audio sources. The ambience track is split into equal sections
    /// (calm -> terrifying); the hunt picks a section and it loops inside it, with crossfades.
    /// </summary>
    internal sealed class AudioRig
    {
        const int SR = 22050;
        const float Fade = 1.6f;

        GameObject _go;
        AudioSource _ambA, _ambB, _static, _heart, _oneShot;
        AudioSource _ambCur, _ambOld;
        float _wCur, _wOld;
        AudioClip _ambClip;
        int _stage = -1;
        int _sections = 4;
        bool _paused;
        readonly System.Random _rng = new System.Random();

        public int Stage => _stage;

        public void Ensure()
        {
            if (_go != null && _ambA != null) return;
            _all = null;                  // sources are being made again: the cached list is stale
            _go = new GameObject("SlenderMode_Audio");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _ambA = MakeSource(false);
            _ambB = MakeSource(false);
            _static = MakeSource(true);
            _heart = MakeSource(true);
            _oneShot = MakeSource(false);
            _chase = MakeSource(true);
            _scare = MakeSource(false);
            _scare.volume = 1f;   // PlayOneShot volume is multiplied by this; MakeSource starts at 0
            _ambCur = _ambA; _ambOld = _ambB;

            _static.clip = MakeStaticClip();
            _heart.clip = MakeHeartbeatClip();
            _stage = -1;
        }

        AudioSource MakeSource(bool loop)
        {
            var s = _go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.volume = 0f;
            s.priority = 0;
            s.ignoreListenerPause = true;
            s.bypassListenerEffects = true;
            return s;
        }

        /// <summary>Unity only plays sound if a listener exists; add one if the scene has none.</summary>
        public void EnsureListener()
        {
            try
            {
                var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<AudioListener>());
                if (found == null || found.Length == 0)
                {
                    Ensure();
                    _go.AddComponent<AudioListener>();
                }
            }
            catch { }
        }

        public void SetAmbienceClip(AudioClip clip)
        {
            _ambClip = clip;
            // Long tracks (like the 8-minute Slender ambience) are treated as 4 escalating sections.
            _sections = (clip != null && clip.length >= 120f) ? 4 : 1;
        }

        float SectionLength => _ambClip != null ? _ambClip.length / _sections : 0f;

        /// <summary>stage: -1 = silent, 0..3 = calm..terrifying. fromHit: start at the section's opening hit.</summary>
        public void SetStage(int stage, bool fromHit)
        {
            SlenderMod.Instance?.T("audio: set ambience section " + stage);
            Ensure();
            if (_ambClip == null) { _stage = -1; return; }
            stage = Math.Min(stage, _sections - 1);
            if (stage == _stage) return;
            _stage = stage;
            if (stage < 0) return; // Update() fades everything out

            float start = stage * SectionLength + (fromHit ? 0f : Math.Min(3f, SectionLength * 0.1f));
            StartOnFreshSource(start);
        }

        void StartOnFreshSource(float time)
        {
            SlenderMod.Instance?.T("audio: start ambience at " + time.ToString("0.0") + " s");
            // swap: the old current fades out, a fresh source fades in
            var tmp = _ambOld; _ambOld = _ambCur; _ambCur = tmp;
            _wOld = _wCur; _wCur = 0f;
            _ambCur.Stop();
            _ambCur.clip = _ambClip;
            _ambCur.volume = 0f;
            _ambCur.Play();
            try { _ambCur.time = Math.Max(0f, Math.Min(time, _ambClip.length - 0.1f)); } catch { }
        }

        AudioSource _scare, _chase;
        AudioClip _chaseClip;
        float _chaseVol;

        /// <summary>The track that loops while the chaser is on you (Mods/SlenderMode/sounds/chase_music).</summary>
        public void SetChaseClip(AudioClip clip)
        {
            Ensure();
            _chaseClip = clip;
            if (_chase != null && clip != null) { _chase.clip = clip; _chase.loop = true; }
        }

        public bool HasChaseMusic => _chaseClip != null;

        /// <summary>Fades the chase track towards target (fast in, slower out). Call every frame.</summary>
        public void UpdateChase(float dt, float target)
        {
            if (_go == null || _chase == null || _chaseClip == null || _paused) return;
            // in fast, so it lands as a hit when he closes in; out slow, so escaping is a relief
            float step = target > _chaseVol ? dt / 0.18f : dt / 1.8f;
            _chaseVol = Mathf.MoveTowards(_chaseVol, target, step);
            if (_chaseVol > 0.005f)
            {
                if (!_chase.isPlaying)
                {
                    SlenderMod.Instance?.T("audio: chase music start");
                    try { _chase.time = 0f; } catch { }
                    _chase.Play();
                }
                _chase.volume = _chaseVol;
            }
            else if (_chase.isPlaying)
            {
                _chase.volume = 0f;
                _chase.Stop();
            }
        }

        /// <summary>Plays a jumpscare on its own channel, over the ambience.</summary>
        public void PlayScare(AudioClip clip, float volume)
        {
            Ensure();
            if (clip == null || _scare == null || _paused) return;
            _scare.volume = 1f;
            _scare.PlayOneShot(clip, volume);
        }

        public void StopScares() { if (_scare != null) _scare.Stop(); }

        public void PlayKill(AudioClip clip, float volume)
        {
            Ensure();
            if (clip == null) return;
            _oneShot.Stop();
            _oneShot.clip = clip;
            _oneShot.volume = volume;
            _oneShot.Play();
        }

        public void StopKill() { if (_oneShot != null) _oneShot.Stop(); }

        AudioSource[] _all;
        /// <summary>The sources that pause with the game, as one list, built once so pausing allocates nothing.
        /// The jumpscare channel is deliberately not here: a sting that has started plays out.</summary>
        AudioSource[] All => _all ??= new[] { _ambA, _ambB, _static, _heart, _oneShot, _chase };

        public void SetPaused(bool paused)
        {
            if (_go == null || paused == _paused) return;
            _paused = paused;
            foreach (var s in All)
            {
                if (s == null) continue;
                if (paused) s.Pause(); else s.UnPause();
            }
        }

        /// <summary>Call every frame.</summary>
        public void Update(float dt, float ambienceVolume, float staticVolume, float heartVolume, float heartPitch)
        {
            if (_go == null || _paused) return;

            // --- ambience crossfades + looping inside the current section ---
            float step = dt / Fade;
            if (_stage >= 0)
            {
                _wCur = Math.Min(1f, _wCur + step);
                if (_ambCur.isPlaying && _ambClip != null)
                {
                    float secStart = _stage * SectionLength;
                    float secEnd = secStart + SectionLength;
                    if (_ambCur.time >= secEnd - Fade - 0.05f)
                        StartOnFreshSource(secStart + Math.Min(3f, SectionLength * 0.1f)); // seamless loop
                }
            }
            else
            {
                _wCur = Math.Max(0f, _wCur - step * 0.5f);
            }
            _wOld = Math.Max(0f, _wOld - step);

            _ambCur.volume = _wCur * ambienceVolume;
            _ambOld.volume = _wOld * ambienceVolume;
            if (_wOld <= 0f && _ambOld.isPlaying) _ambOld.Stop();
            if (_stage < 0 && _wCur <= 0f && _ambCur.isPlaying) _ambCur.Stop();

            // --- procedural static + heartbeat ---
            SetLoop(_static, staticVolume, 1f);
            SetLoop(_heart, heartVolume, heartPitch);
        }

        static void SetLoop(AudioSource s, float vol, float pitch)
        {
            s.volume = vol;
            s.pitch = pitch;
            if (vol > 0.005f) { if (!s.isPlaying) { SlenderMod.Instance?.T("audio: play loop " + s.clip?.name); s.Play(); } }
            else if (s.isPlaying) s.Stop();
        }

        // ---------- procedural clips ----------

        AudioClip MakeStaticClip()
        {
            int n = SR * 2;
            var data = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)(_rng.NextDouble() * 2 - 1);
                lp += (white - lp) * 0.55f;                                   // soften the hiss a little
                float crackle = _rng.NextDouble() < 0.0015 ? (float)(_rng.NextDouble() * 2 - 1) * 0.9f : 0f;
                data[i] = lp * 0.35f + crackle;
            }
            // fade the ends so the loop does not click
            for (int i = 0; i < 400; i++) { float k = i / 400f; data[i] *= k; data[n - 1 - i] *= k; }
            return MakeClip("SlenderMode_Static", data);
        }

        AudioClip MakeHeartbeatClip()
        {
            int n = SR; // one beat per second at pitch 1
            var data = new float[n];
            Thump(data, 0.00f, 62f, 0.9f);
            Thump(data, 0.27f, 52f, 0.6f);
            return MakeClip("SlenderMode_Heartbeat", data);
        }

        static void Thump(float[] data, float at, float freq, float amp)
        {
            int start = (int)(at * SR);
            int len = (int)(0.16f * SR);
            for (int i = 0; i < len && start + i < data.Length; i++)
            {
                float t = i / (float)SR;
                float env = (float)Math.Exp(-t * 28f) * Math.Min(1f, i / 60f);
                data[start + i] += (float)Math.Sin(2 * Math.PI * freq * t) * env * amp;
            }
        }

        static AudioClip MakeClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SR, false);
            Il2CppStructArray<float> arr = data;
            clip.SetData(arr, 0);
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return clip;
        }
    }
}
