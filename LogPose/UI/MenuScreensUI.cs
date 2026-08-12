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
                ImposeBrowser();
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

        // ------------------------------------------- 4a lobby browser ------------------
        //
        // The redesigned browser (multiplayer-lobby.md, frame 4a): format tabs with an
        // accent underline + open count, a search/filter row, the lobby list with
        // left-anchored leader thumbs and SELECT-THEN-JOIN rows (a TaskOnClick prefix
        // turns the first click into a selection; the row's Join button re-invokes it
        // armed), a deck panel + create panel rail, and the sponsor tiles as a bottom
        // strip. Geometry is computed from the live canvas width each poll.

        private static RectTransform _browserChrome;
        private static readonly List<UnityEngine.UI.Button> _chips = new List<UnityEngine.UI.Button>();
        private static readonly string[] ChipLabels =
            { "Standard", "OP17", "Extra Regulation", "Unlimited", "Korean", "Private" };

        private static Image _tabLine, _deckPanel, _createPanel, _railThumb;
        private static TextMeshProUGUI _bEyebrow, _openCount, _deckKick, _createKick,
            _quickCap, _baseLbl, _incLbl, _railMeta;
        private static readonly List<UnityEngine.UI.Button> _segs = new List<UnityEngine.UI.Button>();
        private static readonly string[] SegLabels = { "All", "Timed", "Untimed" };
        private static TMP_InputField _search;
        private static string _searchText = "";
        private static int _segment;
        private static string _railCap;
        private static UnityEngine.UI.Button _joinBtn;
        private static string _selectedId;
        private static object _selectedLobby;
        private static GameLobbies _lobbies;
        private static bool _joinArmed;

        private static bool BrowserActive()
        {
            return _hjs.go_DeckSelector != null && _hjs.go_DeckSelector.activeSelf
                && (_hjs.go_SoloStart == null || !_hjs.go_SoloStart.activeInHierarchy)
                && ((_hjs.go_HostGame != null && _hjs.go_HostGame.activeSelf)
                 || (_hjs.go_JoinGame != null && _hjs.go_JoinGame.activeSelf));
        }

        // First click on a lobby row SELECTS it; only the armed Join click passes
        // through to the vanilla join. Kills the live build's join-on-misclick.
        [HarmonyLib.HarmonyPrefix]
        [HarmonyLib.HarmonyPatch(typeof(GameLobbies), "TaskOnClick")]
        private static bool TaskOnClick_Prefix(GameLobbies __instance, Unity.Services.Lobbies.Models.Lobby lobby)
        {
            if (!Plugin.CfgUiReskin.Value)
                return true;
            if (_joinArmed)
            {
                _joinArmed = false;
                return true;
            }
            _lobbies = __instance;
            _selectedLobby = lobby;
            _selectedId = "lobby" + lobby.Id;
            Plugin.OnScreenSwitched();
            return false;
        }

        private static void JoinSelected()
        {
            if (_lobbies == null || _selectedLobby == null)
                return;
            _joinArmed = true;
            try { _lobbies.TaskOnClick((Unity.Services.Lobbies.Models.Lobby)_selectedLobby); }
            catch { _joinArmed = false; }
        }

        private static Sprite _tabClear, _tabHover;

        private static void TabStyle(UnityEngine.UI.Button b, bool active)
        {
            if (_tabClear == null)
            {
                _tabClear = UISprites.RoundedRect(24, 24, 6f, Color.clear, Color.clear, 0f, 7f);
                _tabHover = UISprites.RoundedRect(24, 24, 6f, Theme.WithA(Theme.Text, 0.05f), Color.clear, 0f, 7f);
            }
            Image img = b.GetComponent<Image>();
            if (img != null && img.sprite != _tabClear)
            { img.sprite = _tabClear; img.type = Image.Type.Sliced; }
            b.spriteState = new UnityEngine.UI.SpriteState
            { highlightedSprite = _tabHover, pressedSprite = _tabHover, selectedSprite = _tabClear, disabledSprite = _tabClear };
            TMP_Text t = b.GetComponentInChildren<TMP_Text>(true);
            if (t != null)
            {
                Color want = active ? Theme.Accent300 : Theme.WithA(Theme.Text, 0.55f);
                if (t.color != want)
                    t.color = want;
            }
        }

        private static void ImposeBrowser()
        {
            bool on = BrowserActive();
            if (_browserChrome != null && _browserChrome.gameObject.activeSelf != on)
                _browserChrome.gameObject.SetActive(on);
            if (!on)
                return;
            bool priv = _hjs.go_JoinGame != null && _hjs.go_JoinGame.activeSelf;
            Transform cn = _hjs.go_DeckSelector.transform.parent;
            RectTransform cnrt = cn as RectTransform;
            float Wc = cnrt != null ? cnrt.rect.width : 1920f;
            float xL = -Wc * 0.5f + 72f, xR = Wc * 0.5f - 72f;
            float railCx = xR - 256f;
            float leftR = xR - 568f;
            float leftCx = (xL + leftR) * 0.5f;
            float leftW = leftR - xL;
            float rowW = leftW - 40f;

            if (_browserChrome == null)
            {
                GameObject root = W.Go("LogPoseBrowserUI", cn);
                _browserChrome = root.GetComponent<RectTransform>();
                _browserChrome.sizeDelta = Vector2.zero;
                C(_browserChrome, 0f, 0f);
                root.transform.SetSiblingIndex(1);
                Transform t = root.transform;

                _bEyebrow = CLabel(t, "M U L T I P L A Y E R", 0f, 505f, 320f, 18f, 11f,
                    Theme.Accent300, 600, TextAlignmentOptions.MidlineLeft);

                _chips.Clear();
                for (int i = 0; i < 6; i++)
                {
                    int idx = i;
                    UnityEngine.UI.Button b = W.Btn(t, ChipLabels[i], 0f, 0f, 128f, 44f,
                        BtnKind.Secondary, () => ChipClick(idx), 13f);
                    _chips.Add(b);
                }
                GameObject ul = W.Go("TabLine", t);
                _tabLine = ul.AddComponent<Image>();
                _tabLine.sprite = UISprites.RoundedRect(16, 4, 1f, Theme.Accent, Color.clear, 0f, 1f);
                _tabLine.type = Image.Type.Sliced;
                _tabLine.raycastTarget = false;
                _openCount = CLabel(t, "", 0f, 0f, 160f, 20f, 12f,
                    Theme.WithA(Theme.Text, 0.45f), 400, TextAlignmentOptions.MidlineRight, true);

                _segs.Clear();
                for (int i = 0; i < 3; i++)
                {
                    int idx = i;
                    _segs.Add(W.Btn(t, SegLabels[i], 0f, 0f, 86f, 44f, BtnKind.Secondary,
                        () => { _segment = idx; Plugin.OnScreenSwitched(); }, 13f));
                }

                _deckPanel = Panel(t, "DeckPanel", 0f, 0f, 512f, 250f);
                _deckKick = CLabel(t, "Y O U R   D E C K", 0f, 0f, 300f, 18f, 11f,
                    Theme.WithA(Theme.Text, 0.55f), 600, TextAlignmentOptions.MidlineLeft);
                _railThumb = Thumb(t, "RailThumb", 0f, 0f);
                _railMeta = CLabel(t, "", 0f, 0f, 320f, 18f, 11f, Theme.TextMuted, 400,
                    TextAlignmentOptions.Center, true);
                _quickCap = CLabel(t, "Drops you into the oldest open lobby", 0f, 0f, 420f, 18f,
                    12f, Theme.WithA(Theme.Text, 0.45f), 400);
                _createPanel = Panel(t, "CreatePanel", 0f, 0f, 512f, 545f);
                _createKick = CLabel(t, "C R E A T E   A   L O B B Y", 0f, 0f, 340f, 18f, 11f,
                    Theme.Accent300, 600, TextAlignmentOptions.MidlineLeft);
                _baseLbl = CLabel(t, "BASE TIME", 0f, 0f, 104f, 18f, 10f,
                    Theme.WithA(Theme.Text, 0.5f), 600, TextAlignmentOptions.MidlineRight);
                _incLbl = CLabel(t, "INCREMENT", 0f, 0f, 104f, 18f, 10f,
                    Theme.WithA(Theme.Text, 0.5f), 600, TextAlignmentOptions.MidlineRight);
                _railCap = null;
            }

            // Search field: a clone of the vanilla description input.
            if (_search == null && _hjs.go_LobbyDescription != null)
            {
                GameObject sgo = Object.Instantiate(_hjs.go_LobbyDescription, _browserChrome.transform);
                sgo.name = "LogPoseSearch";
                sgo.SetActive(true);
                _search = sgo.GetComponent<TMP_InputField>();
                if (_search != null)
                {
                    _search.onValueChanged.RemoveAllListeners();
                    _search.onEndEdit.RemoveAllListeners();
                    _search.text = "";
                    _search.onValueChanged.AddListener(v => { _searchText = v ?? ""; });
                    TMP_Text ph = _search.placeholder as TMP_Text;
                    if (ph != null)
                        ph.text = "Search host or leader…";
                }
            }

            // ---- per-poll layout ----
            // Chrome children can't edge-anchor (the container's rect is zero-size, so
            // its "edges" all sit at the canvas center) — compute from xL instead.
            C(_bEyebrow.rectTransform, xL + 358f, 505f);
            GameObject header = _hjs.go_LobbyHeader;
            if (header != null)
            {
                Edge(RT(header), 0f, 480f, 481f, 420f, 30f);
                TMP_Text ht = header.GetComponent<TMP_Text>();
                if (ht != null)
                {
                    ht.enableAutoSizing = false;
                    ht.fontSize = 21f;
                    ht.alignment = TextAlignmentOptions.MidlineLeft;
                    if (ht.raycastTarget)
                        ht.raycastTarget = false;
                }
            }
            GameObject back = _hjs.go_BackButton;
            if (back != null && back.activeSelf)
            {
                Edge(RT(back), 0f, 167f, 494f, 190f, 48f);
                BoardHUD.StyleAsButton(back, 190f, 48f, 14f, BtnKind.Secondary);
                Relabel(back, "←  Main menu", 14f);
            }
            Transform vol = cn.Find("Volume");
            Transform mus = cn.Find("Music");
            if (vol != null) Edge(vol as RectTransform, 1f, -104f, 494f, 48f, 48f);
            if (mus != null) Edge(mus as RectTransform, 1f, -160f, 494f, 48f, 48f);

            // Format tabs + underline + open count.
            int active = _hjs.gls_GameplayLogic != null
                ? ActiveChip(_hjs.gls_GameplayLogic.eMultiStyle.ToString(), priv) : -1;
            for (int i = 0; i < _chips.Count; i++)
            {
                if (_chips[i] == null)
                    continue;
                C(_chips[i].GetComponent<RectTransform>(), xL + 64f + i * 134f, 406f, 128f, 44f);
                TabStyle(_chips[i], i == active);
            }
            if (_tabLine != null)
            {
                bool showLine = active >= 0;
                if (_tabLine.gameObject.activeSelf != showLine)
                    _tabLine.gameObject.SetActive(showLine);
                if (showLine)
                    C(_tabLine.rectTransform, xL + 64f + active * 134f, 384f, 116f, 2f);
            }
            C(_openCount.rectTransform, leftR - 90f, 406f, 160f, 20f);

            // Filter row (public only).
            bool pubMode = !priv;
            if (_search != null && _search.gameObject.activeSelf != pubMode)
                _search.gameObject.SetActive(pubMode);
            for (int i = 0; i < _segs.Count; i++)
                if (_segs[i] != null && _segs[i].gameObject.activeSelf != pubMode)
                    _segs[i].gameObject.SetActive(pubMode);
            if (_openCount.gameObject.activeSelf != pubMode)
                _openCount.gameObject.SetActive(pubMode);
            if (pubMode)
            {
                float segX = leftR - 386f;
                if (_search != null)
                {
                    float sw = leftW - 500f;
                    C(RT(_search.gameObject), xL + sw * 0.5f, 332f, sw, 46f);
                }
                for (int i = 0; i < _segs.Count; i++)
                {
                    C(_segs[i].GetComponent<RectTransform>(), segX + i * 92f, 332f, 86f, 44f);
                    BoardHUD.StyleAsButton(_segs[i].gameObject, 86f, 44f, 13f,
                        _segment == i ? BtnKind.Primary : BtnKind.Secondary);
                }
                GameObject refresh = _hjs.go_RefreshLobbies;
                if (refresh != null)
                {
                    C(RT(refresh), leftR - 52f, 332f, 96f, 44f);
                    BoardHUD.StyleAsButton(refresh, 96f, 44f, 13f, BtnKind.Secondary);
                    Relabel(refresh, "Refresh", 13f);
                }
            }

            // The status label lands between the list and the sponsor strip.
            Transform guide = cn.Find("GuideText");
            if (guide != null)
            {
                C(guide as RectTransform, leftCx, -362f, 700f, 34f);
                TMP_Text gt = guide.GetComponent<TMP_Text>();
                if (gt != null)
                {
                    if (gt.raycastTarget) gt.raycastTarget = false;
                    gt.enableAutoSizing = false;
                    gt.fontSize = 13f;
                    gt.alignment = TextAlignmentOptions.Center;
                }
            }

            // Deck rail (both modes; private hides Quick join, so the panel shrinks).
            C(_deckPanel.rectTransform, railCx, priv ? 316f : 279f, 512f, priv ? 196f : 274f);
            C(_deckKick.rectTransform, railCx - 96f, 384f, 300f, 18f);
            C(_railThumb.transform.parent as RectTransform, railCx - 186f, 296f, 76f, 106f);
            C(RT(_hjs.go_DeckSelector), railCx + 42f, 312f, 320f, 48f);
            CaptionSize(_hjs.go_DeckSelector);
            C(_railMeta.rectTransform, railCx + 42f, 274f, 320f, 18f);
            if (_hjs.go_PlayerDeckText != null && _hjs.go_PlayerDeckText.activeSelf)
                _hjs.go_PlayerDeckText.SetActive(false);
            GameObject reason = _hjs.go_DeckValidateReason;
            if (reason != null)
            {
                C(RT(reason), railCx + 42f, 244f, 330f, 26f);
                TMP_Text rt2 = reason.GetComponent<TMP_Text>();
                if (rt2 != null)
                { rt2.enableAutoSizing = false; rt2.fontSize = 13f; rt2.alignment = TextAlignmentOptions.Center; }
            }
            RefreshSeat(_hjs.go_DeckSelector, _railThumb, _railMeta, ref _railCap);
            GameObject quick = _hjs.go_QuickJoin;
            if (quick != null && quick.activeSelf)
            {
                C(RT(quick), railCx, 208f, 464f, 58f);
                BoardHUD.StyleAsButton(quick, 464f, 58f, 17f, BtnKind.Primary);
                Relabel(quick, "Quick join", 17f);
            }
            // The caption belongs to Quick join — clear of the button above and the
            // panel border below, and hidden with it in private mode.
            if (_quickCap.gameObject.activeSelf != !priv)
                _quickCap.gameObject.SetActive(!priv);
            C(_quickCap.rectTransform, railCx, 162f, 440f, 18f);

            // Create panel — the private variant hides the description and share
            // checkbox, so its contents compact upward and the panel shrinks to fit.
            C(_createPanel.rectTransform, railCx, priv ? 20f : -125f, 512f, priv ? 320f : 545f);
            C(_createKick.rectTransform, railCx - 76f, priv ? 150f : 118f, 340f, 18f);
            if (_hjs.go_LobbyDescription != null && _hjs.go_LobbyDescription.activeSelf)
                C(RT(_hjs.go_LobbyDescription), railCx, 72f, 464f, 48f);
            if (_hjs.go_ShareLeaderInfo != null && _hjs.go_ShareLeaderInfo.activeSelf)
            {
                C(RT(_hjs.go_ShareLeaderInfo), railCx - 90f, 22f, 0f, 0f);
                RelabelToggle(_hjs.go_ShareLeaderInfo, "Show my leader in the list");
            }
            float timedY = priv ? 100f : -24f;
            C(RT(_hjs.go_IsTimerLobby), railCx - 90f, timedY, 0f, 0f);
            RelabelToggle(_hjs.go_IsTimerLobby, "Timed lobby");
            // The stepper follows the Timed toggle at (+150,-66): its two rows land at
            // toggle-y − 38 and − 92 — the labels right-align against them.
            C(_baseLbl.rectTransform, railCx - 160f, timedY - 38f, 104f, 18f);
            C(_incLbl.rectTransform, railCx - 160f, timedY - 92f, 104f, 18f);

            if (!priv)
            {
                GameObject list = _hjs.go_LobbyBG;
                if (list != null)
                {
                    C(RT(list), leftCx, -30f, leftW, 620f);
                    if (list.transform.childCount > 0)
                    {
                        RectTransform gl = list.transform.GetChild(0) as RectTransform;
                        if (gl != null && gl.sizeDelta != new Vector2(leftW - 24f, 584f))
                        { gl.sizeDelta = new Vector2(leftW - 24f, 584f); gl.anchoredPosition = new Vector2(0f, -6f); }
                    }
                }
                GameObject host = _hjs.go_HostGame;
                if (host != null)
                {
                    C(RT(host), railCx, -330f, 464f, 56f);
                    BoardHUD.StyleAsButton(host, 464f, 56f, 16f, BtnKind.Secondary);
                    Relabel(host, "Create lobby", 16f);
                }
                RowPass(rowW);
            }
            else
            {
                float tx = leftCx - 310f;
                foreach (GameObject tog in new[] { _hjs.go_WesternToggle, _hjs.go_NationalsToggle,
                    _hjs.go_EasternToggle, _hjs.go_UnlimitedToggle, _hjs.go_KoreanToggle, _hjs.go_PrivateToggle })
                {
                    if (tog != null && tog.activeSelf)
                    {
                        C(RT(tog), tx, 330f, 116f, 44f);
                        RulesetChip(tog);
                        tx += 124f;
                    }
                }
                GameObject hostP = _hjs.go_HostGamePrivateUnlimited;
                if (hostP != null)
                {
                    C(RT(hostP), railCx, -90f, 464f, 58f);
                    BoardHUD.StyleAsButton(hostP, 464f, 58f, 15f, BtnKind.Primary);
                }
                if (_hjs.go_IPAddress != null)
                    C(RT(_hjs.go_IPAddress), leftCx, 60f, 360f, 52f);
                GameObject join = _hjs.go_JoinGame;
                if (join != null)
                {
                    C(RT(join), leftCx, -10f, 360f, 56f);
                    BoardHUD.StyleAsButton(join, 360f, 56f, 16f, BtnKind.Secondary);
                }
            }

            // Community strip: uniform sponsor tiles along the bottom of the left lane.
            float tileStep = leftW / 4f;
            StackLeft(_hjs.go_Sponsor, leftCx - 1.5f * tileStep, -438f);
            StackLeft(_hjs.go_Sponsor2, leftCx - 0.5f * tileStep, -438f);
            StackLeft(_hjs.go_OPBounty, leftCx + 0.5f * tileStep, -438f);
            StackLeft(_hjs.go_MatchHistory, leftCx + 1.5f * tileStep, -438f);
            StackLeft(_hjs.go_SponsorButton1, xL + 24f, -505f);
            StackLeft(_hjs.go_SponsorButton2, xL + 68f, -505f);
            StackLeft(_hjs.go_SponsorButton3, xL + 112f, -505f);
        }

        // Filter + restyle every lobby row, maintain the selection and its Join button.
        private static void RowPass(float rowW)
        {
            GameObject listBG = _hjs.go_LobbyBG;
            if (listBG == null || listBG.transform.childCount == 0)
                return;
            GameLobbies gl = listBG.transform.GetChild(0).GetComponent<GameLobbies>();
            if (gl == null)
                return;
            // The serialized `content` field is unassigned at runtime — walk the
            // scroll view instead.
            Transform content = gl.content != null ? (Transform)gl.content
                : (gl.scrollViewContent != null ? gl.scrollViewContent.transform
                : gl.transform.Find("Viewport/Content"));
            if (content == null)
                return;
            _lobbies = gl;
            string q = (_searchText ?? "").Trim().ToLowerInvariant();
            int visible = 0;
            Transform selRow = null;
            for (int i = 0; i < content.childCount; i++)
            {
                Transform row = content.GetChild(i);
                if (!row.name.StartsWith("lobby"))
                    continue;
                TextMeshProUGUI[] txts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
                bool timed = txts.Length > 1 && !string.IsNullOrEmpty(txts[1].text);
                string label = txts.Length > 0 ? (txts[0].text ?? "") : "";
                bool show = (_segment == 0 || (_segment == 1) == timed)
                    && (q.Length == 0 || label.ToLowerInvariant().Contains(q));
                if (row.gameObject.activeSelf != show)
                    row.gameObject.SetActive(show);
                if (!show)
                    continue;
                visible++;
                // Each row carries a HorizontalLayoutGroup (children laid out in
                // SIBLING ORDER with flex widths) - anchor writes get overwritten every
                // layout pass, so restructure by reordering + LayoutElement instead.
                Transform lead = row.Find("LeaderTemplate");
                if (lead != null)
                {
                    if (lead.GetSiblingIndex() != 0)
                        lead.SetSiblingIndex(0);
                    LayoutElement le = lead.GetComponent<LayoutElement>();
                    if (le != null && le.preferredWidth != 44f)
                    { le.flexibleWidth = 0f; le.minWidth = 44f; le.preferredWidth = 44f; }
                }
                if (txts.Length > 0)
                {
                    Transform t0t = txts[0].transform;
                    if (t0t.parent == row && t0t.GetSiblingIndex() != 1)
                        t0t.SetSiblingIndex(1);
                    txts[0].alignment = TextAlignmentOptions.MidlineLeft;
                    if (txts[0].enableAutoSizing) txts[0].enableAutoSizing = false;
                    if (txts[0].fontSize != 19f) txts[0].fontSize = 19f;
                    if (txts[0].raycastTarget) txts[0].raycastTarget = false;
                }
                if (txts.Length > 1)
                {
                    txts[1].alignment = TextAlignmentOptions.Midline;
                    if (txts[1].enableAutoSizing) txts[1].enableAutoSizing = false;
                    if (txts[1].fontSize != 14f) txts[1].fontSize = 14f;
                    if (txts[1].raycastTarget) txts[1].raycastTarget = false;
                    LayoutElement le1 = txts[1].GetComponent<LayoutElement>();
                    if (le1 != null && le1.preferredWidth != 240f)
                    { le1.flexibleWidth = 0f; le1.preferredWidth = 240f; }
                }
                LayoutElement rle = row.GetComponent<LayoutElement>();
                if (rle == null)
                    rle = row.gameObject.AddComponent<LayoutElement>();
                if (rle.minHeight != 80f)
                    rle.minHeight = 80f;
                bool isSel = row.name == _selectedId;
                Image rimg = row.GetComponent<Image>();
                // Vanilla stamps WHITE on legal rows and red/green on illegal ones —
                // only tint the legal ones so the validity signal survives.
                if (rimg != null && rimg.color.g > 0.85f && rimg.color.r > 0.85f)
                    rimg.color = isSel ? new Color(0.72f, 0.66f, 1f, 0.75f) : new Color(1f, 1f, 1f, 0.42f);
                if (isSel)
                    selRow = row;
            }
            if (_openCount != null)
            {
                string want = visible + " open";
                if (_openCount.text != want)
                    _openCount.text = want;
            }
            if (selRow != null)
            {
                if (_joinBtn == null)
                    _joinBtn = W.Btn(_browserChrome.transform, "Join", 0f, 0f, 104f, 44f,
                        BtnKind.Primary, JoinSelected, 15f);
                Transform jt = _joinBtn.transform;
                if (jt.parent != selRow)
                    jt.SetParent(selRow, false);
                RectTransform jrt = jt as RectTransform;
                jrt.anchorMin = jrt.anchorMax = new Vector2(1f, 0.5f);
                jrt.pivot = new Vector2(0.5f, 0.5f);
                jrt.anchoredPosition = new Vector2(-66f, 0f);
                jrt.sizeDelta = new Vector2(104f, 44f);
                if (!_joinBtn.gameObject.activeSelf)
                    _joinBtn.gameObject.SetActive(true);
            }
            else if (_joinBtn != null && _joinBtn.gameObject.activeSelf)
            {
                _joinBtn.transform.SetParent(_browserChrome.transform, false);
                _joinBtn.gameObject.SetActive(false);
            }
        }

        // Vanilla checkbox toggles: spec checkbox sizing + spec wording.
        private static void RelabelToggle(GameObject tog, string text)
        {
            if (tog == null)
                return;
            TMP_Text txt = tog.GetComponentInChildren<TMP_Text>(true);
            if (txt != null)
            {
                if (txt.text != text) txt.text = text;
                if (txt.enableAutoSizing) txt.enableAutoSizing = false;
                if (txt.fontSize != 14f) txt.fontSize = 14f;
                txt.alignment = TextAlignmentOptions.MidlineLeft;
            }
            if (_chipBox == null)
            {
                _chipBox = UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Text, 0.04f),
                    Theme.WithA(Theme.Text, 0.22f), 1f, 10f);
                _chipFill = UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Accent, 0.16f),
                    Theme.Accent, 1f, 10f);
            }
            UnityEngine.UI.Toggle tg = tog.GetComponentInChildren<UnityEngine.UI.Toggle>(true);
            Transform bg = tg != null ? tg.transform.Find("Background") : null;
            if (bg != null)
            {
                RectTransform brt = bg as RectTransform;
                brt.sizeDelta = new Vector2(26f, 26f);
                Image bi = bg.GetComponent<Image>();
                if (bi != null && bi.sprite != _chipBox)
                { bi.sprite = _chipBox; bi.type = Image.Type.Sliced; bi.color = Color.white; }
                Transform chk = bg.Find("Checkmark");
                if (chk != null)
                {
                    RectTransform crt = chk as RectTransform;
                    crt.anchorMin = Vector2.zero;
                    crt.anchorMax = Vector2.one;
                    crt.anchoredPosition = Vector2.zero;
                    crt.sizeDelta = Vector2.zero;
                    Image ci = chk.GetComponent<Image>();
                    if (ci != null && ci.sprite != _chipFill)
                    { ci.sprite = _chipFill; ci.type = Image.Type.Sliced; ci.color = Color.white; }
                }
            }
        }

        private static readonly string[] ChipFormats =
            { "Western", "Nationals", "Eastern", "Unlimited", "Korean", "Private" };

        private static void ChipClick(int idx)
        {
            if (_hjs == null)
                return;
            EnterFormat(idx);
            if (idx >= 0 && idx < ChipFormats.Length)
                Plugin.CfgLastFormat.Value = ChipFormats[idx];
            Plugin.OnScreenSwitched();
        }

        private static void EnterFormat(int idx)
        {
            switch (idx)
            {
                case 0: _hjs.MultiPlayerWestern(); break;
                case 1: _hjs.MultiPlayerNationals(); break;
                case 2: _hjs.MultiPlayerEastern(); break;
                case 3: _hjs.MultiPlayerUnlimited(); break;
                case 4: _hjs.MultiPlayerKorean(); break;
                case 5: _hjs.MultiPlayer(); break;
            }
        }

        // The format-select screen is redundant now that the browser carries format
        // chips — Multiplayer goes straight to the browser on the last-used format.
        [HarmonyLib.HarmonyPrefix]
        [HarmonyLib.HarmonyPatch(typeof(HostJoinScript), "ShowMultiplayerCanvas")]
        private static bool ShowMultiplayerCanvas_Prefix(HostJoinScript __instance)
        {
            if (!Plugin.CfgUiReskin.Value)
                return true;
            try
            {
                _hjs = __instance;
                int idx = System.Array.IndexOf(ChipFormats, Plugin.CfgLastFormat.Value);
                EnterFormat(idx >= 0 ? idx : 0);
                Plugin.OnScreenSwitched();
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static int ActiveChip(string style, bool priv)
        {
            if (priv)
                return 5;
            switch (style)
            {
                case "Western": return 0;
                case "Nationals": return 1;
                case "Eastern": return 2;
                case "Unlimited": return 3;
                case "Korean": return 4;
                default: return -1;
            }
        }

        private static void StackLeft(GameObject go, float x, float y)
        {
            if (go == null || !go.activeSelf)
                return;
            RectTransform rt = RT(go);
            C(rt, x, y, 0f, 0f);
            if (rt != null && rt.localScale.x != 0.8f)
                rt.localScale = new Vector3(0.8f, 0.8f, 1f);
        }

        private static Sprite _chipBox, _chipFill;

        // The ruleset toggles carry LEGACY Text labels that are stretch-anchored with a
        // +272px sizeDelta — on a 116-wide chip the invisible label rect overhangs onto
        // BOTH neighbors and (raycasting by default) eats their clicks: the same disease
        // as the old "buttons are offset" bug. Margins + no raycast, and the checkbox
        // Background/Checkmark stretch into a full-chip selected fill.
        private static void RulesetChip(GameObject tog)
        {
            Text lt = tog.GetComponentInChildren<Text>(true);
            if (lt != null)
            {
                if (lt.raycastTarget)
                    lt.raycastTarget = false;
                lt.fontSize = 13;
                lt.alignment = TextAnchor.MiddleCenter;
                RectTransform lrt = lt.rectTransform;
                if (lrt.anchorMin.x != lrt.anchorMax.x && lrt.sizeDelta != new Vector2(-8f, -8f))
                { lrt.sizeDelta = new Vector2(-8f, -8f); lrt.anchoredPosition = Vector2.zero; }
            }
            if (_chipBox == null)
            {
                _chipBox = UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Text, 0.04f),
                    Theme.WithA(Theme.Text, 0.22f), 1f, 10f);
                _chipFill = UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Accent, 0.16f),
                    Theme.Accent, 1f, 10f);
            }
            Transform bg = tog.transform.Find("Background");
            if (bg != null)
            {
                RectTransform brt = bg as RectTransform;
                brt.anchorMin = Vector2.zero;
                brt.anchorMax = Vector2.one;
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.anchoredPosition = Vector2.zero;
                brt.sizeDelta = Vector2.zero;
                Image bi = bg.GetComponent<Image>();
                if (bi != null && bi.sprite != _chipBox)
                { bi.sprite = _chipBox; bi.type = Image.Type.Sliced; bi.color = Color.white; }
                Transform chk = bg.Find("Checkmark");
                if (chk != null)
                {
                    RectTransform crt = chk as RectTransform;
                    crt.anchorMin = Vector2.zero;
                    crt.anchorMax = Vector2.one;
                    crt.anchoredPosition = Vector2.zero;
                    crt.sizeDelta = Vector2.zero;
                    Image ci = chk.GetComponent<Image>();
                    if (ci != null && ci.sprite != _chipFill)
                    { ci.sprite = _chipFill; ci.type = Image.Type.Sliced; ci.color = Color.white; }
                }
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
