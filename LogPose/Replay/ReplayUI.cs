using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LogPose.Replay
{
    // F7 panel. Open it from a Solo v Self game: pick an .rz1, scrub with the transport
    // controls, and the real board re-renders each position through ReplayBridge.
    internal static class ReplayUI
    {
        private static bool _visible;
        private static Vector2 _scroll;
        private static Rect _windowRect = new Rect(60f, 60f, 470f, 420f);
        private static string[] _files = new string[0];
        private static List<Rz1File> _pendingGames;
        private static ReplaySession _session;
        private static int _pos;
        private static bool _revealAll;
        private static bool _autoPlay;
        private static float _autoSpeed = 4f; // events per second
        private static float _autoAccum;
        private static string _status = "";

        internal static void Update()
        {
            if (_pendingOpen != null)
            {
                GameplayLogicScript pendingBoard = ReplayBridge.FindBoard();
                if (pendingBoard != null && ReplayBridge.IsSoloBoard(pendingBoard) && --_pendingDelay <= 0)
                {
                    Rz1File game = _pendingOpen;
                    _pendingOpen = null;
                    OpenGame(game);
                    MatchHistoryUI.HideLoadingCover();
                }
            }
            // Leaving the board (Back to Main, Cancel Match) must tear the replay UI down too.
            if (_session != null && Time.frameCount % 15 == 0)
            {
                HostJoinScript hjs = UnityEngine.Object.FindFirstObjectByType<HostJoinScript>();
                if (hjs != null && hjs.go_SoloVSelf != null && hjs.go_SoloVSelf.activeSelf)
                    ExitReplay();
            }
            if (Input.GetKeyDown(Plugin.CfgReplayKey.Value))
            {
                _visible = !_visible;
                if (_visible)
                    RefreshFileList();
            }
            // Quick-open: load the newest replay immediately.
            if (Input.GetKeyDown(Plugin.CfgReplayQuickKey.Value))
            {
                GameplayLogicScript board = ReplayBridge.FindBoard();
                if (board != null && ReplayBridge.IsSoloBoard(board))
                {
                    RefreshFileList();
                    if (_files.Length > 0)
                    {
                        try
                        {
                            List<Rz1File> games = Rz1Parser.ParseGames(_files[0]);
                            if (games.Count > 0)
                            {
                                // Quick-open jumps straight into the last game in the file
                                // (the most recent match).
                                OpenGame(games[games.Count - 1]);
                                _visible = true;
                                Plugin.Log.LogInfo("Replay: quick-opened " + _files[0] +
                                    " (game " + games.Count + "/" + games.Count + ")");
                            }
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogWarning("Replay quick-open failed: " + e);
                        }
                    }
                }
            }
            // Keyboard transport while a replay is open.
            if (_session != null)
            {
                if (Input.GetKeyDown(KeyCode.RightArrow)) Seek(_pos + 1);
                if (Input.GetKeyDown(KeyCode.LeftArrow)) Seek(_pos - 1);
                if (Input.GetKeyDown(KeyCode.DownArrow)) JumpAction(1);
                if (Input.GetKeyDown(KeyCode.UpArrow)) JumpAction(-1);
                if (Input.GetKeyDown(KeyCode.PageDown)) Seek(_session.NextTurnMark(_pos));
                if (Input.GetKeyDown(KeyCode.PageUp)) Seek(_session.PrevTurnMark(_pos));
                if (Input.GetKeyDown(KeyCode.End)) Seek(_session.EventCount);
                if (Input.GetKeyDown(KeyCode.Home)) Seek(0);
            }
            if (_autoPlay && _session != null)
            {
                _autoAccum += Time.deltaTime * _autoSpeed;
                int steps = (int)_autoAccum;
                if (steps > 0)
                {
                    _autoAccum -= steps;
                    Seek(_pos + steps);
                    if (_pos >= _session.EventCount)
                        _autoPlay = false;
                }
            }
        }

        internal static void OnGUI()
        {
            if (!_visible)
                return;
            _windowRect = GUILayout.Window(0x10905E2, _windowRect, DrawWindow, "LogPose Replay");
        }

        private static void RefreshFileList()
        {
            try
            {
                string dir = Path.Combine("CombatLogs", "AutoSaved");
                _files = Directory.Exists(dir)
                    ? Directory.GetFiles(dir, "*.rz1").OrderByDescending(f => File.GetLastWriteTime(f)).Take(15).ToArray()
                    : new string[0];
            }
            catch
            {
                _files = new string[0];
            }
        }

        private static void OpenGame(Rz1File game)
        {
            _session = new ReplaySession(game);
            _session.BuildDeckActivityLines(id =>
            {
                CardDefinition def = CardDatabaseScript.Instance != null
                    ? CardDatabaseScript.Instance.FindDefinition(id)
                    : null;
                return (def != null && !string.IsNullOrEmpty(def.characterName))
                    ? def.characterName + " [" + id + "]"
                    : id;
            });
            _pos = 0;
            _autoPlay = false;
            ReplayBridge.ResetLiveCards();
            ReplayLogView.ResetForNewSession();
            GameplayLogicScript board = ReplayBridge.FindBoard();
            if (board != null)
                NativeReplayPanel.Show(board);
            _visible = false; // native panel takes over; F7 reopens the picker window
            Seek(0);
        }

        private static void Seek(int target)
        {
            if (_session == null)
                return;
            int prevPos = _pos;
            _pos = Mathf.Clamp(target, 0, _session.EventCount);
            _session.SeekTo(_pos);
            GameplayLogicScript board = ReplayBridge.FindBoard();
            if (board != null)
            {
                ReplayBridge.Apply(board, _session, _revealAll);
                ReplayLogView.Sync(board, _session, _pos);
                RevealRow.Sync(board, _session, _pos, prevPos);
            }
            NativeReplayPanel.Refresh(_session, _pos, _autoPlay);
        }

        // Transport entry points shared by the native panel and the keyboard.
        internal static void SeekTo(int pos) { Seek(pos); }
        internal static void SeekToEnd() { if (_session != null) Seek(_session.EventCount); }
        internal static void StepBy(int n) { Seek(_pos + n); }
        internal static void JumpTurn(int dir)
        {
            if (_session == null)
                return;
            Seek(dir > 0 ? _session.NextTurnMark(_pos) : _session.PrevTurnMark(_pos));
        }
        internal static void JumpAction(int dir)
        {
            if (_session == null)
                return;
            // Recordings without a sibling .log have no action marks — step coarsely instead
            // of leaping to the ends.
            if (_session.ActionMarks.Count == 0)
            {
                Seek(_pos + dir * 10);
                return;
            }
            Seek(dir > 0 ? _session.NextActionMark(_pos) : _session.PrevActionMark(_pos));
        }
        internal static void TogglePlay()
        {
            _autoPlay = !_autoPlay;
            _autoAccum = 0f;
            NativeReplayPanel.Refresh(_session, _pos, _autoPlay);
        }
        internal static void ChangeSpeed(float delta)
        {
            _autoSpeed = Mathf.Clamp(_autoSpeed + delta, 1f, 20f);
        }
        private static Rz1File _pendingOpen;
        private static int _pendingDelay;

        // Called by the match-history page: open this game once a solo board exists.
        internal static void QueuePendingOpen(Rz1File game)
        {
            _pendingOpen = game;
            _pendingDelay = 45;
        }

        internal static void OpenExternal(Rz1File game)
        {
            OpenGame(game);
        }

        internal static void ExitReplay()
        {
            _session = null;
            _autoPlay = false;
            NativeReplayPanel.Hide();
            RevealRow.Clear();
            _visible = false;
        }

        private static void DrawWindow(int id)
        {
            GameplayLogicScript board = ReplayBridge.FindBoard();
            if (board == null)
            {
                GUILayout.Label("No game board active.\nStart a Solo v Self game first, then open a replay here.");
                if (GUILayout.Button("Close"))
                    _visible = false;
                GUI.DragWindow();
                return;
            }
            if (!ReplayBridge.IsSoloBoard(board))
            {
                GUILayout.Label("Replay viewing is only available in Solo v Self\n(not during a live multiplayer match).");
                if (GUILayout.Button("Close"))
                    _visible = false;
                GUI.DragWindow();
                return;
            }

            if (_session == null && _pendingGames != null)
            {
                GUILayout.Label("This log holds " + _pendingGames.Count + " games — pick one:");
                for (int i = 0; i < _pendingGames.Count; i++)
                {
                    Rz1File g = _pendingGames[i];
                    if (GUILayout.Button("Game " + (i + 1) + ": " + g.Player1 + " vs " + g.Player2 +
                        "  (" + g.Events.Count + " events)"))
                    {
                        OpenGame(g);
                        _pendingGames = null;
                    }
                }
                if (GUILayout.Button("Back"))
                    _pendingGames = null;
                GUI.DragWindow();
                return;
            }

            if (_session == null)
            {
                GUILayout.Label("Pick a replay (.rz1) file:");
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(260f));
                foreach (string f in _files)
                {
                    if (GUILayout.Button(Path.GetFileNameWithoutExtension(f)))
                    {
                        try
                        {
                            List<Rz1File> games = Rz1Parser.ParseGames(f);
                            if (games.Count == 0)
                                _status = "No replay events in that file.";
                            else if (games.Count == 1)
                            {
                                OpenGame(games[0]);
                                _status = "";
                            }
                            else
                            {
                                _pendingGames = games;
                                _status = "";
                            }
                        }
                        catch (Exception e)
                        {
                            _status = "Failed to load: " + e.Message;
                        }
                    }
                }
                GUILayout.EndScrollView();
                if (GUILayout.Button("Refresh list"))
                    RefreshFileList();
                if (!string.IsNullOrEmpty(_status))
                    GUILayout.Label(_status);
                if (GUILayout.Button("Close"))
                    _visible = false;
                GUI.DragWindow();
                return;
            }

            Rz1File rf = _session.File;
            GUILayout.Label(rf.Player1 + " (" + rf.Leader1 + ")  vs  " + rf.Player2 + " (" + rf.Leader2 + ")");
            GUILayout.Label("Accuracy: " + _session.ValidationSummary);
            GUILayout.Label("Event " + _pos + " / " + _session.EventCount +
                "   Turn " + _session.TurnAt(_pos) + " / " + (rf.TurnMarks.Count + 1));

            int slider = (int)GUILayout.HorizontalSlider(_pos, 0f, _session.EventCount);
            if (slider != _pos)
                Seek(slider);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("|<")) Seek(0);
            if (GUILayout.Button("< Turn")) Seek(_session.PrevTurnMark(_pos));
            if (GUILayout.Button("< 1")) Seek(_pos - 1);
            if (GUILayout.Button("1 >")) Seek(_pos + 1);
            if (GUILayout.Button("Turn >")) Seek(_session.NextTurnMark(_pos));
            if (GUILayout.Button(">|")) Seek(_session.EventCount);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_autoPlay ? "Pause" : "Play"))
            {
                _autoPlay = !_autoPlay;
                _autoAccum = 0f;
            }
            GUILayout.Label("Speed", GUILayout.Width(45f));
            _autoSpeed = GUILayout.HorizontalSlider(_autoSpeed, 1f, 20f, GUILayout.Width(110f));
            GUILayout.Label(_autoSpeed.ToString("0") + "/s", GUILayout.Width(35f));
            bool reveal = GUILayout.Toggle(_revealAll, "Reveal hidden");
            if (reveal != _revealAll)
            {
                _revealAll = reveal;
                Seek(_pos);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Different replay"))
            {
                _session = null;
                _autoPlay = false;
                RefreshFileList();
            }
            if (GUILayout.Button("Close"))
                _visible = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("Tip: don't interact with the board while replaying —\nrestart Solo v Self to return to normal play.");
            GUI.DragWindow();
        }
    }
}
