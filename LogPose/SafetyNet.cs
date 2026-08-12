using System;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx;

namespace LogPose
{
    // Game updates replace the install folder and preserve only *.deck files — the
    // 2026-08-11 update silently discarded alt-art sidecars, the mod config and every
    // combat-log recording. On each boot, mirror the small irreplaceable files to
    // Documents\LogPose\backup, which no game updater touches.
    internal static class SafetyNet
    {
        private const int MaxLogFiles = 200;   // newest recordings; a full pair is ~1 MB

        internal static void Run()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string root = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LogPose", "backup");
                    int copied = 0;
                    copied += Mirror(Path.Combine(Paths.GameRootPath, "Decks"),
                        Path.Combine(root, "Decks"), "*.deck", int.MaxValue);
                    copied += Mirror(Path.Combine(Paths.GameRootPath, "Decks"),
                        Path.Combine(root, "Decks"), "*.arts.json", int.MaxValue);
                    copied += Mirror(Path.Combine(Paths.ConfigPath),
                        Path.Combine(root, "config"), "*.cfg", int.MaxValue);
                    copied += Mirror(Path.Combine(Paths.GameRootPath, "CustomArts"),
                        Path.Combine(root, "CustomArts"), "*.*", int.MaxValue);
                    copied += Mirror(Path.Combine(Paths.GameRootPath, "CombatLogs", "AutoSaved"),
                        Path.Combine(root, "CombatLogs"), "*.*", MaxLogFiles);
                    if (copied > 0)
                        Plugin.Log.LogInfo("SafetyNet: backed up " + copied + " changed file(s) to Documents\\LogPose\\backup.");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogDebug("SafetyNet skipped: " + e.Message);
                }
            });
        }

        // Copy new/changed files (by write time + size); returns number copied.
        private static int Mirror(string srcDir, string dstDir, string pattern, int maxNewest)
        {
            if (!Directory.Exists(srcDir))
                return 0;
            Directory.CreateDirectory(dstDir);
            int copied = 0;
            var files = new DirectoryInfo(srcDir).GetFiles(pattern)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(maxNewest);
            foreach (FileInfo src in files)
            {
                string dst = Path.Combine(dstDir, src.Name);
                FileInfo old = new FileInfo(dst);
                if (old.Exists && old.LastWriteTimeUtc == src.LastWriteTimeUtc && old.Length == src.Length)
                    continue;
                src.CopyTo(dst, true);
                copied++;
            }
            return copied;
        }
    }
}
