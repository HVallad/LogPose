using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LogPose.Replay
{
    internal class Rz1Event
    {
        public int Seq;
        public int Player;      // 1 or 2
        public string CardId;
        public int Oz, Os, Dz, Ds;
        public int Vp, Ve;      // visible to player 1 / player 2 (from recorder's sync)
        public bool Tapped;
        public int PowDelta, CostDelta;
        // Zone-count checksum (CHK line) that followed this move, if any:
        // deck, hand, deploy, life, donDeck, donCost, trash, stage, leader, equippedDon
        public int[] Check;
        public int CheckPlayer;
        // Ordinal of this move across the ENTIRE source file — used to correlate replay
        // position with the human-readable lines of the sibling .log file.
        public int GlobalIndex;
    }

    internal class Rz1File
    {
        public string Path;
        public string Version = "?";
        public string Player1 = "Player 1";
        public string Player2 = "Player 2";
        public string Leader1 = "";
        public string Leader2 = "";
        public List<Rz1Event> Events = new List<Rz1Event>();
        // Event indexes that look like the start of a refresh phase (leader re-published
        // untapped in place by LogPose's PlayerUntap patch) — used as turn jump markers.
        public List<int> TurnMarks = new List<int>();
        // First CHK counts seen per player (index 0/1) — the initial zone sizes to seed
        // (deck, hand, deploy, life, donDeck, donCost, trash, stage, leader, equippedDon).
        public int[][] InitialCounts = new int[2][];
        // Human-readable combat log lines from the sibling .log file, keyed by the global
        // move index they precede (shared by every game parsed from the same file).
        public List<KeyValuePair<int, string>> HumanLines;
    }

    internal static class Rz1Parser
    {
        // One autosaved log can hold several games back-to-back (rematches in the same room
        // don't clear the log buffer). Each game starts with its own HDR line and restarts
        // its sequence counter, so the file is split into independent Rz1File segments.
        public static List<Rz1File> ParseGames(string path)
        {
            var games = new List<Rz1File>();
            var f = new Rz1File { Path = path };
            games.Add(f);
            int globalIndex = 0;
            foreach (string raw in File.ReadAllLines(path))
            {
                if (!raw.StartsWith("RZ1|", StringComparison.Ordinal))
                    continue;
                string[] p = raw.Split('|');
                if (p.Length >= 4 && p[1] == "HDR")
                {
                    if (f.Events.Count > 0 || f.Leader1 != "")
                    {
                        f = new Rz1File { Path = path };
                        games.Add(f);
                    }
                    f.Version = p[2];
                    continue;
                }
                if (p.Length >= 5 && p[1] == "PLY")
                {
                    if (p[2] == "1") { f.Player1 = p[3]; f.Leader1 = p[4]; }
                    else { f.Player2 = p[3]; f.Leader2 = p[4]; }
                    continue;
                }
                if (p[1] == "CHK")
                {
                    // RZ1|CHK|seq|player|deck|hand|deploy|life|donDeck|donCost|trash|stage|leader|eqDon
                    if (p.Length >= 14)
                    {
                        try
                        {
                            int chkPlayer = int.Parse(p[3], CultureInfo.InvariantCulture);
                            var counts = new int[10];
                            for (int i = 0; i < 10; i++)
                                counts[i] = int.Parse(p[4 + i], CultureInfo.InvariantCulture);
                            int pi = (chkPlayer == 2) ? 1 : 0;
                            if (f.InitialCounts[pi] == null)
                                f.InitialCounts[pi] = counts;
                            if (f.Events.Count > 0 && f.Events[f.Events.Count - 1].Check == null)
                            {
                                f.Events[f.Events.Count - 1].Check = counts;
                                f.Events[f.Events.Count - 1].CheckPlayer = chkPlayer;
                            }
                        }
                        catch { }
                    }
                    continue;
                }
                if (p.Length < 13)
                    continue;
                try
                {
                    var ev = new Rz1Event
                    {
                        Seq = int.Parse(p[1], CultureInfo.InvariantCulture),
                        Player = int.Parse(p[2], CultureInfo.InvariantCulture),
                        CardId = p[3],
                        Oz = int.Parse(p[4], CultureInfo.InvariantCulture),
                        Os = int.Parse(p[5], CultureInfo.InvariantCulture),
                        Dz = int.Parse(p[6], CultureInfo.InvariantCulture),
                        Ds = int.Parse(p[7], CultureInfo.InvariantCulture),
                        Vp = int.Parse(p[8], CultureInfo.InvariantCulture),
                        Ve = int.Parse(p[9], CultureInfo.InvariantCulture),
                        Tapped = p[10] == "1",
                        PowDelta = int.Parse(p[11], CultureInfo.InvariantCulture),
                        CostDelta = int.Parse(p[12], CultureInfo.InvariantCulture),
                        GlobalIndex = globalIndex++,
                    };
                    if (ev.Oz == 8 && ev.Dz == 8 && ev.Os == 0 && ev.Ds == 0 && !ev.Tapped)
                        f.TurnMarks.Add(f.Events.Count);
                    f.Events.Add(ev);
                }
                catch
                {
                    // tolerate malformed lines
                }
            }
            games.RemoveAll(g => g.Events.Count == 0);
            List<Rz1File> result = SplitByCheckpoints(games);
            List<KeyValuePair<int, string>> human = LoadHumanLines(path);
            foreach (Rz1File g in result)
                g.HumanLines = human;
            return result;
        }

        // The vanilla .log interleaves human lines with RZ1 lines in emission order, which
        // encodes their correspondence: a human line "belongs" right before the Nth move line.
        private static readonly System.Text.RegularExpressions.Regex TmpTags =
            new System.Text.RegularExpressions.Regex("<[^<>]{1,64}?>");
        private static readonly System.Text.RegularExpressions.Regex InvisibleChars =
            new System.Text.RegularExpressions.Regex("[\u200B\u200C\u200D\u2060\uFEFF]");

        private static List<KeyValuePair<int, string>> LoadHumanLines(string rz1Path)
        {
            var result = new List<KeyValuePair<int, string>>();
            try
            {
                string basePath = rz1Path.EndsWith(".rz1", StringComparison.OrdinalIgnoreCase)
                    ? rz1Path.Substring(0, rz1Path.Length - 4)
                    : rz1Path;
                string logPath = basePath + ".log";
                if (File.Exists(logPath))
                {
                    int moveCount = 0;
                    foreach (string raw in File.ReadAllLines(logPath))
                    {
                        if (raw.StartsWith("RZ1|", StringComparison.Ordinal))
                        {
                            string[] p = raw.Split('|');
                            if (p.Length >= 13 && p[1] != "CHK" && p[1] != "HDR" && p[1] != "PLY")
                                moveCount++;
                            continue;
                        }
                        string line = InvisibleChars.Replace(TmpTags.Replace(raw, ""), "").Trim();
                        if (line.Length > 0)
                            result.Add(new KeyValuePair<int, string>(moveCount, line));
                    }
                    return result;
                }
                // Recordings without a vanilla .log (it was blocked while the clean pair still
                // wrote) keep their narration in .clean.log, but the RZ1 interleaving — and
                // with it the line-to-move correlation — is lost. Recover approximate keys by
                // anchoring each line's [CARD-ID] mentions against the move stream in order.
                string cleanPath = basePath + ".clean.log";
                if (File.Exists(cleanPath))
                    AlignCleanLog(cleanPath, rz1Path, result);
            }
            catch { }
            return result;
        }

        private static readonly System.Text.RegularExpressions.Regex CardIdRef =
            new System.Text.RegularExpressions.Regex("\\[([A-Za-z0-9]{1,8}-\\d{2,4})\\]");
        private static readonly System.Text.RegularExpressions.Regex EndTurnRef =
            new System.Text.RegularExpressions.Regex("^\\[[^\\]]+\\] End Turn$");

        private static void AlignCleanLog(string cleanPath, string rz1Path,
            List<KeyValuePair<int, string>> result)
        {
            var moveIds = new List<string>();
            var isDraw = new List<bool>();
            var untaps = new List<int>();
            foreach (string raw in File.ReadAllLines(rz1Path))
            {
                if (!raw.StartsWith("RZ1|", StringComparison.Ordinal))
                    continue;
                string[] p = raw.Split('|');
                if (p.Length < 13 || p[1] == "CHK" || p[1] == "HDR" || p[1] == "PLY")
                    continue;
                // Leader in-place untap = the turn-start refresh, same signature TurnMarks use.
                if (p[4] == "8" && p[6] == "8" && p[5] == "0" && p[7] == "0" && p[10] == "0")
                    untaps.Add(moveIds.Count);
                isDraw.Add(p[4] == "0" && p[6] == "1");
                moveIds.Add(p[3]);
            }
            // Three anchor classes keep the cursor from drifting across the big unlogged
            // shuffle/mulligan blobs:
            //  1. "End Turn" lines snap to the next leader untap — the game prints the line,
            //     then the next player's refresh emits (exact, unbounded).
            //  2. Draw narrations snap to the named card's next deck-to-hand move (exact,
            //     unbounded; carries the cursor across game-boundary setup).
            //  3. Any other card mention advances past its own emitted moves within a SHORT
            //     window only — lines whose action emits no matchable move (don attaches
            //     naming the leader, etc.) must NOT grab that card's next-turn event, which
            //     is how an earlier resync-window version compounded a whole game of drift.
            const int Window = 8;
            int cursor = 0;
            foreach (string raw in File.ReadAllLines(cleanPath))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;

                if (EndTurnRef.IsMatch(line))
                {
                    // Backward tolerance matters: short-window creep from narration-only
                    // lines can push the cursor a dozen moves past the refresh untap, and
                    // snapping to the untap AFTER that would skip a whole turn. A real
                    // turn is far longer than the tolerance, so this can't re-grab the
                    // current turn's own untap.
                    int u = -1;
                    foreach (int pos in untaps)
                        if (pos >= cursor - 24)
                        {
                            u = pos;
                            break;
                        }
                    if (u >= 0)
                    {
                        // Reset the cursor to the untap even when that moves it backward:
                        // every following line describes something after this turn start.
                        result.Add(new KeyValuePair<int, string>(u, line));
                        cursor = u + 1;
                        continue;
                    }
                }

                var ids = CardIdRef.Matches(line);
                bool drawLine = ids.Count > 0
                    && (line.Contains("Drew card from deck") || line.Contains("Reveal and Draw"));
                if (drawLine)
                {
                    // Bounded on both sides: a few moves back because class-3 matches can
                    // nudge the cursor slightly past the true draw (an unbounded forward
                    // search would then leap to the same card's NEXT draw, games later),
                    // and far enough forward to carry across a game-boundary setup blob.
                    string id = ids[ids.Count - 1].Groups[1].Value;
                    int hit = -1;
                    int from = Math.Max(0, cursor - 24);
                    int to = Math.Min(moveIds.Count, cursor + 250);
                    for (int i = from; i < to; i++)
                        if (isDraw[i] && moveIds[i] == id)
                        {
                            hit = i;
                            break;
                        }
                    if (hit >= 0)
                    {
                        result.Add(new KeyValuePair<int, string>(hit + 1, line));
                        cursor = Math.Max(cursor, hit + 1);
                        continue;
                    }
                }

                int best = -1;
                foreach (System.Text.RegularExpressions.Match m in ids)
                {
                    string id = m.Groups[1].Value;
                    int limit = Math.Min(moveIds.Count, cursor + Window);
                    for (int i = cursor; i < limit; i++)
                        if (moveIds[i] == id)
                        {
                            if (i > best)
                                best = i;
                            break;
                        }
                }
                if (best >= 0)
                    cursor = best + 1;
                result.Add(new KeyValuePair<int, string>(cursor, line));
            }
        }

        // Segment boundaries (HDR lines) are unreliable in both directions: mid-game stream
        // resets add extra HDRs, and rematches without a reset add none. Ground truth lives in
        // the CHK checksums: a player's counts snapping back to a fresh-game signature (full
        // deck, empty hand/board/trash) after having been mid-game marks a real new game, and
        // everything else is one continuous stream. So: flatten all segments, split on
        // fresh-after-midgame checkpoints, and take names/leaders from the nearest header.
        private static List<Rz1File> SplitByCheckpoints(List<Rz1File> segments)
        {
            var games = new List<Rz1File>();
            Rz1File current = null;
            bool midGame = false;
            foreach (Rz1File seg in segments)
            {
                foreach (Rz1Event ev in seg.Events)
                {
                    bool fresh = ev.Check != null && IsFreshCheck(ev.Check);
                    if (current == null || (fresh && midGame))
                    {
                        current = new Rz1File
                        {
                            Path = seg.Path,
                            Version = seg.Version,
                            Player1 = seg.Player1,
                            Player2 = seg.Player2,
                            Leader1 = seg.Leader1,
                            Leader2 = seg.Leader2,
                        };
                        games.Add(current);
                        midGame = false;
                    }
                    if (ev.Check != null && IsMidGameCheck(ev.Check))
                        midGame = true;
                    current.Events.Add(ev);
                }
                // A later segment header carries fresher names/leaders for the game in progress
                // (a proper HDR reset mid-file); prefer it when the current game has few events.
                if (current != null && seg.Leader1 != "" && current.Events.Count == seg.Events.Count)
                {
                    current.Player1 = seg.Player1;
                    current.Player2 = seg.Player2;
                    current.Leader1 = seg.Leader1;
                    current.Leader2 = seg.Leader2;
                }
            }
            foreach (Rz1File g in games)
            {
                RecomputeTurnMarks(g);
                RecomputeInitialCounts(g);
                RecomputeLeaders(g);
            }
            games.RemoveAll(g => g.Events.Count < 10);
            return games;
        }

        // Header PLY lines can be stale for rematch games that never re-emitted a header, but
        // LogPose's tap patches publish the leader in place (zone 8 -> 8) with its card id —
        // trust the events over the header.
        private static void RecomputeLeaders(Rz1File f)
        {
            string l1 = null, l2 = null;
            foreach (Rz1Event ev in f.Events)
            {
                if (ev.Oz != 8 || ev.Dz != 8 || string.IsNullOrEmpty(ev.CardId))
                    continue;
                if (ev.Player == 1 && l1 == null) l1 = ev.CardId;
                if (ev.Player == 2 && l2 == null) l2 = ev.CardId;
                if (l1 != null && l2 != null) break;
            }
            if (l1 != null) f.Leader1 = l1;
            if (l2 != null) f.Leader2 = l2;
        }

        // Fresh-game signature: full-ish deck and nothing anywhere else yet.
        private static bool IsFreshCheck(int[] c)
        {
            return c[0] >= 40 && c[1] == 0 && c[2] == 0 && c[3] == 0
                && c[5] == 0 && c[6] == 0 && c[7] == 0 && c[9] == 0;
        }

        // Setup phases (draws, mulligans, life placement) can bounce counts back to
        // fresh-looking, so a new-game boundary only arms once actual play happened:
        // characters deployed, don in the cost area, or cards in the trash.
        private static bool IsMidGameCheck(int[] c)
        {
            return c[2] > 0 || c[5] > 0 || c[6] > 0;
        }

        private static void RecomputeInitialCounts(Rz1File f)
        {
            f.InitialCounts = new int[2][];
            foreach (Rz1Event ev in f.Events)
            {
                if (ev.Check == null)
                    continue;
                int p = (ev.CheckPlayer == 2) ? 1 : 0;
                if (f.InitialCounts[p] == null)
                    f.InitialCounts[p] = ev.Check;
                if (f.InitialCounts[0] != null && f.InitialCounts[1] != null)
                    break;
            }
        }

        private static void RecomputeTurnMarks(Rz1File f)
        {
            f.TurnMarks.Clear();
            for (int i = 0; i < f.Events.Count; i++)
            {
                Rz1Event ev = f.Events[i];
                if (ev.Oz == 8 && ev.Dz == 8 && ev.Os == 0 && ev.Ds == 0 && !ev.Tapped)
                    f.TurnMarks.Add(i);
            }
        }
    }
}
