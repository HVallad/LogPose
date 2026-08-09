using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;

namespace LogPose
{
    // Downloads official parallel arts for a specific set of cards from inside the game —
    // the per-deck, on-demand version of tools\Fetch-AltArts.ps1. The EN card site is probed
    // first, the JP site as fallback (except P promos, whose numbering differs between
    // regions). Downloads run on a worker thread; thumbnails need Texture2D and are queued
    // back to the main thread.
    internal static class AltArtFetcher
    {
        private const string EnBase = "https://en.onepiece-cardgame.com/images/cardlist/card/";
        private const string JpBase = "https://www.onepiece-cardgame.com/images/cardlist/card/";
        private const string ManifestUrl = "https://raw.githubusercontent.com/HVallad/LogPose/main/variant-manifest.txt";
        private const int MaxVariants = 9;
        private const int Workers = 4;

        internal static volatile bool Running;
        internal static volatile string Status = "";
        private static volatile bool _finished;
        private static int _added;

        // cardID -> (suffix, jp-only) for every variant known to exist, published in the
        // repo. With it, fetching requests exactly the files that exist instead of probing
        // slot by slot — most of the old fetch time was 404s for cards with no parallels.
        private static Dictionary<string, List<KeyValuePair<string, bool>>> _manifest;
        private static bool _manifestTried;

        private static readonly ConcurrentQueue<KeyValuePair<string, string>> ThumbJobs =
            new ConcurrentQueue<KeyValuePair<string, string>>();
        private static readonly object ManifestLock = new object();

        private class Job
        {
            public string CardId;
            public string Folder;
            public bool JpAllowed;
        }

        internal static int Added { get { return _added; } }

        internal static bool ConsumeFinished()
        {
            if (!_finished)
                return false;
            _finished = false;
            return true;
        }

        // Resolve set folders on the main thread (uses the game's card database), then hand
        // the whole work list to a background thread.
        internal static void StartFetch(List<string> cardIds)
        {
            if (Running)
                return;
            var jobs = new List<Job>();
            foreach (string id in cardIds)
            {
                string rel = AltArtManager.FindImageFolderRelative(id);
                if (rel == null)
                    continue;
                string folder = Path.Combine(Application.streamingAssetsPath, rel);
                if (!Directory.Exists(folder))
                    continue;
                string setName = Path.GetFileName(rel);
                jobs.Add(new Job
                {
                    CardId = id,
                    Folder = folder,
                    JpAllowed = !string.Equals(setName, "P", StringComparison.OrdinalIgnoreCase),
                });
            }
            if (jobs.Count == 0)
                return;
            Running = true;
            _added = 0;
            Status = "Fetching...";
            string manifest = Path.Combine(Application.streamingAssetsPath, Path.Combine("Cards", "jp-variants.txt"));
            ThreadPool.QueueUserWorkItem(_ => Work(jobs, manifest));
        }

