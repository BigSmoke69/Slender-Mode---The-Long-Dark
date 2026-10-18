using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace SlenderMode
{
    /// <summary>A place a page can be pinned: a point on a tree, rock or wall, plus the way it faces.</summary>
    internal sealed class PageSpot
    {
        public string Scene;
        public Vector3 Pos, Normal;
        public int Art;          // which of the 8 page drawings (0..7)

        public string Write()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join(",", Scene,
                Pos.x.ToString("0.###", c), Pos.y.ToString("0.###", c), Pos.z.ToString("0.###", c),
                Normal.x.ToString("0.###", c), Normal.y.ToString("0.###", c), Normal.z.ToString("0.###", c), Art.ToString(c));
        }

        public static PageSpot Read(string s)
        {
            var p = s.Split(',');
            if (p.Length < 7) return null;
            var c = CultureInfo.InvariantCulture;
            try
            {
                return new PageSpot
                {
                    Scene = p[0],
                    Pos = new Vector3(float.Parse(p[1], c), float.Parse(p[2], c), float.Parse(p[3], c)),
                    Normal = new Vector3(float.Parse(p[4], c), float.Parse(p[5], c), float.Parse(p[6], c)),
                    Art = p.Length > 7 ? int.Parse(p[7], c) : 0
                };
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// The 8 pages of a Slender game.
    ///  - spots.txt: every spot you recorded with the page spot editor (F8 add, F9 remove nearest).
    ///  - games.txt: per Slender save, the spots picked for it and which pages you already took.
    /// Pages are 3D sheets made the same safe way as the creature (scene material + texture block).
    /// </summary>
    internal sealed class PageSystem
    {
        public const int PagesPerGame = 8;

        sealed class GameRecord
        {
            public uint Id;
            public int Round = 1;
            public int Mask;                       // bit i = page i taken
            public List<PageSpot> Pages = new List<PageSpot>();
        }

        readonly MelonLogger.Instance _log;
        readonly string _folder, _spotsFile, _gamesFile;
        readonly System.Random _rng = new System.Random();
        public readonly List<PageSpot> Spots = new List<PageSpot>();
        readonly Dictionary<uint, GameRecord> _games = new Dictionary<uint, GameRecord>();

        public PageSystem(MelonLogger.Instance log, string modFolder)
        {
            _log = log;
            _folder = Path.Combine(modFolder, "pages");
            Directory.CreateDirectory(_folder);
            _spotsFile = Path.Combine(_folder, "spots.txt");
            _gamesFile = Path.Combine(_folder, "games.txt");
            LoadSpots();
            LoadGames();
        }

        // ------------------------------------------------------------ files

        void LoadSpots()
        {
            Spots.Clear();
            try
            {
                if (!File.Exists(_spotsFile)) return;
                foreach (var line in File.ReadAllLines(_spotsFile))
                {
                    if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
                    var s = PageSpot.Read(line.Trim());
                    if (s != null) Spots.Add(s);
                }
                _log.Msg("Page spots recorded: " + Spots.Count);
            }
            catch (Exception e) { _log.Warning("Could not read page spots: " + e.Message); }
        }

        void SaveSpots()
        {
            try
            {
                var lines = new List<string> { "# Slender Mode page spots: scene,x,y,z,nx,ny,nz,page. Recorded with the page spot editor (F8 / F9)." };
                foreach (var s in Spots) lines.Add(s.Write());
                File.WriteAllLines(_spotsFile, lines);
            }
            catch (Exception e) { _log.Warning("Could not save page spots: " + e.Message); }
        }

        void LoadGames()
        {
            _games.Clear();
            try
            {
                if (!File.Exists(_gamesFile)) return;
                foreach (var line in File.ReadAllLines(_gamesFile))
                {
                    // per line, so one damaged line cannot cost you every other game's progress
                    try
                    {
                        var p = line.Split('|');
                        if (p.Length < 4 || !uint.TryParse(p[0], out uint id)) continue;
                        if (!int.TryParse(p[1], out int round) || !int.TryParse(p[2], out int mask)) continue;
                        var g = new GameRecord { Id = id, Round = round, Mask = mask };
                        foreach (var s in p[3].Split(';'))
                        {
                            var spot = s.Length > 0 ? PageSpot.Read(s) : null;
                            if (spot != null) g.Pages.Add(spot);
                        }
                        _games[id] = g;
                    }
                    catch (Exception e) { _log.Warning("Skipped a damaged page progress line: " + e.Message); }
                }
            }
            catch (Exception e) { _log.Warning("Could not read page progress: " + e.Message); }
        }

        void SaveGames()
        {
            try
            {
                var lines = new List<string>();
                foreach (var g in _games.Values)
                {
                    var spots = new List<string>();
                    foreach (var s in g.Pages) spots.Add(s.Write());
                    lines.Add(g.Id + "|" + g.Round + "|" + g.Mask + "|" + string.Join(";", spots));
                }
                File.WriteAllLines(_gamesFile, lines);
            }
            catch (Exception e) { _log.Warning("Could not save page progress: " + e.Message); }
        }

        // ------------------------------------------------------------ game progress

        static uint GameId() { try { return SaveGameSystem.m_CurrentGameId; } catch { return 0; } }
        public static string Scene() { try { return GameManager.m_ActiveScene ?? ""; } catch { return ""; } }

        GameRecord _cur;
        uint _curId = uint.MaxValue;
        int _idFrame = -1;

        GameRecord Current
        {
            get
            {
                // The game id is read across the interop boundary and this is asked many times a
                // frame (the page counter, the escalation level, the smoke), so it is read once.
                int f;
                try { f = Time.frameCount; } catch { f = -1; }
                if (f == _idFrame && f >= 0) return _cur;
                _idFrame = f;
                uint id = GameId();
                if (id != _curId) { _curId = id; _games.TryGetValue(id, out _cur); _built = false; }
                return _cur;
            }
        }

        public bool HasPages { get { var g = Current; return g != null && g.Pages.Count > 0; } }
        public int Total => Current?.Pages.Count ?? 0;
        public int Round => Current?.Round ?? 1;
        public int Collected
        {
            get
            {
                var g = Current; if (g == null) return 0;
                int n = 0; for (int i = 0; i < g.Pages.Count; i++) if ((g.Mask & (1 << i)) != 0) n++;
                return n;
            }
        }
        public bool AllCollected => HasPages && Collected >= Total;

        /// <summary>Makes sure the current Slender save has its pages. Returns a message to show, or null.</summary>
        public string EnsureForCurrentGame()
        {
            if (HasPages) return null;
            string scene = Scene();
            var pool = new List<PageSpot>();
            foreach (var s in Spots) if (s.Scene == scene) pool.Add(s);
            if (pool.Count == 0) return null;
            var g = Current ?? new GameRecord { Id = GameId() };
            PickPages(g, pool);
            _games[g.Id] = g; _cur = g; _curId = g.Id;
            SaveGames();
            _log.Msg("Pages placed for this game: " + g.Pages.Count + " (from " + pool.Count + " recorded spots).");
            return g.Pages.Count < PagesPerGame
                ? "Only " + g.Pages.Count + " page spots recorded here, so you need " + g.Pages.Count + " pages. Record more with the page spot editor."
                : null;
        }

        void PickPages(GameRecord g, List<PageSpot> pool)
        {
            for (int i = pool.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); var t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
            var art = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
            for (int i = art.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); int t = art[i]; art[i] = art[j]; art[j] = t; }
            g.Pages.Clear(); g.Mask = 0;
            for (int i = 0; i < Math.Min(PagesPerGame, pool.Count); i++)
                g.Pages.Add(new PageSpot { Scene = pool[i].Scene, Pos = pool[i].Pos, Normal = pool[i].Normal, Art = art[i] });
            _built = false;
        }

        /// <summary>After the ending: the same save gets a fresh set of pages.</summary>
        /// <summary>
        /// The game reuses save-slot ids, so a brand-new Slender game can land on an id that an
        /// earlier game already recorded pages against. Without this it would start part-way through.
        /// </summary>
        public void ForgetCurrentGame()
        {
            try
            {
                uint id = GameId();
                if (id == 0) return;
                if (_games.Remove(id)) SaveGames();
                _cur = null; _curId = 0; _built = false; _idFrame = -1;
            }
            catch { }
        }

        public void NewRound()
        {
            var g = Current; if (g == null) return;
            string scene = Scene();
            var pool = new List<PageSpot>();
            foreach (var s in Spots) if (s.Scene == scene) pool.Add(s);
            if (pool.Count == 0) foreach (var s in g.Pages) pool.Add(s);
            g.Round++;
            PickPages(g, pool);
            SaveGames();
        }

        /// <summary>Takes the page you're looking at, if any. Returns its number (1..8) or 0.</summary>
        public int TryTake(Camera cam)
        {
            int i = Aimed(cam);
            if (i < 0) return 0;
            var g = Current;
            if (g == null) return 0;
            g.Mask |= 1 << i;
            SaveGames();
            HidePage(i);
            return Collected;
        }

        /// <summary>
        /// Testing: marks pages of the current game as already taken until that many are collected.
        /// It only ever grants pages, so confirming a lower number than you have taken does nothing.
        /// Returns how many are collected afterwards.
        /// </summary>
        public int GrantCollected(int n)
        {
            var g = Current;
            if (g == null || g.Pages.Count == 0) return 0;
            n = Math.Max(0, Math.Min(n, g.Pages.Count));
            int got = Collected;
            if (n <= got) return got;
            for (int i = 0; i < g.Pages.Count && got < n; i++)
            {
                if ((g.Mask & (1 << i)) != 0) continue;
                g.Mask |= 1 << i;
                HidePage(i);
                got++;
            }
            SaveGames();
            return got;
        }

        /// <summary>Index of the page in reach that the camera is aimed at, or -1.</summary>
        public int Aimed(Camera cam)
        {
            var g = Current; if (g == null || cam == null) return -1;
            string scene = Scene();
            Vector3 cp = cam.transform.position, f = cam.transform.forward;
            int best = -1; float bestDot = 0.86f; // within ~30 degrees
            for (int i = 0; i < g.Pages.Count; i++)
            {
                if ((g.Mask & (1 << i)) != 0 || g.Pages[i].Scene != scene) continue;
                Vector3 d = g.Pages[i].Pos - cp;
                float len = d.magnitude;
                if (len > 2.6f || len < 0.01f) continue;
                float dot = Vector3.Dot(f, d / len);
                if (dot > bestDot) { bestDot = dot; best = i; }
            }
            return best;
        }

        /// <summary>Uncollected pages in this scene (for the smoke).</summary>
        public IEnumerable<KeyValuePair<int, PageSpot>> OpenPages()
        {
            var g = Current; if (g == null) yield break;
            string scene = Scene();
            for (int i = 0; i < g.Pages.Count; i++)
                if ((g.Mask & (1 << i)) == 0 && g.Pages[i].Scene == scene) yield return new KeyValuePair<int, PageSpot>(i, g.Pages[i]);
        }

        // ------------------------------------------------------------ spot editor

        /// <summary>Records a spot where the camera is aimed (a tree, rock or wall within 4 m).</summary>
        public string AddSpot(Camera cam)
        {
            Vector3 cp = cam.transform.position, f = cam.transform.forward;
            RaycastHit hit;
            bool ok;
            try { ok = Physics.Raycast(cp + f * 0.3f, f, out hit, 4f, -1, QueryTriggerInteraction.Ignore); }
            catch { ok = false; hit = default; }
            if (!ok) return "Aim at a tree, rock or wall (closer than 4 m) to pin a page there.";
            Vector3 n = hit.normal;
            if (n.y > 0.85f) return "That's the ground. Aim at a tree, rock or wall.";
            var s = new PageSpot { Scene = Scene(), Pos = hit.point + n * 0.02f, Normal = n, Art = Spots.Count % 8 };
            Spots.Add(s);
            SaveSpots();
            _built = false;
            return "Page spot " + Spots.Count + " saved (" + CountInScene() + " in this region).";
        }

        public string RemoveNearestSpot(Vector3 from)
        {
            string scene = Scene();
            int best = -1; float bestD = 6f;
            for (int i = 0; i < Spots.Count; i++)
            {
                if (Spots[i].Scene != scene) continue;
                float d = Vector3.Distance(Spots[i].Pos, from);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return "No page spot within 6 m.";
            Spots.RemoveAt(best);
            SaveSpots();
            _built = false;
            return "Page spot removed (" + CountInScene() + " left in this region).";
        }

        public int CountInScene()
        {
            string scene = Scene(); int n = 0;
            foreach (var s in Spots) if (s.Scene == scene) n++;
            return n;
        }

        // ------------------------------------------------------------ 3D pages

        // Built in small steps like the creature; any failure turns 3D pages off and the GUI fallback
        // draws them instead.
        public Action<string> OnStep;
        public Action OnStepSettled;
        /// <summary>Called once the 3D pages have been standing in the world long enough to trust.</summary>
        public Action OnProven;
        public bool Disabled;          // 3D pages off (crash guard or error): GUI sprites are used instead
        float _provenAt = -1f;
        bool _proven;

        readonly Texture2D[] _tex = new Texture2D[8];
        Mesh _mesh;
        Material _mat;
        readonly List<GameObject> _objs = new List<GameObject>();
        readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
        int _setup, _wait;
        bool _built, _showingEditor;

        public Texture2D PageTexture(int art) { LoadTextures(); return _tex[Math.Max(0, Math.Min(7, art))]; }

        void LoadTextures()
        {
            if (_tex[0] != null) return;
            for (int i = 0; i < 8; i++)
            {
                string f = Path.Combine(_folder, "page_" + (i + 1) + ".jpg");
                try
                {
                    var t = new Texture2D(2, 2);
                    if (File.Exists(f))
                    {
                        Il2CppStructArray<byte> bytes = File.ReadAllBytes(f);
                        if (!ImageConversion.LoadImage(t, bytes)) t = Plain();
                    }
                    else t = Plain();
                    t.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    _tex[i] = t;
                }
                catch (Exception e)
                {
                    _log.Warning("Page image failed: " + f + " (" + e.Message + ")");
                    var fallback = Plain();
                    fallback.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    _tex[i] = fallback;
                }
            }
        }

        static Texture2D Plain()
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var c = new Color32[16];
            for (int i = 0; i < 16; i++) c[i] = new Color32(214, 206, 186, 255);
            Il2CppStructArray<Color32> a = c;
            t.SetPixels32(a); t.Apply(false, false);
            return t;
        }

        const float PageHalfW = 0.11f, PageHalfH = 0.14f;   // ~22 x 28 cm sheet

        /// <summary>
        /// How far off the surface the sheet has to sit for none of its four corners to be buried
        /// in it. Pinned to a tree trunk or a lumpy rock, a flat sheet sitting 2 cm proud of the
        /// point you aimed at has corners that disappear inside the curve - which is what reads as
        /// the page clipping through the tree. A corner hanging over open air constrains nothing.
        /// </summary>
        static float ClearOffset(Vector3 pos, Quaternion rot, Vector3 n)
        {
            float off = 0f;
            try
            {
                Vector3 right = rot * Vector3.right, up = rot * Vector3.up;
                for (int i = 0; i < 4; i++)
                {
                    float sx = (i == 0 || i == 3) ? -PageHalfW : PageHalfW;
                    float sy = i < 2 ? -PageHalfH : PageHalfH;
                    Vector3 c = pos + right * sx + up * sy;
                    // look back down the normal at the surface under this corner
                    if (!Physics.Raycast(c + n * 0.5f, -n, out RaycastHit hit, 1f, -1, QueryTriggerInteraction.Ignore))
                        continue;
                    float need = (0.5f - hit.distance) + 0.015f;   // clear it by 1.5 cm
                    if (need > off) off = need;
                }
            }
            catch { }
            return Math.Min(off, 0.3f);   // a bad spot should not fling the page into the air
        }

        static Mesh MakeQuad()
        {
            const float w = PageHalfW, h = PageHalfH;
            var v = new[]
            {
                new Vector3(-w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0), new Vector3(w, -h, 0),   // front (faces -Z)
                new Vector3(-w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0), new Vector3(w, -h, 0)    // back
            };
            var n = new[]
            {
                new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1),
                new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1)
            };
            var uv = new[]
            {
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0),
                new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0)
            };
            var tri = new[] { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6 };
            var m = new Mesh();
            { Il2CppStructArray<Vector3> a = v; m.vertices = a; }
            { Il2CppStructArray<Vector3> a = n; m.normals = a; }
            { Il2CppStructArray<Vector2> a = uv; m.uv = a; }
            { Il2CppStructArray<int> a = tri; m.triangles = a; }
            m.RecalculateBounds();
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return m;
        }

        static readonly string[] Steps = { "textures", "mesh", "find material" };

        /// <summary>One setup step per call. True once the pages can be shown.</summary>
        bool Setup()
        {
            if (_setup >= Steps.Length) return true;
            if (_wait > 0) { if (--_wait == 0) OnStepSettled?.Invoke(); return false; }
            OnStep?.Invoke("pages:" + Steps[_setup]);
            switch (_setup)
            {
                case 0: LoadTextures(); break;
                case 1: if (_mesh == null) _mesh = MakeQuad(); break;
                case 2:
                    _mat = CreatureModel.FindSceneMaterial(_log, out string how);
                    if (_mat == null) { _log.Warning("Pages: no usable material, drawing them flat instead."); Disabled = true; return false; }
                    _log.Msg("Pages use scene material: " + how);
                    break;
            }
            _setup++;
            _wait = 2;
            return false;
        }

        /// <summary>Keeps the 3D pages in the world matching the game (or every spot, in editor mode).</summary>
        public void Update(bool slenderGame, bool editor)
        {
            if (Disabled) return;
            if (!slenderGame && !editor) { Settle(); if (_objs.Count > 0) Clear(); return; }
            try
            {
                if (!Setup()) return;
                if (_built && !_proven)
                {
                    if (_provenAt < 0f) _provenAt = Time.unscaledTime;
                    else if (Time.unscaledTime - _provenAt > 10f)
                    {
                        _proven = true;
                        OnProven?.Invoke();   // they stood in the world without taking the game down
                    }
                }
                if (_built && _showingEditor == editor) return;
                Clear();
                _showingEditor = editor;
                string scene = Scene();
                if (editor)
                {
                    foreach (var s in Spots) if (s.Scene == scene) _objs.Add(Spawn(s));
                }
                else
                {
                    var g = Current;
                    if (g != null)
                        for (int i = 0; i < g.Pages.Count; i++)
                            _objs.Add((g.Mask & (1 << i)) == 0 && g.Pages[i].Scene == scene ? Spawn(g.Pages[i]) : null);
                }
                _built = true;
            }
            catch (Exception e)
            {
                Disabled = true;
                _log.Error("3D pages disabled after error (drawing them flat instead): " + e.Message);
                try { Clear(); } catch { }
            }
        }

        GameObject Spawn(PageSpot s)
        {
            var go = new GameObject("SlenderMode_Page");
            Vector3 n = s.Normal.sqrMagnitude > 0.01f ? s.Normal.normalized : Vector3.forward;
            Vector3 up = Math.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up;
            float roll = ((s.Pos.x * 13.7f + s.Pos.z * 7.3f) % 16f) - 8f; // a slightly crooked pin, always the same
            var rot = Quaternion.LookRotation(-n, up) * Quaternion.Euler(0f, 0f, roll);
            go.transform.rotation = rot;
            go.transform.position = s.Pos + n * ClearOffset(s.Pos, rot, n);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            var b = new MaterialPropertyBlock();
            b.SetTexture("_MainTex", _tex[Math.Max(0, Math.Min(7, s.Art))]);
            CreatureModel.Neutralize(b);
            r.SetPropertyBlock(b);
            _blocks.Add(b);
            go.hideFlags = HideFlags.DontSave;
            return go;
        }

        void HidePage(int i)
        {
            try { if (!_showingEditor && i < _objs.Count && _objs[i] != null) { UnityEngine.Object.Destroy(_objs[i]); _objs[i] = null; } } catch { }
        }

        void Clear()
        {
            foreach (var o in _objs) try { if (o != null) UnityEngine.Object.Destroy(o); } catch { }
            _objs.Clear();
            _blocks.Clear();
            _built = false;
        }

        /// <summary>Closes an open setup step (its crash-guard marker) when setup is interrupted, so leaving
        /// the game or changing scene mid-step is never mistaken for a crash.</summary>
        public void Settle()
        {
            if (_wait > 0) { _wait = 0; OnStepSettled?.Invoke(); }
        }

        public void OnSceneChanged()
        {
            Settle();
            _objs.Clear();             // scene objects die with the scene
            _blocks.Clear();
            _mat = null;               // so do scene materials
            _built = false;
            if (_setup > 2) _setup = 2;
            _curId = uint.MaxValue;
            _idFrame = -1;
        }

        /// <summary>Pages to draw as flat sprites when 3D pages are off.</summary>
        public IEnumerable<PageSpot> FlatPages(bool editor)
        {
            if (!Disabled) yield break;
            string scene = Scene();
            if (editor) { foreach (var s in Spots) if (s.Scene == scene) yield return s; yield break; }
            foreach (var kv in OpenPages()) yield return kv.Value;
        }
    }
}
