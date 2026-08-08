using System;
using System.Collections.Generic;

namespace LogPose.Replay
{
    // Virtual board reconstruction. Zones use ReplaySyncZone numbering (0 deck, 1 hand,
    // 2 characters, 3 life, 4 don deck, 5 don cost area, 6 trash, 7 stage, 8 leader);
    // zone 9 (equipped don) lives on the parent card's AttachedDon list, slot-encoded as
    // parent*100 + attachIndex with parent 99 = leader.
    internal class RCard
    {
        public string Id;
        public bool Tapped;
        public bool VisOwner = true;
        public bool VisEnemy = true;
        public List<RCard> AttachedDon = new List<RCard>();

        public RCard Clone()
        {
            var c = new RCard { Id = Id, Tapped = Tapped, VisOwner = VisOwner, VisEnemy = VisEnemy };
            foreach (RCard d in AttachedDon)
                c.AttachedDon.Add(d.Clone());
            return c;
        }
    }

    internal class RState
    {
        public const int ZoneCount = 9;
        public List<RCard>[][] P; // [player 0/1][zone 0-8]
        public int EventIndex;    // number of events applied

        public RState()
        {
            P = new List<RCard>[2][];
            for (int p = 0; p < 2; p++)
            {
                P[p] = new List<RCard>[ZoneCount];
                for (int z = 0; z < ZoneCount; z++)
                    P[p][z] = new List<RCard>();
            }
        }

        public RState Clone()
        {
            var s = new RState { EventIndex = EventIndex };
            for (int p = 0; p < 2; p++)
                for (int z = 0; z < ZoneCount; z++)
                    foreach (RCard c in P[p][z])
                        s.P[p][z].Add(c.Clone());
            return s;
        }
    }

    internal class ReplaySession
    {
        private const int SnapshotEvery = 20;

        public Rz1File File;
        public RState Current;
        private readonly List<RState> _snapshots = new List<RState>();

        public string ValidationSummary = "";

        // Event indexes where a human combat-log line sits — i.e. logical action boundaries.
        public readonly List<int> ActionMarks = new List<int>();

        public ReplaySession(Rz1File file)
        {
            File = file;
            Current = new RState();
            SeedInitialState(Current);
            _snapshots.Add(Current.Clone());
            Validate();
            BuildActionMarks();
        }

        private void BuildActionMarks()
        {
            if (File.HumanLines == null || File.Events.Count == 0)
                return;
            int lower = File.Events[0].GlobalIndex;
            int upper = File.Events[File.Events.Count - 1].GlobalIndex;
            int evIdx = 0;
            foreach (KeyValuePair<int, string> kv in File.HumanLines)
            {
                if (kv.Key < lower)
                    continue;
                if (kv.Key > upper)
                    break;
                while (evIdx < File.Events.Count && File.Events[evIdx].GlobalIndex < kv.Key)
                    evIdx++;
                if (evIdx >= File.Events.Count)
                    break;
                if (ActionMarks.Count == 0 || ActionMarks[ActionMarks.Count - 1] != evIdx)
                    ActionMarks.Add(evIdx);
            }
        }

