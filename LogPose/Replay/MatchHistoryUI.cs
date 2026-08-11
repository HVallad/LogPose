using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose.Replay
{
    // "Match History" on the main menu: every recorded game as a row — your leader left,
    // the enemy's right, outcome colored — built from cloned native menu buttons. Clicking
    // a row starts a Solo v Self board and opens the replay on it.
    internal static class MatchHistoryUI
    {
        private class Entry
        {
            public Rz1File Game;
            public string Label;
            public string UserLeader = "", EnemyLeader = "";
            public string UserName = "", EnemyName = "";
            public string Outcome = "—";   // WIN / LOSS / SOLO / —
        }

        private static GameObject _menuButton;
        private static GameObject _page;

        internal static bool PageOpen => _page != null;

        internal static void Open(HostJoinScript hjs) => OpenPage(hjs);
        private static List<Entry> _entries;
        private static int _pageIdx;
        private const int RowsPerPage = 7;

        public static void Update()
        {
            if (_loadingCover != null && Time.unscaledTime - _coverShownAt > 8f)
                HideLoadingCover();
            if (Time.frameCount % 30 != 0)
                return;
            HostJoinScript hjs = UnityEngine.Object.FindFirstObjectByType<HostJoinScript>();
            if (hjs == null || hjs.go_SoloVSelf == null)
                return;
            if (_menuButton == null)
                CreateMenuButton(hjs);
            if (_menuButton != null)
                _menuButton.SetActive(hjs.go_SoloVSelf.activeSelf && _page == null);
        }

        private static void CreateMenuButton(HostJoinScript hjs)
        {
            try
            {
                GameObject donor = hjs.go_SoloVSelf;
                _menuButton = UnityEngine.Object.Instantiate(donor, donor.transform.parent);
                _menuButton.name = "LogPoseMatchHistory";
                Button b = _menuButton.GetComponent<Button>();
                if (b == null)
                    b = _menuButton.AddComponent<Button>();
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(() => OpenPage(hjs));
                TMP_Text tmp = _menuButton.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null)
                    tmp.text = "Match History";
                RectTransform rt = _menuButton.GetComponent<RectTransform>();
                RectTransform drt = donor.GetComponent<RectTransform>();
                rt.anchoredPosition = drt.anchoredPosition + new Vector2(0f, 168f);
                rt.sizeDelta = new Vector2(drt.sizeDelta.x, drt.sizeDelta.y * 0.62f);
                Plugin.Log.LogInfo("Match history menu button created.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Match history button failed: " + e.Message);
            }
        }

        private static string Normalize(string s)
        {
            if (s == null)
                return "";
            var sb = new StringBuilder();
            foreach (char c in s)
                if (c >= ' ' && c < 127)
                    sb.Append(c);
            return sb.ToString().Trim();
        }

        private static void Scan()
        {
            _entries = new List<Entry>();
            string dir = Path.Combine("CombatLogs", "AutoSaved");
            if (!Directory.Exists(dir))
                return;
            foreach (string f in Directory.GetFiles(dir, "*.rz1").OrderByDescending(x => File.GetLastWriteTime(x)))
            {
                List<Rz1File> games;
                try { games = Rz1Parser.ParseGames(f); }
                catch { continue; }
                string stamp = Path.GetFileNameWithoutExtension(f).Replace("T", "  ").Replace(".", ":");
                for (int gi = games.Count - 1; gi >= 0; gi--)
                {
                    Rz1File g = games[gi];
                    var e = new Entry { Game = g };
                    string p1 = Normalize(g.Player1), p2 = Normalize(g.Player2);
                    // The opponent is whoever "Has Connected" from this client's perspective.
                    string connected = "";
                    if (g.HumanLines != null)
                        foreach (KeyValuePair<int, string> kv in g.HumanLines)
                            if (kv.Value.EndsWith(" Has Connected", StringComparison.Ordinal))
                            {
                                connected = Normalize(kv.Value.Substring(0, kv.Value.Length - " Has Connected".Length));
                                break;
                            }
                    bool userIsP1 = connected != "" && connected == p2;
                    bool solo = p1 == p2;
                    e.UserName = userIsP1 ? p1 : p2;
                    e.EnemyName = userIsP1 ? p2 : p1;
                    e.UserLeader = userIsP1 ? g.Leader1 : g.Leader2;
                    e.EnemyLeader = userIsP1 ? g.Leader2 : g.Leader1;
                    e.Outcome = solo ? "SOLO" : DetectOutcome(g, e.UserName);
                    e.Label = stamp + (games.Count > 1 ? "  ·  game " + (gi + 1) : "");
                    _entries.Add(e);
                }
            }
        }

        private static string DetectOutcome(Rz1File g, string userName)
        {
            if (g.HumanLines == null || g.Events.Count == 0)
                return "—";
            int lower = g.Events[0].GlobalIndex;
            int upper = g.Events[g.Events.Count - 1].GlobalIndex + 1;
            string outcome = "—";
            foreach (KeyValuePair<int, string> kv in g.HumanLines)
            {
                if (kv.Key < lower)
                    continue;
                if (kv.Key > upper)
                    break;
                string line = kv.Value;
                if (!line.StartsWith("[", StringComparison.Ordinal))
                    continue;
                int close = line.IndexOf(']');
                if (close < 0)
                    continue;
                string who = Normalize(line.Substring(1, close - 1));
                string rest = line.Substring(close + 1).Trim();
                bool isUser = who == userName;
                if (rest.StartsWith("Concedes", StringComparison.OrdinalIgnoreCase)
                    || rest.StartsWith("Loses", StringComparison.Ordinal)
                    || rest.IndexOf("Out of Cards, Loses", StringComparison.OrdinalIgnoreCase) >= 0)
                    outcome = isUser ? "LOSS" : "WIN";
                else if (rest.StartsWith("Wins", StringComparison.Ordinal)
                    || rest.IndexOf("Out of Cards, Wins", StringComparison.OrdinalIgnoreCase) >= 0)
                    outcome = isUser ? "WIN" : "LOSS";
            }
            // Lethal endings write no log line (the win/lose overlay is screen-only), and a
            // file's own ending races the autosave. Fall back to the final checksums: the
            // player whose life ended at zero lost.
            if (outcome == "—")
            {
                int lifeP1 = -1, lifeP2 = -1;
                foreach (Rz1Event ev in g.Events)
                {
                    if (ev.Check == null)
                        continue;
                    if (ev.CheckPlayer == 2)
                        lifeP2 = ev.Check[3];
                    else
                        lifeP1 = ev.Check[3];
                }
                bool userIsP1 = Normalize(g.Player1) == userName;
                int userLife = userIsP1 ? lifeP1 : lifeP2;
                int enemyLife = userIsP1 ? lifeP2 : lifeP1;
                if (userLife == 0 && enemyLife != 0)
                    outcome = "LOSS";
                else if (enemyLife == 0 && userLife != 0)
                    outcome = "WIN";
            }
            return outcome;
        }

        private static void OpenPage(HostJoinScript hjs)
        {
            ClosePage();
            Scan();
            _pageIdx = 0;
            BuildPage(hjs);
        }

        private static void ClosePage()
        {
            if (_page != null)
                UnityEngine.Object.Destroy(_page);
            _page = null;
        }

        private static string _filter = "All";   // All / Wins / Losses

        private static List<Entry> Filtered()
        {
            if (_entries == null)
                return new List<Entry>();
            if (_filter == "Wins")
                return _entries.Where(e => e.Outcome == "WIN").ToList();
            if (_filter == "Losses")
                return _entries.Where(e => e.Outcome == "LOSS").ToList();
            return _entries;
        }

        // Frame 2f: scrim over the dimmed menu, 1160-wide surface modal, ground rows with
        // leader thumbs, outcome pill and a Watch replay button, segmented result filter.
        private static void BuildPage(HostJoinScript hjs)
        {
            ClosePage();
            UI.Theme.Ensure();

            _page = new GameObject("LogPoseMatchHistoryPage", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = _page.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            CanvasScaler scaler = _page.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            GameObject scrim = UI.W.Go("Scrim", _page.transform);
            UI.W.Fill(scrim);
            Image dim = scrim.AddComponent<Image>();
            dim.color = UI.Theme.WithA(new Color(0.059f, 0.067f, 0.11f), 0.72f);
            Button scrimBtn = scrim.AddComponent<Button>();
            scrimBtn.transition = Selectable.Transition.None;
            scrimBtn.onClick.AddListener(ClosePage);

            Image panel = UI.W.Panel(_page.transform, "Modal", 380f, 64f, 1160f, 888f, 14f,
                UI.Theme.Surface, UI.Theme.EdgeModal);
            Transform pt = panel.transform;

            UI.W.Label(pt, "LOGPOSE", 32f, 26f, 300f, 18f, 12f, UI.Theme.Accent400, 600,
                TMPro.TextAlignmentOptions.TopLeft, false, 0.1f);
            UI.W.Label(pt, "Match history", 32f, 46f, 400f, 40f, 28f, UI.Theme.Text, 500);

            float segX = 700f;
            foreach (string f in new[] { "All", "Wins", "Losses" })
            {
                string captured = f;
                bool on = _filter == f;
                Button sb = UI.W.Btn(pt, f, segX, 40f, 96f, 40f,
                    on ? UI.BtnKind.Primary : UI.BtnKind.Secondary,
                    () => { _filter = captured; _pageIdx = 0; BuildPage(hjs); }, 14f);
                if (!on)
                {
                    Image si = sb.GetComponent<Image>();
                    si.sprite = UI.UISprites.RoundedRect(48, 48, 8f, Color.clear, Color.clear, 0f, 12f);
                }
                segX += 102f;
            }
            UI.W.Btn(pt, "×", 1080f, 26f, 48f, 48f, UI.BtnKind.Secondary, ClosePage, 20f);

            List<Entry> list = Filtered();
            int start = _pageIdx * RowsPerPage;
            if (list.Count == 0)
            {
                UI.W.Label(pt, _entries == null || _entries.Count == 0
                        ? "No recorded matches yet.\nPlay some games and come back!"
                        : "No " + _filter.ToLowerInvariant() + " on record.",
                    0f, 380f, 1160f, 100f, 20f, UI.Theme.TextMuted, 400, TMPro.TextAlignmentOptions.Center);
            }
            for (int i = start; i < Math.Min(start + RowsPerPage, list.Count); i++)
            {
                Entry e = list[i];
                float y = 126f + (i - start) * 106f;
                GameObject row = UI.W.Go("Row" + i, pt);
                UI.W.TL(row, 32f, y, 1096f, 96f);
                Image rbg = row.AddComponent<Image>();
                rbg.sprite = UI.UISprites.RoundedRect(32, 32, 8f, UI.Theme.Ground, Color.clear, 0f, 9f);
                rbg.type = Image.Type.Sliced;
                Button rb = row.AddComponent<Button>();
                rb.targetGraphic = rbg;
                rb.transition = Selectable.Transition.SpriteSwap;
                rb.spriteState = new UnityEngine.UI.SpriteState
                {
                    highlightedSprite = UI.UISprites.RoundedRect(32, 32, 8f, UI.Theme.WithA(UI.Theme.Accent, 0.08f), Color.clear, 0f, 9f),
                    pressedSprite = UI.UISprites.RoundedRect(32, 32, 8f, UI.Theme.WithA(UI.Theme.Accent, 0.16f), UI.Theme.Accent, 1f, 9f)
                };
                Entry captured = e;
                rb.onClick.AddListener(() => Watch(captured, hjs));

                MakeThumb(row, e.UserLeader, 16f);
                string vs = e.UserName + " <alpha=#66>vs<alpha=#FF> " + e.EnemyName;
                UI.W.Label(row.transform, vs, 92f, 18f, 560f, 30f, 20f, UI.Theme.Text, 500);
                UI.W.Label(row.transform, e.Label, 92f, 54f, 560f, 22f, 13f, UI.Theme.TextMuted, 400, TMPro.TextAlignmentOptions.TopLeft, true);

                bool win = e.Outcome == "WIN";
                bool loss = e.Outcome == "LOSS";
                GameObject pill = UI.W.Go("Pill", row.transform);
                UI.W.TL(pill, 700f, 33f, 76f, 30f);
                Image pi = pill.AddComponent<Image>();
                pi.sprite = UI.UISprites.RoundedRect(24, 24, 6f, win ? UI.Theme.DonActiveFill : UI.Theme.Edge, Color.clear, 0f, 7f);
                pi.type = Image.Type.Sliced;
                pi.raycastTarget = false;
                TMPro.TextMeshProUGUI pl = UI.W.Label(pill.transform, e.Outcome, 0f, 0f, 76f, 30f, 12f,
                    win ? UI.Theme.Accent300 : loss ? UI.Theme.Text : UI.Theme.TextMuted, 600, TMPro.TextAlignmentOptions.Center);
                UI.W.Fill(pl.gameObject);

                UI.W.Btn(row.transform, "Watch replay", 812f, 26f, 172f, 44f, UI.BtnKind.Secondary,
                    () => Watch(captured, hjs), 14f);
                MakeThumb(row, e.EnemyLeader, 1096f - 72f);
            }

            int wins = _entries == null ? 0 : _entries.Count(x => x.Outcome == "WIN");
            int losses = _entries == null ? 0 : _entries.Count(x => x.Outcome == "LOSS");
            int total = _entries == null ? 0 : _entries.Count;
            UI.W.Label(pt, total + " games · " + wins + "W · " + losses + "L",
                32f, 838f, 400f, 24f, 14f, UI.Theme.TextMuted, 400);

            int pages = Math.Max(1, (list.Count + RowsPerPage - 1) / RowsPerPage);
            UI.W.Btn(pt, "Previous", 700f, 828f, 120f, 44f, UI.BtnKind.Secondary, () =>
            {
                if (_pageIdx > 0) { _pageIdx--; BuildPage(hjs); }
            }, 14f);
            UI.W.Label(pt, "Page " + (_pageIdx + 1) + " / " + pages, 830f, 838f, 130f, 24f, 14f,
                UI.Theme.TextMuted, 400, TMPro.TextAlignmentOptions.Center);
            UI.W.Btn(pt, "Next", 970f, 828f, 120f, 44f, UI.BtnKind.Secondary, () =>
            {
                if ((_pageIdx + 1) * RowsPerPage < list.Count) { _pageIdx++; BuildPage(hjs); }
            }, 14f);
        }

        private static void MakeThumb(GameObject row, string leaderId, float x)
        {
            if (string.IsNullOrEmpty(leaderId) || CardDatabaseScript.Instance == null)
                return;
            Sprite s = null;
            try { s = CardDatabaseScript.Instance.GetCardImage(leaderId, SpriteState.Thumbnail); }
            catch { }
            if (s == null)
                return;
            GameObject img = UI.W.Go("Thumb", row.transform);
            UI.W.TL(img, x, 9f, 56f, 78f);
            Image im = img.AddComponent<Image>();
            im.sprite = s;
            im.raycastTarget = false;
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
            RectTransform rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static GameObject _loadingCover;
        private static float _coverShownAt;

        // Full-screen curtain over the solo-start flash while the replay loads underneath.
        private static void ShowLoadingCover(HostJoinScript hjs)
        {
            HideLoadingCover();
            Canvas canvas = hjs.go_SoloVSelf != null ? hjs.go_SoloVSelf.GetComponentInParent<Canvas>() : null;
            if (canvas == null)
                return;
            _loadingCover = new GameObject("LogPoseLoadingCover", typeof(RectTransform));
            _loadingCover.transform.SetParent(canvas.transform, false);
            RectTransform rt = _loadingCover.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = _loadingCover.AddComponent<Image>();
            img.color = new Color(0.07f, 0.05f, 0.03f, 1f);
            MakeLabel(hjs.go_SoloVSelf, _loadingCover, "Loading replay...", Vector2.zero, new Vector2(600f, 80f), 40f);
            TMP_Text lbl = _loadingCover.GetComponentInChildren<TMP_Text>(true);
            if (lbl != null)
                lbl.color = new Color(0.9f, 0.84f, 0.7f);
            Canvas cv = _loadingCover.AddComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = 5000;
            _loadingCover.AddComponent<GraphicRaycaster>();
            _coverShownAt = Time.unscaledTime;
        }

        public static void HideLoadingCover()
        {
            if (_loadingCover != null)
                UnityEngine.Object.Destroy(_loadingCover);
            _loadingCover = null;
        }

        private static void Watch(Entry e, HostJoinScript hjs)
        {
            ClosePage();
            bool atMenu = hjs.go_SoloVSelf != null && hjs.go_SoloVSelf.activeSelf;
            if (atMenu)
            {
                ShowLoadingCover(hjs);
                try
                {
                    hjs.SinglePlayer();
                    hjs.SoloSelf();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("Match history: solo start failed: " + ex.Message);
                    HideLoadingCover();
                    return;
                }
                ReplayUI.QueuePendingOpen(e.Game);
            }
            else
            {
                ReplayUI.OpenExternal(e.Game);
            }
        }
    }
}
