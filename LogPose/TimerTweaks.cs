using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose
{
    // The base game ships a full server-authoritative chess clock (isTimerLobby /
    // myTurnTime / opTurnTime, ticked by TurnHandler and synced to the other client every
    // second) — but the bank is hardcoded to 1050s (17.5 min) and the lobby checkbox is the
    // only control. LogPose makes the time configurable: the HOST of a private timed lobby
    // sets a custom bank, and because the host's clock is the authority, the opponent's
    // display and the timeout both follow it — they don't even need the mod.
    internal static class TimerPatches
    {
        // Fischer-style recovery: credit the bank of the player who just COMPLETED a turn.
        // EndTurn_Internal runs on every client for every completed turn (locally for your
        // own End Turn, via EndTurnClientRpc for the opponent's) and ONLY when a turn
        // actually completed — unlike PlayerUntap it never fires at game start, so there is
        // no first-turn case to special-case. By postfix time the turn flags have flipped:
        // the completer is the player NOT about to act. Applied on the HOST, whose
        // once-a-second sync carries the new bank to the opponent.
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.EndTurn_Internal))]
        private static void EndTurn_Postfix(GameplayLogicScript __instance)
        {
            Credit(__instance, __instance.gsv_CurrentGame != null && __instance.gsv_CurrentGame.iPlayerAction != 0);
        }

        // "Take Another Turn" effects bypass EndTurn_Internal: the same player untaps and
        // goes again. Their previous turn still completed, so they get the credit.
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.SecondTurn_Internal))]
        private static void SecondTurn_Postfix(GameplayLogicScript __instance)
        {
            Credit(__instance, creditLocal: true);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.OpponentSecondTurn_Internal))]
        private static void OpponentSecondTurn_Postfix(GameplayLogicScript __instance)
        {
            Credit(__instance, creditLocal: false);
        }

        private static void Credit(GameplayLogicScript gls, bool creditLocal)
        {
            try
            {
                float inc = Plugin.CfgTimerRecoverySeconds.Value;
                if (inc <= 0f)
                    return;
                if (!gls.isTimerLobby || !gls.isLobbyServer || !gls.isPrivate)
                    return;
                if (gls.e_GameStyle == GameStyle.SoloVSelf)
                    return;
                if (creditLocal)
                {
                    gls.myTurnTime += inc;
                    Plugin.Log.LogInfo("Recovery: +" + inc + "s to local player, bank now " + Mathf.RoundToInt(gls.myTurnTime) + "s");
                }
                else
                {
                    gls.opTurnTime += inc;
                    Plugin.Log.LogInfo("Recovery: +" + inc + "s to opponent, bank now " + Mathf.RoundToInt(gls.opTurnTime) + "s");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Recovery credit failed: " + e);
            }
        }

        // ---- Pre-game clock sync -------------------------------------------------
        // The vanilla sync only starts with turn 1 (TurnHandler bails while
        // iTurnNumber == 0), so a joiner stares at the default 17:30 through turn
        // selection and mulligans. While the pre-game window lasts, the host pushes
        // its banks through the game's own sync RPC once a second; vanilla ticking
        // takes over seamlessly at turn 1.
        private static GameplayLogicScript _syncGls;

        internal static void SyncUpdate()
        {
            if (_syncGls == null || Time.frameCount % 60 != 0)
                return;
            try
            {
                GameplayLogicScript gls = _syncGls;
                if (gls.gsv_CurrentGame == null || gls.gsv_CurrentGame.iTurnNumber >= 1 ||
                    gls.e_CurrentState == GameplayState.MainMenu || gls.e_CurrentState == GameplayState.GameOver)
                {
                    _syncGls = null;
                    return;
                }
                if (gls.nps_Local != null)
                    gls.nps_Local.SetTimerServerRpc(gls.myTurnTime, gls.opTurnTime);
            }
            catch
            {
                _syncGls = null;
            }
        }

        // Joiner-side: repaint BOTH clock labels whenever a timer sync arrives.
        // Vanilla only repaints the acting player's label each tick, so the idle
        // side keeps a stale 17:30 until that player's first turn.
        [HarmonyPostfix, HarmonyPatch(typeof(NetworkPlayerScript), "SetTimerClientRpc")]
        private static void SetTimerClientRpc_Postfix(GameplayLogicScript ___gls_Gameplay)
        {
            try
            {
                GameplayLogicScript gls = ___gls_Gameplay;
                if (gls == null || gls.isLobbyServer || !gls.isTimerLobby)
                    return;
                if (gls.my_text_TurnTime != null)
                    gls.my_text_TurnTime.text = FormatClock(gls.myTurnTime);
                if (gls.op_text_TurnTime != null)
                    gls.op_text_TurnTime.text = FormatClock(gls.opTurnTime);
            }
            catch { }
        }

        private static string FormatClock(float t)
        {
            if (t < 0f)
                t = 0f;
            return string.Format("{0:00}:{1:00}", Mathf.FloorToInt(t / 60f), Mathf.FloorToInt(t % 60f));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.GameStartMultiplayer))]
        private static void GameStartMultiplayer_Postfix(GameplayLogicScript __instance)
        {
            _syncGls = null;
            try
            {
                if (!__instance.isTimerLobby || !__instance.isLobbyServer || !__instance.isPrivate)
                    return;
                _syncGls = __instance;
                float mins = Plugin.CfgTimerMinutes.Value;
                if (mins < 1f || Mathf.Approximately(mins, 17.5f))
                    return;
                float secs = mins * 60f;
                __instance.myTurnTime = secs;
                __instance.opTurnTime = secs;
                string label = string.Format("{0:00}:{1:00}",
                    Mathf.FloorToInt(secs / 60f), Mathf.FloorToInt(secs % 60f));
                if (__instance.my_text_TurnTime != null)
                    __instance.my_text_TurnTime.text = label;
                if (__instance.op_text_TurnTime != null)
                    __instance.op_text_TurnTime.text = label;
                Plugin.Log.LogInfo("Timer: private lobby clock set to " + mins + " min per player.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Timer override failed: " + e.Message);
            }
        }
    }

    // A small stepper under the vanilla "Timer Lobby" checkbox on the host screen: pick the
    // minutes per player for private timed lobbies. Persists to the config.
    internal static class TimerLobbyUI
    {
        private static readonly float[] Presets = { 5f, 10f, 15f, 17.5f, 20f, 25f, 30f };
        private static readonly float[] RecoveryPresets = { 0f, 5f, 10f, 15f, 20f, 30f, 45f, 60f };
        private static GameObject _root;
        private static TMP_Text _label;
        private static TMP_Text _recLabel;

        internal static void Update()
        {
            if (Time.frameCount % 30 != 0)
                return;
            HostJoinScript hjs = UnityEngine.Object.FindFirstObjectByType<HostJoinScript>();
            GameObject anchor = hjs != null ? hjs.go_IsTimerLobby : null;
            if (anchor == null || !anchor.activeInHierarchy)
            {
                if (_root != null && _root.activeSelf)
                    _root.SetActive(false);
                return;
            }
            if (_root == null)
            {
                try { Build(hjs, anchor); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Timer stepper failed: " + e.Message);
                    return;
                }
            }
            if (_root != null)
            {
                if (!_root.activeSelf)
                    _root.SetActive(true);
                // The browser imposer moves the Timed Lobby toggle per poll — follow it.
                RectTransform art = anchor.GetComponent<RectTransform>();
                RectTransform rt = _root.GetComponent<RectTransform>();
                Vector2 want = art.anchoredPosition + new Vector2(150f, -66f);
                if (rt.anchorMin != art.anchorMin)
                { rt.anchorMin = art.anchorMin; rt.anchorMax = art.anchorMax; rt.pivot = art.pivot; }
                if (rt.anchoredPosition != want)
                    rt.anchoredPosition = want;
                RefreshLabel();
            }
        }

        private static int Nearest(float[] presets, float current)
        {
            int idx = 0;
            float best = float.MaxValue;
            for (int i = 0; i < presets.Length; i++)
            {
                float d = Mathf.Abs(presets[i] - current);
                if (d < best)
                {
                    best = d;
                    idx = i;
                }
            }
            return idx;
        }

        private static void Step(int dir)
        {
            int idx = Mathf.Clamp(Nearest(Presets, Plugin.CfgTimerMinutes.Value) + dir, 0, Presets.Length - 1);
            Plugin.CfgTimerMinutes.Value = Presets[idx];
            RefreshLabel();
        }

        private static void StepRecovery(int dir)
        {
            int idx = Mathf.Clamp(Nearest(RecoveryPresets, Plugin.CfgTimerRecoverySeconds.Value) + dir, 0, RecoveryPresets.Length - 1);
            Plugin.CfgTimerRecoverySeconds.Value = RecoveryPresets[idx];
            RefreshLabel();
        }

        private static void RefreshLabel()
        {
            if (_label != null)
            {
                float m = Plugin.CfgTimerMinutes.Value;
                string text = (m == Mathf.Floor(m) ? ((int)m).ToString() : m.ToString("0.#")) + " min";
                if (Mathf.Approximately(m, 17.5f))
                    text += "  <size=60%>(default)</size>";
                _label.text = text;
            }
            if (_recLabel != null)
            {
                int r = Mathf.RoundToInt(Plugin.CfgTimerRecoverySeconds.Value);
                _recLabel.text = r > 0 ? "+" + r + "s / turn" : "no recovery";
            }
        }

        private static void Build(HostJoinScript hjs, GameObject anchor)
        {
            RectTransform art = anchor.GetComponent<RectTransform>();
            _root = new GameObject("LogPoseTimerStepper", typeof(RectTransform));
            _root.transform.SetParent(anchor.transform.parent, false);
            _root.transform.SetSiblingIndex(anchor.transform.GetSiblingIndex() + 1);
            RectTransform rt = _root.GetComponent<RectTransform>();
            rt.anchorMin = art.anchorMin;
            rt.anchorMax = art.anchorMax;
            rt.pivot = art.pivot;
            // Position is re-pinned to the checkbox every poll in Update.
            rt.anchoredPosition = art.anchoredPosition + new Vector2(150f, -66f);
            rt.sizeDelta = new Vector2(290f, 106f);

            MakeButton(hjs, "<", new Vector2(-105f, 28f), () => Step(-1));
            MakeButton(hjs, ">", new Vector2(105f, 28f), () => Step(1));
            _label = MakeRowLabel(hjs, anchor, new Vector2(0f, 28f), 26f);

            MakeButton(hjs, "<", new Vector2(-105f, -28f), () => StepRecovery(-1));
            MakeButton(hjs, ">", new Vector2(105f, -28f), () => StepRecovery(1));
            _recLabel = MakeRowLabel(hjs, anchor, new Vector2(0f, -28f), 22f);

            RefreshLabel();
        }

        private static TMP_Text MakeRowLabel(HostJoinScript hjs, GameObject anchor, Vector2 pos, float fontSize)
        {
            GameObject lbl = new GameObject("Label", typeof(RectTransform));
            lbl.transform.SetParent(_root.transform, false);
            TextMeshProUGUI tmp = lbl.AddComponent<TextMeshProUGUI>();
            TMP_Text donorText = anchor.GetComponentInChildren<TMP_Text>(true);
            if (donorText == null && hjs.go_SoloVSelf != null)
                donorText = hjs.go_SoloVSelf.GetComponentInChildren<TMP_Text>(true);
            if (donorText != null)
            {
                tmp.font = donorText.font;
                tmp.color = donorText.color;
            }
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            RectTransform lrt = lbl.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = pos;
            lrt.sizeDelta = new Vector2(180f, 44f);
            return tmp;
        }

        private static void MakeButton(HostJoinScript hjs, string label, Vector2 pos, Action onClick)
        {
            GameObject donor = hjs.go_SoloVSelf;
            if (donor == null)
                return;
            GameObject btn = UnityEngine.Object.Instantiate(donor, _root.transform);
            btn.name = "LogPoseTimerBtn" + label;
            btn.SetActive(true);
            Button b = btn.GetComponent<Button>();
            if (b == null)
                b = btn.AddComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(() =>
            {
                onClick();
                // Deselect so a later Enter/Space (e.g. sending a chat message) can't
                // re-fire the last-clicked stepper button and silently change the config.
                if (UnityEngine.EventSystems.EventSystem.current != null)
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            });
            TMP_Text tmp = btn.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = label;
                tmp.enableAutoSizing = false;
                tmp.fontSize = 30f;
            }
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(54f, 46f);
        }
    }
}
