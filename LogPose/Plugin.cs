using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace LogPose
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.hunter.logpose";
        public const string NAME = "LogPose";
        public const string VERSION = "1.0.29";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        private static bool _sceneJustLoaded;

        internal static void OnScreenSwitched() => _sceneJustLoaded = true;

        internal static ConfigEntry<bool> CfgEmitMissingReplayLines;
        internal static ConfigEntry<bool> CfgWriteCleanLog;
        internal static ConfigEntry<bool> CfgWriteReplayFile;
        internal static ConfigEntry<float> CfgLogFontSize;
        internal static ConfigEntry<KeyCode> CfgAltArtKey;
        internal static ConfigEntry<KeyCode> CfgReplayKey;
        internal static ConfigEntry<KeyCode> CfgReplayQuickKey;
        internal static ConfigEntry<bool> CfgCheckForUpdates;
        internal static ConfigEntry<float> CfgTimerMinutes;
        internal static ConfigEntry<float> CfgTimerRecoverySeconds;
        internal static ConfigEntry<bool> CfgUiReskin;
        internal static ConfigEntry<string> CfgUiColorway;
        internal static ConfigEntry<string> CfgLastFormat;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            CfgEmitMissingReplayLines = Config.Bind("Replay", "EmitMissingReplayLines", true,
                "Emit RZ1 replay lines for tap/untap/refresh events the base game does not record (fixes DON!! rest states in replays).");
            CfgWriteCleanLog = Config.Bind("CombatLog", "WriteCleanLog", true,
                "Also write a cleaned .clean.log (no markup, no invisible characters, no replay lines, UTF-8) whenever the game autosaves a combat log.");
            CfgWriteReplayFile = Config.Bind("CombatLog", "WriteReplayFile", true,
                "Also write the RZ1 replay stream to a separate .rz1 file whenever the game autosaves a combat log.");
            CfgLogFontSize = Config.Bind("CombatLog", "LogFontSize", 0f,
                "Font size for combat log lines in the in-game log panel. 0 = keep the game's default.");
            CfgAltArtKey = Config.Bind("AltArt", "ToggleKey", KeyCode.F6,
                "Key that opens the Alt Art selector while in the deck editor.");
            CfgReplayKey = Config.Bind("Replay", "ViewerKey", KeyCode.F7,
                "Key that opens the replay viewer while in a Solo v Self game.");
            CfgReplayQuickKey = Config.Bind("Replay", "QuickOpenKey", KeyCode.F8,
                "Key that immediately opens the newest .rz1 replay while in a Solo v Self game. " +
                "With a replay open: Left/Right arrows step events, PageUp/PageDown jump turns, Home/End jump to start/end.");

            CfgCheckForUpdates = Config.Bind("General", "CheckForUpdates", true,
                "Check GitHub for a newer LogPose release on startup and show an update button on the main menu when one exists.");
            CfgTimerMinutes = Config.Bind("Timer", "MinutesPerPlayer", 17.5f,
                "Time bank per player for PRIVATE timed lobbies you host (the game's built-in chess clock, normally fixed at 17.5). " +
                "The host's clock is authoritative, so the opponent does not need LogPose. Also settable in-game next to the Timer Lobby checkbox.");
            CfgTimerRecoverySeconds = Config.Bind("Timer", "RecoverySeconds", 0f,
                "Seconds returned to a player's bank each time they complete a turn (Fischer-style increment) in private timed lobbies you host. 0 = off.");

            CfgUiReskin = Config.Bind("UI", "Reskin", true,
                "Replace the game's menu chrome with the LogPose 1.0 redesign. Turn off to keep the vanilla look.");
            CfgUiColorway = Config.Bind("UI", "Colorway", "Nocturne",
                "Reskin colorway: Nocturne (blurple) or Batsu (brand magenta).");
            CfgLastFormat = Config.Bind("UI", "LastMultiplayerFormat", "Western",
                "The lobby browser format the Multiplayer button opens with (remembers your last pick). " +
                "Western=Standard, Nationals=OP17, Eastern=Extra Regulation, plus Unlimited, Korean, Private.");

            // Scene loads restyle IMMEDIATELY — waiting for the next poll paints a
            // flash of vanilla UI on every transition.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) =>
            {
                _sceneJustLoaded = true;
                Log.LogInfo("Scene '" + s.name + "' loaded at t="
                    + UnityEngine.Time.realtimeSinceStartup.ToString("F3") + "s");
            };

            var harmony = new Harmony(GUID);
            harmony.PatchAll(typeof(ReplaySyncPatches));
            harmony.PatchAll(typeof(CombatLogPatches));
            harmony.PatchAll(typeof(AltArtPatches));
            harmony.PatchAll(typeof(Replay.RecorderPatches));
            harmony.PatchAll(typeof(TimerPatches));
            harmony.PatchAll(typeof(UI.BoardLayoutPatches));
            harmony.PatchAll(typeof(UI.DeckEditorUI));
            harmony.PatchAll(typeof(UI.MenuScreensUI));
            UI.MenuPerfPatches.Apply(harmony);

            // Canvas switches inside the menu scene fire no scene-load event, so hook
            // every screen-switch method — the restyle pass runs the same frame the
            // new screen appears instead of on the next poll tick.
            try
            {
                var canvasSwitch = new HarmonyMethod(typeof(Plugin), nameof(OnScreenSwitched));
                foreach (var m in typeof(HostJoinScript).GetMethods(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    if (m.DeclaringType != typeof(HostJoinScript) || m.GetParameters().Length != 0)
                        continue;
                    if (!(m.Name.StartsWith("Show") || m.Name == "SinglePlayer"
                        || m.Name == "BackToMain" || m.Name == "SoloSelf" || m.Name == "LoadMain"))
                        continue;
                    try { harmony.Patch(m, postfix: canvasSwitch); } catch { }
                }
            }
            catch (System.Exception e)
            {
                Log.LogWarning("Screen-switch hooks failed: " + e.Message);
            }

            UpdateCheck.Init();
            SafetyNet.Run();
            Log.LogInfo(NAME + " " + VERSION + " loaded.");
        }

        private void Update()
        {
            AltArtUI.Update();
            Replay.ReplayUI.Update();
            Replay.MatchHistoryUI.Update();
            UpdateCheck.Update();
            TimerLobbyUI.Update();
            TimerPatches.SyncUpdate();
            bool sceneLoaded = _sceneJustLoaded;
            _sceneJustLoaded = false;
            long forceT0 = sceneLoaded ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            UI.MainMenuUI.Update(sceneLoaded);
            UI.VanillaRestyle.Update(sceneLoaded);
            UI.BoardHUD.Update();
            UI.DeckEditorUI.Update(sceneLoaded);
            UI.MenuScreensUI.Update(sceneLoaded);
            if (sceneLoaded)
            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - forceT0) * 1000.0
                    / System.Diagnostics.Stopwatch.Frequency;
                Log.LogInfo("Forced restyle pass: " + ms.ToString("F0") + " ms (t="
                    + UnityEngine.Time.realtimeSinceStartup.ToString("F3") + "s)");
            }
            UI.DevDump.Update();
        }

        private void OnGUI()
        {
            Replay.ReplayUI.OnGUI();
        }
    }
}
