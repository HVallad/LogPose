using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LogPose
{
    // The base game only emits RZ1 replay lines on zone *moves*. Tap-state changes from the
    // refresh phase, attacks, blockers and manual taps never reach the stream, so replayers
    // (e.g. OPTCGReplay) show stale DON!!/rest states. These postfixes re-publish the affected
    // cards in place via the game's own ReplaySync_EmitCurrentZoneState, keeping seq/CHK lines
    // canonical. See analysis/log-coverage-and-don-analysis.md for the full gap list.
    internal static class ReplaySyncPatches
    {
        private static readonly MethodInfo EmitCurrentZoneState =
            AccessTools.Method(typeof(GameplayLogicScript), "ReplaySync_EmitCurrentZoneState");

        private static void Emit(GameplayLogicScript gls, PlayerState ps, GameObject go)
        {
            if (!Plugin.CfgEmitMissingReplayLines.Value || gls == null || ps == null || go == null)
                return;
            try
            {
                EmitCurrentZoneState.Invoke(gls, new object[] { ps, go });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Replay emit failed: " + e.Message);
            }
        }

        private static void EmitLeaderOrDeploy(GameplayLogicScript gls, PlayerState ps, int iCardIndex)
        {
            if (ps == null)
                return;
            GameObject go = null;
            if (iCardIndex == -1)
            {
                if (ps.Lgo_MyLeader != null && ps.Lgo_MyLeader.Count > 0)
                    go = ps.Lgo_MyLeader[0];
            }
            else if (ps.Lgo_MyDeploy != null && iCardIndex >= 0 && iCardIndex < ps.Lgo_MyDeploy.Count)
            {
                go = ps.Lgo_MyDeploy[iCardIndex];
            }
            Emit(gls, ps, go);
        }

        // Refresh phase: leader/characters/stage/cost-area DON are all untapped silently.
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), "PlayerUntap")]
        private static void PlayerUntap_Postfix(GameplayLogicScript __instance, PlayerState ps_Player)
        {
            if (ps_Player == null)
                return;
            if (ps_Player.Lgo_MyLeader != null && ps_Player.Lgo_MyLeader.Count > 0)
                Emit(__instance, ps_Player, ps_Player.Lgo_MyLeader[0]);
            if (ps_Player.Lgo_MyDeploy != null)
                for (int i = 0; i < ps_Player.Lgo_MyDeploy.Count; i++)
                    Emit(__instance, ps_Player, ps_Player.Lgo_MyDeploy[i]);
            if (ps_Player.Lgo_MyDonCostArea != null)
                for (int i = 0; i < ps_Player.Lgo_MyDonCostArea.Count; i++)
                    Emit(__instance, ps_Player, ps_Player.Lgo_MyDonCostArea[i]);
            if (ps_Player.Lgo_MyStage != null)
                for (int i = 0; i < ps_Player.Lgo_MyStage.Count; i++)
                    Emit(__instance, ps_Player, ps_Player.Lgo_MyStage[i]);
        }

        // Attacker rest, effect-driven rest/activate of leader and characters.
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.TapCard_Internal))]
        private static void TapCard_Postfix(GameplayLogicScript __instance, PlayerState ps_Player, int iCardIndex)
        {
            EmitLeaderOrDeploy(__instance, ps_Player, iCardIndex);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.UntapCard_Internal))]
        private static void UntapCard_Postfix(GameplayLogicScript __instance, PlayerState ps_Player, int iCardIndex)
        {
            EmitLeaderOrDeploy(__instance, ps_Player, iCardIndex);
        }

        // Blocker rests silently when declared.
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.SetBlocker))]
        private static void SetBlocker_Postfix(GameplayLogicScript __instance, PlayerState ps_Player, int iBlockerIndex)
        {
            EmitLeaderOrDeploy(__instance, ps_Player, iBlockerIndex);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.TapStageCard_Internal))]
        private static void TapStage_Postfix(GameplayLogicScript __instance, PlayerState ps_Player)
        {
            if (ps_Player != null && ps_Player.Lgo_MyStage != null && ps_Player.Lgo_MyStage.Count > 0)
                Emit(__instance, ps_Player, ps_Player.Lgo_MyStage[0]);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.UntapStage_Internal))]
        private static void UntapStage_Postfix(GameplayLogicScript __instance, PlayerState ps_Player)
        {
            if (ps_Player != null && ps_Player.Lgo_MyStage != null && ps_Player.Lgo_MyStage.Count > 0)
                Emit(__instance, ps_Player, ps_Player.Lgo_MyStage[0]);
        }

        // Freeze marks a rested don to skip the next refresh; re-publish the cost area so the
        // stream stays consistent with the refresh emits above.
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), nameof(GameplayLogicScript.FreezeDonCard_Internal))]
        private static void FreezeDon_Postfix(GameplayLogicScript __instance, PlayerState ps_Player)
        {
            if (ps_Player == null || ps_Player.Lgo_MyDonCostArea == null)
                return;
            for (int i = 0; i < ps_Player.Lgo_MyDonCostArea.Count; i++)
                Emit(__instance, ps_Player, ps_Player.Lgo_MyDonCostArea[i]);
        }
    }
}
