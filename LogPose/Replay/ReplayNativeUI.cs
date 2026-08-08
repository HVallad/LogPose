using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose.Replay
{
    // Mirrors the human-readable combat log into the game's own side log panel as the replay
    // advances, using the move-index correlation parsed from the sibling .log file.
    internal static class ReplayLogView
    {
        private static readonly List<GameObject> _lines = new List<GameObject>();
        private static bool _cleared;
        private const int MaxLines = 80;

        public static void ResetForNewSession()
        {
            _cleared = false;
        }

        public static void Sync(GameplayLogicScript gls, ReplaySession session, int pos)
        {
            try
            {
                SyncInner(gls, session, pos);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Replay log sync failed: " + e.Message);
            }
        }

        private static void SyncInner(GameplayLogicScript gls, ReplaySession session, int pos)
        {
            if (gls == null || gls.go_LogView == null || gls.go_LogText == null
                || session.File.HumanLines == null || session.File.Events.Count == 0)
                return;
            Transform content = gls.go_LogView.transform.GetChild(0).GetChild(0);
            if (!_cleared)
            {
                // The underlying solo game's log lines make no sense under a replay.
                for (int i = content.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
                _lines.Clear();
                _cleared = true;
            }
            foreach (GameObject go in _lines)
                if (go != null)
                    UnityEngine.Object.Destroy(go);
            _lines.Clear();

            List<Rz1Event> evs = session.File.Events;
            int lower = evs[0].GlobalIndex;
            int upper = pos < evs.Count ? evs[pos].GlobalIndex : evs[evs.Count - 1].GlobalIndex + 1;

            var show = new List<string>();
            List<KeyValuePair<int, string>> human = session.File.HumanLines;
            List<KeyValuePair<int, string>> deck = session.DeckActivityLines;
            int hi = 0, di = 0;
            while (hi < human.Count || di < deck.Count)
            {
                // At equal keys the deck-activity line describes the action that just ended,
                // so it goes before the next action's human lines.
                bool takeDeck = di < deck.Count && (hi >= human.Count || deck[di].Key <= human[hi].Key);
                KeyValuePair<int, string> kv = takeDeck ? deck[di++] : human[hi++];
                if (kv.Key < lower)
                    continue;
                if (kv.Key > upper)
                    break;
                show.Add(kv.Value);
            }
            int start = Math.Max(0, show.Count - MaxLines);
            for (int i = start; i < show.Count; i++)
            {
                GameObject line = UnityEngine.Object.Instantiate(gls.go_LogText, content);
                line.SetActive(true);
                TMP_Text tmp = line.GetComponent<TMP_Text>();
                if (tmp != null)
                    tmp.text = show[i];
                LogTextData data = line.GetComponent<LogTextData>();
                if (data != null)
                    data.gls_Script = gls;
                _lines.Add(line);
            }
            ScrollRect scroll = gls.go_LogView.GetComponentInChildren<ScrollRect>(true);
            if (scroll != null)
            {
                // The layout only recalculates next frame, which would undo the scroll —
                // force it now, stick to bottom, and re-stick after the frame settles.
                Canvas.ForceUpdateCanvases();
                RectTransform contentRect = content as RectTransform;
                if (contentRect != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
                scroll.verticalNormalizedPosition = 0f;
                if (Plugin.Instance != null)
                    Plugin.Instance.StartCoroutine(StickToBottom(scroll));
            }
        }

        private static System.Collections.IEnumerator StickToBottom(ScrollRect scroll)
        {
            yield return new WaitForEndOfFrame();
            if (scroll != null)
                scroll.verticalNormalizedPosition = 0f;
        }
    }

    // Transport controls built from the game's own button prefab and font, docked under the
    // right-hand button stack so the viewer reads as a built-in feature.
    internal static class NativeReplayPanel
    {
        private static GameObject _root;
        private static TMP_Text _info;
        private static TMP_Text _playLabel;

        public static void Show(GameplayLogicScript gls)
        {
            Hide();
            if (gls == null || gls.cn_Canvas == null || gls.go_ChoiceButton1 == null)
                return;
            try
            {
                Build(gls);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Native replay panel failed (falling back to F7 window): " + e.Message);
                Hide();
            }
        }

        private static void Build(GameplayLogicScript gls)
        {
            _root = new GameObject("LogPoseReplayPanel", typeof(RectTransform));
            _root.transform.SetParent(gls.cn_Canvas.transform, false);
            RectTransform rt = _root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-25f, -270f);
            rt.sizeDelta = new Vector2(400f, 230f);

            Image donorImg = gls.go_ChoiceButton1.GetComponent<Image>();
            Image bg = _root.AddComponent<Image>();
            if (donorImg != null)
            {
                bg.sprite = donorImg.sprite;
                bg.type = donorImg.type;
            }
            bg.color = new Color(0.92f, 0.86f, 0.72f, 0.97f);

            _info = MakeLabel(gls, "Replay", new Vector2(0f, 85f), new Vector2(380f, 60f), 24f);

            float y1 = 25f;
            float w = 56f, h = 52f;
            MakeButton(gls, "|<", new Vector2(-165f, y1), new Vector2(w, h), () => ReplayUI.SeekTo(0));
            MakeButton(gls, "<T", new Vector2(-99f, y1), new Vector2(w, h), () => ReplayUI.JumpTurn(-1));
            MakeButton(gls, "<A", new Vector2(-33f, y1), new Vector2(w, h), () => ReplayUI.JumpAction(-1));
            MakeButton(gls, "A>", new Vector2(33f, y1), new Vector2(w, h), () => ReplayUI.JumpAction(1));
            MakeButton(gls, "T>", new Vector2(99f, y1), new Vector2(w, h), () => ReplayUI.JumpTurn(1));
            MakeButton(gls, ">|", new Vector2(165f, y1), new Vector2(w, h), () => ReplayUI.SeekToEnd());

            float y2 = -35f;
            GameObject play = MakeButton(gls, "Play", new Vector2(-120f, y2), new Vector2(110f, h), () => ReplayUI.TogglePlay());
            _playLabel = play.GetComponentInChildren<TMP_Text>(true);
            MakeButton(gls, "Spd -", new Vector2(-15f, y2), new Vector2(85f, h), () => ReplayUI.ChangeSpeed(-2f));
            MakeButton(gls, "Spd +", new Vector2(78f, y2), new Vector2(85f, h), () => ReplayUI.ChangeSpeed(2f));
            MakeButton(gls, "Exit", new Vector2(160f, y2), new Vector2(65f, h), () => ReplayUI.ExitReplay());

            MakeLabel(gls, "A: action (↑/↓)   T: turn (PgUp/PgDn)   ←/→: event", new Vector2(0f, -90f), new Vector2(390f, 30f), 16f);
        }

        private static GameObject MakeButton(GameplayLogicScript gls, string label, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btn = UnityEngine.Object.Instantiate(gls.go_ChoiceButton1, _root.transform);
            btn.name = "LogPoseBtn_" + label;
            btn.SetActive(true);
            Button b = btn.GetComponent<Button>();
            if (b == null)
                b = btn.AddComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(onClick);
            b.interactable = true;
            TMP_Text tmp = btn.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = label;
                tmp.fontSize = Mathf.Min(tmp.fontSize, 26f);
            }
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            btn.transform.localScale = Vector3.one;
            return btn;
        }

        private static TMP_Text MakeLabel(GameplayLogicScript gls, string text, Vector2 pos, Vector2 size, float fontSize)
        {
            TMP_Text donor = gls.go_ChoiceButton1.GetComponentInChildren<TMP_Text>(true);
            GameObject lbl;
            TMP_Text tmp;
            if (donor != null)
            {
                lbl = UnityEngine.Object.Instantiate(donor.gameObject, _root.transform);
                foreach (Transform child in lbl.transform)
                    UnityEngine.Object.Destroy(child.gameObject);
                tmp = lbl.GetComponent<TMP_Text>();
            }
            else
            {
                lbl = new GameObject("LogPoseLabel", typeof(RectTransform));
                lbl.transform.SetParent(_root.transform, false);
                tmp = lbl.AddComponent<TextMeshProUGUI>();
            }
            lbl.name = "LogPoseLabel";
            lbl.SetActive(true);
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = new Color(0.13f, 0.09f, 0.05f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            RectTransform rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            lbl.transform.localScale = Vector3.one;
            return tmp;
        }

        public static void Refresh(ReplaySession session, int pos, bool playing)
        {
            if (_root == null || session == null)
                return;
            if (_info != null)
                _info.text = session.File.Player1 + " vs " + session.File.Player2 + "\n" +
                    "Turn " + session.TurnAt(pos) + "/" + (session.File.TurnMarks.Count + 1) +
                    "   ·   Event " + pos + "/" + session.EventCount;
            if (_playLabel != null)
                _playLabel.text = playing ? "Pause" : "Play";
        }

        public static void Hide()
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            _root = null;
            _info = null;
            _playLabel = null;
        }
    }
}
