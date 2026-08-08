using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LogPose.Replay
{
    // Pushes a reconstructed RState onto the real game board, mirroring what the game's own
    // GameLoadState does: destroy all card objects, rebuild each zone list, restore turn info,
    // park the state machine, refresh every zone layout.
    internal static class ReplayBridge
    {
        private static readonly string[] RefreshMethods =
        {
            "RefreshLifePositions", "RefreshTrashPositions", "RefreshDeployPositions",
            "RefreshDeckPositions", "RefreshHandPositions", "RefreshDonPositions",
            "RefreshStagePositions", "RefreshLeaderPositions",
        };

        public static bool InReplay;

        public static GameplayLogicScript FindBoard()
        {
            return UnityEngine.Object.FindFirstObjectByType<GameplayLogicScript>();
        }

        public static bool IsSoloBoard(GameplayLogicScript gls)
        {
            try
            {
                var style = Traverse.Create(gls).Field("e_GameStyle").GetValue();
                return style != null && style.ToString() != "Multiplayer";
            }
            catch
            {
                return false;
            }
        }

        public static void Apply(GameplayLogicScript gls, ReplaySession session, bool revealAll)
        {
            if (gls == null || gls.Lps_Players == null || gls.Lps_Players.Count < 2)
                return;
            RState st = session.Current;
            for (int p = 0; p < 2; p++)
            {
                PlayerState ps = gls.Lps_Players[p];
                for (int z = 0; z < RState.ZoneCount; z++)
                    RebuildZone(gls, p, z, st.P[p][z], GetList(ps, z), revealAll);
            }

            gls.Lps_Players[0].s_PlayerName = session.File.Player1;
            gls.Lps_Players[1].s_PlayerName = session.File.Player2;

            if (gls.gsv_CurrentGame != null)
            {
                int idx = st.EventIndex;
                gls.gsv_CurrentGame.iTurnNumber = session.TurnAt(idx);
            }

            var t = Traverse.Create(gls);
            TrySet(t, "e_CurrentState", GameplayState.PlayerTurn_Action);
            TrySet(t, "go_Attacker", null);
            TrySet(t, "go_Defender", null);
            TrySet(t, "go_PendingChoice", null);
            try { gls.RemoveChoices(); } catch { }

            foreach (string m in RefreshMethods)
            {
                try
                {
                    MethodInfo mi = AccessTools.Method(typeof(GameplayLogicScript), m);
                    if (mi == null)
                        continue;
                    ParameterInfo[] pars = mi.GetParameters();
                    object[] args = new object[pars.Length];
                    for (int i = 0; i < pars.Length; i++)
                        args[i] = pars[i].HasDefaultValue
                            ? pars[i].DefaultValue
                            : (pars[i].ParameterType.IsValueType ? Activator.CreateInstance(pars[i].ParameterType) : null);
                    mi.Invoke(gls, args);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Replay: " + m + " failed: " + e.Message);
                }
            }

            // The refreshes queue MoveTo animations from spawn position to each card's slot,
            // which reads as re-dealing the whole board on every seek. Snap every card straight
            // to its destination in the same frame so scrubbing is instant.
            for (int p = 0; p < 2; p++)
            {
                PlayerState ps = gls.Lps_Players[p];
                for (int z = 0; z < RState.ZoneCount; z++)
                {
                    List<GameObject> list = GetList(ps, z);
                    if (list == null)
                        continue;
                    foreach (GameObject go in list)
                        SnapCard(go);
                }
            }

            // Park the rules engine: every win/lose/concede path checks bHasGameEnded first,
            // so raising it keeps the game from adjudicating the replayed position.
            TrySet(t, "bHasGameEnded", true);
            try { if (gls.go_YouLose != null) gls.go_YouLose.SetActive(false); } catch { }
            try { if (gls.go_YouWin != null) gls.go_YouWin.SetActive(false); } catch { }

            try
            {
                var turnText = Traverse.Create(gls).Field("text_TurnCount").GetValue() as TMPro.TMP_Text;
                if (turnText != null)
                    turnText.text = "Turn " + session.TurnAt(st.EventIndex) + "  [REPLAY]";
            }
            catch { }
            InReplay = true;
        }

        private static List<GameObject> GetList(PlayerState ps, int zone)
        {
            switch (zone)
            {
                case 0: return ps.Lgo_MyDeck;
                case 1: return ps.Lgo_MyHand;
                case 2: return ps.Lgo_MyDeploy;
                case 3: return ps.Lgo_MyLifeDeck;
                case 4: return ps.Lgo_MyDonDeck;
                case 5: return ps.Lgo_MyDonCostArea;
                case 6: return ps.Lgo_MyTrash;
                case 7: return ps.Lgo_MyStage;
                case 8: return ps.Lgo_MyLeader;
                default: return null;
            }
        }

        private static void RebuildZone(GameplayLogicScript gls, int player, int zone, List<RCard> src,
            List<GameObject> dest, bool revealAll)
        {
            if (dest == null)
                return;
            foreach (GameObject go in dest)
            {
                if (go == null)
                    continue;
                // Attached don are separate GameObjects referenced only by the parent card —
                // destroy them too or they linger on the canvas as phantom don.
                CardLogicScript old = go.GetComponent<CardLogicScript>();
                if (old != null && old.lgo_AttachedDon != null)
                    foreach (GameObject don in old.lgo_AttachedDon)
                        if (don != null)
                            UnityEngine.Object.Destroy(don);
                UnityEngine.Object.Destroy(go);
            }
            dest.Clear();

            for (int i = 0; i < src.Count; i++)
            {
                RCard rc = src[i];
                GameObject go = MakeCard(gls, rc.Id, player, zone, i, rc, revealAll);
                if (go == null)
                    continue;
                CardLogicScript cls = go.GetComponent<CardLogicScript>();
                foreach (RCard don in rc.AttachedDon)
                {
                    GameObject dgo = MakeCard(gls, "Don", player, 5, 0, don, revealAll: true);
                    if (dgo != null)
                        cls.lgo_AttachedDon.Add(dgo);
                }
                dest.Add(go);
            }
        }

        private static GameObject MakeCard(GameplayLogicScript gls, string id, int player, int zone,
            int indexInZone, RCard rc, bool revealAll)
        {
            if (CardDatabaseScript.Instance == null)
                return null;
            // "?" = still-hidden card seeded from checksums; render as a face-down stand-in.
            CardDefinition def = CardDatabaseScript.Instance.FindDefinition(id == "?" ? "Don" : id);
            if (def == null)
                return null;
            GameObject go = UnityEngine.Object.Instantiate(gls.prefab_CardTemplate);
            go.name = id;
            go.transform.SetParent(gls.cn_Canvas.transform);
            go.transform.localScale = new Vector3(1f, 1f);
            CardLogicScript cls = go.GetComponent<CardLogicScript>();
            cls.LoadCardDefinition(def);

            // Vp records what the recorder's client could see; zones 0/1/3/4 are the private ones
            // (deck, hand, life, don deck) — everything else is public information.
            bool privateZone = zone == 0 || zone == 1 || zone == 3 || zone == 4;
            bool visible = (revealAll && rc.Id != "?") || (!revealAll && rc.VisOwner && rc.Id != "?");
            cls.SetFaceUp(!privateZone || visible);
            if (zone == 3 && visible)
                cls.myCard.bForcedFaceUp = true;
            cls.myCard.bTapped = rc.Tapped;
            cls.myCard.iHandUIOrder = indexInZone;
            cls.myCard.deckUniqueID = (player == 0) ? (1000 + indexInZone) : (-1000 - indexInZone);
            return go;
        }

        private static void SnapCard(GameObject go)
        {
            if (go == null)
                return;
            CardLogicScript cls = go.GetComponent<CardLogicScript>();
            if (cls == null)
                return;
            if (cls.vDestination != Vector3.zero)
                go.transform.localPosition = cls.vDestination;
            if (cls.lgo_AttachedDon != null)
                foreach (GameObject don in cls.lgo_AttachedDon)
                    SnapCard(don);
        }

        private static void TrySet(Traverse t, string field, object value)
        {
            try
            {
                var f = t.Field(field);
                if (f.FieldExists())
                    f.SetValue(value);
            }
            catch { }
        }
    }
}
