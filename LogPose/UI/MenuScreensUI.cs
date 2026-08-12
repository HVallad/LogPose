using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose.UI
{
    // Frames 2c / 2d / 2e: the settings page, the multiplayer format select and the
    // solo setup screen, imposed over the vanilla controls each poll (positions only —
    // every button keeps its own onClick). The game canvas is HEIGHT-matched, so
    // vertical center anchoring is safe everywhere; horizontal placement is either
    // centered content or edge-anchored corners (back button, sound, version).
    //
    // Shared-object trap: go_DeckSelector / go_BackButton / go_PlayerDeckText serve BOTH
    // the solo screen and the private lobby. The solo imposer only runs while SoloStart
    // is active, and the private branch restores the vanilla spots so the lobby never
    // inherits solo-layout positions.
    internal static class MenuScreensUI
    {
        private static HostJoinScript _hjs;

        internal static void Update(bool force = false)
        {
            if (!Plugin.CfgUiReskin.Value || (!force && Time.frameCount % 30 != 0))
                return;
            try
            {
                if (_hjs == null)
                    _hjs = Object.FindFirstObjectByType<HostJoinScript>();
                if (_hjs == null)
                    return;
                Theme.Ensure();
                ImposeSolo();
                ImposePrivateRestore();
                ImposeModeSelect();
                ImposeSettings();
            }
            catch { }
        }

        // ------------------------------------------------------------ shared helpers --

        private static RectTransform RT(GameObject go) => go != null ? go.transform as RectTransform : null;

        private static void C(RectTransform rt, float x, float y, float w = 0f, float h = 0f)
        {
            if (rt == null)
                return;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            if (w > 0f)
                rt.sizeDelta = new Vector2(w, h);
        }

        private static void Edge(RectTransform rt, float ax, float x, float y, float w = 0f, float h = 0f)
        {
            if (rt == null)
                return;
            rt.anchorMin = rt.anchorMax = new Vector2(ax, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            if (w > 0f)
                rt.sizeDelta = new Vector2(w, h);
        }

        private static TextMeshProUGUI CLabel(Transform t, string text, float x, float y,
            float w, float h, float size, Color col, int weight,
            TextAlignmentOptions align = TextAlignmentOptions.Center, bool mono = false, float track = 0f)
        {
            TextMeshProUGUI l = W.Label(t, text, 0f, 0f, w, h, size, col, weight, align, mono, track);
            C(l.rectTransform, x, y, w, h);
            return l;
        }

        private static Image Panel(Transform t, string name, float x, float y, float w, float h)
        {
            GameObject go = W.Go(name, t);
            Image im = go.AddComponent<Image>();
            im.sprite = UISprites.RoundedRect(64, 64, 14f, Theme.Surface, Theme.WithA(Theme.Text, 0.09f), 1f, 18f);
            im.type = Image.Type.Sliced;
            im.raycastTarget = false;
            C(go.GetComponent<RectTransform>(), x, y, w, h);
            return im;
        }

        // Relabel a vanilla button and fix its stretch-label margins (never raycast).
        private static void Relabel(GameObject go, string text, float size)
        {
            if (go == null)
                return;
            TMP_Text txt = go.GetComponentInChildren<TMP_Text>(true);
            if (txt == null)
                return;
            if (txt.raycastTarget)
                txt.raycastTarget = false;
            if (txt.enableAutoSizing)
                txt.enableAutoSizing = false;
            if (text != null && txt.text != text)
                txt.text = text;
            if (txt.fontSize != size)
                txt.fontSize = size;
            RectTransform trt = txt.rectTransform;
            bool stretch = trt.anchorMin.x != trt.anchorMax.x;
            Vector2 want = stretch ? new Vector2(-12f, -8f) : trt.sizeDelta;
            if (stretch && trt.sizeDelta != want)
            { trt.sizeDelta = want; trt.anchoredPosition = Vector2.zero; }
        }

        // ------------------------------------------------------------- 2e solo setup --

        private static RectTransform _soloChrome;
        private static Image _thumbP1, _thumbP2;
        private static TextMeshProUGUI _metaP1, _metaP2;
        private static string _capP1, _capP2;
        private static readonly Dictionary<string, KeyValuePair<string, int>> _deckLeaders
            = new Dictionary<string, KeyValuePair<string, int>>();

        private static void ImposeSolo()
        {
            GameObject start = _hjs.go_SoloStart;
            if (start == null)
                return;
            bool on = start.activeInHierarchy;
            if (_soloChrome != null && _soloChrome.gameObject.activeSelf != on)
                _soloChrome.gameObject.SetActive(on);
            if (!on)
                return;

            Transform cn = start.transform.parent;
            if (_soloChrome == null)
            {
                GameObject root = W.Go("LogPoseSoloUI", cn);
                _soloChrome = root.GetComponent<RectTransform>();
                _soloChrome.sizeDelta = Vector2.zero;
                C(_soloChrome, 0f, 0f);
                root.transform.SetSiblingIndex(1);   // above the BG, under every control
                Transform t = root.transform;

                CLabel(t, "S O L O   P L A Y", 0f, 382f, 400f, 22f, 12f, Theme.Accent300, 600);
                CLabel(t, "Pilot both sides", 0f, 322f, 800f, 56f, 42f, Theme.Text, 500);
                CLabel(t, "V S", 0f, 55f, 80f, 30f, 20f, Theme.WithA(Theme.Text, 0.35f), 500);

                Panel(t, "SeatP1", -360f, 55f, 560f, 250f);
                Panel(t, "SeatP2", 360f, 55f, 560f, 250f);

                _thumbP1 = Thumb(t, "ThumbP1", -550f, 40f);
                _thumbP2 = Thumb(t, "ThumbP2", 170f, 40f);
                _metaP1 = CLabel(t, "", -255f, 18f, 330f, 20f, 12f, Theme.TextMuted, 400,
                    TextAlignmentOptions.Center, true);
                _metaP2 = CLabel(t, "", 465f, 18f, 330f, 20f, 12f, Theme.TextMuted, 400,
                    TextAlignmentOptions.Center, true);
                _capP1 = _capP2 = null;
            }

            // Kickers reuse the vanilla P1/P2 labels so translation mods keep a target.
            Kicker(_hjs.go_P1DeckText, "P L A Y E R   1", -460f, 150f);
            Kicker(_hjs.go_P2DeckText, "P L A Y E R   2", 260f, 150f);

            C(RT(_hjs.go_DeckSelector), -255f, 80f, 330f, 56f);
            C(RT(_hjs.go_EnemyDeckSelector), 465f, 80f, 330f, 56f);
            CaptionSize(_hjs.go_DeckSelector);
            CaptionSize(_hjs.go_EnemyDeckSelector);

            C(RT(start), 0f, -200f, 420f, 68f);
            BoardHUD.StyleAsButton(start, 420f, 68f, 22f, BtnKind.Primary);
            Relabel(start, "Start match", 22f);

            GameObject load = _hjs.go_LoadStateFromClipboardButton;
            if (load != null)
            {
                C(RT(load), 0f, -278f, 320f, 48f);
                BoardHUD.StyleAsButton(load, 320f, 48f, 14f, BtnKind.Secondary);
                Relabel(load, "Load state from clipboard", 14f);
            }

            GameObject back = _hjs.go_BackButton;
            if (back != null)
            {
                Edge(RT(back), 0f, 150f, 468f, 190f, 48f);
                BoardHUD.StyleAsButton(back, 190f, 48f, 14f, BtnKind.Secondary);
                Relabel(back, "←  Main menu", 14f);
            }

            Transform vol = cn.Find("Volume");
            Transform mus = cn.Find("Music");
            if (vol != null) Edge(vol as RectTransform, 1f, -70f, 450f, 52f, 52f);
            if (mus != null) Edge(mus as RectTransform, 1f, -134f, 450f, 52f, 52f);
            Transform ver = cn.Find("Version Number");
            if (ver != null) Edge(ver as RectTransform, 1f, -230f, -505f);

            RefreshSeat(_hjs.go_DeckSelector, _thumbP1, _metaP1, ref _capP1);
            RefreshSeat(_hjs.go_EnemyDeckSelector, _thumbP2, _metaP2, ref _capP2);
        }

        private static Image Thumb(Transform t, string name, float x, float y)
        {
            GameObject slot = W.Go(name, t);
            Image bg = slot.AddComponent<Image>();
            bg.sprite = UISprites.RoundedRect(32, 32, 8f, Theme.Ground, Theme.WithA(Theme.Text, 0.12f), 1f, 10f);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
            C(slot.GetComponent<RectTransform>(), x, y, 120f, 168f);
            GameObject art = W.Go("Art", slot.transform);
            Image im = art.AddComponent<Image>();
            im.raycastTarget = false;
            im.enabled = false;
            RectTransform art_rt = art.GetComponent<RectTransform>();
            art_rt.anchorMin = Vector2.zero;
            art_rt.anchorMax = Vector2.one;
            art_rt.sizeDelta = new Vector2(-8f, -8f);
            return im;
        }

        private static void Kicker(GameObject go, string text, float x, float y)
        {
            if (go == null)
                return;
            TMP_Text txt = go.GetComponent<TMP_Text>();
            if (txt != null)
            {
                if (txt.text != text) txt.text = text;
                if (txt.fontSize != 12f) { txt.enableAutoSizing = false; txt.fontSize = 12f; }
                Color want = Theme.WithA(Theme.Text, 0.55f);
                if (txt.color != want) txt.color = want;
                txt.alignment = TextAlignmentOptions.MidlineLeft;
                if (txt.raycastTarget) txt.raycastTarget = false;
            }
            C(RT(go), x, y, 220f, 22f);
        }

        private static void CaptionSize(GameObject dd)
        {
            if (dd == null)
                return;
            Transform label = dd.transform.Find("Label");
            TMP_Text txt = label != null ? label.GetComponent<TMP_Text>() : null;
            if (txt != null && (txt.enableAutoSizing || txt.fontSize != 18f))
            { txt.enableAutoSizing = false; txt.fontSize = 18f; }
        }

        // Selected deck -> leader thumbnail + "Name · ID · N cards" (cached per deck file).
        private static void RefreshSeat(GameObject dd, Image thumb, TMP_Text meta, ref string lastCaption)
        {
            if (dd == null || thumb == null || meta == null)
                return;
            Transform label = dd.transform.Find("Label");
            TMP_Text cap = label != null ? label.GetComponent<TMP_Text>() : null;
            string deck = cap != null ? cap.text : null;
            if (deck == lastCaption)
                return;
            lastCaption = deck;
            thumb.enabled = false;
            meta.text = "";
            if (string.IsNullOrEmpty(deck))
                return;

            KeyValuePair<string, int> info;
            if (!_deckLeaders.TryGetValue(deck, out info))
            {
                string id = null;
                int count = 0;
                try
                {
                    string path = Path.Combine("Decks", deck + ".deck");
                    if (File.Exists(path))
                        foreach (string raw in File.ReadAllLines(path))
                        {
                            string line = raw.Trim();
                            int x = line.IndexOf('x');
                            if (x <= 0)
                                continue;
                            int n;
                            if (!int.TryParse(line.Substring(0, x), out n))
                                continue;
                            count += n;
                            if (id == null)
                                id = line.Substring(x + 1).Trim();
                        }
                }
                catch { }
                info = new KeyValuePair<string, int>(id, count);
                _deckLeaders[deck] = info;
            }
            if (info.Key == null)
                return;
            try
            {
                CardDatabaseScript db = CardDatabaseScript.Instance;
                Sprite s = db != null ? db.GetCardImage(info.Key, SpriteState.Thumbnail) : null;
                if (s != null)
                { thumb.sprite = s; thumb.enabled = true; }
                CardDefinition def = db != null ? db.FindDefinition(info.Key) : null;
                string name = def != null && !string.IsNullOrEmpty(def.characterName) ? def.characterName : info.Key;
                meta.text = name + " · " + info.Key + " · " + info.Value + " cards";
            }
            catch { }
        }

        // The private lobby lives on the MAIN canvas and borrows the solo screen's deck
        // selector and back button. When it's the owner (selector visible while the solo
        // screen is not), pin the selector back to its vanilla spot and keep the shared
        // back button in the design's top-left corner.
        private static void ImposePrivateRestore()
        {
            if (_hjs.go_DeckSelector == null || !_hjs.go_DeckSelector.activeSelf)
                return;
            if (_hjs.go_SoloStart != null && _hjs.go_SoloStart.activeInHierarchy)
                return;
            C(RT(_hjs.go_DeckSelector), -600f, 300f, 320f, 60f);
            GameObject back = _hjs.go_BackButton;
            if (back != null && back.activeSelf)
            {
                Edge(RT(back), 0f, 150f, 468f, 190f, 48f);
                BoardHUD.StyleAsButton(back, 190f, 48f, 14f, BtnKind.Secondary);
                Relabel(back, "←  Main menu", 14f);
            }
        }

        // -------------------------------------------------------- 2d format select ----

        private static RectTransform _modeChrome;

        private static void ImposeModeSelect()
        {
            GameObject mp = _hjs.go_MultiplayerCanvas;
            if (mp == null)
                return;
            bool canvasOn = mp.activeInHierarchy;
            if (_modeChrome != null && _modeChrome.gameObject.activeSelf != canvasOn)
                _modeChrome.gameObject.SetActive(canvasOn);
            if (!canvasOn)
                return;

            Transform cn = mp.transform;
            if (_modeChrome == null)
            {
                GameObject root = W.Go("LogPoseModeUI", cn);
                _modeChrome = root.GetComponent<RectTransform>();
                _modeChrome.sizeDelta = Vector2.zero;
                C(_modeChrome, 0f, 0f);
                CLabel(root.transform, "M U L T I P L A Y E R", 0f, 395f, 400f, 22f, 12f, Theme.Accent300, 600);
                CLabel(root.transform, "Choose a format", 0f, 338f, 800f, 54f, 40f, Theme.Text, 500);
            }

            float y = 240f;
            Row(_hjs.go_WesternButton, _hjs.go_WesternDescription, ref y);
            Row(_hjs.go_NationalsButton, _hjs.go_NationalsDescription, ref y);
            Row(_hjs.go_EasternButton, _hjs.go_EasternDescription, ref y);
            Row(_hjs.go_UnlimitedButton, _hjs.go_UnlimitedDescription, ref y);
            Row(_hjs.go_KoreanButton, _hjs.go_KoreanDescription, ref y);
            Row(_hjs.go_PrivateButton, _hjs.go_PrivateDescription, ref y);

            GameObject back = _hjs.go_MultiplayerBack;
            if (back != null)
            {
                Edge(RT(back), 0f, 150f, 468f, 190f, 48f);
                BoardHUD.StyleAsButton(back, 190f, 48f, 14f, BtnKind.Secondary);
                Relabel(back, "←  Back", 14f);
            }
        }

        private static void Row(GameObject btn, GameObject desc, ref float y)
        {
            if (btn == null || !btn.activeSelf)
            {
                if (desc != null && desc.activeSelf && btn != null && !btn.activeSelf)
                    desc.SetActive(false);
                return;
            }
            C(RT(btn), 0f, y, 900f, 88f);
            BoardHUD.StyleAsButton(btn, 900f, 88f, 20f, BtnKind.Secondary);
            TMP_Text title = btn.GetComponentInChildren<TMP_Text>(true);
            if (title != null)
            {
                title.alignment = TextAlignmentOptions.MidlineLeft;
                RectTransform trt = title.rectTransform;
                if (trt.anchorMin.x != trt.anchorMax.x)
                {
                    if (trt.sizeDelta != new Vector2(-48f, -8f))
                        trt.sizeDelta = new Vector2(-48f, -8f);
                    if (trt.anchoredPosition != new Vector2(0f, 16f))
                        trt.anchoredPosition = new Vector2(0f, 16f);
                }
            }
            if (desc != null)
            {
                TMP_Text d = desc.GetComponent<TMP_Text>();
                if (d != null)
                {
                    if (d.text.Contains("<br>"))
                        d.text = d.text.Replace("<br>", " · ");
                    d.alignment = TextAlignmentOptions.MidlineLeft;
                    d.enableAutoSizing = false;
                    d.fontSize = 13f;
                    d.color = Theme.WithA(Theme.Text, 0.55f);
                    if (d.raycastTarget)
                        d.raycastTarget = false;
                }
                C(RT(desc), -26f, y - 18f, 800f, 24f);
            }
            y -= 100f;
        }

        // ------------------------------------------------------------- 2c settings ----

        private static RectTransform _settingsChrome;
        private static Sprite _rowSprite, _boxSprite, _knobSprite;

        private static void ImposeSettings()
        {
            GameObject sc = _hjs.go_SettingsCanvas;
            if (sc == null)
                return;
            bool on = sc.activeInHierarchy;
            if (_settingsChrome != null && _settingsChrome.gameObject.activeSelf != on)
                _settingsChrome.gameObject.SetActive(on);
            if (!on)
                return;

            Transform cn = sc.transform;
            if (_settingsChrome == null)
            {
                GameObject root = W.Go("LogPoseSettingsUI", cn);
                _settingsChrome = root.GetComponent<RectTransform>();
                _settingsChrome.sizeDelta = Vector2.zero;
                C(_settingsChrome, 0f, 0f);
                root.transform.SetSiblingIndex(1);   // above the BG, under the toggles
                Transform t = root.transform;

                CLabel(t, "P R E F E R E N C E S", 0f, 395f, 400f, 22f, 12f, Theme.Accent300, 600);
                CLabel(t, "Settings", 0f, 338f, 600f, 54f, 40f, Theme.Text, 500);

                Section(t, "TURN FLOW", -544f, 250f, 3);
                Section(t, "COMBAT", 0f, 250f, 5);
                Section(t, "DISPLAY", 544f, 250f, 1);
                Section(t, "PRIVACY", 544f, 92f, 2);
            }

            // column x, panel top, row index
            ToggleRow(_hjs.go_AutoDraw, -544f, 250f, 0);
            ToggleRow(_hjs.go_ConfirmEnd, -544f, 250f, 1);
            ToggleRow(_hjs.go_OffsetEndTurn, -544f, 250f, 2);
            ToggleRow(_hjs.go_SkipBlock, 0f, 250f, 0);
            ToggleRow(_hjs.go_SkipTrigger, 0f, 250f, 1, warn: true);
            ToggleRow(_hjs.go_ConfirmDon, 0f, 250f, 2);
            ToggleRow(_hjs.go_ConfirmCounter, 0f, 250f, 3);
            ToggleRow(_hjs.go_AttachAll, 0f, 250f, 4);
            ToggleRow(_hjs.go_DynamicPlaysheets, 544f, 250f, 0);
            ToggleRow(_hjs.go_HideNames, 544f, 92f, 0);
            ToggleRow(_hjs.go_DontShare, 544f, 92f, 1);

            GameObject back = _hjs.go_SettingsBackButton;
            if (back != null)
            {
                Edge(RT(back), 0f, 150f, 468f, 190f, 48f);
                BoardHUD.StyleAsButton(back, 190f, 48f, 14f, BtnKind.Secondary);
                Relabel(back, "←  Back", 14f);
            }
        }

        private static void Section(Transform t, string heading, float x, float top, int rowCount)
        {
            float h = 40f + rowCount * 84f + (rowCount - 1) * 8f + 14f;
            Panel(t, "Sec" + heading, x, top - h * 0.5f, 520f, h);
            CLabel(t, string.Join(" ", heading.ToCharArray()), x - 130f, top - 24f, 240f, 20f,
                11f, Theme.Accent300, 600, TextAlignmentOptions.MidlineLeft);
        }

        private static void ToggleRow(GameObject tog, float colX, float panelTop, int i, bool warn = false)
        {
            if (tog == null)
                return;
            float y = panelTop - 40f - 42f - i * 92f;
            RectTransform rt = RT(tog);
            C(rt, colX, y, 484f, 84f);

            if (_rowSprite == null)
            {
                _rowSprite = UISprites.RoundedRect(32, 32, 8f, Theme.Ground, Theme.WithA(Theme.Text, 0.07f), 1f, 10f);
                _boxSprite = UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Text, 0.06f), Theme.WithA(Theme.Text, 0.3f), 1f, 10f);
                _knobSprite = UISprites.RoundedRect(24, 24, 6f, Theme.Accent, Theme.Accent300, 1f, 7f);
            }
            // The row surface lives on the toggle root, so the WHOLE row is clickable.
            Image row = tog.GetComponent<Image>();
            if (row == null)
                row = tog.AddComponent<Image>();
            if (row.sprite != _rowSprite)
            {
                row.sprite = _rowSprite;
                row.type = Image.Type.Sliced;
                row.color = Color.white;
                row.raycastTarget = true;
                Toggle tg = tog.GetComponent<Toggle>();
                if (tg != null)
                {
                    tg.targetGraphic = row;
                    ColorBlock cb = tg.colors;
                    cb.normalColor = Color.white;
                    cb.highlightedColor = new Color(0.92f, 0.91f, 1f, 1f);
                    cb.pressedColor = new Color(0.84f, 0.82f, 1f, 1f);
                    cb.selectedColor = Color.white;
                    tg.colors = cb;
                }
            }

            Transform bg = tog.transform.Find("Background");
            if (bg != null)
            {
                RectTransform brt = bg as RectTransform;
                brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.anchoredPosition = new Vector2(-46f, 0f);
                brt.sizeDelta = new Vector2(40f, 40f);
                Image bi = bg.GetComponent<Image>();
                if (bi != null && bi.sprite != _boxSprite)
                { bi.sprite = _boxSprite; bi.type = Image.Type.Sliced; bi.color = Color.white; }
                Transform chk = bg.Find("Checkmark");
                if (chk != null)
                {
                    RectTransform crt = chk as RectTransform;
                    crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
                    crt.anchoredPosition = Vector2.zero;
                    crt.sizeDelta = new Vector2(24f, 24f);
                    Image ci = chk.GetComponent<Image>();
                    if (ci != null && ci.sprite != _knobSprite)
                    { ci.sprite = _knobSprite; ci.type = Image.Type.Sliced; ci.color = Color.white; }
                }
            }

            Text label = tog.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                if (label.raycastTarget)
                    label.raycastTarget = false;
                label.alignment = TextAnchor.UpperLeft;
                label.fontSize = 17;
                label.color = Theme.Text;
                RectTransform lrt = label.rectTransform;
                if (lrt.anchorMin.x != lrt.anchorMax.x)
                {
                    lrt.sizeDelta = new Vector2(-130f, -24f);
                    lrt.anchoredPosition = new Vector2(-24f, -2f);
                }
            }

            // The matching description object sits beside the canvas root, named
            // "<toggle name> Description" — modulo vanilla's inconsistent spacing
            // ("Offset End Turn" pairs with "Offset EndTurn Description").
            Transform parent = tog.transform.parent;
            Transform desc = parent != null ? parent.Find(tog.name + " Description") : null;
            if (desc == null && parent != null)
            {
                string want = (tog.name + " Description").Replace(" ", "");
                for (int c = 0; c < parent.childCount; c++)
                {
                    Transform ch = parent.GetChild(c);
                    if (ch.name.EndsWith("Description") && ch.name.Replace(" ", "") == want)
                    { desc = ch; break; }
                }
            }
            if (desc != null)
            {
                TMP_Text d = desc.GetComponent<TMP_Text>();
                if (d != null)
                {
                    d.alignment = TextAlignmentOptions.TopLeft;
                    d.enableAutoSizing = false;
                    d.fontSize = 12f;
                    d.color = warn ? new Color(0.87f, 0.44f, 0.37f, 1f) : Theme.WithA(Theme.Text, 0.55f);
                    if (d.raycastTarget)
                        d.raycastTarget = false;
                }
                C(RT(desc.gameObject), colX - 34f, y - 14f, 380f, 40f);
            }
        }
    }
}
