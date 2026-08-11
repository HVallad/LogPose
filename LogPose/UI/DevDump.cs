using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose.UI
{
    // Development aid: F9 dumps every active uGUI graphic (path, sprite, color, size)
    // to BepInEx\logpose-uidump.txt so the restyler's swap tables can be built from
    // real scene data instead of guesses. Costs nothing unless pressed.
    internal static class DevDump
    {
        internal static void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
                DumpWorld();
            if (!Input.GetKeyDown(KeyCode.F9))
                return;
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (c.name.StartsWith("LogPose"))
                        continue;
                    sb.AppendLine("=== CANVAS " + c.name + " (sort " + c.sortingOrder + ", mode " + c.renderMode + ") ===");
                    foreach (Graphic g in c.GetComponentsInChildren<Graphic>(false))
                    {
                        RectTransform rt = g.rectTransform;
                        string kind = g.GetType().Name;
                        string detail = "";
                        Image img = g as Image;
                        if (img != null)
                            detail = "sprite=" + (img.sprite != null ? img.sprite.name : "null") + " type=" + img.type;
                        TMP_Text tmp = g as TMP_Text;
                        if (tmp != null)
                        {
                            string t = (tmp.text ?? "").Replace("\n", "\\n");
                            detail = "font=" + (tmp.font != null ? tmp.font.name : "null") + " size=" + tmp.fontSize
                                + " text=\"" + (t.Length > 40 ? t.Substring(0, 40) : t) + "\"";
                        }
                        sb.AppendLine(Path(g.transform) + " | " + kind + " | " + detail
                            + " | col=#" + ColorUtility.ToHtmlStringRGBA(g.color)
                            + " | " + Mathf.RoundToInt(rt.rect.width) + "x" + Mathf.RoundToInt(rt.rect.height));
                    }
                }
                string file = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "logpose-uidump.txt");
                File.WriteAllText(file, sb.ToString());
                Plugin.Log.LogInfo("UI dump written: " + file);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("UI dump failed: " + e.Message);
            }
        }

        // F10: world-space renderers (the board field is scene geometry, not uGUI).
        private static void DumpWorld()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (SpriteRenderer sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    string spriteName = sr.sprite != null ? sr.sprite.name : "null";
                    string texSize = sr.sprite != null ? sr.sprite.texture.width + "x" + sr.sprite.texture.height : "-";
                    sb.AppendLine("SR " + Path(sr.transform) + " | sprite=" + spriteName + " tex=" + texSize
                        + " | col=#" + ColorUtility.ToHtmlStringRGBA(sr.color)
                        + " | pos=" + sr.transform.position.ToString("F1")
                        + " scale=" + sr.transform.lossyScale.ToString("F2")
                        + " | bounds=" + sr.bounds.size.ToString("F1")
                        + " | active=" + sr.gameObject.activeInHierarchy + " layer=" + sr.sortingOrder);
                }
                foreach (MeshRenderer mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    string mat = mr.sharedMaterial != null ? mr.sharedMaterial.name : "null";
                    string tex = mr.sharedMaterial != null && mr.sharedMaterial.mainTexture != null
                        ? mr.sharedMaterial.mainTexture.name : "-";
                    sb.AppendLine("MR " + Path(mr.transform) + " | mat=" + mat + " tex=" + tex
                        + " | pos=" + mr.transform.position.ToString("F1")
                        + " | bounds=" + mr.bounds.size.ToString("F1")
                        + " | active=" + mr.gameObject.activeInHierarchy);
                }
                string file = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "logpose-worlddump.txt");
                File.WriteAllText(file, sb.ToString());
                Plugin.Log.LogInfo("World dump written: " + file);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("World dump failed: " + e.Message);
            }
        }

        private static string Path(Transform t)
        {
            string s = t.name;
            int guard = 0;
            while (t.parent != null && guard++ < 12)
            {
                t = t.parent;
                s = t.name + "/" + s;
            }
            return s;
        }
    }
}
