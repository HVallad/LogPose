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
        public const string VERSION = "0.3.2";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> CfgEmitMissingReplayLines;
        internal static ConfigEntry<bool> CfgWriteCleanLog;
        internal static ConfigEntry<bool> CfgWriteReplayFile;
        internal static ConfigEntry<float> CfgLogFontSize;
        internal static ConfigEntry<KeyCode> CfgAltArtKey;
        internal static ConfigEntry<KeyCode> CfgReplayKey;
        internal static ConfigEntry<KeyCode> CfgReplayQuickKey;
        internal static ConfigEntry<bool> CfgCheckForUpdates;

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

            var harmony = new Harmony(GUID);
            harmony.PatchAll(typeof(ReplaySyncPatches));
            harmony.PatchAll(typeof(CombatLogPatches));
            harmony.PatchAll(typeof(AltArtPatches));
            harmony.PatchAll(typeof(Replay.RecorderPatches));

            UpdateCheck.Init();
            Log.LogInfo(NAME + " " + VERSION + " loaded.");
        }

        private void Update()
        {
            AltArtUI.Update();
            Replay.ReplayUI.Update();
            Replay.MatchHistoryUI.Update();
            UpdateCheck.Update();
        }

        private void OnGUI()
        {
            AltArtUI.OnGUI();
            Replay.ReplayUI.OnGUI();
        }
    }
}
