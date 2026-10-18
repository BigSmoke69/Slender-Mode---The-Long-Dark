using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlenderMode
{
    /// <summary>
    /// Custom creature from Mods/SlenderMode/model: one .obj (groups become submeshes) plus base-color
    /// textures matched to each group by name ("..._skin" -> "*skin*basecolor*.jpg").
    /// The OBJ is parsed on a background thread; Unity objects are only created once a game scene is
    /// running and the creature is actually needed.
    /// </summary>
    internal sealed class CreatureModel
    {
        sealed class MeshData
        {
            public Vector3[] Verts;
            public Vector3[] Normals;
            public Vector2[] Uvs;
            public List<int[]> Groups = new List<int[]>();
            public List<string> GroupNames = new List<string>();
            public float Height;
            /// <summary>For each emitted vertex, which "v" line of the OBJ it came from. A baked
            /// animation is stored in OBJ order, so this maps its frames onto our vertices.</summary>
            public int[] SrcVert;
            public int SrcCount;
        }

        readonly MelonLogger.Instance _log;
        Task<MeshData> _parse;
        MeshData _data;
        string _folder;

        GameObject _root;
        readonly List<Texture2D> _textures = new List<Texture2D>();
        bool _materialsReady, _failed;

        public CreatureModel(MelonLogger.Instance log) { _log = log; }

        public bool Available => !_failed && (_parse != null || _data != null);
        public bool Ready => _data != null && !_failed;
        public string Status { get; private set; } = "No model";

        // ------------------------------------------------------------ loading

        public void BeginLoad(string folder)
        {
            _folder = folder;
            _failed = false;
            _data = null;
            _parse = null;
            if (!Directory.Exists(folder)) { Status = "No model folder"; return; }

            string obj = null;
            // newest .obj wins (so a freshly installed model beats an old one left in the folder)
            DateTime newest = DateTime.MinValue;
            foreach (var f in Directory.GetFiles(folder, "*.obj"))
            {
                var t = File.GetLastWriteTimeUtc(f);
                if (obj == null || t > newest) { obj = f; newest = t; }
            }
            if (obj == null) { Status = "No .obj in model folder"; return; }

            // a baked run cycle next to it: "<model>.anim" first, otherwise any .anim here
            _pendingAnim = null;
            try
            {
                string beside = Path.Combine(folder, Path.GetFileNameWithoutExtension(obj) + ".anim");
                if (File.Exists(beside)) _pendingAnim = beside;
                else
                {
                    var anims = Directory.GetFiles(folder, "*.anim");
                    if (anims.Length > 0) _pendingAnim = anims[0];
                }
            }
            catch { }

            Status = "Parsing " + Path.GetFileName(obj);
            _parse = Task.Run(() => Parse(obj));
        }

        // ------------------------------------------------------- baked animation
        // The model has no skeleton in game, so a run cycle is baked to whole vertex frames
        // (see tools/bake_run.py) and played back by swapping the mesh's vertices.
        string _pendingAnim;
        Vector3[][] _anim;
        int _animFrames;
        Vector3[] _animBuf;
        float _lastPhase = -2f, _lastAnimTime;
        bool _animBroken;

        public bool HasAnimation => _anim != null && _anim.Length > 1 && !_animBroken;

        void LoadAnimation(string path)
        {
            try
            {
                if (_data == null || !File.Exists(path)) return;
                byte[] b = File.ReadAllBytes(path);
                if (b.Length < 16 || b[0] != (byte)'S' || b[1] != (byte)'L' || b[2] != (byte)'A' || b[3] != (byte)'N')
                { _log.Warning("Animation " + Path.GetFileName(path) + " is not a baked cycle; ignored."); return; }
                int ver = BitConverter.ToInt32(b, 4);
                int frames = BitConverter.ToInt32(b, 8);
                int verts = BitConverter.ToInt32(b, 12);
                if (ver != 1 || frames < 2 || verts != _data.SrcCount)
                {
                    _log.Warning("Animation does not match this model (" + verts + " vertices, model has " +
                                 _data.SrcCount + "); ignored.");
                    return;
                }
                long need = 16L + (long)frames * verts * 12L;
                if (b.Length < need) { _log.Warning("Animation file is truncated; ignored."); return; }

                var src = _data.SrcVert;
                var raw = new float[verts * 3];
                var anim = new Vector3[frames][];
                int off = 16;
                for (int f = 0; f < frames; f++)
                {
                    Buffer.BlockCopy(b, off, raw, 0, raw.Length * 4);
                    off += raw.Length * 4;
                    var arr = new Vector3[src.Length];
                    for (int i = 0; i < src.Length; i++)
                    {
                        int s = src[i] * 3;
                        arr[i] = new Vector3(raw[s], raw[s + 1], raw[s + 2]);
                    }
                    anim[f] = arr;
                }
                _anim = anim;
                _animFrames = frames;
                _log.Msg("Animation loaded: " + frames + " frames (" + Path.GetFileName(path) + ").");
            }
            catch (Exception e) { _log.Warning("Animation failed to load: " + e.Message); }
        }

        /// <summary>Plays the baked cycle. phase counts cycles; only the fraction is used.</summary>
        public void SetAnimPhase(float phase)
        {
            if (_animBroken || _anim == null || _partMeshes.Count == 0 || !_materialsReady) return;
            float now = UnityEngine.Time.unscaledTime;
            if (now - _lastAnimTime < 0.045f) return;          // ~22 updates a second is plenty
            float p = phase - (float)Math.Floor(phase);
            if (Math.Abs(p - _lastPhase) < 0.004f) return;
            _lastAnimTime = now; _lastPhase = p;
            try
            {
                float fp = p * _animFrames;
                int f0 = (int)fp; if (f0 < 0 || f0 >= _animFrames) f0 = 0;
                int f1 = (f0 + 1) % _animFrames;
                float t = fp - f0;
                var a = _anim[f0]; var bb = _anim[f1];
                if (_animBuf == null || _animBuf.Length != a.Length) _animBuf = new Vector3[a.Length];
                for (int i = 0; i < a.Length; i++)
                {
                    Vector3 u = a[i], v = bb[i];
                    _animBuf[i] = new Vector3(u.x + (v.x - u.x) * t, u.y + (v.y - u.y) * t, u.z + (v.z - u.z) * t);
                }
                Il2CppStructArray<Vector3> arr = _animBuf;
                foreach (var m in _partMeshes) { m.vertices = arr; m.RecalculateBounds(); }
            }
            catch (Exception e) { _animBroken = true; _log.Warning("Animation disabled: " + e.Message); }
        }

        /// <summary>Call every frame; finishes the background parse.</summary>
        public void Poll()
        {
            if (_parse == null || !_parse.IsCompleted) return;
            if (_parse.IsFaulted)
            {
                _failed = true;
                Status = "Model failed to load: " + (_parse.Exception?.GetBaseException().Message ?? "unknown error");
                _log.Error(Status);
            }
            else
            {
                _data = _parse.Result;
                Status = "Model parsed: " + _data.Verts.Length + " vertices, " + _data.GroupNames.Count + " parts, " + _data.Height.ToString("0.00") + " m tall";
                _log.Msg(Status);
                if (_pendingAnim != null) LoadAnimation(_pendingAnim);
            }
            _parse = null;
        }

        static MeshData Parse(string path)
        {
            var pos = new List<Vector3>();
            var uvs = new List<Vector2>();
            var nrm = new List<Vector3>();
            var outV = new List<Vector3>();
            var outUV = new List<Vector2>();
            var outN = new List<Vector3>();
            var outSrc = new List<int>();
            var groups = new List<List<int>>();
            var names = new List<string>();
            var map = new Dictionary<string, int>();
            List<int> cur = null;
            string curName = "default";
            var ci = CultureInfo.InvariantCulture;
            var corner = new List<int>(8);

            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length < 2 || line[0] == '#') continue;
                string[] p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                switch (p[0])
                {
                    case "v": pos.Add(new Vector3(-float.Parse(p[1], ci), float.Parse(p[2], ci), float.Parse(p[3], ci))); break; // RH -> LH
                    case "vt": uvs.Add(new Vector2(float.Parse(p[1], ci), float.Parse(p[2], ci))); break;
                    case "vn": nrm.Add(new Vector3(-float.Parse(p[1], ci), float.Parse(p[2], ci), float.Parse(p[3], ci))); break;
                    case "g":
                    case "usemtl":
                        curName = p.Length > 1 ? p[p.Length - 1] : "default";
                        cur = null;
                        break;
                    case "f":
                        if (cur == null)
                        {
                            int existing = names.IndexOf(curName);
                            if (existing >= 0) cur = groups[existing];
                            else { cur = new List<int>(); groups.Add(cur); names.Add(curName); }
                        }
                        corner.Clear();
                        for (int k = 1; k < p.Length; k++)
                        {
                            if (!map.TryGetValue(p[k], out int idx))
                            {
                                string[] s = p[k].Split('/');
                                int vi = Index(s[0], pos.Count);
                                int ti = s.Length > 1 && s[1].Length > 0 ? Index(s[1], uvs.Count) : -1;
                                int ni = s.Length > 2 && s[2].Length > 0 ? Index(s[2], nrm.Count) : -1;
                                idx = outV.Count;
                                outV.Add(pos[vi]);
                                outSrc.Add(vi);
                                outUV.Add(ti >= 0 ? uvs[ti] : new Vector2(0f, 0f));
                                outN.Add(ni >= 0 ? nrm[ni] : new Vector3(0f, 1f, 0f));
                                map[p[k]] = idx;
                            }
                            corner.Add(idx);
                        }
                        for (int k = 1; k + 1 < corner.Count; k++) { cur.Add(corner[0]); cur.Add(corner[k + 1]); cur.Add(corner[k]); }
                        break;
                }
            }
            if (outV.Count == 0) throw new Exception("OBJ has no faces");

            float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var v in outV)
            {
                minY = Math.Min(minY, v.y); maxY = Math.Max(maxY, v.y);
                minX = Math.Min(minX, v.x); maxX = Math.Max(maxX, v.x);
                minZ = Math.Min(minZ, v.z); maxZ = Math.Max(maxZ, v.z);
            }
            float cx = (minX + maxX) / 2f, cz = (minZ + maxZ) / 2f;
            var d = new MeshData { Verts = new Vector3[outV.Count], Normals = outN.ToArray(), Uvs = outUV.ToArray(), Height = Math.Max(0.01f, maxY - minY) };
            for (int i = 0; i < d.Verts.Length; i++) d.Verts[i] = new Vector3(outV[i].x - cx, outV[i].y - minY, outV[i].z - cz);
            d.SrcVert = outSrc.ToArray();
            d.SrcCount = pos.Count;
            foreach (var g in groups) d.Groups.Add(g.ToArray());
            d.GroupNames.AddRange(names);
            return d;
        }

        static int Index(string s, int count)
        {
            int i = int.Parse(s, CultureInfo.InvariantCulture);
            return i > 0 ? i - 1 : count + i;
        }

        // ------------------------------------------------------------ building (main thread, in-game only)

        // Model setup runs ONE step per call, with a pause between steps, so any background (render
        // thread) failure happens right after the step that caused it. Each step is logged and written
        // to the crash guard first.
        //
        // IMPORTANT (found by testing on this game version): creating ANY material from mod code crashes
        // the game - `new Material(...)` and `Instantiate(material)` alike. So the creature uses an
        // existing scene material unchanged, and its own textures go on through a MaterialPropertyBlock.
        // Each part (suit, skin) is its own child object with its own mesh, so no material arrays.
        static readonly string[] StepNames =
        {
            "meshes", "textures", "objects", "mesh filters", "mesh renderers",
            "find material", "assign material", "texture blocks", "activate"
        };
        const int FramesBetweenSteps = SlenderMod.Diagnostics ? 15 : 2; // short pause per setup step
        int _step, _wait;
        readonly List<Mesh> _partMeshes = new List<Mesh>();
        readonly List<GameObject> _parts = new List<GameObject>();
        readonly List<MeshRenderer> _partRenderers = new List<MeshRenderer>();
        Material _shared;

        /// <summary>Called with "<Tag>:<step>" before each step (the mod writes it to the crash guard).</summary>
        public Action<string> OnStep;
        public Action OnStepSettled;

        /// <summary>Which figure this is ("model" = Slender, "chaser" = the chaser). Keeps the two
        /// instances' scene objects and crash-guard steps apart.</summary>
        public string Tag = "model";

        bool BuildStepwise()
        {
            if (_failed || _data == null) return false;
            // The pause is drained first, so the LAST step gets its "it survived" call too - without
            // this, finishing setup left that step's crash-guard marker open.
            if (_wait > 0)
            {
                _wait--;
                if (_wait == 0) OnStepSettled?.Invoke(); // the step survived its pause
                if (_step < StepNames.Length) return false;
            }
            if (_step >= StepNames.Length) return true;

            OnStep?.Invoke(Tag + ":" + StepNames[_step]);
            if (SlenderMod.Diagnostics) _log.Msg("[step] " + Tag + " " + (_step + 1) + "/" + StepNames.Length + ": " + StepNames[_step]);
            int parts = _data.Groups.Count;
            switch (_step)
            {
                case 0: // one mesh per part; all share the vertex data
                    if (_partMeshes.Count == 0)
                    {
                        for (int g = 0; g < parts; g++)
                        {
                            var mesh = new Mesh();
                            if (_data.Verts.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
                            { Il2CppStructArray<Vector3> va = _data.Verts; mesh.vertices = va; }
                            { Il2CppStructArray<Vector3> na = _data.Normals; mesh.normals = na; }
                            { Il2CppStructArray<Vector2> ua = _data.Uvs; mesh.uv = ua; }
                            { Il2CppStructArray<int> ta = _data.Groups[g]; mesh.triangles = ta; }
                            mesh.RecalculateBounds();
                            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                            _partMeshes.Add(mesh);
                        }
                    }
                    break;
                case 1:
                    if (_textures.Count == 0) LoadTextures();
                    break;
                case 2:
                    _root = new GameObject("SlenderMode_" + Tag);
                    _root.SetActive(false);
                    _parts.Clear();
                    for (int g = 0; g < parts; g++)
                    {
                        var child = new GameObject("SlenderMode_" + Tag + "_Part" + g);
                        child.transform.SetParent(_root.transform, false);
                        _parts.Add(child);
                    }
                    break;
                case 3:
                    for (int g = 0; g < parts; g++) _parts[g].AddComponent<MeshFilter>().sharedMesh = _partMeshes[g];
                    break;
                case 4:
                    _partRenderers.Clear();
                    for (int g = 0; g < parts; g++) _partRenderers.Add(_parts[g].AddComponent<MeshRenderer>());
                    break;
                case 5:
                    _shared = FindSourceMaterial(out string how);
                    if (_shared == null) { _log.Warning("No usable material found; model stays hidden."); _failed = true; return false; }
                    _log.Msg("  using scene material: " + how);
                    break;
                case 6:
                    foreach (var r in _partRenderers) r.sharedMaterial = _shared;
                    break;
                case 7:
                    for (int g = 0; g < parts; g++)
                    {
                        var tex = g < _textures.Count && _textures[g] != null ? _textures[g] : DarkTexture();
                        var block = new MaterialPropertyBlock();
                        block.SetTexture("_MainTex", tex);
                        Neutralize(block);
                        _partRenderers[g].SetPropertyBlock(block);
                        _blocks.Add(block); // keep alive
                    }
                    break;
                case 8:
                    _root.SetActive(true);
                    _materialsReady = true;
                    _log.Msg((Tag == "chaser" ? "Chaser" : "Creature") + " model ready.");
                    break;
            }
            _step++;
            _wait = FramesBetweenSteps;
            return _step >= StepNames.Length;
        }
        readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();

        /// <summary>Backup in case a tinted material slips through: plain white tint, no extra colour.</summary>
        internal static void Neutralize(MaterialPropertyBlock b)
        {
            b.SetColor("_Color", Color.white);
            b.SetColor("_TintColor", Color.white);
            b.SetColor("_Tint", Color.white);
        }

        Texture2D _dark;
        Texture2D DarkTexture()
        {
            if (_dark != null) return _dark;
            _dark = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var cols = new Color32[16];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(10, 10, 12, 255);
            Il2CppStructArray<Color32> arr = cols;
            _dark.SetPixels32(arr);
            _dark.Apply(false, false);
            _dark.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _dark;
        }

        void LoadTextures()
        {
            _textures.Clear();
            var files = new List<string>();
            foreach (var f in Directory.GetFiles(_folder))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") files.Add(f);
            }
            foreach (string group in _data.GroupNames)
            {
                string token = group;
                int us = group.LastIndexOf('_');
                if (us >= 0 && us < group.Length - 1) token = group.Substring(us + 1);
                string pick = BestTexture(files, token.ToLowerInvariant());
                Texture2D tex = null;
                if (pick != null)
                {
                    try
                    {
                        tex = new Texture2D(2, 2);
                        Il2CppStructArray<byte> bytes = File.ReadAllBytes(pick);
                        if (!ImageConversion.LoadImage(tex, bytes)) tex = null;
                        else tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    }
                    catch (Exception e) { _log.Warning("Texture failed: " + pick + " (" + e.Message + ")"); tex = null; }
                    _log.Msg("  part '" + token + "' -> " + Path.GetFileName(pick) + (tex == null ? " (FAILED)" : ""));
                }
                _textures.Add(tex);
            }
        }

        static string BestTexture(List<string> files, string token)
        {
            string best = null; int bestScore = -1;
            foreach (var f in files)
            {
                string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (n.Contains("normal") || n.Contains("rough") || n.Contains("metal")) continue;
                int score = 0;
                if (n.Contains(token)) score += 10;
                if (n.Contains("basecolor") || n.Contains("albedo") || n.Contains("diffuse") || n.Contains("color")) score += 3;
                if (score > bestScore) { bestScore = score; best = f; }
            }
            return best;
        }

        /// <summary>
        /// Finds an existing opaque material to clone: one using Unity's Standard shader if the scene
        /// has one, otherwise the game's plain non-skinned standard shader.
        /// </summary>
        Material FindSourceMaterial(out string how) => FindSceneMaterial(_log, out how);

        // The search below walks every MeshRenderer in the scene and reads a name off each one, which
        // is not cheap. Three things want a material (Slender, the chaser, the pages), and they all
        // want the same one, so the answer is worked out once per scene and shared.
        static Material _sceneMat;
        static string _sceneMatHow, _sceneMatScene;

        internal static void ForgetSceneMaterial() { _sceneMat = null; _sceneMatScene = null; }

        internal static Material FindSceneMaterial(MelonLogger.Instance _log, out string how)
        {
            string scene = "";
            try { scene = GameManager.m_ActiveScene ?? ""; } catch { }
            if (_sceneMat != null && _sceneMatScene == scene)
            {
                how = _sceneMatHow;
                return _sceneMat;
            }

            Material best = null; int bestScore = int.MinValue; string bestName = null;
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<MeshRenderer>());
                int n = 0;
                foreach (var o in all)
                {
                    if (++n > 4000) break;
                    var r = o.TryCast<MeshRenderer>();
                    if (r == null || !r.enabled) continue;
                    if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    string objName = r.gameObject.name.ToLowerInvariant();
                    if (objName.Contains("shadow") || objName.Contains("_col") || objName.Contains("lod") || objName.Contains("proxy")) continue;
                    var m = r.sharedMaterial;
                    if (m == null || m.shader == null) continue;
                    string name = m.shader.name;
                    int score = name == "Standard" ? 100 : ScoreShader(name.ToLowerInvariant());
                    if (score == int.MinValue) continue;
                    if (score > bestScore) { bestScore = score; best = m; bestName = name + " (on '" + r.gameObject.name + "')"; }
                }
            }
            catch (Exception e) { _log.Warning("Material search failed: " + e.Message); }
            how = best != null ? bestName : "none";
            if (best != null) { _sceneMat = best; _sceneMatHow = how; _sceneMatScene = scene; }
            return best;
        }

        /// <summary>int.MinValue = never use.</summary>
        static int ScoreShader(string n)
        {
            foreach (var bad in new[] { "skinned", "skin", "terrain", "tree", "leaf", "leaves", "foliage", "grass", "water", "ice",
                                        "transparent", "particle", "glass", "decal", "sky", "snow", "hidden", "/fx", "ui/",
                                        "billboard", "wind", "cloth", "hair", "fur", "fade", "cutout", "additive", "blend",
                                        "shadow", "projector", "volum", "fog", "impostor", "instanc", "vertexanim", "emiss", "glow" })
                if (n.Contains(bad)) return int.MinValue;
            int score = 0;
            // tinted / colour-mask shaders paint their own colour over the texture (orange spray can on the pages):
            // only a last resort
            foreach (var soft in new[] { "tint", "mask", "paint", "dye", "color", "colour" })
                if (n.Contains(soft)) score -= 1000;
            if (n.Contains("standard")) score += 10;
            if (n.Contains("tld")) score += 3;
            if (n.Contains("diffuse")) score += 2;
            if (n.Contains("specular") || n.Contains("bumped")) score += 1;
            return score;
        }

        // ------------------------------------------------------------ per frame

        public bool IsShowing { get { try { return _root != null && _materialsReady && _root.activeSelf; } catch { return false; } } }

        /// <summary>Shows or hides the creature. Returns true if it is actually visible.</summary>
        public bool Show(bool visible)
        {
            if (!visible)
            {
                if (_root != null && _root.activeSelf) _root.SetActive(false);
                return false;
            }
            try
            {
                if (!BuildStepwise()) return false;
                if (!_root.activeSelf) _root.SetActive(true);
                return true;
            }
            catch (Exception e)
            {
                _failed = true;
                Status = "Model disabled after error: " + e.Message;
                _log.Error(Status);
                try { if (_root != null) _root.SetActive(false); } catch { }
                return false;
            }
        }

        // ---------------------------------------------------------- reaching arms
        // The model has no skeleton, so the arms are bent in the mesh itself: each vertex gets an "arm"
        // weight (0 at the torso, 1 on the arm, smooth across the shoulder) and is rotated forward around
        // the shoulder. Updated at most ~15 times a second and only when the angle changed.
        float[] _armW;
        Vector3[] _deformed;
        float _lastReach = -999f, _lastReachTime;
        bool _armsBroken;
        // Fitted to the slenderman.glb model (fractions of its height, so any size works):
        // the sleeves and hands sit outside the torso/trousers, the shoulder pivot is at 80 % height.
        const float ArmXUpper = 0.115f, ArmXLower = 0.104f, ArmLowerBelow = 0.62f;
        const float ArmBottom = 0.25f, ArmTop = 0.86f;
        const float FadeTop = 0.83f, FadeBottom = 0.75f;      // weight 0 at the shoulder top, 1 below the armpit
        const float PivotY = 0.80f, PivotZ = -0.015f;
        float _pivotY, _pivotZ;

        void BuildArmWeights()
        {
            var v = _data.Verts;
            _armW = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float h = _data.Height;
                float ax = Math.Abs(v[i].x) / h, y = v[i].y / h;
                bool arm = ax > (y < ArmLowerBelow ? ArmXLower : ArmXUpper) && y > ArmBottom && y < ArmTop;
                float t = Math.Min(1f, Math.Max(0f, (FadeTop - y) / (FadeTop - FadeBottom)));
                // only ONE arm reaches (the +x side); the other keeps hanging
                _armW[i] = (arm && v[i].x > 0f) ? t * t * (3f - 2f * t) : 0f;
            }
            _pivotY = PivotY * _data.Height;
            _pivotZ = PivotZ * _data.Height;
            _deformed = new Vector3[v.Length];
        }

        /// <summary>Raise both arms forward by this many degrees (0 = hanging).</summary>
        public void SetReach(float degrees)
        {
            if (_armsBroken || _data == null || _partMeshes.Count == 0 || !_materialsReady) return;
            float now = UnityEngine.Time.unscaledTime;
            if (Math.Abs(degrees - _lastReach) < 0.75f || now - _lastReachTime < 0.066f) return;
            _lastReach = degrees; _lastReachTime = now;
            try
            {
                if (_armW == null) BuildArmWeights();
                var v = _data.Verts;
                float rad = degrees * (float)(Math.PI / 180.0);
                for (int i = 0; i < v.Length; i++)
                {
                    float w = _armW[i];
                    if (w <= 0f) { _deformed[i] = v[i]; continue; }
                    float a = rad * w;
                    float c = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                    float dy = v[i].y - _pivotY, dz = v[i].z - _pivotZ;   // rotate in the y/z plane: down -> forward (+z)
                    _deformed[i] = new Vector3(v[i].x, _pivotY + c * dy - sn * dz, _pivotZ + sn * -dy + c * dz);
                }
                Il2CppStructArray<Vector3> arr = _deformed;
                foreach (var m in _partMeshes) { m.vertices = arr; m.RecalculateBounds(); }
            }
            catch (Exception e) { _log.Warning("Arm animation disabled: " + e.Message); _armsBroken = true; }
        }

        public void Place(Vector3 feet, Vector3 lookAt, float height, float yawOffset)
        {
            if (_root == null || _data == null) return;
            float dx = lookAt.x - feet.x, dz = lookAt.z - feet.z;
            float yaw = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI) + yawOffset;
            _root.transform.position = feet;
            _root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            float s = height / _data.Height;
            _root.transform.localScale = new Vector3(s, s, s);
        }

        public void OnSceneChanged()
        {
            try { if (_root != null) _root.SetActive(false); } catch { }
            _root = null;
            _parts.Clear();
            _partRenderers.Clear();
            _blocks.Clear();
            _shared = null;               // scene materials die with the scene
            _materialsReady = false;
            ForgetSceneMaterial();
            // the meshes live on, but nothing has been written into them for this scene yet
            _lastPhase = -2f; _lastReach = -999f;
            if (_step > 2) { _step = 2; _wait = FramesBetweenSteps; }
        }
    }
}
