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

        private static void BuildPage(HostJoinScript hjs)
        {
            ClosePage();
            GameObject donor = hjs.go_SoloVSelf;
            Canvas canvas = donor.GetComponentInParent<Canvas>();
            if (canvas == null)
                return;

            _page = new GameObject("LogPoseMatchHistoryPage", typeof(RectTransform));
            _page.transform.SetParent(canvas.transform, false);
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
            rt.sizeDelta = new Vector2(960f, 1000f);
            Image bg = panel.AddComponent<Image>();
            Image donorImg = donor.GetComponent<Image>();
            if (donorImg != null)
            {
                bg.sprite = donorImg.sprite;
                bg.type = donorImg.type;
            }
            bg.color = new Color(0.93f, 0.87f, 0.72f, 0.98f);

            MakeLabel(donor, panel, "Match History", new Vector2(0f, 450f), new Vector2(600f, 70f), 42f);

            int start = _pageIdx * RowsPerPage;
            if (_entries == null || _entries.Count == 0)
            {
                MakeLabel(donor, panel, "No recorded matches yet.\nPlay some games and come back!",
                    new Vector2(0f, 0f), new Vector2(700f, 200f), 30f);
            }
            for (int i = start; i < Math.Min(start + RowsPerPage, _entries.Count); i++)
            {
                Entry e = _entries[i];
                float y = 350f - (i - start) * 108f;
                GameObject row = UnityEngine.Object.Instantiate(donor, panel.transform);
                row.name = "Row" + i;
                row.SetActive(true);
                Button rb = row.GetComponent<Button>();
                if (rb == null)
                    rb = row.AddComponent<Button>();
                rb.onClick = new Button.ButtonClickedEvent();
                Entry captured = e;
                rb.onClick.AddListener(() => Watch(captured, hjs));
                RectTransform rrt = row.GetComponent<RectTransform>();
                rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
                rrt.pivot = new Vector2(0.5f, 0.5f);
                rrt.anchoredPosition = new Vector2(0f, y);
                rrt.sizeDelta = new Vector2(880f, 98f);
                TMP_Text rowText = row.GetComponentInChildren<TMP_Text>(true);
                if (rowText != null)
                {
                    string oc = e.Outcome == "WIN" ? "<color=#1E7A1E><b>WIN</b></color>"
                        : e.Outcome == "LOSS" ? "<color=#9B1B1B><b>LOSS</b></color>"
                        : e.Outcome;
                    rowText.text = e.UserName + "  vs  " + e.EnemyName + "    " + oc +
                        "\n<size=60%>" + e.Label + "</size>";
                    rowText.fontSize = 27f;
                    // Inset the text so the leader thumbnails don't cover it.
                    RectTransform trt = rowText.rectTransform;
                    trt.offsetMin = new Vector2(90f, trt.offsetMin.y);
                    trt.offsetMax = new Vector2(-90f, trt.offsetMax.y);
                }
                MakeThumb(row, e.UserLeader, new Vector2(-380f, 0f));
                MakeThumb(row, e.EnemyLeader, new Vector2(380f, 0f));
            }

            int pages = _entries == null ? 1 : Math.Max(1, (_entries.Count + RowsPerPage - 1) / RowsPerPage);
            MakeSmallButton(donor, panel, "< Prev", new Vector2(-260f, -445f), () =>
            {
                if (_pageIdx > 0) { _pageIdx--; BuildPage(hjs); }
            });
            MakeLabel(donor, panel, "Page " + (_pageIdx + 1) + "/" + pages, new Vector2(0f, -445f), new Vector2(240f, 55f), 26f);
            MakeSmallButton(donor, panel, "Next >", new Vector2(260f, -445f), () =>
            {
                if ((_pageIdx + 1) * RowsPerPage < _entries.Count) { _pageIdx++; BuildPage(hjs); }
            });
            MakeSmallButton(donor, panel, "Close", new Vector2(430f, 450f), ClosePage);
        }

        private static void MakeThumb(GameObject row, string leaderId, Vector2 pos)
        {
            if (string.IsNullOrEmpty(leaderId) || CardDatabaseScript.Instance == null)
                return;
            Sprite s = null;
            try { s = CardDatabaseScript.Instance.GetCardImage(leaderId, SpriteState.Thumbnail); }
            catch { }
            if (s == null)
                return;
            GameObject img = new GameObject("Thumb", typeof(RectTransform));
            img.transform.SetParent(row.transform, false);
            Image im = img.AddComponent<Image>();
            im.sprite = s;
            im.raycastTarget = false;
            RectTransform rt = img.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(62f, 86f);
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
            rt.sizeDelta = new Vector2(170f, 60f);
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
