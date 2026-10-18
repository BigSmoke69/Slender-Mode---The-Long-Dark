using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SlenderMode
{
    /// <summary>Screen-space drawing: the built-in creature, TV static, fades and captions.</summary>
    internal sealed class Visuals
    {
        const int CreatureW = 256, CreatureH = 640;
        const int StaticW = 192, StaticH = 108, StaticFrames = 6;
        public const float CreatureAspect = CreatureW / (float)CreatureH;

        Texture2D _creature;
        Texture2D[] _static;
        Texture2D _white;
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
            int frame = (int)(Time.unscaledTime * 24f) % StaticFrames;
            var tex = _static[frame];
            if (tex == null) return;
            // random offset so the pattern never looks frozen
            float ox = (float)_rng.NextDouble() * Screen.width, oy = (float)_rng.NextDouble() * Screen.height;
            SlenderMod.Instance?.T("gui: static noise");
            GUI.color = new Color(1f, 1f, 1f, Math.Min(0.92f, level));
            GUI.DrawTexture(new Rect(-ox, -oy, Screen.width * 2f, Screen.height * 2f), tex);
            // dark shade that deepens with the static
            SlenderMod.Instance?.T("gui: static shade");
            GUI.color = new Color(0f, 0f, 0f, level * 0.35f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
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
