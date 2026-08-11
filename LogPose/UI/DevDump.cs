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
            if (Input.GetKeyDown(KeyCode.F11))
                DumpLocations();
            if (Input.GetKeyDown(KeyCode.F12))
                DumpEditor();
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
                            + " | " + Mathf.RoundToInt(rt.rect.width) + "x" + Mathf.RoundToInt(rt.rect.height)
                            + " | anch=" + rt.anchoredPosition.ToString("F0")
                            + " a=" + rt.anchorMin.ToString("F1") + rt.anchorMax.ToString("F1")
                            + " sib=" + rt.GetSiblingIndex());
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

        // F11: the board's layout numbers — every LocationSet in sc_Locations plus the
        // transforms of the containers cards live under. This is the ground truth the
        // field re-zoning is computed from.
        private static void DumpLocations()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                GameplayLogicScript gls = Object.FindFirstObjectByType<GameplayLogicScript>();
                if (gls == null || gls.sc_Locations == null)
                {
                    Plugin.Log.LogWarning("Locations dump: no GameplayLogicScript/sc_Locations");
                    return;
                }
                for (int i = 0; i < gls.sc_Locations.playerLocations.Count; i++)
                {
                    LocationPlayer p = gls.sc_Locations.playerLocations[i];
                    sb.AppendLine("=== playerLocations[" + i + "] ===");
                    Set(sb, "deck", p.deck); Set(sb, "donDeck", p.donDeck);
                    Set(sb, "leader", p.leader); Set(sb, "hand", p.hand);
                    Set(sb, "life", p.life); Set(sb, "donCost", p.donCost);
                    Set(sb, "deploy", p.deploy); Set(sb, "donEquipped", p.donEquipped);
                    Set(sb, "discard", p.discard); Set(sb, "stage", p.stage);
                    Set(sb, "topDeck", p.topDeck); Set(sb, "topDeckSquish", p.topDeckSquish);
                }
                sb.AppendLine("bFlipField=" + gls.bFlipField + " gameStyle=" + gls.e_GameStyle);
                Canvas cn = gls.cn_Canvas;
                if (cn != null)
                {
                    foreach (Transform t in cn.GetComponentsInChildren<Transform>(true))
                    {
                        string n = t.name;
                        if (n != "Deck" && n != "Player0" && n != "Player1" && n != "SideField"
                            && n != "Player" && n != "Opponent" && n != "PlayerPlaymat" && n != "OpponentPlaymat"
                            && n != "LogScrollView" && n != "CardPreview" && n != "GuideText"
                            && n != "ChoiceButton1" && n != "ChoiceButton2" && n != "ChoiceButton3" && n != "ChoiceButton4"
                            && n != "DownloadLog" && n != "ReportBug" && n != "CancelMatch" && n != "SaveState"
                            && n != "SaveStateButtons" && n != "Volume" && n != "Music" && n != "BG"
                            && n != "ActionActor" && !n.Contains("HandCount") && !n.Contains("ReturnToMain"))
                            continue;
                        RectTransform rt = t as RectTransform;
                        sb.AppendLine("T " + Path(t)
                            + " | anch=" + (rt != null ? rt.anchoredPosition.ToString("F1") : "-")
                            + " local=" + t.localPosition.ToString("F1")
                            + " | size=" + (rt != null ? rt.rect.width.ToString("F0") + "x" + rt.rect.height.ToString("F0") : "-")
                            + " | scale=" + t.localScale.ToString("F2")
                            + " rotZ=" + t.localEulerAngles.z.ToString("F0")
                            + " | sib=" + t.GetSiblingIndex()
                            + " active=" + t.gameObject.activeInHierarchy);
                    }
                }
                string file = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "logpose-locdump.txt");
                File.WriteAllText(file, sb.ToString());
                Plugin.Log.LogInfo("Locations dump written: " + file);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Locations dump failed: " + e.Message);
            }
        }

        private static void Set(StringBuilder sb, string name, LocationSet s)
        {
            if (s == null) { sb.AppendLine("  " + name + ": null"); return; }
            sb.AppendLine("  " + name + ": x=" + s.x + " y=" + s.y + " step=" + s.step
                + " step2=" + s.step2 + " width=" + s.width);
        }

        // F12: the deck editor's layout numbers — the deck-grid constants, scrollview
        // transforms, the browser's GridLayoutGroup settings and every toggle root.
        private static void DumpEditor()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                DeckEditorScript ed = Object.FindFirstObjectByType<DeckEditorScript>();
                if (ed == null)
                {
                    Plugin.Log.LogWarning("Editor dump: no DeckEditorScript");
                    return;
                }
                sb.AppendLine("DeckXStart=" + ed.DeckXStart + " DeckXStep=" + ed.DeckXStep
                    + " DeckYStart=" + ed.DeckYStart + " DeckYStep=" + ed.DeckYStep
                    + " DeckColumns=" + ed.DeckColumns + " DeckHeight=" + ed.DeckHeight
                    + " DeckStackXStep=" + ed.DeckStackXStep + " DeckStackYStep=" + ed.DeckStackYStep);
                DumpRt(sb, "DeckScrollview", ed.tf_DeckScrollview);
                DumpRt(sb, "SelectorContent", ed.tf_CardSelectorScrollview as RectTransform);
                if (ed.tf_CardSelectorScrollview != null)
                {
                    GridLayoutGroup grid = ed.tf_CardSelectorScrollview.GetComponent<GridLayoutGroup>();
                    if (grid != null)
                        sb.AppendLine("SelectorGrid cell=" + grid.cellSize.ToString("F0")
                            + " spacing=" + grid.spacing.ToString("F0")
                            + " padding=" + grid.padding.left + "," + grid.padding.top
                            + " constraint=" + grid.constraint + " count=" + grid.constraintCount);
                    else
                        sb.AppendLine("SelectorGrid: none");
                }
                Toggle[] toggles = { ed.t_Red, ed.t_Green, ed.t_Blue, ed.t_Purple, ed.t_Black,
                    ed.t_Yellow, ed.t_Limit4, ed.t_sortByCost, ed.t_hideRotated, ed.t_hideNumbers };
                foreach (Toggle t in toggles)
                    if (t != null)
                        DumpRt(sb, t.name, t.transform as RectTransform);
                if (ed.lgo_CurrentDeck != null && ed.lgo_CurrentDeck.Count > 0 && ed.lgo_CurrentDeck[0] != null)
                    DumpRt(sb, "DeckCard0", ed.lgo_CurrentDeck[0].transform as RectTransform);
                if (ed.lgo_AvailableCards != null && ed.lgo_AvailableCards.Count > 0 && ed.lgo_AvailableCards[0] != null)
                    DumpRt(sb, "SelCard0", ed.lgo_AvailableCards[0].transform as RectTransform);

                // World-space rects of every active top-level control + LogPose chrome
                // children, for an offline overlap audit.
                sb.AppendLine("--- WORLD RECTS ---");
                Transform cnv = ed.go_MainCanvas != null ? ed.go_MainCanvas.transform : null;
                if (cnv != null)
                {
                    Vector3[] c = new Vector3[4];
                    foreach (Transform child in cnv)
                    {
                        if (!child.gameObject.activeInHierarchy)
                            continue;
                        if (child.name == "LogPoseEditorChrome")
                        {
                            foreach (Transform cc in child)
                            {
                                WorldRect(sb, "chrome/" + cc.name, cc as RectTransform, c);
                                foreach (Transform ccc in cc)
                                    WorldRect(sb, "chrome/" + cc.name + "/" + ccc.name, ccc as RectTransform, c);
                            }
                            continue;
                        }
                        WorldRect(sb, child.name, child as RectTransform, c);
                    }
                }
                string file = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "logpose-editordump.txt");
                File.WriteAllText(file, sb.ToString());
                Plugin.Log.LogInfo("Editor dump written: " + file);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Editor dump failed: " + e.Message);
            }
        }

        private static void WorldRect(StringBuilder sb, string name, RectTransform rt, Vector3[] c)
        {
            if (rt == null)
                return;
            rt.GetWorldCorners(c);
            sb.AppendLine("W " + name + " | " + c[0].x.ToString("F0") + "," + c[0].y.ToString("F0")
                + " -> " + c[2].x.ToString("F0") + "," + c[2].y.ToString("F0"));
        }

        private static void DumpRt(StringBuilder sb, string name, RectTransform rt)
        {
            if (rt == null) { sb.AppendLine(name + ": null"); return; }
            sb.AppendLine(name + ": anch=" + rt.anchoredPosition.ToString("F0")
                + " size=" + rt.sizeDelta.ToString("F0") + " rect=" + rt.rect.width.ToString("F0")
                + "x" + rt.rect.height.ToString("F0")
                + " aMin=" + rt.anchorMin.ToString("F1") + " aMax=" + rt.anchorMax.ToString("F1")
                + " scale=" + rt.localScale.ToString("F2")
                + " parent=" + (rt.parent != null ? rt.parent.name : "-"));
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
