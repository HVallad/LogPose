using System;
using System.Linq;
using TMPro;
using UnityEngine;

namespace LogPose.UI
{
    // The design calls for Inter (400/500/600) + JetBrains Mono. Neither ships with the
    // game, so try OS-installed faces via TMP dynamic font assets, walking a preference
    // list (Segoe UI / Cascadia are on every Win11 box), and fall back to the game's own
    // TMP font as a last resort so text never goes invisible.
    internal static class UIFonts
    {
        private static TMP_FontAsset _sans, _sansSemi, _mono;
        private static TMP_FontAsset _donor;
        private static string[] _installed;

        internal static void SetDonor(TMP_FontAsset donor)
        {
            if (donor != null && _donor == null)
                _donor = donor;
        }

        internal static TMP_FontAsset Sans =>
            _sans != null ? _sans
                : (_sans = FromOs(("Inter", "Regular"), ("Segoe UI", "Regular")) ?? _donor);

        // Used for 600-weight text (kickers, labels). 500 stays on the regular face.
        internal static TMP_FontAsset SansSemi =>
            _sansSemi != null ? _sansSemi
                : (_sansSemi = FromOs(("Inter", "SemiBold"), ("Inter", "Semi Bold"),
                    ("Segoe UI", "Semibold"), ("Segoe UI Semibold", "Regular")) ?? Sans);

        internal static TMP_FontAsset Mono =>
            _mono != null ? _mono
                : (_mono = FromOs(("JetBrains Mono", "Regular"), ("Cascadia Mono", "Regular"),
                    ("Cascadia Code", "Regular"), ("Consolas", "Regular")) ?? Sans);

        private static TMP_FontAsset FromOs(params (string family, string style)[] faces)
        {
            foreach (var face in faces)
            {
                try
                {
                    // The (family, style, size) overload asks the OS font engine directly —
                    // the Font-object overload does not accept dynamic OS fonts.
                    TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(face.family, face.style, 64);
                    if (fa == null)
                        continue;
                    fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                    // Runtime-created assets reference the SDF shader by name, which the game
                    // may have stripped — borrow the shader from the game's own font instead.
                    if (_donor != null && _donor.material != null && fa.material != null)
                        fa.material.shader = _donor.material.shader;
                    Plugin.Log.LogInfo("UI font: using OS face '" + face.family + " " + face.style + "'.");
                    return fa;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("UI font '" + face.family + "' failed: " + e.Message);
                }
            }
            Plugin.Log.LogWarning("UI font: no OS face matched; falling back to the game font.");
            return null;
        }
    }
}
