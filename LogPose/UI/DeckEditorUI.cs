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
        private static readonly Image[] _bars = new Image[8];
        private static readonly TextMeshProUGUI[] _barCounts = new TextMeshProUGUI[8];
        private static string _shownLeader = "?";

        internal static void Update()
        {
            if (Time.frameCount % 30 != 0 || !Plugin.CfgUiReskin.Value)
                return;
            if (_ed == null)
            {
                _ed = Object.FindFirstObjectByType<DeckEditorScript>();
                if (_ed == null)
                {
                    _chrome = null;   // scene gone; clones died with it
                    _shownLeader = "?";
                    return;
                }
            }
            try
            {
                if (_chrome == null)
                    BuildChrome();
                Impose();
                RefreshDeckPanel();
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

            // The vanilla controls are all center-anchored, and the canvas can be wider
            // than the 1920 design (aspect). Panels are center-anchored on the same basis
            // and their headings live INSIDE them, so nothing can drift out of alignment.
            Image rail = W.Panel(t, "Rail", 0f, 0f, 320f, 920f, 14f, Theme.WithA(Theme.Surface, 0.55f),
                Theme.WithA(Theme.Text, 0.1f));
            rail.raycastTarget = false;
            Center(rail.rectTransform, -764f, -44f);
            W.Label(rail.transform, "COLOR", 40f, 72f, 200f, 18f, 12f, Theme.WithA(Theme.Text, 0.5f), 600,
                TextAlignmentOptions.TopLeft, false, 0.12f);
            W.Label(rail.transform, "OPTIONS", 40f, 226f, 200f, 18f, 12f, Theme.WithA(Theme.Text, 0.5f), 600,
                TextAlignmentOptions.TopLeft, false, 0.12f);

            Image deckPanel = W.Panel(t, "DeckPanel", 0f, 0f, 500f, 920f, 14f, Theme.WithA(Theme.Surface, 0.55f),
                Theme.WithA(Theme.Text, 0.1f));
            deckPanel.raycastTarget = false;
            Center(deckPanel.rectTransform, 672f, -44f);
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
            for (int i = 0; i < 8; i++)
            {
                GameObject b = W.Go("Bar" + i, dp);
                RectTransform rt = b.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0.5f, 0f);       // grow upward from the baseline
                rt.anchoredPosition = new Vector2(56f + i * 57f, -286f);
                rt.sizeDelta = new Vector2(44f, 4f);
                _bars[i] = b.AddComponent<Image>();
                _bars[i].sprite = UISprites.RoundedRect(24, 24, 4f, Color.white, Color.clear, 0f, 5f);
                _bars[i].type = Image.Type.Sliced;
                _bars[i].raycastTarget = false;
                W.Label(dp, i < 7 ? i.ToString() : "7+", 34f + i * 57f, 292f, 44f, 16f, 11f,
                    Theme.WithA(Theme.Text, 0.45f), 400, TextAlignmentOptions.Center, true);
                _barCounts[i] = W.Label(dp, "", 34f + i * 57f, 0f, 44f, 16f, 11f,
                    Theme.WithA(Theme.Text, 0.7f), 600, TextAlignmentOptions.Center, true);
            }
            _shownLeader = "?";
            Plugin.Log.LogInfo("Deck editor 2b chrome built.");
        }

        private static void Center(RectTransform rt, float x, float y)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
        }

        // ------------------------------------------------------------- repositioning --

        private static void Impose()
        {
            Transform cn = Cn();
            float railX = -764f;

            // Top bar (y 496 in centered coords).
            Move(cn, "BackButton", -892f, 496f, 90f, 48f);
            SetText(cn, "BackButton", "← Back", 14f);
            Move(cn, "DeckName", -660f, 496f, 360f, 48f);
            Move(cn, "NotificationText", -90f, 430f, 500f, 36f);
            RectTransform count = Move(cn, "DeckCount", -350f, 494f, 240f, 40f);
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
            Move(cn, "DeckSelector", 170f, 496f, 250f, 48f);
            Move(cn, "LoadButton", 400f, 496f, 110f, 48f);
            SetText(cn, "LoadButton", "Load", 16f);
            Move(cn, "PasteFromClipboard", 532f, 496f, 130f, 48f);
            SetText(cn, "PasteFromClipboard", "Import", 15f);
            Move(cn, "LogPoseAltArts", 664f, 496f, 120f, 48f);
            SetText(cn, "LogPoseAltArts", "Alt arts", 15f);
            RectTransform save = Move(cn, "SaveButton", 806f, 496f, 140f, 48f);
            if (save != null)
                BoardHUD.StyleAsButton(save.gameObject, 140f, 48f, 16f, BtnKind.Primary);

            // Left rail. The search field's pivot is off-center in the prefab — normalize
            // it so the position lands where aimed instead of poking out of the panel.
            RectTransform search = Move(cn, "SearchField", railX, 384f, 272f, 44f);
            if (search != null && search.pivot != new Vector2(0.5f, 0.5f))
            {
                search.pivot = new Vector2(0.5f, 0.5f);
                search.anchoredPosition = new Vector2(railX, 384f);
            }
            // Color roots shrink to 120 so the two columns' CLICK rects can't overlap.
            MoveToggle(cn, "Red", -845f, 302f, 120f);
            MoveToggle(cn, "Green", -725f, 302f, 120f);
            MoveToggle(cn, "Blue", -845f, 260f, 120f);
            MoveToggle(cn, "Purple", -725f, 260f, 120f);
            MoveToggle(cn, "Black", -845f, 218f, 120f);
            MoveToggle(cn, "Yellow", -725f, 218f, 120f);
            MoveToggle(cn, "Limit4", -830f, 148f);
            MoveToggle(cn, "Rotation", -830f, 106f);
            MoveToggle(cn, "SortByCost", -812f, 64f);
            MoveToggle(cn, "HideNumbers", -812f, 22f);
            // Bottom stack, spaced so nothing collides: help text, sponsor, utilities.
            RectTransform help = Move(cn, "SearchHelp", railX, -160f, 276f, 250f);
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
            RectTransform eg = Move(cn, "EgmanEvents", railX, -345f, 0f, 0f);
            if (eg != null && eg.localScale.x != 0.7f)
                eg.localScale = new Vector3(0.7f, 0.7f, 1f);
            RectTransform egx = Move(cn, "EgmanExplanation", railX, -410f, 260f, 24f);
            if (egx != null)
            {
                TMP_Text xt = egx.GetComponent<TMP_Text>();
                if (xt != null && xt.fontSize != 13f)
                {
                    xt.enableAutoSizing = false;
                    xt.fontSize = 13f;
                }
            }
            Move(cn, "Customize Images", railX, -448f, 210f, 38f);
            SetText(cn, "Customize Images", "Customize images", 14f);
            Move(cn, "DeleteButton", railX, -488f, 210f, 36f);
            SetText(cn, "DeleteButton", "Delete selected deck file", 12f);

            // Center browser.
            Move(cn, "Card Selector Scrollview", -90f, -44f, 980f, 920f);
            Move(cn, "NoCardsIndicator", -90f, -44f, 0f, 0f);
            if (_ed.tf_CardSelectorScrollview != null)
            {
                GridLayoutGroup grid = _ed.tf_CardSelectorScrollview.GetComponent<GridLayoutGroup>();
                if (grid != null && grid.constraintCount != 9)
                    grid.constraintCount = 9;
            }

            // Right deck panel: vanilla deck grid tucked under the leader/curve header.
            Move(cn, "Deck Scrollview", 672f, -160f, 470f, 560f);
            _ed.DeckXStart = 60f;
            _ed.DeckXStep = 110f;
            _ed.DeckYStart = -80f;
            _ed.DeckYStep = -150f;
            _ed.DeckColumns = 4;
            _ed.DeckHeight = 150f;
            RectTransform copy = Move(cn, "CopyToClipboard", 562f, -475f, 220f, 44f);
            if (copy != null)
                SetText(cn, "CopyToClipboard", "Copy list", 15f);
            RectTransform clear = Move(cn, "ClearButton", 800f, -475f, 190f, 44f);
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

        private static void MoveToggle(Transform cn, string name, float x, float y, float w = 0f)
        {
            RectTransform rt = Move(cn, name, x, y, w, w > 0f ? 40f : 0f);
            if (rt != null && rt.localScale.x != 0.85f)
                rt.localScale = new Vector3(0.85f, 0.85f, 1f);
        }

        private static void SetText(Transform cn, string name, string text, float size)
        {
            Transform t = cn.Find(name);
            if (t == null)
                return;
            TMP_Text txt = t.GetComponentInChildren<TMP_Text>(true);
            if (txt == null)
                return;
            if (txt.enableAutoSizing)
                txt.enableAutoSizing = false;
            if (txt.text != text)
                txt.text = text;
            if (txt.fontSize != size)
                txt.fontSize = size;
            RectTransform prt = t as RectTransform;
            if (prt != null && txt.rectTransform.sizeDelta.x != prt.sizeDelta.x - 12f)
                txt.rectTransform.sizeDelta = new Vector2(prt.sizeDelta.x - 12f, prt.sizeDelta.y - 8f);
        }

        // ------------------------------------------------------------- deck insights --

        private static void RefreshDeckPanel()
        {
            if (_ed.lgo_CurrentDeck == null)
                return;
            string leaderId = null;
            var counts = new int[8];
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
                counts[Mathf.Clamp(def.cardCost, 0, 7)]++;
                total++;
            }

            int max = 1;
            foreach (int c in counts)
                if (c > max)
                    max = c;
            for (int i = 0; i < 8; i++)
            {
                float hgt = counts[i] > 0 ? 8f + 86f * counts[i] / max : 4f;
                _bars[i].rectTransform.sizeDelta = new Vector2(44f, hgt);
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
