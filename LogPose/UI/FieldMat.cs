using System.IO;
using System.Reflection;
using UnityEngine;

namespace LogPose.UI
{
    // The board field is two 710x500 uGUI Images whose playmat texture bakes in all the
    // zone placards. The design mats are authored against the re-zoned geometry in
    // BoardLayoutPatches (regenerate with tools/Generate-FieldMats.py). The mockup mirrors
    // the opponent half vertically rather than rotating it, so each side gets its own
    // texture and BoardHUD zeroes the vanilla 180-degree rotation on the opponent copy.
    internal static class FieldMat
    {
        private static Sprite _player, _opponent;
        private static string _loadedFor;

        internal static Sprite Get(bool opponentHalf)
        {
            Theme.Ensure();
            string want = Plugin.CfgUiColorway.Value != null &&
                Plugin.CfgUiColorway.Value.Trim().ToLowerInvariant() == "batsu" ? "batsu" : "nocturne";
            if (_loadedFor != want)
            {
                _player = Load("LogPose.Assets.mat-" + want + ".png");
                _opponent = Load("LogPose.Assets.mat-" + want + "-opp.png");
                _loadedFor = want;
            }
            return opponentHalf ? _opponent : _player;
        }

        private static Sprite Load(string res)
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
                {
                    if (s == null)
                        return null;
                    byte[] bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length)
                        read += s.Read(bytes, read, bytes.Length - read);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(bytes))
                        return null;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Field mat load failed (" + res + "): " + e.Message);
                return null;
            }
        }
    }
}
