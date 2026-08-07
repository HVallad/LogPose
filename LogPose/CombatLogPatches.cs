using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace LogPose
{
    internal static class CombatLogPatches
    {
        private static readonly Regex TmpTags = new Regex("<[^<>]{1,64}?>", RegexOptions.Compiled);
        private static readonly Regex InvisibleChars = new Regex("[\u200B\u200C\u200D\u2060\uFEFF]", RegexOptions.Compiled);

        // The vanilla autosave writes markup-laden lines interleaved with RZ1 replay lines using
        // the platform default encoding (mojibake for names with zero-width characters). Write a
        // clean human log and a separate replay stream next to it, both UTF-8.
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.SaveMyLogLines))]
        private static void SaveMyLogLines_Postfix(GameplayLogicScript __instance)
        {
            try
            {
                WriteCleanedLogs(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Clean log write failed: " + e.Message);
            }
        }

        private static void WriteCleanedLogs(GameplayLogicScript gls)
        {
            if (gls == null || gls.currentCombatLog == null || gls.currentCombatLog.Count == 0)
                return;
            bool clean = Plugin.CfgWriteCleanLog.Value;
            bool replay = Plugin.CfgWriteReplayFile.Value;
            if (!clean && !replay)
                return;

            string dir = Path.Combine("CombatLogs", "AutoSaved");
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyy'-'MM'-'dd'T'HH'.'mm'.'ss");

            var human = new List<string>();
            var rz = new List<string>();
            foreach (string raw in gls.currentCombatLog)
            {
                if (raw == null)
                    continue;
                if (raw.StartsWith("RZ1|", StringComparison.Ordinal))
                {
                    rz.Add(raw);
                    continue;
                }
                string line = TmpTags.Replace(raw, "");
                line = InvisibleChars.Replace(line, "");
                human.Add(line);
            }

            var utf8 = new UTF8Encoding(false);
            if (clean && human.Count > 0)
                File.WriteAllLines(Path.Combine(dir, stamp + ".clean.log"), human.ToArray(), utf8);
            if (replay && rz.Count > 0)
                File.WriteAllLines(Path.Combine(dir, stamp + ".rz1"), rz.ToArray(), utf8);
        }

        // Display polish: configurable font size for new log lines.
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.AddLocalLogLine))]
        private static void AddLocalLogLine_Postfix(GameplayLogicScript __instance)
        {
            float size = Plugin.CfgLogFontSize.Value;
            if (size <= 0f || __instance == null || __instance.go_LogView == null)
                return;
            try
            {
                Transform content = __instance.go_LogView.transform.GetChild(0).GetChild(0);
                if (content.childCount == 0)
                    return;
                TMP_Text tmp = content.GetChild(content.childCount - 1).GetComponent<TMP_Text>();
                if (tmp != null)
                    tmp.fontSize = size;
            }
            catch
            {
                // layout not as expected; never break the log pipeline
            }
        }
    }
}
