using System.Reflection;
using HarmonyLib;

namespace LogPose.Replay
{
    // The base game only resets its replay-sync stream in CleanupMatch, which rematches in the
    // same room can skip — producing logs where a second game continues the first game's
    // sequence numbers with no HDR line. Resetting at every game start guarantees each game
    // gets its own RZ1|HDR / RZ1|PLY header, so replay files split cleanly.
    internal static class RecorderPatches
    {
        private static readonly MethodInfo ReplaySyncReset =
            AccessTools.Method(typeof(GameplayLogicScript), "ReplaySync_Reset");

        private static void Reset(GameplayLogicScript gls)
        {
            try
            {
                if (ReplaySyncReset != null)
                    ReplaySyncReset.Invoke(gls, null);
            }
            catch { }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.GameStartSolo))]
        private static void GameStartSolo_Prefix(GameplayLogicScript __instance)
        {
            Reset(__instance);
            ReplayBridge.InReplay = false;
            GameTimer.ResetForNewGame();
        }

        // While a replay is loaded, the board holds replayed history — autosaving it would
        // produce garbage log files full of re-emitted RZ1 lines.
        [HarmonyPrefix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.SaveMyLogLines))]
        private static bool SaveMyLogLines_Prefix()
        {
            return !ReplayBridge.InReplay;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.GameStartMultiplayer))]
        private static void GameStartMultiplayer_Prefix(GameplayLogicScript __instance)
        {
            Reset(__instance);
            // A real match is starting — replay mode is over, logs must record again.
            ReplayBridge.InReplay = false;
            GameTimer.ResetForNewGame();
        }
    }
}
