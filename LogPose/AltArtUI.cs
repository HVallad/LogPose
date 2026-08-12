using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose
{
    // Frame 2g's alt-art selector: a full-bleed modal split into a card list (every deck
    // card that has variant art) and an art pane (printing thumbnails, a large preview and
    // an explicit "Use this art" apply). Opened with the editor's Alt Arts button or the
    // configured key (F6).
    internal static class AltArtUI
    {
        private static GameObject _menuButton;
        private static GameObject _page;
        private static TMP_Text _fetchLabel;
        private static int _pageIdx;
        private static string _selCard;
        private static string _selArt = "";
        private const int RowsPerPage = 9;
        private const int MaxArtsPerRow = 7;

        // While the page is open the editor's physics-based card hover/click is suppressed
        // (see AltArtPatches) — otherwise clicking a thumbnail would also click the deck
        // card behind the overlay.
        internal static bool PageOpen { get { return _page != null; } }

        internal static void Update()
        {
            // Thumbnails and fetch completion must process even if the user left the editor
            // while a fetch was still streaming in.
            AltArtFetcher.MainThreadPump();
            bool fetchDone = AltArtFetcher.ConsumeFinished();
            if (fetchDone)
                AltArtManager.InvalidateVariantCache();

            DeckEditorScript editor = UnityEngine.Object.FindFirstObjectByType<DeckEditorScript>();
            if (editor == null)
            {
                _page = null;        // scene unloaded; clones died with it
                _menuButton = null;
                _fetchLabel = null;
                return;
            }
            if (fetchDone && _page != null)
                Rebuild();
            if (_page != null && AltArtFetcher.Running && _fetchLabel != null)
                _fetchLabel.text = AltArtFetcher.Status;
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
                _selCard = null;
                BuildPage(editor);
            }
        }

        private static void ClosePage()
        {
            if (_page != null)
                UnityEngine.Object.Destroy(_page);
            _page = null;
            _fetchLabel = null;
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
                // Right after the donor in the hierarchy, so the enlarged card hover preview
                // (drawn later on the canvas) stays on top of it like it does for the donor.
                _menuButton.transform.SetSiblingIndex(donor.transform.GetSiblingIndex() + 1);
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

        // Every unique card in the deck — custom art can be added to any of them, so
        // the list no longer gates on official variants existing.
        private static List<string> DeckCards(DeckEditorScript editor)
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
                if (!result.Contains(id))
                    result.Add(id);
            }
            return result;
        }

        private static string CardName(string cardID)
        {
            if (cardID == "Don")
                return "DON!!";
            CardDefinition def = CardDatabaseScript.Instance != null
                ? CardDatabaseScript.Instance.FindDefinition(cardID) : null;
            return def != null && !string.IsNullOrEmpty(def.characterName) ? def.characterName : cardID;
        }

        private static string ArtName(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
                return "Base print";
            if (suffix.StartsWith("custom:"))
            {
                string n = suffix.Substring(7);
                if (_selCard != null && n.StartsWith(_selCard, StringComparison.OrdinalIgnoreCase))
                    n = n.Substring(_selCard.Length).TrimStart('_', ' ', '-');
                return string.IsNullOrEmpty(n) ? "Custom" : "Custom · " + n;
            }
            string s = suffix.TrimStart('_');
            if (s.StartsWith("p"))
                return "Parallel " + s.Substring(1);
            if (s.StartsWith("alt"))
                return "Alternate " + s.Substring(3);
            return s;
        }

        private static void BuildPage(DeckEditorScript editor)
        {
            ClosePage();
            UI.Theme.Ensure();
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
            dim.color = new Color(0.06f, 0.066f, 0.11f, 0.72f);

            GameObject panel = UI.W.Go("Modal", _page.transform);
            RectTransform rt = panel.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1800f, 960f);
            Image bg = panel.AddComponent<Image>();
            bg.sprite = UI.UISprites.RoundedRect(64, 64, 14f, UI.Theme.Surface, UI.Theme.EdgeModal, 1f, 18f);
            bg.type = Image.Type.Sliced;
            Transform pt = panel.transform;

            // Header.
            UI.W.Label(pt, "LOGPOSE", 32f, 22f, 200f, 18f, 12f, UI.Theme.Accent400, 600,
                TextAlignmentOptions.TopLeft, false, 0.12f);
            UI.W.Label(pt, "Alt art selector", 32f, 42f, 500f, 40f, 28f, UI.Theme.Text, 500);
            UI.W.Label(pt, "Pick the printing each card uses in this deck — applied art shows in hand, on the field and in replays.",
                32f, 86f, 1000f, 22f, 13f, UI.Theme.TextMuted, 400);
            Button fetch = UI.W.Btn(pt, AltArtFetcher.Running ? AltArtFetcher.Status : "Fetch arts",
                1546f, 28f, 160f, 44f, UI.BtnKind.Secondary, StartFetchForDeck, 15f);
            _fetchLabel = fetch.GetComponentInChildren<TMP_Text>(true);
            UI.W.Btn(pt, "×", 1724f, 28f, 44f, 44f, UI.BtnKind.Secondary, ClosePage, 22f);

            List<string> cards = DeckCards(editor);
            if (cards.Count == 0)
            {
                UI.W.Label(pt, "Load a deck first — every card in it can then pick official parallels or your own custom art.",
                    450f, 420f, 900f, 120f, 20f, UI.Theme.TextMuted, 400, TextAlignmentOptions.Center);
                return;
            }
            // The DON!! pseudo-entry is always first: its ten cards can each carry a
            // different art.
            cards.Insert(0, "Don");
            if (_selCard == null || !cards.Contains(_selCard))
            {
                _selCard = cards[0];
                string act;
                AltArtManager.ActiveMap.TryGetValue(_selCard, out act);
                _selArt = act ?? "";
            }

            BuildList(pt, cards);
            BuildArtPane(pt);
        }

        // --- left rail: deck cards with variants ----------------------------------

        private static void BuildList(Transform pt, List<string> cards)
        {
            Image list = UI.W.Panel(pt, "List", 32f, 124f, 320f, 804f, 10f,
                UI.Theme.WithA(UI.Theme.Ground, 0.6f), UI.Theme.WithA(UI.Theme.Text, 0.08f));
            Transform lt = list.transform;

            int pages = Math.Max(1, (cards.Count + RowsPerPage - 1) / RowsPerPage);
            _pageIdx = Mathf.Clamp(_pageIdx, 0, pages - 1);
            int start = _pageIdx * RowsPerPage;
            for (int i = start; i < Math.Min(start + RowsPerPage, cards.Count); i++)
                BuildListRow(lt, cards[i], 12f + (i - start) * 74f);

            if (pages > 1)
            {
                UI.W.Btn(lt, "‹", 12f, 754f, 44f, 36f, UI.BtnKind.Secondary, () =>
                {
                    if (_pageIdx > 0) { _pageIdx--; Rebuild(); }
                }, 16f);
                UI.W.Label(lt, "Page " + (_pageIdx + 1) + " / " + pages, 70f, 762f, 180f, 22f, 13f,
                    UI.Theme.TextMuted, 400, TextAlignmentOptions.Center, true);
                UI.W.Btn(lt, "›", 264f, 754f, 44f, 36f, UI.BtnKind.Secondary, () =>
                {
                    if (_pageIdx < pages - 1) { _pageIdx++; Rebuild(); }
                }, 16f);
            }
        }

        private static void BuildListRow(Transform lt, string cardID, float y)
        {
            bool selected = cardID == _selCard;
            GameObject row = UI.W.Go("Row" + cardID, lt);
            UI.W.TL(row, 8f, y, 304f, 68f);
            Image bg = row.AddComponent<Image>();
            bg.sprite = selected
                ? UI.UISprites.RoundedRect(32, 32, 8f, UI.Theme.WithA(UI.Theme.Accent, 0.1f), UI.Theme.Accent, 1f, 9f)
                : UI.UISprites.RoundedRect(32, 32, 8f, UI.Theme.WithA(UI.Theme.Text, 0.02f), Color.clear, 0f, 9f);
            bg.type = Image.Type.Sliced;
            Button b = row.AddComponent<Button>();
            b.targetGraphic = bg;
            b.transition = Selectable.Transition.SpriteSwap;
            b.spriteState = new UnityEngine.UI.SpriteState
            {
                highlightedSprite = UI.UISprites.RoundedRect(32, 32, 8f, UI.Theme.WithA(UI.Theme.Accent, 0.08f),
                    UI.Theme.WithA(UI.Theme.Accent, 0.4f), 1f, 9f),
                pressedSprite = UI.UISprites.RoundedRect(32, 32, 8f, UI.Theme.WithA(UI.Theme.Accent, 0.16f),
                    UI.Theme.Accent, 1f, 9f)
            };
            string captured = cardID;
            b.onClick.AddListener(() =>
            {
                _selCard = captured;
                string act;
                AltArtManager.ActiveMap.TryGetValue(captured, out act);
                _selArt = act ?? "";
                Rebuild();
            });

            if (selected)
            {
                GameObject mark = UI.W.Go("Mark", row.transform);
                UI.W.TL(mark, 0f, 12f, 3f, 44f);
                Image mi = mark.AddComponent<Image>();
                mi.color = UI.Theme.Accent;
                mi.raycastTarget = false;
            }

            string active;
            AltArtManager.ActiveMap.TryGetValue(cardID, out active);
            Sprite thumb = AltArtManager.GetArtSprite(cardID, active ?? "", SpriteState.Thumbnail);
            if (thumb != null)
            {
                GameObject t = UI.W.Go("Thumb", row.transform);
                UI.W.TL(t, 10f, 9f, 36f, 50f);
                Image ti = t.AddComponent<Image>();
                ti.sprite = thumb;
                ti.raycastTarget = false;
            }
            TMP_Text name = UI.W.Label(row.transform, CardName(cardID), 58f, 12f, 236f, 22f, 15f,
                UI.Theme.Text, 500);
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            int artCount = AltArtManager.GetVariants(cardID).Count
                + AltArtManager.GetCustomArts(cardID).Count + 1;
            bool marked = !string.IsNullOrEmpty(active)
                || (cardID == "Don" && AltArtManager.GetDonList().Count > 1);
            string sub = cardID == "Don"
                ? artCount + " arts" + (AltArtManager.GetDonList().Count > 1 ? " · mixed" : "")
                : cardID + " · " + artCount + " arts" + (string.IsNullOrEmpty(active) ? "" : " · picked");
            UI.W.Label(row.transform, sub, 58f, 38f, 236f, 18f, 12f,
                marked ? UI.Theme.Accent300 : UI.Theme.TextMuted, 400,
                TextAlignmentOptions.TopLeft, true);
        }

        // --- art pane -------------------------------------------------------------

        private static void BuildArtPane(Transform pt)
        {
            string active;
            AltArtManager.ActiveMap.TryGetValue(_selCard, out active);
            active = active ?? "";
            var arts = new List<string> { "" };
            arts.AddRange(AltArtManager.GetVariants(_selCard));
            arts.AddRange(AltArtManager.GetCustomArts(_selCard));
            if (!arts.Contains(_selArt))
                _selArt = arts.Contains(active) ? active : "";

            UI.W.Label(pt, CardName(_selCard), 384f, 128f, 600f, 32f, 20f, UI.Theme.Text, 500);
            UI.W.Tag(pt, arts.Count + " ARTS AVAILABLE", 384f, 162f, false, outline: true);

            // Printing thumbnails.
            float x = 384f;
            int shown = 0;
            foreach (string suffix in arts)
            {
                if (shown++ >= MaxArtsPerRow)
                    break;
                BuildArtThumb(pt, suffix, x, 204f, suffix == _selArt, suffix == active);
                x += 184f;
            }
            if (arts.Count > MaxArtsPerRow)
                UI.W.Label(pt, "+" + (arts.Count - MaxArtsPerRow) + " more", x + 8f, 300f, 120f, 24f, 13f,
                    UI.Theme.TextMuted, 400);

            // Preview.
            Sprite full = AltArtManager.GetArtSprite(_selCard, _selArt, SpriteState.Full);
            if (full == null)
                full = AltArtManager.GetArtSprite(_selCard, _selArt, SpriteState.Thumbnail);
            Image pv = UI.W.Panel(pt, "PreviewSlot", 384f, 470f, 330f, 462f, 10f,
                UI.Theme.WithA(UI.Theme.Ground, 0.5f), UI.Theme.WithA(UI.Theme.Text, 0.08f));
            if (full != null)
            {
                GameObject img = UI.W.Go("Preview", pv.transform);
                UI.W.TL(img, 5f, 5f, 320f, 452f);
                Image im = img.AddComponent<Image>();
                im.sprite = full;
                im.raycastTarget = false;
            }

            float dx = 760f;
            UI.W.Label(pt, ArtName(_selArt), dx, 482f, 400f, 30f, 20f, UI.Theme.Text, 500);

            if (_selCard == "Don")
            {
                BuildDonDetails(pt, dx);
                return;
            }

            UI.W.Label(pt, _selCard + (string.IsNullOrEmpty(_selArt) ? "" : _selArt), dx, 516f, 400f, 22f, 14f,
                UI.Theme.TextMuted, 400, TextAlignmentOptions.TopLeft, true);
            UI.W.Rule(pt, dx, 552f, 420f);
            UI.W.Label(pt, "The chosen printing is used whenever this deck plays the card — in your hand, on the field, in the editor and in replays. Custom images live in the CustomArts folder next to the game.",
                dx, 568f, 440f, 70f, 13f, UI.Theme.TextMuted, 400);

            bool isActive = _selArt == active;
            Button use = UI.W.Btn(pt, isActive ? "In use" : "Use this art", dx, 660f, 200f, 48f,
                UI.BtnKind.Primary, ApplySelected, 16f);
            use.interactable = !isActive;
            UI.W.Btn(pt, "Reset to default", dx + 216f, 660f, 180f, 48f, UI.BtnKind.Secondary, () =>
            {
                AltArtManager.ActiveMap.Remove(_selCard);
                AltArtManager.SaveSidecar();
                AltArtManager.RefreshDeckEditorThumbnails();
                _selArt = "";
                Rebuild();
            }, 15f);
            UI.W.Btn(pt, "Add custom art…", dx, 724f, 200f, 44f, UI.BtnKind.Secondary, AddCustom, 14f);
            UI.W.Btn(pt, "Open art folder", dx + 216f, 724f, 180f, 44f, UI.BtnKind.Secondary, OpenCustomFolder, 14f);
        }

        // DON!! details: ten slots, each assignable to the selected art.
        private static void BuildDonDetails(Transform pt, float dx)
        {
            UI.W.Label(pt, "Each of the ten DON!! cards can carry its own art. Pick an art above, then click a slot — or use it for all ten.",
                dx, 516f, 440f, 48f, 13f, UI.Theme.TextMuted, 400);

            List<string> cur = AltArtManager.GetDonList();
            for (int i = 0; i < 10; i++)
            {
                string slotArt = cur.Count == 0 ? "" : cur[i % cur.Count];
                int col = i % 5, row = i / 5;
                BuildDonSlot(pt, i, slotArt, dx + col * 88f, 576f + row * 122f);
            }

            UI.W.Btn(pt, "Use for all 10", dx, 836f, 200f, 48f, UI.BtnKind.Primary, () =>
            {
                var all = new List<string>();
                for (int i = 0; i < 10; i++)
                    all.Add(_selArt);
                AltArtManager.SetDonList(all);
                Rebuild();
            }, 15f);
            UI.W.Btn(pt, "Reset all", dx + 216f, 836f, 120f, 48f, UI.BtnKind.Secondary, () =>
            {
                AltArtManager.SetDonList(new List<string>());
                Rebuild();
            }, 14f);
            UI.W.Btn(pt, "Add custom…", dx + 352f, 836f, 130f, 48f, UI.BtnKind.Secondary, AddCustom, 13f);
        }

        private static void BuildDonSlot(Transform pt, int slot, string slotArt, float x, float y)
        {
            GameObject go = UI.W.Go("DonSlot" + slot, pt);
            UI.W.TL(go, x, y, 76f, 112f);
            Image frame = go.AddComponent<Image>();
            frame.sprite = UI.UISprites.RoundedRect(24, 24, 6f, UI.Theme.WithA(UI.Theme.Text, 0.03f),
                UI.Theme.WithA(UI.Theme.Text, 0.14f), 1f, 7f);
            frame.type = Image.Type.Sliced;
            Button b = go.AddComponent<Button>();
            b.targetGraphic = frame;
            int captured = slot;
            b.onClick.AddListener(() => AssignDonSlot(captured));

            Sprite art = AltArtManager.GetArtSprite("Don", slotArt, SpriteState.Full);
            if (art != null)
            {
                GameObject img = UI.W.Go("Img", go.transform);
                UI.W.TL(img, 4f, 4f, 68f, 88f);
                Image ii = img.AddComponent<Image>();
                ii.sprite = art;
                ii.raycastTarget = false;
            }
            TMP_Text n = UI.W.Label(go.transform, "#" + (slot + 1), 0f, 92f, 76f, 18f, 11f,
                UI.Theme.TextMuted, 600, TextAlignmentOptions.Center, true);
            n.raycastTarget = false;
        }

        private static void AssignDonSlot(int slot)
        {
            List<string> cur = AltArtManager.GetDonList();
            var full = new List<string>();
            for (int i = 0; i < 10; i++)
                full.Add(cur.Count == 0 ? "" : cur[i % cur.Count]);
            full[slot] = _selArt;
            AltArtManager.SetDonList(full);
            Rebuild();
        }

        private static void AddCustom()
        {
            string path = FilePicker.PickImage("Choose an image for " + CardName(_selCard));
            if (string.IsNullOrEmpty(path))
                return;
            string suffix = AltArtManager.AddCustomArt(_selCard, path);
            if (suffix == null)
                return;
            _selArt = suffix;
            Rebuild();
        }

        private static void OpenCustomFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(AltArtManager.CustomArtsDir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AltArtManager.CustomArtsDir,
                    UseShellExecute = true
                });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Open folder failed: " + e.Message);
            }
        }

        private static void BuildArtThumb(Transform pt, string suffix, float x, float y, bool viewSelected, bool inUse)
        {
            Sprite thumb = AltArtManager.GetArtSprite(_selCard, suffix, SpriteState.Thumbnail);
            if (thumb == null)
                thumb = AltArtManager.GetArtSprite(_selCard, suffix, SpriteState.Full);
            if (thumb == null)
                return;

            GameObject slot = UI.W.Go("Art" + suffix, pt);
            UI.W.TL(slot, x, y, 168f, 236f);
            Image frame = slot.AddComponent<Image>();
            frame.sprite = viewSelected
                ? UI.UISprites.RoundedRect(32, 32, 8f, UI.Theme.WithA(UI.Theme.Accent, 0.12f), UI.Theme.Accent, 1f, 9f)
                : UI.UISprites.RoundedRect(32, 32, 8f, UI.Theme.WithA(UI.Theme.Text, 0.03f),
                    UI.Theme.WithA(UI.Theme.Text, 0.12f), 1f, 9f);
            frame.type = Image.Type.Sliced;
            Button b = slot.AddComponent<Button>();
            b.targetGraphic = frame;
            string captured = suffix;
            b.onClick.AddListener(() => { _selArt = captured; Rebuild(); });

            GameObject img = UI.W.Go("Img", slot.transform);
            UI.W.TL(img, 5f, 5f, 158f, 220f);
            Image ii = img.AddComponent<Image>();
            ii.sprite = thumb;
            ii.raycastTarget = false;

            if (inUse)
            {
                TMP_Text tag = UI.W.Label(slot.transform, "IN USE", 0f, 210f, 168f, 20f, 11f,
                    UI.Theme.Accent300, 600, TextAlignmentOptions.Center, false, 0.12f);
                tag.raycastTarget = false;
            }
            else if (suffix.StartsWith("custom:"))
            {
                TMP_Text tag = UI.W.Label(slot.transform, "CUSTOM", 0f, 210f, 168f, 20f, 10f,
                    UI.Theme.WithA(UI.Theme.Text, 0.5f), 600, TextAlignmentOptions.Center, false, 0.12f);
                tag.raycastTarget = false;
            }
        }

        private static void ApplySelected()
        {
            if (string.IsNullOrEmpty(_selArt))
                AltArtManager.ActiveMap.Remove(_selCard);
            else
                AltArtManager.ActiveMap[_selCard] = _selArt;
            AltArtManager.SaveSidecar();
            AltArtManager.RefreshDeckEditorThumbnails();
            Rebuild();
        }

        // Probe the official card sites for every unique card in the current deck — the
        // in-game, per-deck version of tools\Fetch-AltArts.ps1.
        private static void StartFetchForDeck()
        {
            if (AltArtFetcher.Running)
                return;
            DeckEditorScript editor = UnityEngine.Object.FindFirstObjectByType<DeckEditorScript>();
            if (editor == null || editor.lgo_CurrentDeck == null)
                return;
            var ids = new List<string>();
            foreach (GameObject go in editor.lgo_CurrentDeck)
            {
                if (go == null)
                    continue;
                CardLogicScript cls = go.GetComponent<CardLogicScript>();
                if (cls != null && cls.myCard.cardDef != null && !ids.Contains(cls.myCard.cardDef.cardID))
                    ids.Add(cls.myCard.cardDef.cardID);
            }
            AltArtFetcher.StartFetch(ids);
        }
    }
}
