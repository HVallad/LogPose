using UnityEngine;

namespace LogPose.UI
{
    // Design tokens from design/redesign sprite-manifest.md. Two colorways share every
    // size, radius and slice margin — only fills differ, so the palette is data.
    internal static class Theme
    {
        internal static Color Ground, Surface, SurfaceRaised, Edge, EdgeStrong, EdgeModal,
            Text, Accent, Accent400, Accent300, Danger,
            DonActiveFill, DonActiveEdge, HeroTop, HeroGlow;

        internal static Color TextMuted => WithA(Text, 0.55f);

        private static string _loaded;

        internal static void Ensure()
        {
            string want = Plugin.CfgUiColorway.Value;
            if (_loaded == want)
                return;
            _loaded = want;
            bool batsu = want != null && want.Trim().ToLowerInvariant() == "batsu";
            if (batsu)
            {
                Ground        = Hex("#17121e");
                Surface       = Hex("#241d2e");
                SurfaceRaised = Hex("#2e2639");
                Edge          = Hex("#473c52");
                EdgeStrong    = Hex("#655770");
                EdgeModal     = Hex("#a294ab");
                Text          = Hex("#f0e9f2");
                Accent        = Hex("#d81fb4");
                Accent400     = Hex("#ea55c8");
                Accent300     = Hex("#f7a8e4");
                Danger        = Hex("#dd6f5f");
                DonActiveFill = Hex("#5a1149");
                DonActiveEdge = Hex("#b81c98");
                HeroTop       = Hex("#0e3b52");
                HeroGlow      = Hex("#14607f");
            }
            else
            {
                Ground        = Hex("#161826");
                Surface       = Hex("#232532");
                SurfaceRaised = Hex("#2b2d3a");
                Edge          = Hex("#3f424d");
                EdgeStrong    = Hex("#595d6c");
                EdgeModal     = Hex("#9397ab");
                Text          = Hex("#e9e9ed");
                Accent        = Hex("#9184d9");
                Accent400     = Hex("#b5abfc");
                Accent300     = Hex("#d2cefd");
                Danger        = Hex("#dd6f5f");
                DonActiveFill = Hex("#423a6a");
                DonActiveEdge = Hex("#796cbf");
                HeroTop       = Hex("#262a60");
                HeroGlow      = Hex("#353b80");
            }
            UISprites.InvalidateCache();
        }

        internal static Color Hex(string s)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(s, out c) ? c : Color.magenta;
        }

        internal static Color WithA(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
