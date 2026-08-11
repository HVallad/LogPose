using System.IO;
using System.Reflection;
using UnityEngine;

namespace LogPose.UI
{
    // The board field is two 710x500 uGUI Images whose playmat texture bakes in all the
    // zone placards. These design-language mats were authored offline against the vanilla
    // zone geometry (design/redesign/vanilla-mat-grid.png), so every card still lands on
    // its printed slot. Embedded in the DLL; the game rotates the opponent copy itself.
    internal static class FieldMat
    {
        private static Sprite _sprite;
        private static string _loadedFor;

        internal static Sprite Get()
        {
            Theme.Ensure();
            string want = Plugin.CfgUiColorway.Value != null &&
                Plugin.CfgUiColorway.Value.Trim().ToLowerInvariant() == "batsu" ? "batsu" : "nocturne";
            if (_sprite != null && _loadedFor == want)
                return _sprite;
            try
            {
                string res = "LogPose.Assets.mat-" + want + ".png";
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
                    _sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    _loadedFor = want;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Field mat load failed: " + e.Message);
            }
            return _sprite;
        }
    }
}
