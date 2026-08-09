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
        private const int MaxVariants = 9;

        internal static volatile bool Running;
        internal static volatile string Status = "";
        private static volatile bool _finished;
        private static int _added;

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
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                for (int i = 0; i < jobs.Count; i++)
                {
                    Job job = jobs[i];
                    Status = "Fetching " + (i + 1) + "/" + jobs.Count;
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
                        Thread.Sleep(40);   // stay polite to the official site
                    }
                }
                Plugin.Log.LogInfo("Alt art fetch finished: " + _added + " new art(s).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Alt art fetch failed: " + e.Message);
            }
            finally
            {
                Status = "";
                Running = false;
                _finished = true;
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
