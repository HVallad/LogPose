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

    // Renders the cards a search/mill/scry touched as a face-up reveal row — like the game's
    // own search display — below the enemy's side or above yours, while the replay position
    // sits inside that action. The card taken to hand is raised slightly.
    internal static class RevealRow
    {
        private static readonly List<GameObject> _cards = new List<GameObject>();
        private const int MaxCards = 8;
        // Redirect glides only for reasonably local jumps — an End-jump shouldn't fling
        // cards around from long-gone searches.
        private const int MaxRedirectSpan = 100;

        public static void Sync(GameplayLogicScript gls, ReplaySession session, int pos, int prevPos)
        {
            try
            {
                ReplaySession.DeckActivity current = null;
                foreach (ReplaySession.DeckActivity a in session.DeckActivities)
                    if (pos > a.Start && pos <= a.DisplayEnd)
                        current = a;
                Show(gls, current, pos);
                // Any take whose event was crossed by this seek starts its glide from its
                // reveal-row slot instead of the deck pile.
                if (pos > prevPos && pos - prevPos <= MaxRedirectSpan)
                    foreach (ReplaySession.DeckActivity a in session.DeckActivities)
                        RedirectTakenCards(gls, a, prevPos, pos);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Reveal row failed: " + e.Message);
            }
        }

        private static void RedirectTakenCards(GameplayLogicScript gls, ReplaySession.DeckActivity activity, int prevPos, int pos)
        {
            int count = Math.Min(activity.CardIds.Count, MaxCards);
            for (int i = 0; i < count; i++)
            {
                if (!activity.ToHand[i] || activity.EventIdx[i] < prevPos || activity.EventIdx[i] >= pos)
                    continue;
                Plugin.Log.LogInfo("Replay: redirecting " + activity.CardIds[i] + " glide to start from the reveal row");
                int p = activity.Player == 2 ? 1 : 0;
                List<GameObject> hand = gls.Lps_Players[p].Lgo_MyHand;
                for (int h = hand.Count - 1; h >= 0; h--)
                {
                    CardLogicScript cls = hand[h] != null ? hand[h].GetComponent<CardLogicScript>() : null;
                    if (cls == null || cls.myCard.cardDef == null || cls.myCard.cardDef.cardID != activity.CardIds[i])
                        continue;
                    Vector3 slot = SlotPosition(gls, activity, i);
                    hand[h].transform.localPosition = slot;
                    break;
                }
            }
        }

        public static void Clear()
        {
            foreach (GameObject go in _cards)
                if (go != null)
                    UnityEngine.Object.Destroy(go);
            _cards.Clear();
        }

        private static void RowLayout(GameplayLogicScript gls, ReplaySession.DeckActivity activity,
            int count, out float x0, out float y, out float spacing)
        {
            LocationSet loc = null;
            try
            {
                int idx = activity.Player == 2 ? 1 : 0;
                if (gls.sc_Locations != null && gls.sc_Locations.playerLocations != null
                    && gls.sc_Locations.playerLocations.Count > idx)
                    loc = gls.sc_Locations.playerLocations[idx].topDeck;
            }
            catch { }
            if (loc != null && (loc.x != 0f || loc.y != 0f))
            {
                x0 = loc.x;
                y = loc.y;
                spacing = count > 1 ? Mathf.Min(loc.width / (count - 1), loc.step) : loc.step;
            }
            else
            {
                y = activity.Player == 2 ? 150f : -150f;
                spacing = 95f;
                x0 = -spacing * (count - 1) / 2f;
            }
        }

        private static Vector3 SlotPosition(GameplayLogicScript gls, ReplaySession.DeckActivity activity, int i)
        {
            float x0, y, spacing;
            RowLayout(gls, activity, Math.Min(activity.CardIds.Count, MaxCards), out x0, out y, out spacing);
            return new Vector3(x0 + i * spacing, y);
        }

        private static void Show(GameplayLogicScript gls, ReplaySession.DeckActivity activity, int pos)
        {
            Clear();
            if (activity == null || gls == null || CardDatabaseScript.Instance == null)
                return;
            int count = Math.Min(activity.CardIds.Count, MaxCards);
            if (count == 0)
                return;
            float x0, y, spacing;
            RowLayout(gls, activity, count, out x0, out y, out spacing);
            for (int i = 0; i < count; i++)
            {
                // Once its to-hand move has applied, the taken card lives in the hand on the
                // real board — leave a gap in the row instead of showing a duplicate.
                bool taken = i < activity.ToHand.Count && activity.ToHand[i]
                    && i < activity.EventIdx.Count && pos > activity.EventIdx[i];
                if (taken)
                    continue;
                CardDefinition def = CardDatabaseScript.Instance.FindDefinition(activity.CardIds[i]);
                if (def == null)
                    continue;
                GameObject go = UnityEngine.Object.Instantiate(gls.prefab_CardTemplate);
                go.name = "LogPoseReveal_" + activity.CardIds[i];
                go.transform.SetParent(gls.cn_Canvas.transform, false);
                go.transform.localScale = new Vector3(0.9f, 0.9f);
                CardLogicScript cls = go.GetComponent<CardLogicScript>();
                cls.LoadCardDefinition(def);
                cls.SetFaceUp(true);
                // The card about to be grabbed sits raised so the pick is visible.
                bool willTake = i < activity.ToHand.Count && activity.ToHand[i];
                go.transform.localPosition = new Vector3(x0 + i * spacing, y + (willTake ? 30f : 0f));
                cls.vDestination = go.transform.localPosition;
                Canvas cv = go.GetComponent<Canvas>();
                if (cv != null)
                {
                    cv.overrideSorting = true;
                    cv.sortingOrder = 700 + i;
                }
                _cards.Add(go);
            }
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

        private static Image _railFill;
        private static RectTransform _playhead;
        private static RectTransform _rail;
        private static bool _ticksBuilt;
        private static int _eventCount;

        // Frame 2h: REPLAY-tagged header, turn ruler with a draggable accent playhead and
        // per-turn ticks, transport cluster, speed keys and the keyboard hints — drawn with
        // the design system instead of cloned parchment.
        private static void Build(GameplayLogicScript gls)
        {
            UI.Theme.Ensure();
            _ticksBuilt = false;
            _root = new GameObject("LogPoseReplayPanel", typeof(RectTransform));
            _root.transform.SetParent(gls.cn_Canvas.transform, false);
            // The rail's action area (the End Turn stack in a live game) is free during
            // replays — the transport takes its place, full rail width per frame 2h.
            RectTransform rt = _root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(516f, -200f);
            rt.sizeDelta = new Vector2(744f, 240f);

            Image bg = _root.AddComponent<Image>();
            bg.sprite = UI.UISprites.RoundedRect(64, 64, 14f, UI.Theme.Surface, UI.Theme.Edge, 1f, 18f);
            bg.type = Image.Type.Sliced;

            // Header: outline tag + matchup / counters.
            UI.W.Tag(_root.transform, "REPLAY", 16f, 14f, false, outline: true);
            _info = UI.W.Label(_root.transform, "", 116f, 12f, 480f, 40f, 13f, UI.Theme.Text, 500,
                TextAlignmentOptions.TopLeft, true);

            // Turn ruler: track + fill + playhead (draggable) + turn ticks added on first Refresh.
            GameObject rail = UI.W.Go("Rail", _root.transform);
            _rail = UI.W.TL(rail, 20f, 60f, 704f, 12f);
            Image track = rail.AddComponent<Image>();
            track.sprite = UI.UISprites.RoundedRect(24, 24, 6f, UI.Theme.SurfaceRaised, Color.clear, 0f, 7f);
            track.type = Image.Type.Sliced;

            GameObject fill = UI.W.Go("Fill", rail.transform);
            RectTransform frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(0f, 1f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.anchoredPosition = Vector2.zero;
            frt.sizeDelta = new Vector2(0f, 0f);
            _railFill = fill.AddComponent<Image>();
            _railFill.sprite = UI.UISprites.RoundedRect(24, 24, 6f, UI.Theme.WithA(UI.Theme.Accent, 0.28f), Color.clear, 0f, 7f);
            _railFill.type = Image.Type.Sliced;
            _railFill.raycastTarget = false;

            GameObject head = UI.W.Go("Playhead", rail.transform);
            _playhead = head.GetComponent<RectTransform>();
            _playhead.anchorMin = _playhead.anchorMax = new Vector2(0f, 0.5f);
            _playhead.pivot = new Vector2(0.5f, 0.5f);
            _playhead.sizeDelta = new Vector2(20f, 20f);
            Image headImg = head.AddComponent<Image>();
            headImg.sprite = UI.UISprites.Glow(UI.Theme.Accent, 1f);
            headImg.raycastTarget = false;

            var trigger = rail.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            AddDrag(trigger, UnityEngine.EventSystems.EventTriggerType.PointerDown);
            AddDrag(trigger, UnityEngine.EventSystems.EventTriggerType.Drag);

            // Transport cluster (row 1) with the speed keys on its right.
            float y1 = 88f, w = 50f, h = 46f, x = 20f;
            TransportBtn(gls, "|<", ref x, y1, w, h, () => ReplayUI.SeekTo(0));
            TransportBtn(gls, "<T", ref x, y1, w, h, () => ReplayUI.JumpTurn(-1));
            TransportBtn(gls, "<A", ref x, y1, w, h, () => ReplayUI.JumpAction(-1));
            Button play = UI.W.Btn(_root.transform, "Play", x, y1, 74f, h, UI.BtnKind.Primary, () => ReplayUI.TogglePlay(), 15f);
            _playLabel = play.GetComponentInChildren<TMP_Text>(true);
            x += 80f;
            TransportBtn(gls, "A>", ref x, y1, w, h, () => ReplayUI.JumpAction(1));
            TransportBtn(gls, "T>", ref x, y1, w, h, () => ReplayUI.JumpTurn(1));
            TransportBtn(gls, ">|", ref x, y1, w, h, () => ReplayUI.SeekToEnd());
            UI.W.Btn(_root.transform, "Spd −", 552f, y1, 82f, h, UI.BtnKind.Secondary, () => ReplayUI.ChangeSpeed(-2f), 14f);
            UI.W.Btn(_root.transform, "Spd +", 642f, y1, 82f, h, UI.BtnKind.Secondary, () => ReplayUI.ChangeSpeed(2f), 14f);

            // Row 2: keyboard hints left, exit right.
            UI.W.Label(_root.transform, "A: action (↑/↓) · T: turn (PgUp/PgDn) · ←/→: event · Home/End",
                20f, 158f, 500f, 40f, 12f, UI.Theme.TextMuted, 400, TextAlignmentOptions.TopLeft);
            UI.W.Btn(_root.transform, "Exit replay", 588f, 150f, 136f, 44f, UI.BtnKind.Danger, () => ReplayUI.ExitReplay(), 14f);
        }

        private static void TransportBtn(GameplayLogicScript gls, string label, ref float x, float y, float w, float h,
            UnityEngine.Events.UnityAction onClick)
        {
            UI.W.Btn(_root.transform, label, x, y, w, h, UI.BtnKind.Secondary, () => onClick(), 15f);
            x += w + 6f;
        }

        private static void AddDrag(UnityEngine.EventSystems.EventTrigger trigger,
            UnityEngine.EventSystems.EventTriggerType type)
        {
            var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data =>
            {
                if (_rail == null || _eventCount <= 0)
                    return;
                var ped = (UnityEngine.EventSystems.PointerEventData)data;
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rail, ped.position,
                        ped.pressEventCamera, out local))
                    return;
                float frac = Mathf.Clamp01(local.x / _rail.rect.width);
                ReplayUI.SeekTo(Mathf.RoundToInt(frac * _eventCount));
            });
            trigger.triggers.Add(entry);
        }

        private static void BuildTicks(ReplaySession session)
        {
            _ticksBuilt = true;
            if (session.File.TurnMarks == null || session.EventCount <= 0)
                return;
            foreach (int mark in session.File.TurnMarks)
            {
                float frac = Mathf.Clamp01(mark / (float)session.EventCount);
                GameObject tick = UI.W.Go("Tick", _rail.transform);
                RectTransform trt = tick.GetComponent<RectTransform>();
                trt.anchorMin = new Vector2(frac, 0f);
                trt.anchorMax = new Vector2(frac, 1f);
                trt.pivot = new Vector2(0.5f, 0.5f);
                trt.anchoredPosition = Vector2.zero;
                trt.sizeDelta = new Vector2(2f, -4f);
                Image ti = tick.AddComponent<Image>();
                ti.color = UI.Theme.WithA(UI.Theme.Text, 0.25f);
                ti.raycastTarget = false;
            }
            if (_playhead != null)
                _playhead.SetAsLastSibling();
        }

        public static void Refresh(ReplaySession session, int pos, bool playing)
        {
            if (_root == null || session == null)
                return;
            _eventCount = session.EventCount;
            if (!_ticksBuilt)
                BuildTicks(session);
            if (_info != null)
                _info.text = session.File.Player1 + " vs " + session.File.Player2 + "\n" +
                    "TURN " + session.TurnAt(pos) + " / " + (session.File.TurnMarks.Count + 1) +
                    " · EVENT " + pos + " / " + session.EventCount;
            if (_playLabel != null)
                _playLabel.text = playing ? "Pause" : "Play";
            if (_rail != null && _eventCount > 0)
            {
                float frac = Mathf.Clamp01(pos / (float)_eventCount);
                float w = _rail.rect.width;
                if (_railFill != null)
                    _railFill.rectTransform.sizeDelta = new Vector2(frac * w, 0f);
                if (_playhead != null)
                    _playhead.anchoredPosition = new Vector2(frac * w, 0f);
            }
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
