using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LogPose.UI
{
    // Frame 2a's field structure, imposed through the game's own layout machinery.
    //
    // All board geometry lives in sc_Locations.playerLocations[seat + (flip?2:0)] and cards
    // tween toward CardLogicScript.MoveTo targets in Canvas/Deck/PlayerN local space (the
    // canvas is 1920x1080 with a center origin; both card parents sit at (0,0)). Rewriting
    // that table re-zones the whole field: the deck pile joins trash in the outer band, the
    // leader row keeps only leader+stage, the life column tucks inside the mat's left edge
    // for BOTH sides (the mockup mirrors vertically; vanilla point-mirrors), and everything
    // slides left so the right rail owns x 1104..1848. The mat art is regenerated to match
    // (tools/Generate-FieldMats.py shares these numbers).
    internal static class BoardLayoutPatches
    {
        // Whole-field x shift. The canvas width varies with aspect ratio, so BoardHUD
        // recomputes this each poll from the real canvas rect: the field keeps its
        // designed left-of-center home on wide screens and slides left on narrow ones
        // so the right rail still fits. -388 = the 1920-wide design value.
        internal static float FieldShift = -388f;

        internal static void Rezone(GameplayLogicScript gls)
        {
            if (!Plugin.CfgUiReskin.Value || gls == null || gls.sc_Locations == null)
                return;
            var seats = gls.sc_Locations.playerLocations;
            if (seats == null)
                return;
            for (int s = 0; s < seats.Count; s++)
            {
                LocationPlayer p = seats[s];
                if (p == null)
                    continue;
                bool opp = (s % 2) == 1;
                float ys = opp ? 1f : -1f;
                Z(p.deck, 195f, 408f * ys, 0.75f);
                Z(p.donDeck, -300f, 408f * ys, 2f);
                Z(p.leader, opp ? -48f : 48f, 250f * ys);
                Z(p.life, -300f, 238f * ys, opp ? -25f : 25f);
                Z(p.donCost, -190f, 408f * ys, 30f);
                Z(p.deploy, -200f, 90f * ys, 120f);
                Z(p.discard, 305f, 408f * ys, 0.5f);
                Z(p.stage, opp ? -167f : 167f, 250f * ys);
                // The reveal row tracks the field so it can't slide off narrow screens
                // (donEquipped stays vanilla — it's a relative offset).
                if (p.topDeck != null)
                { p.topDeck.x = FieldShift - 487f; p.topDeck.y = -275f; p.topDeck.step = 100f; p.topDeck.step2 = 50f; p.topDeck.width = 400f; }
                if (p.topDeckSquish != null)
                { p.topDeckSquish.x = FieldShift - 452f; p.topDeckSquish.y = -275f; p.topDeckSquish.step = 100f; p.topDeckSquish.step2 = 50f; p.topDeckSquish.width = 350f; }
                if (p.hand != null)
                { p.hand.x = FieldShift - 487f; p.hand.y = 430f * ys; p.hand.step = 100f; p.hand.width = 400f; }
            }
        }

        private static void Z(LocationSet l, float x, float y, float step = float.NaN)
        {
            if (l == null)
                return;
            l.x = x + FieldShift;
            l.y = y;
            if (!float.IsNaN(step))
                l.step = step;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), "SetupBoardObjects")]
        private static void SetupBoardObjects_Postfix(GameplayLogicScript __instance)
        {
            Rezone(__instance);
            BoardHUD.ImposeChrome(__instance);   // vanilla just rewrote every position
        }

        // When false, the fan tucks toward the screen edge so the DON!! band and the
        // deck/trash piles underneath stay readable; moving the pointer toward the hand
        // raises it (BoardHUD drives this and re-runs the layout on change).
        internal static bool HandRaised = true;

        // Frame 2a's hand presentation, written through MoveTo after each vanilla layout
        // pass (raw transform writes get pulled back by the tween; rotations persist).
        // Player 0: centered fan under the mat. Player 1: a compact face-down cluster
        // docked beside their leader — in Solo v Self only while that side is not the
        // one acting (their hand is actively played there); replays always dock it.
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), "RefreshHandPositions")]
        private static void RefreshHandPositions_Postfix(GameplayLogicScript __instance)
        {
            if (!Plugin.CfgUiReskin.Value)
                return;
            try
            {
                if (__instance.Lps_Players == null || __instance.Lps_Players.Count == 0)
                    return;
                FanPlayerHand(__instance);
                if (__instance.Lps_Players.Count > 1 && DockWanted(__instance))
                    DockOpponentHand(__instance);
            }
            catch { }
        }

        internal static bool DockWanted(GameplayLogicScript gls)
        {
            if (Replay.ReplayBridge.InReplay)
                return true;
            if (gls.e_GameStyle != GameStyle.SoloVSelf)
                return true;
            // Solo: dock the second hand except while that seat is the one acting
            // mid-game. Mulligan docks too — the cluster is face-up and hoverable,
            // which is enough to make the keep/mulligan call.
            return gls.gsv_CurrentGame == null || gls.gsv_CurrentGame.iTurnNumber < 1
                || gls.gsv_CurrentGame.iPlayerAction == 0;
        }

        private static void FanPlayerHand(GameplayLogicScript gls)
        {
            List<GameObject> hand = gls.Lps_Players[0].Lgo_MyHand;
            if (hand == null || hand.Count == 0)
                return;
            int n = hand.Count;
            LocationSet loc = gls.sc_Locations.playerLocations[gls.bFlipField ? 2 : 0].hand;
            float baseY = -430f;
            if (loc != null && loc.y < 0f)
                baseY = loc.y;
            if (!HandRaised)
                baseY -= 95f;   // tuck: a slim peek stays above the screen edge
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
                cls.MoveTo(new Vector3(FieldShift + k * dx, baseY - 18f - Mathf.Abs(k) * 11f));
                hand[i].transform.localRotation = Quaternion.Euler(0f, 0f, -k * 3.5f);
            }
        }

        private static void DockOpponentHand(GameplayLogicScript gls)
        {
            List<GameObject> hand = gls.Lps_Players[1].Lgo_MyHand;
            if (hand == null || hand.Count == 0)
                return;
            int n = hand.Count;
            float m = (n - 1) * 0.5f;
            float dx = Mathf.Min(30f, 150f / Mathf.Max(n - 1, 1));
            for (int i = 0; i < n; i++)
            {
                if (hand[i] == null)
                    return;
                CardLogicScript cls = hand[i].GetComponent<CardLogicScript>();
                if (cls == null)
                    return;
                float k = i - m;
                cls.MoveTo(new Vector3(FieldShift + 185f + k * dx, 252f - Mathf.Abs(k) * 3f));
                hand[i].transform.localRotation = Quaternion.Euler(0f, 0f, -k * 3f);
            }
        }
    }
}
