using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose.UI
{
    // Frame 2b's deck editor, imposed on the vanilla scene: an 88h top bar (back, name
    // field, count, Load/Import/Alt arts + Save primary), a 320 filter rail (search,
    // color/cost toggles, options, search help, sponsor), the card browser filling the
    // center, and a 500 deck panel on the right with a LogPose leader header + live
    // cost-curve histogram over the vanilla deck grid (grid constants rewritten to fit).
    // Everything stays the game's own controls — only geometry and chrome change.
    internal static class DeckEditorUI
    {
        private static DeckEditorScript _ed;
        private static GameObject _chrome;          // scene object: dies on scene unload
        private static Transform _leaderThumb;
        private static TextMeshProUGUI _leaderName, _leaderCode;
        private const int CostBuckets = 11;         // 0..10, every printable cost
        private static readonly Image[] _bars = new Image[CostBuckets];
        private static readonly TextMeshProUGUI[] _barCounts = new TextMeshProUGUI[CostBuckets];
        private static string _shownLeader = "?";
        private static bool _dropdownStyled;

        internal static void Update()
        {
            if (Time.frameCount % 30 != 0 || !Plugin.CfgUiReskin.Value)
                return;
            if (_ed == null)
            {
                _ed = UnityEngine.Object.FindFirstObjectByType<DeckEditorScript>();
                if (_ed == null)
                {
                    _chrome = null;   // scene gone; clones died with it
                    _shownLeader = "?";
                    _dropdownStyled = false;
                    _chipsStyled = false;
                    return;
                }
            }
            try
            {
                if (_chrome == null)
                    BuildChrome();
                Impose();
                RefreshDeckPanel();
                RefreshBadges(_ed);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Deck editor reskin failed: " + e.Message);
                _ed = null;
            }
        }

        private static Transform Cn()
        {
            return _ed.go_MainCanvas != null ? _ed.go_MainCanvas.transform
                : _ed.inp_DeckName.GetComponentInParent<Canvas>().transform;
        }

        // ------------------------------------------------------------------- chrome ---

        private static void BuildChrome()
        {
            Theme.Ensure();
            Transform cn = Cn();
            _chrome = W.Go("LogPoseEditorChrome", cn);
            _chrome.transform.SetSiblingIndex(2);   // over the background, under everything else
            W.Fill(_chrome);

            Transform t = _chrome.transform;
            // Top bar strip — oversized so wider-than-1920 canvases stay covered.
            Image bar = W.Panel(t, "TopBar", -120f, 0f, 2200f, 88f, 0.01f,
                Theme.WithA(Theme.Surface, 0.9f), Color.clear, 0f);
            bar.raycastTarget = false;
            GameObject hair = W.Go("Hairline", t);
            W.TL(hair, -120f, 88f, 2200f, 1f);
            Image hi = hair.AddComponent<Image>();
            hi.color = Theme.WithA(Theme.Text, 0.1f);
            hi.raycastTarget = false;

            // The canvas width varies with aspect ratio, so each column anchors to the
            // screen edge it hugs (headings live INSIDE the panels and follow for free).
            Image rail = W.Panel(t, "Rail", 0f, 0f, 320f, 920f, 14f, Theme.WithA(Theme.Surface, 0.55f),
                Theme.WithA(Theme.Text, 0.1f));
            rail.raycastTarget = false;
            Edge(rail.rectTransform, 0f, 196f, -44f);
            W.Label(rail.transform, "COLOR", 40f, 72f, 200f, 18f, 12f, Theme.WithA(Theme.Text, 0.5f), 600,
                TextAlignmentOptions.TopLeft, false, 0.12f);
            W.Label(rail.transform, "OPTIONS", 40f, 226f, 200f, 18f, 12f, Theme.WithA(Theme.Text, 0.5f), 600,
                TextAlignmentOptions.TopLeft, false, 0.12f);

            Image deckPanel = W.Panel(t, "DeckPanel", 0f, 0f, 500f, 920f, 14f, Theme.WithA(Theme.Surface, 0.55f),
                Theme.WithA(Theme.Text, 0.1f));
            deckPanel.raycastTarget = false;
            Edge(deckPanel.rectTransform, 1f, -288f, -44f);
            Transform dp = deckPanel.transform;

            // Deck panel: leader header + cost curve (panel-relative coords).
            W.Label(dp, "LEADER", 136f, 34f, 200f, 18f, 12f, Theme.Accent400, 600,
                TextAlignmentOptions.TopLeft, false, 0.12f);
            GameObject thumbSlot = W.Go("LeaderThumb", dp);
            W.TL(thumbSlot, 32f, 28f, 78f, 110f);
            Image ts = thumbSlot.AddComponent<Image>();
            ts.sprite = UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Ground, 0.5f),
                Theme.WithA(Theme.Text, 0.12f), 1f, 9f);
            ts.type = Image.Type.Sliced;
            ts.raycastTarget = false;
            GameObject thumb = W.Go("Img", thumbSlot.transform);
            W.TL(thumb, 4f, 4f, 70f, 102f);
            Image ti = thumb.AddComponent<Image>();
            ti.raycastTarget = false;
            ti.enabled = false;
            _leaderThumb = thumb.transform;
            _leaderName = W.Label(dp, "No leader", 136f, 56f, 330f, 30f, 20f, Theme.Text, 500);
            _leaderName.overflowMode = TextOverflowModes.Ellipsis;
            _leaderName.enableWordWrapping = false;
            _leaderCode = W.Label(dp, "", 136f, 88f, 330f, 22f, 13f, Theme.TextMuted, 400,
                TextAlignmentOptions.TopLeft, true);

            W.Label(dp, "COST CURVE", 32f, 162f, 200f, 18f, 12f, Theme.WithA(Theme.Text, 0.5f), 600,
                TextAlignmentOptions.TopLeft, false, 0.12f);
            for (int i = 0; i < CostBuckets; i++)
            {
                GameObject b = W.Go("Bar" + i, dp);
                RectTransform rt = b.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0.5f, 0f);       // grow upward from the baseline
                rt.anchoredPosition = new Vector2(48f + i * 39f, -286f);
                rt.sizeDelta = new Vector2(32f, 4f);
                _bars[i] = b.AddComponent<Image>();
                _bars[i].sprite = UISprites.RoundedRect(24, 24, 4f, Color.white, Color.clear, 0f, 5f);
                _bars[i].type = Image.Type.Sliced;
                _bars[i].raycastTarget = false;
                W.Label(dp, i.ToString(), 28.5f + i * 39f, 292f, 39f, 16f, 11f,
                    Theme.WithA(Theme.Text, 0.45f), 400, TextAlignmentOptions.Center, true);
                _barCounts[i] = W.Label(dp, "", 28.5f + i * 39f, 0f, 39f, 16f, 11f,
                    Theme.WithA(Theme.Text, 0.7f), 600, TextAlignmentOptions.Center, true);
            }
            _shownLeader = "?";
            Plugin.Log.LogInfo("Deck editor 2b chrome built.");
        }

        private static void Edge(RectTransform rt, float ax, float x, float y)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
        }

        // ------------------------------------------------------------- repositioning --

        private static void Impose()
        {
            Transform cn = Cn();
            const float railX = 196f;   // rail column center, measured from the LEFT edge

            // Top bar (y 496 in centered coords): the name/count cluster hugs the left
            // edge, the action cluster hugs the right — aspect-ratio-proof.
            MoveEdge(cn, "BackButton", 68f, 496f, 90f, 48f, 0f);
            SetText(cn, "BackButton", "← Back", 14f);
            MoveEdge(cn, "DeckName", 300f, 496f, 360f, 48f, 0f);
            Move(cn, "NotificationText", -90f, 430f, 500f, 36f);
            RectTransform count = MoveEdge(cn, "DeckCount", 610f, 494f, 240f, 40f, 0f);
            if (count != null)
            {
                TMP_Text ct = count.GetComponent<TMP_Text>();
                if (ct != null)
                {
                    ct.font = UIFonts.Mono;
                    ct.fontSize = 24f;
                    ct.color = Theme.Accent300;
                    ct.alignment = TextAlignmentOptions.MidlineLeft;
                }
            }
            MoveEdge(cn, "DeckSelector", -790f, 496f, 250f, 48f, 1f);
            StyleDropdown(cn);
            MoveEdge(cn, "LoadButton", -560f, 496f, 110f, 48f, 1f);
            SetText(cn, "LoadButton", "Load", 16f);
            MoveEdge(cn, "PasteFromClipboard", -428f, 496f, 130f, 48f, 1f);
            SetText(cn, "PasteFromClipboard", "Import", 15f);
            MoveEdge(cn, "LogPoseAltArts", -296f, 496f, 120f, 48f, 1f);
            SetText(cn, "LogPoseAltArts", "Alt arts", 15f);
            RectTransform save = MoveEdge(cn, "SaveButton", -154f, 496f, 140f, 48f, 1f);
            if (save != null)
                BoardHUD.StyleAsButton(save.gameObject, 140f, 48f, 16f, BtnKind.Primary);

            // Left rail (everything left-anchored). The search field's pivot is
            // off-center in the prefab — normalize it so the position lands as aimed.
            RectTransform search = MoveEdge(cn, "SearchField", railX, 384f, 272f, 44f, 0f);
            if (search != null && search.pivot != new Vector2(0.5f, 0.5f))
            {
                search.pivot = new Vector2(0.5f, 0.5f);
                search.anchoredPosition = new Vector2(railX, 384f);
            }
            // Color roots shrink to 120 so the two columns' CLICK rects can't overlap.
            MoveToggle(cn, "Red", 115f, 302f, 120f);
            MoveToggle(cn, "Green", 235f, 302f, 120f);
            MoveToggle(cn, "Blue", 115f, 260f, 120f);
            MoveToggle(cn, "Purple", 235f, 260f, 120f);
            MoveToggle(cn, "Black", 115f, 218f, 120f);
            MoveToggle(cn, "Yellow", 235f, 218f, 120f);
            MoveToggle(cn, "Limit4", 130f, 148f);
            MoveToggle(cn, "Rotation", 130f, 106f);
            MoveToggle(cn, "SortByCost", 148f, 64f);
            MoveToggle(cn, "HideNumbers", 148f, 22f);
            StyleToggles(cn);
            // Bottom stack, spaced so nothing collides: help text, sponsor, utilities.
            RectTransform help = MoveEdge(cn, "SearchHelp", railX, -160f, 276f, 250f, 0f);
            if (help != null)
            {
                TMP_Text ht = help.GetComponent<TMP_Text>();
                if (ht != null)
                {
                    if (ht.enableAutoSizing)
                        ht.enableAutoSizing = false;
                    if (ht.fontSize != 12f)
                        ht.fontSize = 12f;
                    ht.overflowMode = TextOverflowModes.Ellipsis;
                }
            }
            RectTransform eg = MoveEdge(cn, "EgmanEvents", railX, -345f, 0f, 0f, 0f);
            if (eg != null && eg.localScale.x != 0.7f)
                eg.localScale = new Vector3(0.7f, 0.7f, 1f);
            RectTransform egx = MoveEdge(cn, "EgmanExplanation", railX, -410f, 260f, 24f, 0f);
            if (egx != null)
            {
                TMP_Text xt = egx.GetComponent<TMP_Text>();
                if (xt != null && xt.fontSize != 13f)
                {
                    xt.enableAutoSizing = false;
                    xt.fontSize = 13f;
                }
            }
            MoveEdge(cn, "Customize Images", railX, -448f, 210f, 38f, 0f);
            SetText(cn, "Customize Images", "Customize images", 14f);
            MoveEdge(cn, "DeleteButton", railX, -488f, 210f, 36f, 0f);
            SetText(cn, "DeleteButton", "Delete selected deck file", 12f);

            // Center browser: stretches between the two side columns so any aspect
            // ratio keeps it clear of them; the grid's column count follows its width.
            Transform sel = cn.Find("Card Selector Scrollview");
            if (sel != null)
            {
                RectTransform srt = (RectTransform)sel;
                if (srt.anchorMin.x != 0f || srt.anchorMax.x != 1f)
                {
                    srt.anchorMin = new Vector2(0f, 0.5f);
                    srt.anchorMax = new Vector2(1f, 0.5f);
                    srt.pivot = new Vector2(0.5f, 0.5f);
                }
                srt.offsetMin = new Vector2(376f, -504f);
                srt.offsetMax = new Vector2(-562f, 416f);
                if (_ed.tf_CardSelectorScrollview != null)
                {
                    GridLayoutGroup grid = _ed.tf_CardSelectorScrollview.GetComponent<GridLayoutGroup>();
                    if (grid != null)
                    {
                        int cols = Mathf.Max(3, Mathf.FloorToInt((srt.rect.width - 12f) / 101f));
                        if (grid.constraintCount != cols)
                            grid.constraintCount = cols;
                    }
                }
            }
            Move(cn, "NoCardsIndicator", -90f, -44f, 0f, 0f);

            // Right deck panel: vanilla deck grid tucked under the leader/curve header.
            // Stack steps at zero make every copy of a card sit exactly on the first one —
            // a single visible card per printing, with the ×N badge carrying the count
            // (clicks still hit the top copy and remove one at a time, as vanilla).
            MoveEdge(cn, "Deck Scrollview", -288f, -160f, 470f, 560f, 1f);
            _ed.DeckXStart = 60f;
            _ed.DeckXStep = 110f;
            _ed.DeckYStart = -80f;
            _ed.DeckYStep = -150f;
            _ed.DeckColumns = 4;
            _ed.DeckHeight = 150f;
            _ed.DeckStackXStep = 0f;
            _ed.DeckStackYStep = 0f;
            RectTransform copy = MoveEdge(cn, "CopyToClipboard", -398f, -475f, 220f, 44f, 1f);
            if (copy != null)
                SetText(cn, "CopyToClipboard", "Copy list", 15f);
            RectTransform clear = MoveEdge(cn, "ClearButton", -160f, -475f, 190f, 44f, 1f);
            if (clear != null)
                BoardHUD.StyleAsButton(clear.gameObject, 190f, 44f, 15f, BtnKind.Danger);
        }

        private static RectTransform Move(Transform cn, string name, float x, float y, float w, float h)
        {
            Transform t = cn.Find(name);
            if (t == null)
                return null;
            RectTransform rt = t as RectTransform;
            if (rt == null)
                return null;
            rt.anchoredPosition = new Vector2(x, y);
            if (w > 0f)
                rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        // Anchor to a screen edge (ax: 0 = left, 1 = right) so the layout survives any
        // aspect ratio, then position (x is measured from that edge).
        private static RectTransform MoveEdge(Transform cn, string name, float x, float y,
            float w, float h, float ax)
        {
            Transform t = cn.Find(name);
            if (t == null)
                return null;
            RectTransform rt = t as RectTransform;
            if (rt == null)
                return null;
            if (rt.anchorMin.x != ax || rt.anchorMin.y != 0.5f)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(ax, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
            }
            rt.anchoredPosition = new Vector2(x, y);
            if (w > 0f)
                rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private static void MoveToggle(Transform cn, string name, float x, float y, float w = 0f)
        {
            RectTransform rt = MoveEdge(cn, name, x, y, w, w > 0f ? 40f : 0f, 0f);
            if (rt != null && rt.localScale.x != 0.85f)
                rt.localScale = new Vector3(0.85f, 0.85f, 1f);
        }

        // The color filters become design chips: the whole control is the chip, and the
        // toggle's checkmark graphic — which Unity cross-fades with the on/off state —
        // stretches to a full-chip accent fill, so the active state reads instantly.
        // The option toggles keep the checkbox look but get a bright accent mark and
        // legible labels (the vanilla legacy-Text labels were near-black on dark).
        private static bool _chipsStyled;
        private static readonly string[] ColorChips = { "Red", "Green", "Blue", "Purple", "Black", "Yellow" };
        private static readonly string[] OptionToggles = { "Limit4", "Rotation", "SortByCost", "HideNumbers" };

        private static void StyleToggles(Transform cn)
        {
            if (_chipsStyled)
                return;
            _chipsStyled = true;
            foreach (string name in ColorChips)
            {
                Transform t = cn.Find(name);
                if (t == null)
                    continue;
                Transform bg = t.Find("Background");
                Transform ck = bg != null ? bg.Find("Checkmark") : null;
                Transform lbl = t.Find("Label");
                if (bg != null)
                {
                    RectTransform brt = (RectTransform)bg;
                    brt.anchorMin = Vector2.zero;
                    brt.anchorMax = Vector2.one;
                    brt.offsetMin = brt.offsetMax = Vector2.zero;
                    Image bi = bg.GetComponent<Image>();
                    if (bi != null)
                    {
                        bi.sprite = UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Text, 0.03f),
                            Theme.WithA(Theme.Text, 0.16f), 1f, 9f);
                        bi.type = Image.Type.Sliced;
                        bi.color = Color.white;
                    }
                }
                if (ck != null)
                {
                    RectTransform crt = (RectTransform)ck;
                    crt.anchorMin = Vector2.zero;
                    crt.anchorMax = Vector2.one;
                    crt.offsetMin = crt.offsetMax = Vector2.zero;
                    Image ci = ck.GetComponent<Image>();
                    if (ci != null)
                    {
                        ci.sprite = UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Accent, 0.2f),
                            Theme.Accent, 1f, 9f);
                        ci.type = Image.Type.Sliced;
                        ci.color = Color.white;
                    }
                }
                StyleToggleLabel(lbl, TextAnchor.MiddleCenter, true);
            }
            foreach (string name in OptionToggles)
            {
                Transform t = cn.Find(name);
                if (t == null)
                    continue;
                Transform bg = t.Find("Background");
                Transform ck = bg != null ? bg.Find("Checkmark") : null;
                if (ck != null)
                {
                    Image ci = ck.GetComponent<Image>();
                    if (ci != null)
                        ci.color = Theme.Accent;
                }
                StyleToggleLabel(t.Find("Label"), TextAnchor.MiddleLeft, false);
            }
        }

        private static void StyleToggleLabel(Transform lbl, TextAnchor anchor, bool fill)
        {
            if (lbl == null)
                return;
            Text lt = lbl.GetComponent<Text>();
            if (lt != null)
            {
                lt.color = Theme.Text;
                lt.alignment = anchor;
            }
            if (fill)
            {
                RectTransform lrt = (RectTransform)lbl;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            }
        }

        // ------------------------------------------------- browser loading performance --

        // The vanilla loader has NO yield on the no-loading-screen path (filter changes),
        // so every not-yet-loaded thumbnail — disk read + texture upload — lands in ONE
        // frame: the multi-second freeze when toggling filters. This budgeted version
        // spends ~6ms per frame and lets thumbnails pop in while the UI stays live.
        private static Comparison<GameObject> _colorSort;

        [HarmonyLib.HarmonyPrefix]
        [HarmonyLib.HarmonyPatch(typeof(DeckEditorScript), "LoadCardImages")]
        private static bool LoadCardImages_Prefix(DeckEditorScript __instance, bool showLoadingScreen,
            ref IEnumerator __result)
        {
            __result = BudgetedLoad(__instance, showLoadingScreen);
            return false;
        }

        private static IEnumerator BudgetedLoad(DeckEditorScript ed, bool showLoadingScreen)
        {
            // Release the cardsLoading gate immediately: RefreshCardSelector DROPS filter
            // changes while it is set, which was invisible when the vanilla loader ran in
            // a single frame but leaves the grid stale for the whole streaming window
            // otherwise (toggle Red off mid-stream and the red cards stayed). Concurrent
            // streams are safe — they drain the same shared list and loading is
            // idempotent per card.
            try
            {
                HarmonyLib.AccessTools.Field(typeof(DeckEditorScript), "cardsLoading").SetValue(ed, false);
            }
            catch { }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Slider bar = showLoadingScreen && ed.CardLoadingBar != null
                ? ed.CardLoadingBar.GetComponent<Slider>() : null;
            int total = ed.lgo_CardsToLoad != null ? ed.lgo_CardsToLoad.Count : 0;
            int done = 0;
            while (ed.lgo_CardsToLoad != null && ed.lgo_CardsToLoad.Count > 0)
            {
                GameObject go = ed.lgo_CardsToLoad[0];
                ed.lgo_CardsToLoad.RemoveAt(0);
                done++;
                if (go != null)
                    LoadThumb(ed, go);
                if (sw.ElapsedMilliseconds > 6)
                {
                    if (bar != null)
                    {
                        bar.value = 100f * done / Mathf.Max(total, 1);
                        if (ed.CardLoadingText != null)
                            ed.CardLoadingText.text = "Loading Cards... " + Mathf.CeilToInt(bar.value) + "%";
                    }
                    yield return null;
                    sw.Restart();
                }
            }
            try
            {
                if (_colorSort == null)
                {
                    var m = HarmonyLib.AccessTools.Method(typeof(DeckEditorScript), "SortByCardColor");
                    _colorSort = (Comparison<GameObject>)Delegate.CreateDelegate(typeof(Comparison<GameObject>), m);
                }
                if (ed.lgo_AvailableCards != null)
                    ed.lgo_AvailableCards.Sort(_colorSort);
                HarmonyLib.AccessTools.Field(typeof(DeckEditorScript), "cardsLoading").SetValue(ed, false);
                if (showLoadingScreen && ed.CardLoadingPanel != null)
                    ed.CardLoadingPanel.SetActive(false);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Budgeted card load tail failed: " + e.Message);
            }
        }

        private static void LoadThumb(DeckEditorScript ed, GameObject go)
        {
            try
            {
                Image img = go.GetComponent<Image>();
                if (img == null || img.sprite != null)
                    return;
                CardLogicScript cls = go.GetComponent<CardLogicScript>();
                if (cls == null || cls.myCard.cardDef == null)
                    return;
                img.sprite = ed.card_Database.GetCardImage(cls.myCard.cardDef.cardID, SpriteState.Thumbnail);
                if (go.transform.childCount <= 6)
                    return;
                Transform fb = go.transform.GetChild(6);
                if (img.sprite == null)
                {
                    fb.gameObject.SetActive(true);
                    TextMeshProUGUI ft = fb.GetComponent<TextMeshProUGUI>();
                    if (ft != null)
                        ft.text = cls.myCard.cardDef.cardID + "\n" + cls.myCard.cardDef.characterName;
                }
                else
                    fb.gameObject.SetActive(false);
            }
            catch { }
        }

        // The deck-file dropdown keeps its vanilla prefab look otherwise (white input
        // sprite + parchment popup). Restyle the closed control and its list template.
        private static void StyleDropdown(Transform cn)
        {
            if (_dropdownStyled)
                return;
            Transform dd = cn.Find("DeckSelector");
            if (dd == null)
                return;
            _dropdownStyled = true;
            Image bg = dd.GetComponent<Image>();
            if (bg != null)
            {
                bg.sprite = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Text, 0.04f),
                    Theme.WithA(Theme.Text, 0.18f), 1f, 12f);
                bg.type = Image.Type.Sliced;
                bg.color = Color.white;
            }
            foreach (TMP_Text txt in dd.GetComponentsInChildren<TMP_Text>(true))
            {
                txt.color = txt.name == "Placeholder" ? Theme.WithA(Theme.Text, 0.5f) : Theme.Text;
                if (txt.fontSize > 16f)
                    txt.fontSize = 15f;
            }
            Transform template = dd.Find("Template");
            if (template != null)
            {
                Image tbg = template.GetComponent<Image>();
                if (tbg != null)
                {
                    tbg.sprite = UISprites.RoundedRect(48, 48, 8f, Theme.Surface, Theme.EdgeModal, 1f, 12f);
                    tbg.type = Image.Type.Sliced;
                    tbg.color = Color.white;
                }
                foreach (Image img in template.GetComponentsInChildren<Image>(true))
                {
                    if (img.name == "Item Background")
                        img.color = Theme.WithA(Theme.Text, 0.05f);
                    else if (img.name == "Item Checkmark")
                        img.color = Theme.Accent;
                }
            }
        }

        private static void SetText(Transform cn, string name, string text, float size)
        {
            Transform t = cn.Find(name);
            if (t == null)
                return;
            TMP_Text txt = t.GetComponentInChildren<TMP_Text>(true);
            if (txt == null)
                return;
            // Never let a label intercept clicks; on stretch-anchored children sizeDelta
            // is a margin — an absolute size there overhangs the button and steals the
            // neighbors' clicks.
            if (txt.raycastTarget)
                txt.raycastTarget = false;
            if (txt.enableAutoSizing)
                txt.enableAutoSizing = false;
            if (txt.text != text)
                txt.text = text;
            if (txt.fontSize != size)
                txt.fontSize = size;
            RectTransform prt = t as RectTransform;
            RectTransform trt = txt.rectTransform;
            if (prt != null)
            {
                bool stretch = trt.anchorMin.x != trt.anchorMax.x;
                Vector2 want = stretch ? new Vector2(-12f, -8f)
                    : new Vector2(prt.sizeDelta.x - 12f, prt.sizeDelta.y - 8f);
                if (trt.sizeDelta != want)
                    trt.sizeDelta = want;
            }
        }

        // ------------------------------------------------------------- count badges ---

        // One ×N pill per printing, bottom-right of the (now unstacked) top copy. The
        // game maintains the count on child 7 of the top card each DisplayDeck pass;
        // the badge mirrors it and the raw number is kept invisible. Respects the
        // vanilla "Hide Counts" toggle, since that deactivates child 7 outright.
        private static readonly List<GameObject> _badges = new List<GameObject>();

        [HarmonyLib.HarmonyPostfix]
        [HarmonyLib.HarmonyPatch(typeof(DeckEditorScript), "DisplayDeck")]
        private static void DisplayDeck_Postfix(DeckEditorScript __instance)
        {
            if (!Plugin.CfgUiReskin.Value)
                return;
            try
            {
                Theme.Ensure();
                RefreshBadges(__instance);
            }
            catch { }
        }

        private static void RefreshBadges(DeckEditorScript ed)
        {
            if (ed.lgo_CurrentDeck == null)
                return;
            var inDeck = new HashSet<Transform>();
            foreach (GameObject go in ed.lgo_CurrentDeck)
                if (go != null)
                    inDeck.Add(go.transform);
            // Cards are pooled: a badge whose card left the deck must not follow it
            // into the browser.
            for (int i = _badges.Count - 1; i >= 0; i--)
            {
                GameObject b = _badges[i];
                if (b == null)
                    _badges.RemoveAt(i);
                else if (!inDeck.Contains(b.transform.parent) && b.activeSelf)
                    b.SetActive(false);
            }

            // Count copies straight from the (sorted) deck list — the vanilla number
            // label depends on the "Hide Counts" toggle and can't be trusted for data.
            var deck = ed.lgo_CurrentDeck;
            int runStart = 0;
            string runId = null;
            for (int i = 0; i <= deck.Count; i++)
            {
                string id = null;
                if (i < deck.Count && deck[i] != null)
                {
                    CardLogicScript cls = deck[i].GetComponent<CardLogicScript>();
                    id = cls != null && cls.myCard.cardDef != null ? cls.myCard.cardDef.cardID : null;
                }
                if (i < deck.Count && id == runId)
                    continue;
                // The run [runStart, i) just ended; its LAST copy is the visible top.
                int n = i - runStart;
                for (int j = runStart; j < i; j++)
                {
                    if (deck[j] == null)
                        continue;
                    Transform card = deck[j].transform;
                    if (card.childCount >= 8)
                    {
                        TMP_Text num = card.GetChild(7).GetComponent<TMP_Text>();
                        if (num != null && num.alpha != 0f)
                            num.alpha = 0f;   // the badge replaces the raw number
                    }
                    bool show = j == i - 1 && n > 1;
                    Transform badge = card.Find("LogPoseBadge");
                    if (!show)
                    {
                        if (badge != null && badge.gameObject.activeSelf)
                            badge.gameObject.SetActive(false);
                        continue;
                    }
                    if (badge == null)
                    {
                        badge = BuildBadge(card);
                        _badges.Add(badge.gameObject);
                    }
                    if (!badge.gameObject.activeSelf)
                        badge.gameObject.SetActive(true);
                    TMP_Text bt = badge.GetComponentInChildren<TMP_Text>(true);
                    string want = "×" + n;
                    if (bt != null && bt.text != want)
                        bt.text = want;
                }
                runStart = i;
                runId = id;
            }
        }

        private static Transform BuildBadge(Transform card)
        {
            GameObject b = W.Go("LogPoseBadge", card);
            RectTransform rt = b.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-3f, 3f);
            rt.sizeDelta = new Vector2(32f, 22f);
            Image im = b.AddComponent<Image>();
            im.sprite = UISprites.RoundedRect(24, 24, 6f, Theme.WithA(Theme.Ground, 0.92f),
                Theme.WithA(Theme.Accent, 0.5f), 1f, 7f);
            im.type = Image.Type.Sliced;
            im.raycastTarget = false;
            TMP_Text label = W.Label(b.transform, "×2", 0f, 0f, 32f, 22f, 12f,
                Theme.Accent300, 600, TextAlignmentOptions.Center);
            W.Fill(label.gameObject);
            return b.transform;
        }

        // ------------------------------------------------------------- deck insights --

        private static void RefreshDeckPanel()
        {
            if (_ed.lgo_CurrentDeck == null)
                return;
            string leaderId = null;
            var counts = new int[CostBuckets];
            int total = 0;
            foreach (GameObject go in _ed.lgo_CurrentDeck)
            {
                if (go == null)
                    continue;
                CardLogicScript cls = go.GetComponent<CardLogicScript>();
                if (cls == null || cls.myCard.cardDef == null)
                    continue;
                CardDefinition def = cls.myCard.cardDef;
                if (def.cardType == CardType.Leader)
                {
                    leaderId = def.cardID;
                    continue;
                }
                counts[Mathf.Clamp(def.cardCost, 0, CostBuckets - 1)]++;
                total++;
            }

            int max = 1;
            foreach (int c in counts)
                if (c > max)
                    max = c;
            for (int i = 0; i < CostBuckets; i++)
            {
                float hgt = counts[i] > 0 ? 8f + 86f * counts[i] / max : 4f;
                _bars[i].rectTransform.sizeDelta = new Vector2(32f, hgt);
                _bars[i].color = counts[i] == max && counts[i] > 0 ? Theme.Accent
                    : Theme.WithA(Theme.Accent, counts[i] > 0 ? 0.45f : 0.12f);
                RectTransform crt = _barCounts[i].rectTransform;
                crt.anchoredPosition = new Vector2(crt.anchoredPosition.x, -286f + hgt + 4f);
                _barCounts[i].text = counts[i] > 0 ? counts[i].ToString() : "";
            }

            if (leaderId == _shownLeader)
                return;
            _shownLeader = leaderId;
            Image img = _leaderThumb.GetComponent<Image>();
            if (leaderId == null)
            {
                _leaderName.text = "No leader";
                _leaderCode.text = "";
                img.enabled = false;
                return;
            }
            CardDefinition ldef = CardDatabaseScript.Instance != null
                ? CardDatabaseScript.Instance.FindDefinition(leaderId) : null;
            _leaderName.text = ldef != null && !string.IsNullOrEmpty(ldef.characterName)
                ? ldef.characterName : leaderId;
            _leaderCode.text = leaderId;
            Sprite s = CardDatabaseScript.Instance != null
                ? CardDatabaseScript.Instance.GetCardImage(leaderId, SpriteState.Thumbnail) : null;
            img.sprite = s;
            img.enabled = s != null;
        }
    }
}
