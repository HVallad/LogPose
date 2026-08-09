using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LogPose
{
    // Native alt-art selector for the deck editor: one row per deck card that has variant
    // art, every art (base + parallels) as a clickable thumbnail with the active pick
    // highlighted, hover to enlarge. Built from the editor's own button visuals so it reads
    // as a built-in feature. Opened with the "Alt Arts" button or the configured key (F6).
    internal static class AltArtUI
    {
        private static GameObject _menuButton;
        private static GameObject _page;
        private static GameObject _hoverPreview;
        private static int _pageIdx;
        private const int RowsPerPage = 5;
        private const int MaxArtsPerRow = 10;

        // While the page is open the editor's physics-based card hover/click is suppressed
        // (see AltArtPatches) — otherwise clicking a thumbnail would also click the deck
        // card behind the overlay.
        internal static bool PageOpen { get { return _page != null; } }

        internal static void Update()
        {
            DeckEditorScript editor = UnityEngine.Object.FindFirstObjectByType<DeckEditorScript>();
            if (editor == null)
            {
                _page = null;        // scene unloaded; clones died with it
                _menuButton = null;
                _hoverPreview = null;
                return;
            }
            if (_menuButton == null && Time.frameCount % 30 == 0)
                CreateMenuButton(editor);
            if (Input.GetKeyDown(Plugin.CfgAltArtKey.Value))
                Toggle(editor);
            if (_page != null && Input.GetKeyDown(KeyCode.Escape))
                ClosePage();
        }

        private static void Toggle(DeckEditorScript editor)
        {
            if (_page != null)
            {
                ClosePage();
            }
            else
            {
                _pageIdx = 0;
                BuildPage(editor);
            }
        }

        private static void ClosePage()
        {
            if (_page != null)
                UnityEngine.Object.Destroy(_page);
            _page = null;
            _hoverPreview = null;
        }

        private static void Rebuild()
        {
            DeckEditorScript editor = UnityEngine.Object.FindFirstObjectByType<DeckEditorScript>();
            if (editor != null)
                BuildPage(editor);
        }

        private static void CreateMenuButton(DeckEditorScript editor)
        {
            try
            {
                GameObject donor = editor.go_CustomImages;
                if (donor == null)
                    return;
                _menuButton = UnityEngine.Object.Instantiate(donor, donor.transform.parent);
                _menuButton.name = "LogPoseAltArts";
                _menuButton.SetActive(true);
                Button b = _menuButton.GetComponent<Button>();
                if (b == null)
                    b = _menuButton.AddComponent<Button>();
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(() =>
                {
                    DeckEditorScript ed = UnityEngine.Object.FindFirstObjectByType<DeckEditorScript>();
                    if (ed != null)
                        Toggle(ed);
                });
                TMP_Text tmp = _menuButton.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null)
                {
                    tmp.text = "Alt Arts";
                    // The donor's label auto-sizes; a shorter string would balloon.
                    tmp.enableAutoSizing = false;
                    tmp.fontSize = 28f;
                }
                RectTransform rt = _menuButton.GetComponent<RectTransform>();
                RectTransform drt = donor.GetComponent<RectTransform>();
                rt.anchoredPosition = drt.anchoredPosition + new Vector2(0f, -drt.sizeDelta.y - 16f);
                Plugin.Log.LogInfo("Alt Arts editor button created.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Alt Arts button failed: " + e.Message);
            }
        }

        private static List<string> DeckCardsWithVariants(DeckEditorScript editor)
        {
            var result = new List<string>();
            if (editor.lgo_CurrentDeck == null)
                return result;
            foreach (GameObject go in editor.lgo_CurrentDeck)
            {
                if (go == null)
                    continue;
                CardLogicScript cls = go.GetComponent<CardLogicScript>();
                if (cls == null || cls.myCard.cardDef == null)
                    continue;
                string id = cls.myCard.cardDef.cardID;
                if (!result.Contains(id) && AltArtManager.GetVariants(id).Count > 0)
                    result.Add(id);
            }
            return result;
        }

        private static void BuildPage(DeckEditorScript editor)
        {
            ClosePage();
            GameObject donor = editor.go_CustomImages;
            Canvas canvas = donor != null ? donor.GetComponentInParent<Canvas>() : null;
            if (canvas == null)
                return;

            _page = new GameObject("LogPoseAltArtPage", typeof(RectTransform));
            _page.transform.SetParent(canvas.transform, false);
            _page.transform.SetAsLastSibling();
            RectTransform prt = _page.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            Image dim = _page.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);

            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(_page.transform, false);
            RectTransform rt = panel.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1560f, 1000f);
            Image bg = panel.AddComponent<Image>();
            Image donorImg = donor.GetComponent<Image>();
            if (donorImg != null)
            {
                bg.sprite = donorImg.sprite;
                bg.type = donorImg.type;
            }
            bg.color = new Color(0.93f, 0.87f, 0.72f, 0.98f);

            MakeLabel(donor, panel, "Alt Art Selector", new Vector2(0f, 450f), new Vector2(700f, 70f), 42f);
            MakeLabel(donor, panel, "Click an art to use it — choices save immediately.  Hover an art to enlarge it.",
                new Vector2(0f, 402f), new Vector2(1200f, 40f), 22f);

            List<string> cards = DeckCardsWithVariants(editor);
            if (cards.Count == 0)
                MakeLabel(donor, panel,
                    "No variant art found for the cards in this deck.\n\nRun tools\\Fetch-AltArts.ps1 to download official parallel arts.",
                    new Vector2(0f, 0f), new Vector2(1100f, 220f), 28f);

            int start = _pageIdx * RowsPerPage;
            for (int i = start; i < Math.Min(start + RowsPerPage, cards.Count); i++)
                BuildRow(donor, panel, cards[i], 295f - (i - start) * 158f);

            int pages = Math.Max(1, (cards.Count + RowsPerPage - 1) / RowsPerPage);
            MakeSmallButton(donor, panel, "< Prev", new Vector2(-260f, -452f), () =>
            {
                if (_pageIdx > 0) { _pageIdx--; Rebuild(); }
            });
            MakeLabel(donor, panel, "Page " + (_pageIdx + 1) + "/" + pages, new Vector2(0f, -452f), new Vector2(240f, 55f), 26f);
            MakeSmallButton(donor, panel, "Next >", new Vector2(260f, -452f), () =>
            {
                if ((_pageIdx + 1) * RowsPerPage < cards.Count) { _pageIdx++; Rebuild(); }
            });
            MakeSmallButton(donor, panel, "Close", new Vector2(700f, 450f), ClosePage);

            // Enlarged art shown while the pointer rests on a thumbnail; never a raycast
            // target so it can't steal the hover it's illustrating.
            GameObject prev = new GameObject("HoverPreview", typeof(RectTransform));
            prev.transform.SetParent(_page.transform, false);
            prev.transform.SetAsLastSibling();
            Image pImg = prev.AddComponent<Image>();
            pImg.raycastTarget = false;
            RectTransform pRt = prev.GetComponent<RectTransform>();
            pRt.sizeDelta = new Vector2(460f, 642f);
            pRt.anchoredPosition = Vector2.zero;
            prev.SetActive(false);
            _hoverPreview = prev;
        }

        private static void BuildRow(GameObject donor, GameObject panel, string cardID, float y)
        {
            string current;
            AltArtManager.ActiveMap.TryGetValue(cardID, out current);
            CardDefinition def = CardDatabaseScript.Instance != null
                ? CardDatabaseScript.Instance.FindDefinition(cardID) : null;
            string name = (def != null && !string.IsNullOrEmpty(def.characterName)) ? def.characterName : cardID;
            MakeLabel(donor, panel, name + "\n<size=70%>" + cardID + "</size>",
                new Vector2(-645f, y), new Vector2(230f, 145f), 23f);

            var arts = new List<string> { "" };
            arts.AddRange(AltArtManager.GetVariants(cardID));
            const float x0 = -460f;
            const float step = 104f;
            for (int k = 0; k < arts.Count && k < MaxArtsPerRow; k++)
            {
                string suffix = arts[k];
                bool selected = string.IsNullOrEmpty(current) ? suffix == "" : current == suffix;
                MakeThumb(panel, cardID, suffix, new Vector2(x0 + k * step, y), selected);
            }
        }

        private static void MakeThumb(GameObject panel, string cardID, string suffix, Vector2 pos, bool selected)
        {
            Sprite thumb = AltArtManager.GetArtSprite(cardID, suffix, SpriteState.Thumbnail);
            if (thumb == null)
                thumb = AltArtManager.GetArtSprite(cardID, suffix, SpriteState.Full);
            if (thumb == null)
                return;

            if (selected)
            {
                GameObject sel = new GameObject("Selected", typeof(RectTransform));
                sel.transform.SetParent(panel.transform, false);
                Image selImg = sel.AddComponent<Image>();
                selImg.color = new Color(0.16f, 0.52f, 0.18f, 0.95f);
                selImg.raycastTarget = false;
                RectTransform srt = sel.GetComponent<RectTransform>();
                srt.anchoredPosition = pos;
                srt.sizeDelta = new Vector2(104f, 146f);
            }

            GameObject go = new GameObject("Art_" + cardID + suffix, typeof(RectTransform));
            go.transform.SetParent(panel.transform, false);
            Image img = go.AddComponent<Image>();
            img.sprite = thumb;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(94f, 131f);

            string capturedSuffix = suffix;
            Button b = go.AddComponent<Button>();
            b.onClick.AddListener(() =>
            {
                if (string.IsNullOrEmpty(capturedSuffix))
                    AltArtManager.ActiveMap.Remove(cardID);
                else
                    AltArtManager.ActiveMap[cardID] = capturedSuffix;
                AltArtManager.SaveSidecar();
                AltArtManager.RefreshDeckEditorThumbnails();
                Rebuild();
            });

            EventTrigger trig = go.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(delegate
            {
                if (_hoverPreview == null)
                    return;
                Sprite full = AltArtManager.GetArtSprite(cardID, capturedSuffix, SpriteState.Full);
                if (full == null)
                    return;
                _hoverPreview.GetComponent<Image>().sprite = full;
                _hoverPreview.SetActive(true);
            });
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(delegate
            {
                if (_hoverPreview != null)
                    _hoverPreview.SetActive(false);
            });
            trig.triggers.Add(enter);
            trig.triggers.Add(exit);
        }

        private static void MakeLabel(GameObject donor, GameObject parent, string text, Vector2 pos, Vector2 size, float fontSize)
        {
            TMP_Text dt = donor.GetComponentInChildren<TMP_Text>(true);
            GameObject lbl = new GameObject("LogPoseLabel", typeof(RectTransform));
            lbl.transform.SetParent(parent.transform, false);
            TextMeshProUGUI tmp = lbl.AddComponent<TextMeshProUGUI>();
            if (dt != null)
                tmp.font = dt.font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = new Color(0.13f, 0.09f, 0.05f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            RectTransform rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void MakeSmallButton(GameObject donor, GameObject parent, string label, Vector2 pos, Action onClick)
        {
            GameObject btn = UnityEngine.Object.Instantiate(donor, parent.transform);
            btn.name = "LogPoseBtn_" + label;
            btn.SetActive(true);
            Button b = btn.GetComponent<Button>();
            if (b == null)
                b = btn.AddComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(() => onClick());
            TMP_Text tmp = btn.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = label;
                tmp.fontSize = 26f;
            }
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(190f, 58f);
        }
    }
}
