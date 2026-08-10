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
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.GameStartMultiplayer))]
        private static void GameStartMultiplayer_Postfix(GameplayLogicScript __instance)
        {
            try
            {
                if (!__instance.isTimerLobby || !__instance.isLobbyServer || !__instance.isPrivate)
                    return;
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
        private static GameObject _root;
        private static TMP_Text _label;

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
                RefreshLabel();
            }
        }

        private static void Step(int dir)
        {
            float current = Plugin.CfgTimerMinutes.Value;
            int idx = 0;
            float best = float.MaxValue;
            for (int i = 0; i < Presets.Length; i++)
            {
                float d = Mathf.Abs(Presets[i] - current);
                if (d < best)
                {
                    best = d;
                    idx = i;
                }
            }
            idx = Mathf.Clamp(idx + dir, 0, Presets.Length - 1);
            Plugin.CfgTimerMinutes.Value = Presets[idx];
            RefreshLabel();
        }

        private static void RefreshLabel()
        {
            if (_label == null)
                return;
            float m = Plugin.CfgTimerMinutes.Value;
            string text = (m == Mathf.Floor(m) ? ((int)m).ToString() : m.ToString("0.#")) + " min";
            if (Mathf.Approximately(m, 17.5f))
                text += "  <size=60%>(default)</size>";
            _label.text = text;
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
            rt.anchoredPosition = art.anchoredPosition + new Vector2(0f, -58f);
            rt.sizeDelta = new Vector2(320f, 50f);

            MakeButton(hjs, "<", new Vector2(-120f, 0f), () => Step(-1));
            MakeButton(hjs, ">", new Vector2(120f, 0f), () => Step(1));

            GameObject lbl = new GameObject("Label", typeof(RectTransform));
            lbl.transform.SetParent(_root.transform, false);
            _label = lbl.AddComponent<TextMeshProUGUI>();
            TMP_Text donorText = anchor.GetComponentInChildren<TMP_Text>(true);
            if (donorText == null && hjs.go_SoloVSelf != null)
                donorText = hjs.go_SoloVSelf.GetComponentInChildren<TMP_Text>(true);
            if (donorText != null)
            {
                _label.font = donorText.font;
                _label.color = donorText.color;
            }
            _label.fontSize = 26f;
            _label.alignment = TextAlignmentOptions.Center;
            RectTransform lrt = lbl.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(180f, 44f);
            RefreshLabel();
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
            b.onClick.AddListener(() => onClick());
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
