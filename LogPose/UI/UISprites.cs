using System.Collections.Generic;
using UnityEngine;

namespace LogPose.UI
{
    // Runtime sprite factory. Every texture in the sprite manifest is a rounded rect with
    // a solid fill and an optional 1px edge, so instead of shipping PNGs the mod draws
    // them on demand (anti-aliased via a signed-distance function) and caches by recipe.
    internal static class UISprites
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        internal static void InvalidateCache()
        {
            foreach (Sprite s in Cache.Values)
                if (s != null)
                {
                    Object.Destroy(s.texture);
                    Object.Destroy(s);
                }
            Cache.Clear();
        }

        // 9-sliceable rounded rect. slice = uniform slice margin (manifest values are uniform
        // per sprite); pass 0 for an unsliced texture drawn at final size.
        internal static Sprite RoundedRect(int w, int h, float radius, Color fill, Color edge, float edgeW, float slice)
        {
            string key = string.Concat("rr", w, "x", h, "r", radius, "f", ColorUtility.ToHtmlStringRGBA(fill),
                "e", ColorUtility.ToHtmlStringRGBA(edge), edgeW, "s", slice);
            Sprite hit;
            if (Cache.TryGetValue(key, out hit) && hit != null)
                return hit;

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float py = y + 0.5f - h * 0.5f;
                for (int x = 0; x < w; x++)
                {
                    float pxx = x + 0.5f - w * 0.5f;
                    float d = Sdf(pxx, py, w, h, radius);
                    px[y * w + x] = Shade(d, fill, edge, edgeW);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Vector4 border = slice > 0f ? new Vector4(slice, slice, slice, slice) : Vector4.zero;
            Sprite sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
            Cache[key] = sp;
            return sp;
        }

        // Rounded rect with a vertical gradient fill, drawn unsliced at final size
        // (9-slicing would stretch the gradient). Used for the menu destination cards.
        internal static Sprite RoundedRectVGradient(int w, int h, float radius, Color top, Color bottom, Color edge, float edgeW)
        {
            string key = string.Concat("rg", w, "x", h, "r", radius, ColorUtility.ToHtmlStringRGBA(top),
                ColorUtility.ToHtmlStringRGBA(bottom), ColorUtility.ToHtmlStringRGBA(edge), edgeW);
            Sprite hit;
            if (Cache.TryGetValue(key, out hit) && hit != null)
                return hit;

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float py = y + 0.5f - h * 0.5f;
                Color fill = Color.Lerp(bottom, top, (y + 0.5f) / h);   // row 0 = texture bottom
                for (int x = 0; x < w; x++)
                {
                    float pxx = x + 0.5f - w * 0.5f;
                    float d = Sdf(pxx, py, w, h, radius);
                    px[y * w + x] = Shade(d, fill, edge, edgeW);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Sprite sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sp;
            return sp;
        }

        // Soft radial glow (additive-looking via plain alpha falloff). maxA at center -> 0 at rim.
        internal static Sprite Glow(Color c, float maxA)
        {
            const int S = 128;
            string key = "gl" + ColorUtility.ToHtmlStringRGBA(c) + maxA;
            Sprite hit;
            if (Cache.TryGetValue(key, out hit) && hit != null)
                return hit;

            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[S * S];
            float half = S * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float r = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;
                    float a = r >= 1f ? 0f : maxA * (1f - r) * (1f - r);
                    px[y * S + x] = new Color(c.r, c.g, c.b, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Sprite sp = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sp;
            return sp;
        }

        // 1px horizontal rule whose alpha ramps in over rampFrac of each end (rule_fade).
        internal static Sprite RuleFade(Color c)
        {
            const int W = 256;
            string key = "rf" + ColorUtility.ToHtmlStringRGBA(c);
            Sprite hit;
            if (Cache.TryGetValue(key, out hit) && hit != null)
                return hit;

            Texture2D tex = new Texture2D(W, 1, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[W];
            for (int x = 0; x < W; x++)
            {
                float t = Mathf.Min(x, W - 1 - x) / 48f;
                px[x] = new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(t));
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Sprite sp = Sprite.Create(tex, new Rect(0, 0, W, 1), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sp;
            return sp;
        }

        // Vertical linear gradient strip (stretched full-screen behind the menu).
        internal static Sprite VGradient(Color top, Color bottom)
        {
            const int H = 256;
            string key = "vg" + ColorUtility.ToHtmlStringRGBA(top) + ColorUtility.ToHtmlStringRGBA(bottom);
            Sprite hit;
            if (Cache.TryGetValue(key, out hit) && hit != null)
                return hit;

            Texture2D tex = new Texture2D(1, H, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[H];
            for (int y = 0; y < H; y++)
                px[y] = Color.Lerp(bottom, top, y / (float)(H - 1));
            tex.SetPixels32(px);
            tex.Apply(false, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Sprite sp = Sprite.Create(tex, new Rect(0, 0, 1, H), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sp;
            return sp;
        }

        // --- helpers ---------------------------------------------------------------

        private static float Sdf(float px, float py, float w, float h, float r)
        {
            float qx = Mathf.Abs(px) - (w * 0.5f - r);
            float qy = Mathf.Abs(py) - (h * 0.5f - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        private static Color32 Shade(float d, Color fill, Color edge, float edgeW)
        {
            float outerA = Mathf.Clamp01(0.5f - d);
            float innerA = edgeW > 0f ? Mathf.Clamp01(0.5f - (d + edgeW)) : outerA;
            float band = outerA - innerA;
            float a = band * edge.a + innerA * fill.a;
            if (a <= 0.001f)
                return new Color32(0, 0, 0, 0);
            float r = (band * edge.a * edge.r + innerA * fill.a * fill.r) / a;
            float g = (band * edge.a * edge.g + innerA * fill.a * fill.g) / a;
            float b = (band * edge.a * edge.b + innerA * fill.a * fill.b) / a;
            return new Color(r, g, b, a);
        }
    }
}
