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
            foreach (var f in Directory.GetFiles(folder, "*.obj")) { obj = f; break; }
            if (obj == null) { Status = "No .obj in model folder"; return; }

            Status = "Parsing " + Path.GetFileName(obj);
            _parse = Task.Run(() => Parse(obj));
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

        /// <summary>Called with "model:<step>" before each step (the mod writes it to the crash guard).</summary>
        public Action<string> OnStep;
        public Action OnStepSettled;

        bool BuildStepwise()
        {
            if (_failed || _data == null) return false;
            if (_step >= StepNames.Length) return true;
            if (_wait > 0)
            {
                _wait--;
                if (_wait == 0) OnStepSettled?.Invoke(); // the step survived its pause
                return false;
            }

            OnStep?.Invoke("model:" + StepNames[_step]);
            if (SlenderMod.Diagnostics) _log.Msg("[step] model " + (_step + 1) + "/" + StepNames.Length + ": " + StepNames[_step]);
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
                    _root = new GameObject("SlenderMode_Creature");
                    _root.SetActive(false);
                    _parts.Clear();
                    for (int g = 0; g < parts; g++)
                    {
                        var child = new GameObject("SlenderMode_Part" + g);
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
                        _partRenderers[g].SetPropertyBlock(block);
                        _blocks.Add(block); // keep alive
                    }
                    break;
                case 8:
                    _root.SetActive(true);
                    _materialsReady = true;
                    _log.Msg("Creature model ready.");
                    break;
            }
            _step++;
            _wait = FramesBetweenSteps;
            return _step >= StepNames.Length;
        }
        readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();

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
        Material FindSourceMaterial(out string how)
        {
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
            return best;
        }

        /// <summary>int.MinValue = never use.</summary>
        static int ScoreShader(string n)
        {
            foreach (var bad in new[] { "skinned", "skin", "terrain", "tree", "leaf", "leaves", "foliage", "grass", "water", "ice",
                                        "transparent", "particle", "glass", "decal", "sky", "snow", "hidden", "/fx", "ui/",
                                        "billboard", "wind", "cloth", "hair", "fur", "fade", "cutout", "additive", "blend",
                                        "shadow", "projector", "volum", "fog", "impostor", "instanc", "vertexanim" })
                if (n.Contains(bad)) return int.MinValue;
            int score = 0;
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
            if (_step > 2) { _step = 2; _wait = FramesBetweenSteps; }
        }
    }
}
