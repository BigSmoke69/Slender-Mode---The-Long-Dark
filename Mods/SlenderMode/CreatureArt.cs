using System;

namespace SlenderMode
{
    /// <summary>
    /// Procedurally paints the default creature (a gaunt antlered Wendigo silhouette)
    /// and the TV-static frames. Pure managed code: returns RGBA bytes, bottom row first.
    /// </summary>
    internal static class CreatureArt
    {
        // World-space canvas (meters): x in [-HalfW, HalfW], y in [0, H]
        const float H = 3.2f;
        const float HalfW = 0.64f;

        public static byte[] PaintCreature(int w, int h)
        {
            var px = new byte[w * h * 4];
            float unitsPerPx = H / h;
            var rng = new Random(1337);

            for (int py = 0; py < h; py++)
            for (int pxi = 0; pxi < w; pxi++)
            {
                float x = -HalfW + (pxi + 0.5f) * unitsPerPx;
                float y = (py + 0.5f) * unitsPerPx;

                // Ragged, fur-like edge: small wobble of the distance field
                float wobble = (Hash(pxi * 7 + py * 131) - 0.5f) * 0.012f
                             + (float)Math.Sin(y * 90f + x * 40f) * 0.004f;
                float d = Body(x, y) + wobble;

                float aa = unitsPerPx * 1.2f;
                float bodyA = Clamp01(0.5f - d / aa);

                // body tone: near-black with faint cold variation, slight rim light
                float rim = Clamp01(1f - Math.Abs(d + 0.02f) / 0.02f) * 0.5f;
                float tone = 7f + Hash(pxi * 31 + py * 17) * 6f + rim * 14f;
                float r = tone, g = tone, b = tone * 1.15f, a = bodyA * 255f;

                // Eyes: pale, reflective, with a warm glow halo
                float eye = Math.Min(Circle(x, y, -0.048f, 2.475f, 0.017f), Circle(x, y, 0.048f, 2.475f, 0.017f));
                float eyeCore = Clamp01(0.5f - eye / aa);
                float glowDist = Math.Max(0f, eye);
                float glow = (float)Math.Exp(-glowDist / 0.03f) * 0.85f;
                if (glow > 0.01f)
                {
                    float gr = 255f, gg = 170f, gb = 90f;
                    r = Lerp(r, gr, glow); g = Lerp(g, gg, glow); b = Lerp(b, gb, glow);
                    a = Math.Max(a, glow * 200f);
                }
                if (eyeCore > 0f)
                {
                    r = Lerp(r, 255f, eyeCore); g = Lerp(g, 246f, eyeCore); b = Lerp(b, 214f, eyeCore);
                    a = Math.Max(a, eyeCore * 255f);
                }

                int i = (py * w + pxi) * 4;
                px[i] = (byte)Clamp(r, 0, 255);
                px[i + 1] = (byte)Clamp(g, 0, 255);
                px[i + 2] = (byte)Clamp(b, 0, 255);
                px[i + 3] = (byte)Clamp(a, 0, 255);
            }
            return px;
        }

