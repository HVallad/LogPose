using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LogPose.UI
{
    // Frame 2a's hand: the player's cards fan in a centered arc instead of a flat row.
    // The game recomputes hand layout through RefreshHandPositions on every change, so the
    // fan is applied as a postfix over whatever it laid out — the row's own center and
    // baseline are reused, which keeps the fan working in games AND replays without
    // hard-coding screen coordinates. Cards leaving the hand get their rotation reset by
    // the zone refreshes the game already runs.
    internal static class BoardLayoutPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), "RefreshHandPositions")]
        private static void RefreshHandPositions_Postfix(GameplayLogicScript __instance)
        {
            if (!Plugin.CfgUiReskin.Value)
                return;
            try
            {
                if (__instance.Lps_Players == null || __instance.Lps_Players.Count == 0)
                    return;
                List<GameObject> hand = __instance.Lps_Players[0].Lgo_MyHand;
                if (hand == null || hand.Count == 0)
                    return;

                int n = hand.Count;
                // The game moves cards by tweening toward MoveTo targets, so the fan must
                // be written through MoveTo as well (a raw transform write is pulled back
                // to the vanilla target next frame). The mat's center sits at local x = 0
                // in the card parent's space; hand.y comes from the game's location table.
                LocationSet loc = __instance.sc_Locations
                    .playerLocations[__instance.bFlipField ? 2 : 0].hand;
                float m = (n - 1) * 0.5f;
                float dx = Mathf.Min(110f, 760f / Mathf.Max(n, 1));
                for (int i = 0; i < n; i++)
                {
                    if (hand[i] == null)
                        return;
                    CardLogicScript cls = hand[i].GetComponent<CardLogicScript>();
                    if (cls == null)
                        return;
                    float k = i - m;
                    cls.MoveTo(new Vector3(k * dx, loc.y - 18f - Mathf.Abs(k) * 11f));
                    hand[i].transform.localRotation = Quaternion.Euler(0f, 0f, -k * 3.5f);
                }
            }
            catch { }
        }
    }
}