        private static void Work(List<Job> jobs, string manifest)
        {
            int start = Environment.TickCount;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            LoadManifest();

            int next = -1;
            int done = 0;
            int active = Math.Min(Workers, jobs.Count);
            for (int w = 0; w < Math.Min(Workers, jobs.Count); w++)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    while (true)
                    {
                        int i = Interlocked.Increment(ref next);
                        if (i >= jobs.Count)
                            break;
                        try
                        {
                            ProcessCard(jobs[i], manifest);
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogWarning("Alt art fetch failed for " + jobs[i].CardId + ": " + e.Message);
                        }
                        Status = "Fetching " + Interlocked.Increment(ref done) + "/" + jobs.Count;
                    }
                    if (Interlocked.Decrement(ref active) == 0)
                    {
                        Plugin.Log.LogInfo("Alt art fetch finished: " + _added + " new art(s) in "
                            + ((Environment.TickCount - start) / 1000f).ToString("0.0") + "s.");
                        Status = "";
                        Running = false;
                        _finished = true;
                    }
                });
            }
        }

        private static void LoadManifest()
        {
            if (_manifestTried)
                return;
            _manifestTried = true;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ManifestUrl);
                req.UserAgent = "LogPose/" + Plugin.VERSION;
                req.Timeout = 10000;
                var dict = new Dictionary<string, List<KeyValuePair<string, bool>>>(StringComparer.OrdinalIgnoreCase);
                int count = 0;
                using (WebResponse resp = req.GetResponse())
                using (StreamReader r = new StreamReader(resp.GetResponseStream()))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                            continue;
                        string[] parts = line.Split(' ');
                        string name = parts[0];
                        bool jp = parts.Length > 1 && string.Equals(parts[1], "jp", StringComparison.OrdinalIgnoreCase);
                        int idx = name.LastIndexOf("_p", StringComparison.OrdinalIgnoreCase);
                        if (idx <= 0)
                            continue;
                        string cardId = name.Substring(0, idx);
                        string suffix = name.Substring(idx);
                        List<KeyValuePair<string, bool>> list;
                        if (!dict.TryGetValue(cardId, out list))
                            dict[cardId] = list = new List<KeyValuePair<string, bool>>();
                        list.Add(new KeyValuePair<string, bool>(suffix, jp));
                        count++;
                    }
                }
                _manifest = dict;
                Plugin.Log.LogInfo("AltArt: variant manifest loaded (" + count + " entries).");
            }
            catch (Exception e)
            {
                _manifest = null;   // offline or repo unreachable — probing still works
                Plugin.Log.LogDebug("AltArt: manifest unavailable, probing instead: " + e.Message);
            }
        }

        private static void ProcessCard(Job job, string manifest)
        {
            List<KeyValuePair<string, bool>> known;
            if (_manifest != null && _manifest.TryGetValue(job.CardId, out known))
            {
                // Known inventory: request exactly the files that exist, from the right site.
                foreach (KeyValuePair<string, bool> kv in known)
                {
                    string name = job.CardId + kv.Key;
                    string png = Path.Combine(job.Folder, name + ".png");
                    string thumb = Path.Combine(job.Folder, name + "_small.jpg");
                    if (File.Exists(png))
                    {
                        if (!File.Exists(thumb))
                            ThumbJobs.Enqueue(new KeyValuePair<string, string>(png, thumb));
                        continue;
                    }
                    bool jp = kv.Value;
                    byte[] data = TryDownload((jp ? JpBase : EnBase) + name + ".png");
                    if (data == null && !jp && job.JpAllowed)
                    {
                        data = TryDownload(JpBase + name + ".png");
                        jp = data != null;
                    }
                    if (data == null)
                        continue;
                    File.WriteAllBytes(png, data);
                    ThumbJobs.Enqueue(new KeyValuePair<string, string>(png, thumb));
                    if (jp)
                        TagJapanese(manifest, name);
                    Interlocked.Increment(ref _added);
                }
                return;
            }

            // Not in the manifest (newer card, or the manifest didn't load): probe slot by
            // slot with the classic 2-miss cutoff.
            int missStreak = 0;
            for (int n = 1; n <= MaxVariants; n++)
            {
                string name = job.CardId + "_p" + n;
                string png = Path.Combine(job.Folder, name + ".png");
                string thumb = Path.Combine(job.Folder, name + "_small.jpg");
                if (File.Exists(png))
                {
                    missStreak = 0;
                    if (!File.Exists(thumb))
                        ThumbJobs.Enqueue(new KeyValuePair<string, string>(png, thumb));
                    continue;
                }
                byte[] data = TryDownload(EnBase + name + ".png");
                bool jp = false;
                if (data == null && job.JpAllowed)
                {
                    data = TryDownload(JpBase + name + ".png");
                    jp = data != null;
                }
                if (data == null)
                {
                    missStreak++;
                    if (missStreak >= 2)
                        break;
                    continue;
                }
                missStreak = 0;
                File.WriteAllBytes(png, data);
                ThumbJobs.Enqueue(new KeyValuePair<string, string>(png, thumb));
                if (jp)
                    TagJapanese(manifest, name);
                Interlocked.Increment(ref _added);
                Thread.Sleep(40);   // probing stays polite
            }
        }

        private static byte[] TryDownload(string url)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "Mozilla/5.0 (LogPose alt-art fetcher; personal use)";
                req.Timeout = 10000;
                using (WebResponse resp = req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    byte[] b = ms.ToArray();
                    // PNG magic check — misses sometimes come back as HTML pages, not 404s.
                    if (b.Length < 100 || b[0] != 0x89 || b[1] != 0x50)
                        return null;
                    return b;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void TagJapanese(string manifest, string name)
        {
            try
            {
                lock (ManifestLock)
                {
                    var lines = new List<string>();
                    if (File.Exists(manifest))
                        lines.AddRange(File.ReadAllLines(manifest));
                    foreach (string l in lines)
                        if (string.Equals(l.Trim(), name, StringComparison.OrdinalIgnoreCase))
                            return;
                    lines.Add(name);
                    File.WriteAllLines(manifest, lines.ToArray());
                }
            }
            catch { }
        }

        // Texture2D work is main-thread only; AltArtUI calls this every frame. A couple of
        // thumbnails per frame keeps the editor responsive while a fetch streams results in.
        internal static void MainThreadPump()
        {
            for (int i = 0; i < 2; i++)
            {
                KeyValuePair<string, string> job;
                if (!ThumbJobs.TryDequeue(out job))
                    return;
                try
                {
                    MakeThumb(job.Key, job.Value);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Thumbnail failed for " + job.Key + ": " + e.Message);
                }
            }
        }

        private static void MakeThumb(string pngPath, string thumbPath)
        {
            byte[] bytes = File.ReadAllBytes(pngPath);
            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(bytes))
                return;
            var small = new Texture2D(120, 167, TextureFormat.RGB24, false);
            for (int y = 0; y < 167; y++)
                for (int x = 0; x < 120; x++)
                    small.SetPixel(x, y, tex.GetPixelBilinear((x + 0.5f) / 120f, (y + 0.5f) / 167f));
            small.Apply();
            File.WriteAllBytes(thumbPath, small.EncodeToJPG(85));
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(small);
        }
    }
}