        /// <summary>Signed distance (meters) to the creature silhouette, mirrored left/right.</summary>
        static float Body(float x, float y)
        {
            float ax = Math.Abs(x); // symmetric
            float d = 1e9f;

            // Legs: hoof -> backward knee -> hip, tapering
            d = Min(d, Capsule(ax, y, 0.13f, 0.03f, 0.165f, 0.78f, 0.032f, 0.045f));
            d = Min(d, Capsule(ax, y, 0.165f, 0.78f, 0.10f, 1.36f, 0.045f, 0.06f));
            d = Min(d, Ellipse(ax, y, 0.135f, 0.04f, 0.05f, 0.035f));                  // hoof
            // Pelvis
            d = Min(d, Ellipse(x, y, 0f, 1.38f, 0.17f, 0.085f));
            // Emaciated torso: pinched waist, ribcage
            d = Min(d, Capsule(x, y, 0f, 1.38f, 0f, 1.62f, 0.075f, 0.075f));
            d = Min(d, Ellipse(x, y, 0f, 1.86f, 0.165f, 0.26f));
            // Shoulders, hunched
            d = Min(d, Capsule(ax, y, 0f, 2.10f, 0.24f, 2.06f, 0.08f, 0.055f));
            // Arms: shoulder -> elbow -> wrist, far too long
            d = Min(d, Capsule(ax, y, 0.24f, 2.06f, 0.33f, 1.56f, 0.048f, 0.034f));
            d = Min(d, Capsule(ax, y, 0.33f, 1.56f, 0.30f, 1.06f, 0.034f, 0.026f));
            d = Min(d, Ellipse(ax, y, 0.30f, 1.03f, 0.04f, 0.05f));                    // hand
            // Claws
            d = Min(d, Capsule(ax, y, 0.28f, 1.02f, 0.235f, 0.72f, 0.012f, 0.004f));
            d = Min(d, Capsule(ax, y, 0.295f, 1.0f, 0.28f, 0.66f, 0.012f, 0.004f));
            d = Min(d, Capsule(ax, y, 0.315f, 1.0f, 0.33f, 0.68f, 0.012f, 0.004f));
            d = Min(d, Capsule(ax, y, 0.33f, 1.02f, 0.375f, 0.76f, 0.011f, 0.004f));
            // Neck
            d = Min(d, Capsule(x, y, 0f, 2.08f, 0f, 2.33f, 0.045f, 0.04f));
            // Long skull, narrowing to a snout
            d = Min(d, Ellipse(x, y, 0f, 2.47f, 0.105f, 0.15f));
            d = Min(d, Capsule(x, y, 0f, 2.42f, 0f, 2.24f, 0.07f, 0.035f));
            // Antlers: main beam + tines
            d = Min(d, Capsule(ax, y, 0.06f, 2.58f, 0.20f, 2.80f, 0.028f, 0.022f));
            d = Min(d, Capsule(ax, y, 0.20f, 2.80f, 0.36f, 3.02f, 0.022f, 0.014f));
            d = Min(d, Capsule(ax, y, 0.36f, 3.02f, 0.40f, 3.16f, 0.014f, 0.006f));
            d = Min(d, Capsule(ax, y, 0.14f, 2.72f, 0.12f, 2.98f, 0.016f, 0.006f));
            d = Min(d, Capsule(ax, y, 0.26f, 2.88f, 0.24f, 3.10f, 0.015f, 0.006f));
            d = Min(d, Capsule(ax, y, 0.31f, 2.95f, 0.52f, 3.03f, 0.014f, 0.005f));
            d = Min(d, Capsule(ax, y, 0.11f, 2.66f, 0.30f, 2.67f, 0.014f, 0.005f));
            d = Min(d, Capsule(ax, y, 0.40f, 3.0f, 0.58f, 2.92f, 0.012f, 0.004f));

            // Ribs carved slightly into the torso (thin gaps make it look starved)
            for (int k = 0; k < 5; k++)
            {
                float ry = 1.70f + k * 0.07f;
                float gap = Capsule(ax, y, 0.045f, ry, 0.15f, ry - 0.035f, 0.008f, 0.006f);
                if (y > 1.62f && y < 2.05f) d = Math.Max(d, -gap + 0.002f);
            }
            return d;
        }

        // ---- static (TV noise) frames: grey noise with scanlines and tears ----
        public static byte[] PaintStatic(int w, int h, int seed)
        {
            var px = new byte[w * h * 4];
            var rng = new Random(seed);
            for (int y = 0; y < h; y++)
            {
                float line = (y % 2 == 0) ? 1f : 0.75f;
                bool tear = rng.NextDouble() < 0.04;
                float tearShift = tear ? (float)rng.NextDouble() * 0.6f + 0.4f : 0f;
                for (int x = 0; x < w; x++)
                {
                    float v = (float)rng.NextDouble();
                    v = (float)Math.Pow(v, 1.4) * line + tearShift * 0.5f;
                    byte c = (byte)Clamp(v * 255f, 0, 255);
                    int i = (y * w + x) * 4;
                    px[i] = c; px[i + 1] = c; px[i + 2] = c;
                    px[i + 3] = (byte)Clamp(120 + v * 135f, 0, 255);
                }
            }
            return px;
        }

        // ---- helpers ----
        static float Capsule(float px, float py, float ax, float ay, float bx, float by, float ra, float rb)
        {
            float pax = px - ax, pay = py - ay, bax = bx - ax, bay = by - ay;
            float t = Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
            float dx = pax - bax * t, dy = pay - bay * t;
            return (float)Math.Sqrt(dx * dx + dy * dy) - Lerp(ra, rb, t);
        }

        static float Ellipse(float px, float py, float cx, float cy, float rx, float ry)
        {
            float dx = (px - cx) / rx, dy = (py - cy) / ry;
            return ((float)Math.Sqrt(dx * dx + dy * dy) - 1f) * Math.Min(rx, ry);
        }

        static float Circle(float px, float py, float cx, float cy, float r)
        {
            float dx = px - cx, dy = py - cy;
            return (float)Math.Sqrt(dx * dx + dy * dy) - r;
        }

        static float Hash(int n)
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f;
        }

        static float Min(float a, float b) => a < b ? a : b;
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
