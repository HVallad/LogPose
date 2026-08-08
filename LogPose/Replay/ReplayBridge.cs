using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LogPose.Replay
{
    // Pushes a reconstructed RState onto the real game board. Card GameObjects are RECONCILED,
    // not rebuilt: RCard instances persist across forward steps, so cards that merely moved
    // keep their GameObject and glide to the new slot via the game's own MoveTo animation.
    // Only newly appearing cards are created (and snapped instantly into place); vanished ones
    // are destroyed. Backward jumps clone fresh RCards, which naturally falls back to a full
    // rebuild-and-snap.
    internal static class ReplayBridge
    {
        private static readonly string[] RefreshMethods =
        {
            "RefreshLifePositions", "RefreshTrashPositions", "RefreshDeployPositions",
            "RefreshDeckPositions", "RefreshHandPositions", "RefreshDonPositions",
            "RefreshStagePositions", "RefreshLeaderPositions",
        };

        public static bool InReplay;

        private static Dictionary<RCard, GameObject> _live = new Dictionary<RCard, GameObject>();
        private static readonly List<GameObject> _newThisApply = new List<GameObject>();

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

        public static void ResetLiveCards()
        {
            _live.Clear();
        }

        public static void Apply(GameplayLogicScript gls, ReplaySession session, bool revealAll)
        {
            if (gls == null || gls.Lps_Players == null || gls.Lps_Players.Count < 2)
                return;
            RState st = session.Current;
            var reused = new Dictionary<RCard, GameObject>();
            _newThisApply.Clear();

            for (int p = 0; p < 2; p++)
            {
                PlayerState ps = gls.Lps_Players[p];
                for (int z = 0; z < RState.ZoneCount; z++)
                    ReconcileZone(gls, p, z, st.P[p][z], GetList(ps, z), revealAll, reused);
            }

            // Anything still in the old map vanished from the board (or belongs to a rewound
            // timeline); anything in a zone list we didn't create belongs to the underlying
            // solo game — both get destroyed by ReconcileZone/leftover pass.
            foreach (var kv in _live)
                if (kv.Value != null)
                    UnityEngine.Object.Destroy(kv.Value);
            _live = reused;

            gls.Lps_Players[0].s_PlayerName = session.File.Player1;
            gls.Lps_Players[1].s_PlayerName = session.File.Player2;
            if (gls.gsv_CurrentGame != null)
                gls.gsv_CurrentGame.iTurnNumber = session.TurnAt(st.EventIndex);

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

            // Newly created cards snap straight to their slot; reused cards keep the queued
            // MoveTo animation so actual game actions glide naturally.
            foreach (GameObject go in _newThisApply)
                SnapCard(go);

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

        private static void ReconcileZone(GameplayLogicScript gls, int player, int zone, List<RCard> src,
            List<GameObject> dest, bool revealAll, Dictionary<RCard, GameObject> reused)
        {
            if (dest == null)
                return;
            // Objects we don't manage (the underlying solo game's cards) get destroyed here;
            // ours are handled by the reuse/leftover passes.
            foreach (GameObject go in dest)
            {
                if (go == null || _live.ContainsValue(go) || reused.ContainsValue(go))
                    continue;
                CardLogicScript old = go.GetComponent<CardLogicScript>();
                if (old != null && old.lgo_AttachedDon != null)
                    foreach (GameObject don in old.lgo_AttachedDon)
                        if (don != null && !_live.ContainsValue(don))
                            UnityEngine.Object.Destroy(don);
                UnityEngine.Object.Destroy(go);
            }
            dest.Clear();

            for (int i = 0; i < src.Count; i++)
            {
                RCard rc = src[i];
                GameObject go = TakeOrMake(gls, rc, player, zone, i, revealAll, reused);
                if (go == null)
                    continue;
                CardLogicScript cls = go.GetComponent<CardLogicScript>();
                cls.lgo_AttachedDon.Clear();
                foreach (RCard don in rc.AttachedDon)
                {
                    GameObject dgo = TakeOrMake(gls, don, player, 5, 0, true, reused);
                    if (dgo != null)
                        cls.lgo_AttachedDon.Add(dgo);
                }
                dest.Add(go);
            }
        }

        private static GameObject TakeOrMake(GameplayLogicScript gls, RCard rc, int player, int zone,
            int indexInZone, bool revealAll, Dictionary<RCard, GameObject> reused)
        {
            if (CardDatabaseScript.Instance == null)
                return null;
            string effectiveId = rc.Id == "?" ? "Don" : rc.Id;
            CardDefinition def = CardDatabaseScript.Instance.FindDefinition(effectiveId);
            if (def == null)
                return null;

            GameObject go;
            bool isNew;
            if (_live.TryGetValue(rc, out go) && go != null)
            {
                _live.Remove(rc);
                isNew = false;
            }
            else
            {
                go = UnityEngine.Object.Instantiate(gls.prefab_CardTemplate);
                go.transform.SetParent(gls.cn_Canvas.transform);
                go.transform.localScale = new Vector3(1f, 1f);
                isNew = true;
            }
            reused[rc] = go;

            CardLogicScript cls = go.GetComponent<CardLogicScript>();
            if (isNew || cls.myCard.cardDef == null || cls.myCard.cardDef.cardID != effectiveId)
                cls.LoadCardDefinition(def);
            go.name = effectiveId;

            bool privateZone = zone == 0 || zone == 1 || zone == 3 || zone == 4;
            bool visible = (revealAll || rc.VisOwner) && rc.Id != "?";
            cls.SetFaceUp(!privateZone || visible);
            cls.myCard.bForcedFaceUp = zone == 3 && visible;
            cls.myCard.bTapped = rc.Tapped;
            cls.myCard.iHandUIOrder = indexInZone;
            cls.myCard.deckUniqueID = (player == 0) ? (1000 + indexInZone) : (-1000 - indexInZone);
            if (isNew)
                _newThisApply.Add(go);
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
