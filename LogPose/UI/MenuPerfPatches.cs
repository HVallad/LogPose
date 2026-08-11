using System.Collections;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace LogPose.UI
{
    // The main scene's Awake stalls scene activation with synchronous, busy-wait
    // web requests: GetMiniUpdates (5 CloudFront GETs re-checking content already
    // fetched at boot) and CheckTwitch (downloads the whole Twitch channel page).
    // Together they own ~450ms of every editor->menu hop. GetMiniUpdates keeps its
    // first (boot) run and its non-Awake callers — LiveUpdateOverrideVersion needs
    // it for multiplayer micropatch sync — but repeat Awake-time runs are skipped.
    // CheckTwitch is replaced everywhere with a yielding coroutine that applies the
    // same is-live logic, so the button still works at zero main-thread cost.
    internal static class MenuPerfPatches
    {
        private static readonly string[] TimedSteps =
        {
            "Awake", "MakeMyLocals", "PopulateDeckNames",
            "LoadCardOverrides", "PopulateBackgroundImage", "LoadPlayerPrefs",
        };

        private static bool _inAwake;
        private static bool _miniUpdatesRan;
        private static FieldInfo _twitchButton;

        internal static void Apply(Harmony harmony)
        {
            var pre = new HarmonyMethod(typeof(MenuPerfPatches), nameof(StepPrefix));
            var post = new HarmonyMethod(typeof(MenuPerfPatches), nameof(StepPostfix));
            foreach (string name in TimedSteps)
            {
                var m = AccessTools.Method(typeof(GameplayLogicScript), name);
                if (m == null) continue;
                try { harmony.Patch(m, prefix: pre, postfix: post); } catch { }
            }

            try
            {
                harmony.Patch(AccessTools.Method(typeof(GameplayLogicScript), "Awake"),
                    prefix: new HarmonyMethod(typeof(MenuPerfPatches), nameof(AwakeFlag_Prefix)),
                    postfix: new HarmonyMethod(typeof(MenuPerfPatches), nameof(AwakeFlag_Postfix)));
                harmony.Patch(AccessTools.Method(typeof(GameplayLogicScript), "GetMiniUpdates"),
                    prefix: new HarmonyMethod(typeof(MenuPerfPatches), nameof(MiniUpdates_Prefix)));
                harmony.Patch(AccessTools.Method(typeof(GameplayLogicScript), "CheckTwitch"),
                    prefix: new HarmonyMethod(typeof(MenuPerfPatches), nameof(CheckTwitch_Prefix)));
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Menu perf patches failed: " + e.Message);
            }
        }

        private static void StepPrefix(out long __state)
            => __state = System.Diagnostics.Stopwatch.GetTimestamp();

        private static void StepPostfix(long __state, MethodBase __originalMethod)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - __state) * 1000.0
                / System.Diagnostics.Stopwatch.Frequency;
            if (ms >= 1.0)
                Plugin.Log.LogInfo("Menu init " + __originalMethod.Name + ": " + ms.ToString("F0") + " ms");
        }

        private static void AwakeFlag_Prefix() => _inAwake = true;
        private static void AwakeFlag_Postfix() => _inAwake = false;

        private static bool MiniUpdates_Prefix()
        {
            if (_inAwake && _miniUpdatesRan)
            {
                Plugin.Log.LogInfo("Menu init GetMiniUpdates: skipped (already checked this session).");
                return false;
            }
            _miniUpdatesRan = true;
            return true;
        }

        private static bool CheckTwitch_Prefix(GameplayLogicScript __instance)
        {
            if (Plugin.Instance == null) return true;
            Plugin.Instance.StartCoroutine(TwitchAsync(__instance));
            return false;
        }

        private static IEnumerator TwitchAsync(GameplayLogicScript gls)
        {
            const string channel = "maebatsu";
            using (var req = UnityWebRequest.Get("https://www.twitch.tv/" + channel))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) yield break;
                string html = req.downloadHandler.text;
                if (!html.Contains("\"isLiveBroadcast\": true\"") && !html.Contains("isLiveBroadcast\":true"))
                    yield break;
                if (gls == null) yield break;
                if (_twitchButton == null)
                    _twitchButton = AccessTools.Field(typeof(GameplayLogicScript), "go_TwitchButton");
                var btn = _twitchButton?.GetValue(gls) as GameObject;
                if (btn == null) yield break;

                string label = "<b>" + channel + "</b> is live!";
                int idx = html.IndexOf("meta name=\"description\" content=");
                if (idx >= 0)
                {
                    string rest = html.Substring(idx + 33);
                    int len = rest.IndexOf('"');
                    if (len > 0) label += "\nStreaming:\n" + rest.Substring(0, len);
                }
                var text = btn.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
                if (text != null) text.text = label;
                btn.SetActive(true);
            }
        }
    }
}
