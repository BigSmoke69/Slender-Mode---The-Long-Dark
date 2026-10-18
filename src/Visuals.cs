using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SlenderMode
{
    /// <summary>Screen-space drawing: the built-in creature, TV static, fades and captions.</summary>
    internal sealed class Visuals
    {
        const int CreatureW = 256, CreatureH = 640;
        const int StaticW = 256, StaticH = 144, StaticFrames = 10;
        public const float CreatureAspect = CreatureW / (float)CreatureH;

        Texture2D _creature;
        Texture2D[] _static;
        Texture2D _white;
        Texture2D[] _puffs;
        const int PuffVariants = 4;
        GUIStyle _caption;
        readonly System.Random _rng = new System.Random();

        public void Ensure()
        {
            if (_creature == null)
                _creature = MakeTexture(CreatureArt.PaintCreature(CreatureW, CreatureH), CreatureW, CreatureH, FilterMode.Bilinear);

            if (_static == null || _static[0] == null)
            {
                _static = new Texture2D[StaticFrames];
                for (int i = 0; i < StaticFrames; i++)
                    _static[i] = MakeTexture(CreatureArt.PaintStatic(StaticW, StaticH, 1000 + i), StaticW, StaticH, FilterMode.Bilinear);
            }

            if (_puffs == null || _puffs[0] == null)
            {
                _puffs = new Texture2D[PuffVariants];
                for (int i = 0; i < PuffVariants; i++)
                    _puffs[i] = MakeTexture(CreatureArt.PaintPuff(64, 77 + i * 31), 64, 64, FilterMode.Bilinear);
            }

            if (_white == null)
            {
                var px = new byte[16 * 16 * 4];
                for (int i = 0; i < px.Length; i++) px[i] = 255;
                _white = MakeTexture(px, 16, 16, FilterMode.Bilinear);
            }
        }

        static Texture2D MakeTexture(byte[] rgba, int w, int h, FilterMode filter)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = filter;
            tex.wrapMode = TextureWrapMode.Clamp;
            var cols = new Color32[w * h];
            for (int i = 0; i < cols.Length; i++)
                cols[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
            Il2CppStructArray<Color32> arr = cols;
            tex.SetPixels32(arr);
            tex.Apply(false, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }

        /// <summary>Draws the built-in creature standing at feetWorld, as seen through cam.</summary>
        public void DrawCreature(Camera cam, Vector3 feetWorld, float height, float alpha, float jitter)
        {
            if (_creature == null || alpha <= 0.01f) return;
            Vector3 feet = cam.WorldToScreenPoint(feetWorld);
            Vector3 head = cam.WorldToScreenPoint(feetWorld + Vector3.up * height);
            if (feet.z <= 0.1f || head.z <= 0.1f) return;

            float hPx = (feet.y - head.y);
            if (hPx < 0f) hPx = -hPx;
            if (hPx < 2f) return;
            float wPx = hPx * CreatureAspect;
            float top = Screen.height - Math.Max(feet.y, head.y);
            float cx = (feet.x + head.x) * 0.5f;

            float jx = jitter > 0f ? (float)(_rng.NextDouble() * 2 - 1) * jitter : 0f;
            float jy = jitter > 0f ? (float)(_rng.NextDouble() * 2 - 1) * jitter * 0.4f : 0f;

            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(cx - wPx / 2f + jx, top + jy, wPx, hPx), _creature);
            GUI.color = Color.white;
        }

        /// <summary>Kill close-up: its head fills the screen.</summary>
        public void DrawCloseUp(float zoom, float alpha, float shake)
        {
            if (_creature == null) return;
            // head + antler region of the texture (UV origin bottom-left)
            var uv = new Rect(0.08f, 0.62f, 0.84f, 0.38f);
            float hPx = Screen.height * 1.15f * zoom;
            float wPx = hPx * (uv.width * CreatureW) / (uv.height * CreatureH);
            float sx = (float)(_rng.NextDouble() * 2 - 1) * shake;
            float sy = (float)(_rng.NextDouble() * 2 - 1) * shake;
            var r = new Rect((Screen.width - wPx) / 2f + sx, (Screen.height - hPx) * 0.42f + sy, wPx, hPx);

            // full-texture rect such that the head region lands exactly on r
            float fullW = r.width / uv.width, fullH = r.height / uv.height;
            var full = new Rect(r.x - uv.x * fullW, r.y - (1f - uv.y - uv.height) * fullH, fullW, fullH);

            SlenderMod.Instance?.T("gui: close-up shade");
            GUI.color = new Color(0f, 0f, 0f, alpha * 0.75f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
            SlenderMod.Instance?.T("gui: close-up creature");
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(full, _creature);
            GUI.color = Color.white;
        }

        public void DrawStatic(float level)
        {
            if (level <= 0.01f || _static == null) return;
            float t = Time.unscaledTime;
            int frame = (int)(t * 20f) % StaticFrames;
            var tex = _static[frame];
            if (tex == null) return;
            float W = Screen.width, H = Screen.height;

            // camcorder breakup: full-screen frame with a small sideways shake (bigger when stronger)
            float shake = (float)(_rng.NextDouble() * 2 - 1) * (4f + level * 18f);
            float flicker = 0.85f + (float)_rng.NextDouble() * 0.15f;
            SlenderMod.Instance?.T("gui: static noise");
            GUI.color = new Color(1f, 1f, 1f, level >= 0.999f ? 1f : Math.Min(0.92f, level) * flicker); // 1 = signal lost
            GUI.DrawTexture(new Rect(shake - 24f, 0, W + 48f, H), tex);

            // a dark bar rolling down the picture, like a tracking error
            float barH = H * 0.14f;
            float barY = (t * 0.45f % 1.2f) * H - barH;
            GUI.color = new Color(0f, 0f, 0f, level * 0.45f);
            GUI.DrawTexture(new Rect(0, barY, W, barH), _white);

            // overall darkening that deepens with the static
            SlenderMod.Instance?.T("gui: static shade");
            GUI.color = new Color(0f, 0f, 0f, level * 0.3f);
            GUI.DrawTexture(new Rect(0, 0, W, H), _white);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Jumpscare breakup in the style of Slender: The Arrival: the picture tears into horizontal
        /// strips that slide sideways, with red/cyan fringes, black dropouts and a flash at the peak.
        /// Only plain GUI.DrawTexture calls (the safe path in this game version).
        /// </summary>
        public void DrawGlitch(float level)
        {
            if (level <= 0.02f || _static == null || _white == null) return;
            float W = Screen.width, H = Screen.height;
            float t = Time.unscaledTime;

            // a white flash right at the peak of the sting
            if (level > 0.85f)
            {
                GUI.color = new Color(1f, 1f, 1f, (level - 0.85f) * 2.2f);
                GUI.DrawTexture(new Rect(0, 0, W, H), _white);
            }

            int strips = 3 + (int)(level * 12f);
            for (int i = 0; i < strips; i++)
            {
                float y = (float)_rng.NextDouble() * H;
                float h = 2f + (float)_rng.NextDouble() * H * (0.015f + 0.06f * level);
                float slide = (float)(_rng.NextDouble() * 2 - 1) * (30f + 140f * level);
                double kind = _rng.NextDouble();
                if (kind < 0.5)
                {
                    // torn strip of picture noise, shoved sideways
                    var tex = _static[_rng.Next(StaticFrames)];
                    if (tex == null) continue;
                    GUI.color = new Color(1f, 1f, 1f, 0.55f + 0.4f * level);
                    GUI.DrawTexture(new Rect(slide - 160f, y, W + 320f, h), tex);
                }
                else if (kind < 0.75)
                {
                    // colour fringe: red or cyan sliver
                    bool red = _rng.NextDouble() < 0.5;
                    GUI.color = red ? new Color(1f, 0.1f, 0.1f, 0.35f * level) : new Color(0.1f, 0.9f, 1f, 0.3f * level);
                    GUI.DrawTexture(new Rect(slide, y, W, h * 0.6f), _white);
                }
                else
                {
                    // signal dropout
                    GUI.color = new Color(0f, 0f, 0f, 0.6f + 0.35f * level);
                    GUI.DrawTexture(new Rect(0, y, W, h), _white);
                }
            }

            // the whole picture briefly rolls (vertical hold slipping)
            if (level > 0.5f && ((int)(t * 30f) % 3 == 0))
            {
                var tex = _static[(int)(t * 20f) % StaticFrames];
                if (tex != null)
                {
                    float roll = (float)_rng.NextDouble() * H;
                    GUI.color = new Color(1f, 1f, 1f, 0.5f * level);
                    GUI.DrawTexture(new Rect(0, roll, W, H * 0.08f), tex);
                }
            }
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------ pages and smoke

        /// <summary>
        /// A thin column of smoke rising from basePos, like a fire that was just put out.
        /// Drawn as soft puffs projected onto the screen (the safe drawing path in this game version).
        /// visibility 0..1 fades it when a hill or building is in the way.
        /// </summary>
        public void DrawSmoke(Camera cam, Vector3 basePos, int seed, float visibility)
        {
            if (_puffs == null || visibility <= 0.01f) return;
            Vector3 cp = cam.transform.position;
            float dist = Vector3.Distance(new Vector3(cp.x, basePos.y, cp.z), basePos);
            float far = Mathf.Clamp01(1f - (dist - 300f) / 250f);     // fades out from 300 to 550 m
            if (far <= 0.01f) return;
            // Behind you, there is nothing to draw: worth one check to save a column's worth of
            // projections (every puff is a call across into the engine).
            Vector3 baseSp = cam.WorldToScreenPoint(basePos + Vector3.up * 8f);
            if (baseSp.z < 1.0f) return;

            float t = Time.time;                                       // game time: smoke freezes while paused
            float tanHalf = (float)Math.Tan(cam.fieldOfView * 0.5f * Deg);
            float pxPerUnit = Screen.height / (2f * tanHalf);
            Vector3 wind = new Vector3(0.8f, 0f, 0.55f);
            const float Rise = 18f, Period = 14f;                     // metres risen, seconds per puff life
            // A column 400 m off is a few pixels wide: thinning it out there costs nothing to look at.
            const int MaxPuffs = 26;
            int puffs = dist < 60f ? MaxPuffs : dist < 150f ? 18 : dist < 280f ? 12 : 8;

            // fewer puffs means less of them overlapping, so each one carries a little more
            float thin = Math.Min(1.5f, MaxPuffs / (float)puffs);

            for (int i = 0; i < puffs; i++)
            {
                // spread over the whole rise whatever the count, so the column is never a stub
                float age = ((t / Period) + i / (float)puffs + seed * 0.137f) % 1f;
                float hgt = age * Rise;
                float wob = (float)Math.Sin(t * 0.35f + i * 1.7f + seed) * (0.3f + age * 1.4f);
                Vector3 w = basePos + Vector3.up * (0.4f + hgt)
                          + wind * (hgt * hgt * 0.035f) + new Vector3(wob, 0f, wob * 0.6f);
                Vector3 sp = cam.WorldToScreenPoint(w);
                if (sp.z < 1.0f) continue;
                float size = (0.7f + age * 4.5f) * pxPerUnit / sp.z;
                if (size < 1.5f) continue;
                if (size > Screen.height * 1.5f) continue;
                float a = Mathf.Clamp01(age / 0.12f) * (1f - age) * (1f - age);   // fade in at the base, thin out as it rises
                a *= 0.42f * far * visibility * thin;
                if (a < 0.01f) continue;
                float shade = 0.5f + 0.12f * (1f - age);
                GUI.color = new Color(shade, shade, shade * 1.05f, a);
                var tex = _puffs[(i + seed) % PuffVariants];
                float y = Screen.height - sp.y;
                GUI.DrawTexture(new Rect(sp.x - size * 0.5f, y - size * 0.5f, size, size), tex);
            }
            GUI.color = Color.white;
        }
        const float Deg = (float)(Math.PI / 180.0);

        /// <summary>Fallback when 3D pages are off: the page as a small sprite at its spot.</summary>
        public void DrawPageSprite(Camera cam, Vector3 pos, Texture2D tex)
        {
            if (tex == null) return;
            Vector3 sp = cam.WorldToScreenPoint(pos);
            if (sp.z < 0.3f || sp.z > 60f) return;
            float tanHalf = (float)Math.Tan(cam.fieldOfView * 0.5f * Deg);
            float h = 0.28f * Screen.height / (2f * tanHalf) / sp.z;
            if (h < 2f) return;
            float w = h * 0.5f;
            GUI.color = new Color(0.8f, 0.8f, 0.78f, 1f);
            GUI.DrawTexture(new Rect(sp.x - w / 2f, Screen.height - sp.y - h / 2f, w, h), tex);
            GUI.color = Color.white;
        }

        GUIStyle _small, _title;

        GUIStyle Small()
        {
            if (_small == null)
            {
                _small = new GUIStyle(GUI.skin.label);
                _small.fontSize = 20;
                _small.fontStyle = FontStyle.Bold;
                _small.alignment = TextAnchor.MiddleLeft;
                _small.normal.textColor = Color.white;
            }
            return _small;
        }

        void Shadowed(Rect r, string text, GUIStyle st, Color c)
        {
            GUI.color = new Color(0f, 0f, 0f, c.a * 0.85f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, st);
            GUI.color = c;
            GUI.Label(r, text, st);
            GUI.color = Color.white;
        }

        /// <summary>"Pages 3/8" on the left, under the distance readout.</summary>
        public void DrawPageCounter(int got, int total)
        {
            var st = Small(); st.alignment = TextAnchor.MiddleLeft;
            Shadowed(new Rect(24f, Screen.height * 0.5f + 22f, 260f, 32f), "Pages " + got + "/" + total, st, new Color(0.9f, 0.88f, 0.8f, 0.95f));
        }

        public void DrawHint(string text)
        {
            var st = Small(); st.alignment = TextAnchor.MiddleCenter;
            Shadowed(new Rect(Screen.width * 0.5f - 250f, Screen.height * 0.58f, 500f, 32f), text, st, new Color(1f, 1f, 1f, 0.95f));
        }

        /// <summary>Editor: a label over a recorded spot.</summary>
        public void DrawSpotLabel(Camera cam, Vector3 pos, string text)
        {
            Vector3 sp = cam.WorldToScreenPoint(pos + Vector3.up * 0.3f);
            if (sp.z < 0.3f || sp.z > 400f) return;
            var st = Small(); st.alignment = TextAnchor.MiddleCenter;
            Shadowed(new Rect(sp.x - 120f, Screen.height - sp.y - 16f, 240f, 32f), text, st, new Color(1f, 0.85f, 0.4f, 1f));
        }

        /// <summary>The ending: black screen with two lines.</summary>
        public void DrawEndCard(string line1, string line2, float alpha2)
        {
            if (_white == null) return;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label);
                _title.fontSize = 46;
                _title.fontStyle = FontStyle.Bold;
                _title.alignment = TextAnchor.MiddleCenter;
                _title.normal.textColor = Color.white;
            }
            GUI.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            GUI.Label(new Rect(0, Screen.height * 0.40f, Screen.width, 70f), line1, _title);
            if (alpha2 > 0.01f && !string.IsNullOrEmpty(line2))
            {
                var st = Small(); st.alignment = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.7f, 0.7f, 0.7f, alpha2);
                GUI.Label(new Rect(0, Screen.height * 0.40f + 80f, Screen.width, 40f), line2, st);
            }
            GUI.color = Color.white;
        }

        Texture2D _vignette, _vignetteW;

        /// <summary>Lens-edge falloff. white=true gives a tintable white version.</summary>
        Texture2D Vignette(bool white)
        {
            if (white && _vignetteW != null) return _vignetteW;
            if (!white && _vignette != null) return _vignette;
            const int S = 128;
            var px = new byte[S * S * 4];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                float r = (float)Math.Sqrt(u * u * 0.85f + v * v);
                float a = Mathf.Clamp01((r - 0.45f) / 0.75f);
                a = a * a * (3f - 2f * a);
                int i = (y * S + x) * 4;
                if (white) { px[i] = 255; px[i + 1] = 255; px[i + 2] = 255; }
                else { px[i] = 0; px[i + 1] = 1; px[i + 2] = 4; }
                px[i + 3] = (byte)(a * 255f);
            }
            var tex = MakeTexture(px, S, S, FilterMode.Bilinear);
            if (white) _vignetteW = tex; else _vignette = tex;
            return tex;
        }

        /// <summary>
        /// The only thing drawn during a chase: as your light holds on it, the lens edges wash
        /// pale, so you can see the light biting before it actually breaks off. No colour flash.
        /// </summary>
        public void DrawChaseEdge(float lightHold)
        {
            if (lightHold <= 0.02f || _white == null) return;
            GUI.color = new Color(0.88f, 0.92f, 1f, Mathf.Clamp01(lightHold) * 0.45f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Vignette(true));
            GUI.color = Color.white;
        }

        /// <summary>
        /// Slender: The Arrival night grade over the world: a cold navy darkening, heavy dark edges like
        /// a camcorder lens, and a light film grain. strength 0..~0.8 (the "Extra darkness" setting).
        /// </summary>
        public void DrawNightGrade(float strength)
        {
            if (strength <= 0.01f || _white == null) return;
            float W = Screen.width, H = Screen.height;
            // cold, deep-blue darkness
            GUI.color = new Color(0.006f, 0.012f, 0.035f, Math.Min(0.92f, strength));
            GUI.DrawTexture(new Rect(0, 0, W, H), _white);
            // dark lens edges
            GUI.color = new Color(1f, 1f, 1f, Math.Min(1f, 0.55f + strength));
            GUI.DrawTexture(new Rect(0, 0, W, H), Vignette(false));
            // faint grain
            if (_static != null)
            {
                var tex = _static[(int)(Time.unscaledTime * 24f) % StaticFrames];
                if (tex != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.035f + 0.03f * strength);
                    GUI.DrawTexture(new Rect(0, 0, W, H), tex);
                }
            }
            GUI.color = Color.white;
        }

        public void DrawFade(Color c, float alpha)
        {
            if (alpha <= 0.001f || _white == null) return;
            SlenderMod.Instance?.T("gui: fade " + alpha.ToString("0.00"));
            GUI.color = new Color(c.r, c.g, c.b, Math.Min(1f, alpha));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
            GUI.color = Color.white;
        }

        GUIStyle _distStyle;

        /// <summary>Distance readout on the left edge of the screen; turns red as it gets close.</summary>
        public void DrawDistance(float meters)
        {
            if (_distStyle == null)
            {
                _distStyle = new GUIStyle(GUI.skin.label);
                _distStyle.fontSize = 26;
                _distStyle.fontStyle = FontStyle.Bold;
                _distStyle.alignment = TextAnchor.MiddleLeft;
                _distStyle.normal.textColor = Color.white;
            }
            string text = meters.ToString("0") + " m";
            float close = Mathf.Clamp01(1f - meters / 45f);           // 45 m and further = white, 0 m = red
            var r = new Rect(24f, Screen.height * 0.5f - 20f, 220f, 40f);
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, _distStyle);
            GUI.color = new Color(1f, 1f - close * 0.8f, 1f - close * 0.8f, 1f);
            GUI.Label(r, text, _distStyle);
            GUI.color = Color.white;
        }

        public void DrawCaption(string text, float alpha)
        {
            if (string.IsNullOrEmpty(text) || alpha <= 0.01f) return;
            if (_caption == null)
            {
                _caption = new GUIStyle(GUI.skin.label);
                _caption.fontSize = 22;
                _caption.fontStyle = FontStyle.Italic;
                _caption.alignment = TextAnchor.MiddleCenter;
                _caption.wordWrap = true;
                _caption.normal.textColor = Color.white;
            }
            var r = new Rect(Screen.width * 0.15f, Screen.height * 0.72f, Screen.width * 0.7f, 60f);
            GUI.color = new Color(0f, 0f, 0f, alpha * 0.8f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, _caption);
            GUI.color = new Color(0.85f, 0.85f, 0.85f, alpha);
            GUI.Label(r, text, _caption);
            GUI.color = Color.white;
        }
    }
}