        // The game prints an action's log line AFTER emitting its moves, so the lines that
        // describe an action sit at its END boundary.
        private bool ActionSaysRevealDraw(int actionEnd)
        {
            if (File.HumanLines == null || File.Events.Count == 0)
                return false;
            int key = actionEnd < File.Events.Count
                ? File.Events[actionEnd].GlobalIndex
                : File.Events[File.Events.Count - 1].GlobalIndex + 1;
            foreach (KeyValuePair<int, string> kv in File.HumanLines)
            {
                if (kv.Key < key)
                    continue;
                if (kv.Key > key)
                    break;
                if (kv.Value.IndexOf("Reveal and Draw", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public int NextActionMark(int eventIndex)
        {
            foreach (int m in ActionMarks)
                if (m > eventIndex)
                    return m;
            return EventCount;
        }

        public int PrevActionMark(int eventIndex)
        {
            int best = 0;
            foreach (int m in ActionMarks)
                if (m < eventIndex)
                    best = m;
                else
                    break;
            return best;
        }

        // Synthetic log lines describing deck-internal activity (searches, mills, scries) per
        // action, keyed like HumanLines by the global move index they precede. The RZ1 stream
        // records real card ids for these moves — including the opponent's searches.
        public readonly List<KeyValuePair<int, string>> DeckActivityLines = new List<KeyValuePair<int, string>>();

        // Same data in visual form: the cards each deck-digging action touched, with the
        // action's event range, so the viewer can display them like the game's own reveal row.
        public class DeckActivity
        {
            public int Start, End;   // event-index range (start exclusive of prior action)
            public int Player;       // 1 or 2
            public readonly List<string> CardIds = new List<string>();
            public readonly List<bool> ToHand = new List<bool>();
            public readonly List<int> EventIdx = new List<int>();  // when each card's move applies
        }
        public readonly List<DeckActivity> DeckActivities = new List<DeckActivity>();

        public void BuildDeckActivityLines(Func<string, string> nameOf)
        {
            DeckActivityLines.Clear();
            DeckActivities.Clear();
            if (File.Events.Count == 0)
                return;
            var bounds = new List<int>(ActionMarks);
            if (bounds.Count == 0 || bounds[0] != 0)
                bounds.Insert(0, 0);
            bounds.Add(File.Events.Count);
            DeckActivity pendingRevealDraw = null;
            for (int a = 0; a + 1 < bounds.Count; a++)
            {
                int start = bounds[a], end = bounds[a + 1];
                var parts = new List<string>();
                var reorders = new List<string>();
                var activity = new DeckActivity { Start = start, End = end };
                int deckTouches = 0;
                bool searchy = false;
                int player = 0;
                for (int i = start; i < end; i++)
                {
                    Rz1Event ev = File.Events[i];
                    bool deckInvolved = ev.Oz == 0 || ev.Dz == 0;
                    if (!deckInvolved || ev.CardId == "" || ev.CardId == "?" || ev.CardId == "Don")
                        continue;
                    deckTouches++;
                    player = ev.Player;
                    string what = null;
                    if (ev.Oz == 0 && ev.Dz == 0)
                    {
                        searchy = true;
                        if (ev.Os == ev.Ds)
                            what = "revealed on deck";
                        else
                        {
                            reorders.Add(nameOf(ev.CardId) + (ev.Ds == 0 ? " to deck bottom" : " reordered"));
                            if (!activity.CardIds.Contains(ev.CardId))
                            {
                                activity.CardIds.Add(ev.CardId);
                                activity.ToHand.Add(false);
                                activity.EventIdx.Add(i);
                            }
                            continue;
                        }
                    }
                    else if (ev.Oz == 0 && ev.Dz == 1) what = "to hand";
                    else if (ev.Oz == 0 && ev.Dz == 6) { what = "milled to trash"; searchy = true; }
                    else if (ev.Oz == 0 && ev.Dz == 2) { what = "into play"; searchy = true; }
                    else if (ev.Oz == 0 && ev.Dz == 3) { what = "to life"; searchy = true; }
                    else if (ev.Oz == 1 && ev.Dz == 0) { what = "hand to deck"; searchy = true; }
                    else if (ev.Dz == 0) { what = "to deck"; searchy = true; }
                    else if (ev.Oz == 0) what = "out of deck";
                    if (what != null)
                    {
                        parts.Add(nameOf(ev.CardId) + " " + what);
                        int existing = activity.CardIds.IndexOf(ev.CardId);
                        if (existing < 0)
                        {
                            activity.CardIds.Add(ev.CardId);
                            activity.ToHand.Add(ev.Oz == 0 && ev.Dz == 1);
                            activity.EventIdx.Add(i);
                        }
                        else if (ev.Oz == 0 && ev.Dz == 1)
                        {
                            activity.ToHand[existing] = true;
                            activity.EventIdx[existing] = i;
                        }
                    }
                }
                // Many reorders in one action = a shuffle, not a search — don't spell out
                // the whole deck (or show a reveal row for it).
                if (reorders.Count >= 8)
                {
                    parts.Add("shuffled the deck");
                    activity.CardIds.Clear();
                    activity.ToHand.Clear();
                    activity.EventIdx.Clear();
                }
                else
                    parts.AddRange(reorders);
                // A plain draw is already narrated by the game's own log line — but a search
                // take ("Reveal and Draw X") looks identical in the stream, so remember it in
                // case the next action bottoms the rest of the looked-at cards.
                if (parts.Count == 0 || (!searchy && deckTouches <= 1))
                {
                    if (activity.CardIds.Count > 0 && ActionSaysRevealDraw(end))
                    {
                        activity.Player = player;
                        pendingRevealDraw = activity;
                    }
                    else
                    {
                        pendingRevealDraw = null;
                    }
                    continue;
                }
                activity.Player = player;
                if (pendingRevealDraw != null && pendingRevealDraw.Player == player
                    && pendingRevealDraw.End == activity.Start)
                {
                    for (int c = pendingRevealDraw.CardIds.Count - 1; c >= 0; c--)
                    {
                        if (activity.CardIds.Contains(pendingRevealDraw.CardIds[c]))
                            continue;
                        activity.CardIds.Insert(0, pendingRevealDraw.CardIds[c]);
                        activity.ToHand.Insert(0, pendingRevealDraw.ToHand[c]);
                        activity.EventIdx.Insert(0, pendingRevealDraw.EventIdx[c]);
                    }
                    activity.Start = pendingRevealDraw.Start;
                }
                pendingRevealDraw = null;
                if (activity.CardIds.Count > 0)
                {
                    // A search spans consecutive actions: "Reveal and Draw X" (the take)
                    // followed by "Placing Cards on Bottom" (the rejects). Merge them so the
                    // reveal row shows the whole looked-at set together.
                    DeckActivity prev = DeckActivities.Count > 0 ? DeckActivities[DeckActivities.Count - 1] : null;
                    if (prev != null && prev.Player == player && prev.End == activity.Start)
                    {
                        for (int c = 0; c < activity.CardIds.Count; c++)
                        {
                            int existing = prev.CardIds.IndexOf(activity.CardIds[c]);
                            if (existing < 0)
                            {
                                prev.CardIds.Add(activity.CardIds[c]);
                                prev.ToHand.Add(activity.ToHand[c]);
                                prev.EventIdx.Add(activity.EventIdx[c]);
                            }
                            else if (activity.ToHand[c])
                            {
                                prev.ToHand[existing] = true;
                                prev.EventIdx[existing] = activity.EventIdx[c];
                            }
                        }
                        prev.End = activity.End;
                    }
                    else
                    {
                        DeckActivities.Add(activity);
                    }
                }
                const int maxShown = 6;
                string body = string.Join(" · ", parts.GetRange(0, Math.Min(parts.Count, maxShown)).ToArray());
                if (parts.Count > maxShown)
                    body += " · +" + (parts.Count - maxShown) + " more";
                int key = end < File.Events.Count
                    ? File.Events[end].GlobalIndex
                    : File.Events[File.Events.Count - 1].GlobalIndex + 1;
                string who = player == 2 ? File.Player2 : File.Player1;
                int hash = who.IndexOf('#');
                if (hash > 0)
                    who = who.Substring(0, hash);
                DeckActivityLines.Add(new KeyValuePair<int, string>(key,
                    "<color=#7A4A1E><i>- " + who + ": " + body + "</i></color>"));
            }
        }

        // Leaders and the initial deck/life/don-deck contents never "move" in the RZ1 stream,
        // so they must be seeded: leader from the PLY line, hidden piles as placeholder cards
        // sized from the first CHK checksum per player (fallback: 50 deck / 10 don).
        private void SeedInitialState(RState st)
        {
            for (int p = 0; p < 2; p++)
            {
                string leader = (p == 0) ? File.Leader1 : File.Leader2;
                if (!string.IsNullOrEmpty(leader))
                    st.P[p][8].Add(new RCard { Id = leader });
                int[] c = File.InitialCounts[p];
                int deck = (c != null) ? c[0] : 50;
                int life = (c != null) ? c[3] : 0;
                int don = (c != null) ? c[4] : 10;
                for (int i = 0; i < deck; i++)
                    st.P[p][0].Add(new RCard { Id = "?", VisOwner = false, VisEnemy = false });
                for (int i = 0; i < life; i++)
                    st.P[p][3].Add(new RCard { Id = "?", VisOwner = false, VisEnemy = false });
                for (int i = 0; i < don; i++)
                    st.P[p][4].Add(new RCard { Id = "Don", VisOwner = false, VisEnemy = false });
            }
        }

        // Replays the whole file once against the CHK checksums embedded in the stream and
        // records how faithful the reconstruction is.
        private void Validate()
        {
            var st = new RState();
            SeedInitialState(st);
            int checks = 0, bad = 0;
            var firstBad = "";
            for (int i = 0; i < File.Events.Count; i++)
            {
                Rz1Event ev = File.Events[i];
                ApplyTo(st, ev);
                if (ev.Check == null)
                    continue;
                checks++;
                int p = (ev.CheckPlayer == 2) ? 1 : 0;
                List<RCard>[] z = st.P[p];
                int eqDon = 0;
                if (z[8].Count > 0) eqDon += z[8][0].AttachedDon.Count;
                foreach (RCard dc in z[2]) eqDon += dc.AttachedDon.Count;
                int[] mine =
                {
                    z[0].Count, z[1].Count, z[2].Count, z[3].Count, z[4].Count,
                    z[5].Count, z[6].Count, z[7].Count, z[8].Count, eqDon,
                };
                for (int k = 0; k < 10; k++)
                {
                    if (mine[k] != ev.Check[k])
                    {
                        bad++;
                        if (firstBad == "")
                            firstBad = string.Format("first mismatch at event {0} (seq {1}) p{2} zone#{3}: mine={4} chk={5}",
                                i, ev.Seq, ev.CheckPlayer, k, mine[k], ev.Check[k]);
                        break;
                    }
                }
            }
            ValidationSummary = string.Format("{0}/{1} checksums OK{2}",
                checks - bad, checks, firstBad == "" ? "" : " — " + firstBad);
            Plugin.Log.LogInfo("Replay validation: " + ValidationSummary);
        }

        public int EventCount { get { return File.Events.Count; } }

        public void SeekTo(int eventIndex)
        {
            eventIndex = Math.Max(0, Math.Min(eventIndex, EventCount));
            if (eventIndex < Current.EventIndex)
            {
                int snap = Math.Min(eventIndex / SnapshotEvery, _snapshots.Count - 1);
                Current = _snapshots[snap].Clone();
            }
            while (Current.EventIndex < eventIndex)
            {
                Apply(File.Events[Current.EventIndex]);
                Current.EventIndex++;
                if (Current.EventIndex % SnapshotEvery == 0 && Current.EventIndex / SnapshotEvery >= _snapshots.Count)
                    _snapshots.Add(Current.Clone());
            }
        }

        // Turn number at the current position (1-based; marker list indexes are event indexes).
        public int TurnAt(int eventIndex)
        {
            int t = 1;
            foreach (int m in File.TurnMarks)
                if (m < eventIndex) t++; else break;
            return t;
        }

        public int NextTurnMark(int eventIndex)
        {
            foreach (int m in File.TurnMarks)
                if (m > eventIndex) return m;
            return EventCount;
        }

        public int PrevTurnMark(int eventIndex)
        {
            int best = 0;
            foreach (int m in File.TurnMarks)
                if (m < eventIndex - 1) best = m; else break;
            return best;
        }

        private void Apply(Rz1Event ev)
        {
            ApplyTo(Current, ev);
        }

        private static void ApplyTo(RState state, Rz1Event ev)
        {
            int p = (ev.Player == 2) ? 1 : 0;
            List<RCard>[] zones = state.P[p];

            bool inPlace = ev.Oz == ev.Dz && ev.Os == ev.Ds;
            if (inPlace)
            {
                RCard c = FindAt(zones, ev.Dz, ev.Ds, ev.CardId);
                if (c != null)
                {
                    c.Tapped = ev.Tapped;
                    c.VisOwner = ev.Vp == 1;
                    c.VisEnemy = ev.Ve == 1;
                }
                return;
            }

            RCard card = RemoveAt(zones, ev.Oz, ev.Os, ev.CardId) ?? new RCard { Id = ev.CardId };
            card.Tapped = ev.Tapped;
            card.VisOwner = ev.Vp == 1;
            card.VisEnemy = ev.Ve == 1;
            InsertAt(zones, ev.Dz, ev.Ds, card);
        }

        private static RCard ParentOf(List<RCard>[] zones, int encodedSlot, out int attachIdx)
        {
            int parent = encodedSlot / 100;
            attachIdx = encodedSlot % 100;
            if (parent == 99)
                return zones[8].Count > 0 ? zones[8][0] : null;
            return (parent >= 0 && parent < zones[2].Count) ? zones[2][parent] : null;
        }

        private static RCard FindAt(List<RCard>[] zones, int zone, int slot, string id)
        {
            if (zone == 9)
            {
                int ai;
                RCard par = ParentOf(zones, slot, out ai);
                if (par != null && ai >= 0 && ai < par.AttachedDon.Count)
                    return par.AttachedDon[ai];
                return null;
            }
            if (zone < 0 || zone > 8)
                return null;
            List<RCard> list = zones[zone];
            if (slot >= 0 && slot < list.Count && list[slot].Id == id)
                return list[slot];
            return list.Find(c => c.Id == id);
        }

        private static RCard RemoveAt(List<RCard>[] zones, int zone, int slot, string id)
        {
            if (zone == 9)
            {
                int ai;
                RCard par = ParentOf(zones, slot, out ai);
                if (par != null && par.AttachedDon.Count > 0)
                {
                    int take = (ai >= 0 && ai < par.AttachedDon.Count) ? ai : 0;
                    RCard don = par.AttachedDon[take];
                    par.AttachedDon.RemoveAt(take);
                    return don;
                }
                // Encoded parent didn't match — a don is definitely attached somewhere, so
                // scan leader then characters rather than fabricating a duplicate.
                if (zones[8].Count > 0 && zones[8][0].AttachedDon.Count > 0)
                {
                    RCard don = zones[8][0].AttachedDon[0];
                    zones[8][0].AttachedDon.RemoveAt(0);
                    return don;
                }
                foreach (RCard ch in zones[2])
                {
                    if (ch.AttachedDon.Count > 0)
                    {
                        RCard don = ch.AttachedDon[0];
                        ch.AttachedDon.RemoveAt(0);
                        return don;
                    }
                }
                return null;
            }
            if (zone < 0 || zone > 8)
                return null;
            List<RCard> list = zones[zone];
            if (slot >= 0 && slot < list.Count && list[slot].Id == id)
            {
                RCard c = list[slot];
                list.RemoveAt(slot);
                return c;
            }
            int idx = list.FindIndex(c => c.Id == id);
            if (idx >= 0)
            {
                RCard c = list[idx];
                list.RemoveAt(idx);
                return c;
            }
            // Hidden piles (deck/life/don deck) are seeded with placeholders — a move out of
            // them is the moment the card's identity is revealed, so consume the placeholder
            // at that slot instead of inventing an extra card.
            if ((zone == 0 || zone == 3 || zone == 4) && list.Count > 0)
            {
                int take = (slot >= 0 && slot < list.Count) ? slot : list.Count - 1;
                RCard c = list[take];
                list.RemoveAt(take);
                c.Id = id;
                return c;
            }
            return null;
        }

        private static void InsertAt(List<RCard>[] zones, int zone, int slot, RCard card)
        {
            if (zone == 9)
            {
                int ai;
                RCard par = ParentOf(zones, slot, out ai);
                if (par == null)
                    par = zones[8].Count > 0 ? zones[8][0] : null; // fall back to leader
                if (par == null)
                    return;
                par.AttachedDon.Insert(Math.Max(0, Math.Min(ai, par.AttachedDon.Count)), card);
                return;
            }
            if (zone < 0 || zone > 8)
                return;
            List<RCard> list = zones[zone];
            list.Insert(Math.Max(0, Math.Min(slot, list.Count)), card);
        }
    }
}
