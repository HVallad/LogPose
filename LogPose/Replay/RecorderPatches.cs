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
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.GameStartMultiplayer))]
        private static void GameStartMultiplayer_Prefix(GameplayLogicScript __instance)
        {
            Reset(__instance);
        }
    }
}
