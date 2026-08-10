using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose
{
    // Chess-clock match timer. Each player has a time bank; the active player's bank ticks
    // down during their turn, with an optional Fischer increment on completing it. Both
    // clients run LogPose with the same settings, derive the same clocks from the same turn
    // changes, and each enforces its own flag: when YOUR bank empties, your client concedes
    // through the game's normal concede path. The panel shows the configured base time so a
    // settings mismatch between players is visible at a glance.
    internal static class GameTimer
    {
        private static readonly FieldInfo StateField = AccessTools.Field(typeof(GameplayLogicScript), "e_CurrentState");
        private static readonly FieldInfo EndedField = AccessTools.Field(typeof(GameplayLogicScript), "bHasGameEnded");
        private static readonly FieldInfo StyleField = AccessTools.Field(typeof(GameplayLogicScript), "e_GameStyle");

        private static readonly float[] Bank = new float[2];
        private static int _lastActive = -1;
        private static bool _flagFired;

        private static GameObject _panel;
        private static TMP_Text _header;
        private static TMP_Text _you;
        private static TMP_Text _opp;

        internal static void ResetForNewGame()
        {
            float secs = Mathf.Max(10f, Plugin.CfgTimerMinutes.Value * 60f);
            Bank[0] = secs;
            Bank[1] = secs;
            _lastActive = -1;
            _flagFired = false;
        }

        internal static void Update()
        {
            if (!Plugin.CfgTimerEnabled.Value)
            {
                HidePanel();
                return;
            }
            GameplayLogicScript gls = Replay.ReplayBridge.FindBoard();
            if (gls == null)
            {
                _panel = null;   // died with the scene
                return;
            }
            if (Replay.ReplayBridge.InReplay || gls.Lps_Players == null || gls.Lps_Players.Count < 2
                || gls.gsv_CurrentGame == null)
            {
                HidePanel();
                return;
            }
            string state = StateField != null ? Convert.ToString(StateField.GetValue(gls)) : "";
            if (state == "MainMenu" || state == "GameOver" || state == "OpponentDisconnect"
                || state.StartsWith("Start_", StringComparison.Ordinal)
                || gls.gsv_CurrentGame.iTurnNumber < 1)
            {
                HidePanel();
                return;
            }

            int active = gls.gsv_CurrentGame.iPlayerAction == 0 ? 0 : 1;
            if (_lastActive != active)
            {
                // Fischer increment: awarded for the turn you just completed.
                if (_lastActive >= 0)
                    Bank[_lastActive] += Mathf.Max(0f, Plugin.CfgTimerIncrementSeconds.Value);
                _lastActive = active;
            }

            bool ended = EndedField != null && EndedField.GetValue(gls) is bool b && b;
            if (!ended)
            {
                Bank[active] = Mathf.Max(0f, Bank[active] - Time.deltaTime);
                if (Bank[0] <= 0f && !_flagFired && Plugin.CfgTimerAutoConcede.Value)
                {
                    _flagFired = true;
                    string style = StyleField != null ? Convert.ToString(StyleField.GetValue(gls)) : "";
                    if (style == "Multiplayer")
                    {
                        Plugin.Log.LogInfo("Timer: out of time — conceding.");
                        try { gls.Concede(); }
                        catch (Exception e) { Plugin.Log.LogWarning("Timer concede failed: " + e.Message); }
                    }
                }
            }
            ShowPanel(gls, active);
        }

        private static string Fmt(float t)
        {
            int s = Mathf.CeilToInt(t);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        private static Color ColorFor(int p, int active)
        {
            if (Bank[p] <= 0f)
                return new Color(0.72f, 0.10f, 0.10f);
            if (Bank[p] <= 30f)
                return new Color(0.82f, 0.33f, 0.08f);
            if (p == active)
                return new Color(0.10f, 0.45f, 0.12f);
            return new Color(0.30f, 0.24f, 0.14f);
        }

        private static void HidePanel()
        {
            if (_panel != null && _panel.activeSelf)
                _panel.SetActive(false);
        }

        private static void ShowPanel(GameplayLogicScript gls, int active)
        {
            if (_panel == null)
            {
                try { BuildPanel(gls); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Timer panel failed: " + e.Message);
                    return;
                }
            }
            if (_panel == null)
                return;
            if (!_panel.activeSelf)
                _panel.SetActive(true);
            int inc = Mathf.RoundToInt(Plugin.CfgTimerIncrementSeconds.Value);
            _header.text = "TIMER  " + Fmt(Plugin.CfgTimerMinutes.Value * 60f) + (inc > 0 ? " +" + inc + "s" : "");
            _you.text = "You  " + Fmt(Bank[0]);
            _opp.text = "Opp  " + Fmt(Bank[1]);
            _you.color = ColorFor(0, active);
            _opp.color = ColorFor(1, active);
            _you.fontStyle = active == 0 ? FontStyles.Bold : FontStyles.Normal;
            _opp.fontStyle = active == 1 ? FontStyles.Bold : FontStyles.Normal;
        }

        private static void BuildPanel(GameplayLogicScript gls)
        {
            if (gls.cn_Canvas == null || gls.go_ChoiceButton1 == null)
                return;
            _panel = new GameObject("LogPoseTimer", typeof(RectTransform));
            _panel.transform.SetParent(gls.cn_Canvas.transform, false);
            RectTransform rt = _panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-430f, -120f);
            rt.sizeDelta = new Vector2(235f, 118f);

            Image donorImg = gls.go_ChoiceButton1.GetComponent<Image>();
            Image bg = _panel.AddComponent<Image>();
            if (donorImg != null)
            {
                bg.sprite = donorImg.sprite;
                bg.type = donorImg.type;
            }
            bg.color = new Color(0.92f, 0.86f, 0.72f, 0.95f);
            bg.raycastTarget = false;

            _header = MakeLabel(gls, new Vector2(0f, 40f), new Vector2(215f, 26f), 16f);
            _header.color = new Color(0.35f, 0.28f, 0.16f);
            _you = MakeLabel(gls, new Vector2(0f, 8f), new Vector2(215f, 36f), 30f);
            _opp = MakeLabel(gls, new Vector2(0f, -30f), new Vector2(215f, 36f), 30f);
        }

        private static TMP_Text MakeLabel(GameplayLogicScript gls, Vector2 pos, Vector2 size, float fontSize)
        {
            TMP_Text donor = gls.go_ChoiceButton1.GetComponentInChildren<TMP_Text>(true);
            GameObject go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(_panel.transform, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (donor != null)
                tmp.font = donor.font;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return tmp;
        }
    }
}
