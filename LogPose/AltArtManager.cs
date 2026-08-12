using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace LogPose
{
    // Per-deck alternate art selection. Variant images live next to the base art in
    // StreamingAssets\Cards\<SET>\ as <CardID>_alt1.png / <CardID>_p1.png (official parallel-art
    // naming), optionally with a matching <CardID>_p1_small.jpg thumbnail. Choices are stored in
    // a sidecar next to the deck: Decks\<name>.deck.arts.json — the .deck file itself is never
    // touched, so decks stay fully compatible with unmodded clients.
    internal static class AltArtManager
    {
        internal static Dictionary<string, string> ActiveMap = new Dictionary<string, string>();
        internal static string ActiveSidecarPath;

        // While set, TryGetVariantSprite declines so the game's own loader serves the BASE
        // art — used by the preview patches to fetch the English card behind a JP variant.
        internal static bool BypassVariant;

        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, List<string>> VariantCache = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, List<string>> CustomCache = new Dictionary<string, List<string>>();

        // User-supplied art lives at the game root in CustomArts\ — any png/jpg whose
        // name starts with a card ID belongs to that card. Sidecar values use the
        // "custom:<filename>" namespace so they flow through the same per-deck save,
        // solo merge and replay pipeline as official parallels. SafetyNet mirrors the
        // folder because game updates wipe root folders.
        internal static string CustomArtsDir
        {
            get { return Path.Combine(BepInEx.Paths.GameRootPath, "CustomArts"); }
        }

        internal static List<string> GetCustomArts(string cardID)
        {
            List<string> cached;
            if (CustomCache.TryGetValue(cardID, out cached))
                return cached;
            var result = new List<string>();
            try
            {
                if (Directory.Exists(CustomArtsDir))
                    foreach (string file in Directory.GetFiles(CustomArtsDir))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
                            continue;
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (!name.StartsWith(cardID, StringComparison.OrdinalIgnoreCase))
                            continue;
                        // Guard against a longer ID sharing the prefix.
                        if (name.Length > cardID.Length && char.IsDigit(name[cardID.Length]))
                            continue;
                        result.Add("custom:" + name);
                    }
            }
            catch { }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            CustomCache[cardID] = result;
            return result;
        }

        // Copy a picked image into CustomArts named <cardID>_<original>. Returns the
        // new suffix, or null on failure.
        internal static string AddCustomArt(string cardID, string sourcePath)
        {
            try
            {
                Directory.CreateDirectory(CustomArtsDir);
                string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
                if (ext == ".jpeg")
                    ext = ".jpg";
                if (ext != ".png" && ext != ".jpg")
                    return null;
                string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                foreach (char c in Path.GetInvalidFileNameChars())
                    baseName = baseName.Replace(c.ToString(), "");
                if (!baseName.StartsWith(cardID, StringComparison.OrdinalIgnoreCase))
                    baseName = cardID + "_" + baseName;
                string target = Path.Combine(CustomArtsDir, baseName + ext);
                int n = 2;
                while (File.Exists(target))
                    target = Path.Combine(CustomArtsDir, baseName + "_" + (n++) + ext);
                File.Copy(sourcePath, target);
                CustomCache.Remove(cardID);
                Plugin.Log.LogInfo("AltArt: custom art added: " + Path.GetFileName(target));
                return "custom:" + Path.GetFileNameWithoutExtension(target);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("AltArt: custom add failed: " + e.Message);
                return null;
            }
        }

        // Replays reconstruct a recorded match on a programmatic solo board, so no single
        // deck sidecar applies. Use the union of every saved deck's picks instead — if the
        // user chose an art for a card anywhere, their replays show it. ActiveSidecarPath is
        // cleared so nothing can accidentally save this merged view over a real sidecar;
        // the next deck load (editor or match) replaces the map with the proper one.
        internal static void LoadMergedForReplay()
        {
            var merged = new Dictionary<string, string>();
            try
            {
                if (Directory.Exists("Decks"))
                    foreach (string f in Directory.GetFiles("Decks", "*.arts.json"))
                    {
                        try
                        {
                            var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(f));
                            if (map == null)
                                continue;
                            foreach (var kv in map)
                                if (!string.IsNullOrEmpty(kv.Value))
                                    merged[kv.Key] = kv.Value;
                        }
                        catch { }
                    }
            }
            catch { }
            ActiveMap = merged;
            ActiveSidecarPath = null;
            Plugin.Log.LogInfo("AltArt: merged " + merged.Count + " art choices for replay.");
        }

        // A match shows two decks at once, so gameplay merges each deck's sidecar into
        // the map instead of replacing it — the enemy deck's (usually empty) sidecar
        // used to wipe the player's picks. Existing entries win: the player's deck
        // loads first in solo, so their choice takes priority when both decks picked
        // an art for the same card. The composite has no owning sidecar, so the path
        // is cleared to keep it from ever being saved over a real one.
        internal static void ResetForMatch()
        {
            ActiveMap = new Dictionary<string, string>();
            ActiveSidecarPath = null;
        }

        internal static void MergeSidecar(string deckFile)
        {
            string path = SidecarPathFor(deckFile);
            ActiveSidecarPath = null;
            try
            {
                if (File.Exists(path))
                {
                    var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                    if (map != null)
                        foreach (var kv in map)
                            if (!string.IsNullOrEmpty(kv.Value) && !ActiveMap.ContainsKey(kv.Key))
                                ActiveMap[kv.Key] = kv.Value;
                }
                Plugin.Log.LogInfo("AltArt: merged sidecar " + path + " (map now " + ActiveMap.Count + " choices)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("AltArt: failed to merge sidecar " + path + ": " + e.Message);
            }
        }

        // ------------------------------------------------------------- multi-DON!! ----
        // The ten DON!! cards can each carry their own art: the sidecar's "__dons" key
        // holds a |-joined suffix list ('' = base art), applied by index to face-up
        // DON!! cards on the board. The plain "Don" key mirrors the first pick so the
        // single-art choke path (and older versions reading the sidecar) stay coherent.
        internal const string DonListKey = "__dons";

        internal static List<string> GetDonList()
        {
            string raw;
            if (ActiveMap.TryGetValue(DonListKey, out raw) && !string.IsNullOrEmpty(raw))
                return new List<string>(raw.Split('|'));
            string single;
            if (ActiveMap.TryGetValue("Don", out single) && !string.IsNullOrEmpty(single))
                return new List<string> { single };
            return new List<string>();
        }

        internal static void SetDonList(List<string> list)
        {
            int last = -1;
            for (int i = 0; i < list.Count; i++)
                if (!string.IsNullOrEmpty(list[i]))
                    last = i;
            if (last < 0)
            {
                ActiveMap.Remove(DonListKey);
                ActiveMap.Remove("Don");
            }
            else
            {
                List<string> trimmed = list.GetRange(0, last + 1);
                ActiveMap[DonListKey] = string.Join("|", trimmed.ToArray());
                string first = trimmed.Find(s => !string.IsNullOrEmpty(s));
                if (first != null)
                    ActiveMap["Don"] = first;
                else
                    ActiveMap.Remove("Don");
            }
            SaveSidecar();
        }

        internal static bool HasActiveVariant(string cardID)
        {
            string suffix;
            return !string.IsNullOrEmpty(cardID)
                && ActiveMap.TryGetValue(cardID, out suffix)
                && !string.IsNullOrEmpty(suffix);
        }

        internal static string SidecarPathFor(string deckFile)
        {
            return deckFile + ".arts.json";
        }

        internal static void LoadSidecar(string deckFile)
        {
            ActiveSidecarPath = SidecarPathFor(deckFile);
            ActiveMap = new Dictionary<string, string>();
            try
            {
                if (File.Exists(ActiveSidecarPath))
                {
                    var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(ActiveSidecarPath));
                    if (map != null)
                        ActiveMap = map;
                }
                Plugin.Log.LogInfo("AltArt: active sidecar " + ActiveSidecarPath + " (" + ActiveMap.Count + " choices)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("AltArt: failed to read sidecar " + ActiveSidecarPath + ": " + e.Message);
            }
        }

        internal static void SaveSidecar(string deckFile = null)
        {
            string path = deckFile != null ? SidecarPathFor(deckFile) : ActiveSidecarPath;
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                var pruned = ActiveMap.Where(kv => !string.IsNullOrEmpty(kv.Value))
                                      .ToDictionary(kv => kv.Key, kv => kv.Value);
                if (pruned.Count == 0)
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                else
                {
                    File.WriteAllText(path, JsonConvert.SerializeObject(pruned, Formatting.Indented));
                }
                ActiveSidecarPath = path;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("AltArt: failed to write sidecar " + path + ": " + e.Message);
            }
        }

        // Resolve the set folder for a card the same way CardDatabaseScript does.
        internal static string FindImageFolderRelative(string cardID)
        {
            CardDatabaseScript db = CardDatabaseScript.Instance;
            if (db == null)
                return null;
            foreach (var kv in db.dict_Sets)
            {
                if (kv.Value != null && kv.Value.cards != null && kv.Value.cards.Contains(cardID))
                    return Path.Combine("Cards", kv.Value.setName);
            }
            return null;
        }

        internal static List<string> GetVariants(string cardID)
        {
            List<string> cached;
            if (VariantCache.TryGetValue(cardID, out cached))
                return cached;

            var result = new List<string>();
            string rel = FindImageFolderRelative(cardID);
            if (rel != null)
            {
                string folder = Path.Combine(Application.streamingAssetsPath, rel);
                if (Directory.Exists(folder))
                {
                    foreach (string file in Directory.GetFiles(folder, cardID + "_*.*"))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext != ".png" && ext != ".jpg")
                            continue;
                        string suffix = Path.GetFileNameWithoutExtension(file).Substring(cardID.Length);
                        if (suffix.EndsWith("_small", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (suffix.IndexOf("OVERRIDE", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        bool known = suffix.StartsWith("_alt", StringComparison.OrdinalIgnoreCase)
                                  || suffix.StartsWith("_p", StringComparison.OrdinalIgnoreCase);
                        if (known && !result.Contains(suffix))
                            result.Add(suffix);
                    }
                }
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            VariantCache[cardID] = result;
            return result;
        }

        internal static void InvalidateVariantCache()
        {
            VariantCache.Clear();
        }

        internal static bool TryGetVariantSprite(string cardID, SpriteState state, out Sprite sprite)
        {
            sprite = null;
            if (BypassVariant)
                return false;
            string suffix;
            if (string.IsNullOrEmpty(cardID) || !ActiveMap.TryGetValue(cardID, out suffix) || string.IsNullOrEmpty(suffix))
                return false;
            sprite = LoadVariantSprite(cardID, suffix, state);
            return sprite != null;
        }

        // Art for any (card, suffix) pair regardless of what the active deck selected — the
        // selector UI uses this to draw every option. Empty suffix = the game's own base art.
        internal static Sprite GetArtSprite(string cardID, string suffix, SpriteState state)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                if (CardDatabaseScript.Instance == null)
                    return null;
                BypassVariant = true;
                try { return CardDatabaseScript.Instance.GetCardImage(cardID, state); }
                finally { BypassVariant = false; }
            }
            return LoadVariantSprite(cardID, suffix, state);
        }

        private static Sprite LoadVariantSprite(string cardID, string suffix, SpriteState state)
        {
            string path = null;
            if (suffix.StartsWith("custom:", StringComparison.Ordinal))
            {
                // Customs have no separate thumbnail — the full image serves both.
                string baseName = Path.Combine(CustomArtsDir, suffix.Substring(7));
                if (File.Exists(baseName + ".png"))
                    path = baseName + ".png";
                else if (File.Exists(baseName + ".jpg"))
                    path = baseName + ".jpg";
                else if (File.Exists(baseName + ".jpeg"))
                    path = baseName + ".jpeg";
            }
            else
            {
                string rel = FindImageFolderRelative(cardID);
                if (rel == null)
                    return null;
                string basePath = Path.Combine(Path.Combine(Application.streamingAssetsPath, rel), cardID + suffix);
                if (state == SpriteState.Thumbnail && File.Exists(basePath + "_small.jpg"))
                    path = basePath + "_small.jpg";
                else if (File.Exists(basePath + ".png"))
                    path = basePath + ".png";
                else if (File.Exists(basePath + ".jpg"))
                    path = basePath + ".jpg";
            }
            if (path == null)
                return null;

            string key = path + "|" + state;
            Sprite sprite;
            if (SpriteCache.TryGetValue(key, out sprite) && sprite != null)
                return sprite;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(bytes))
                    return null;
                sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(tex.width / 2f, tex.height / 2f), 100f, 0u, SpriteMeshType.FullRect);
                SpriteCache[key] = sprite;
                return sprite;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("AltArt: failed loading " + path + ": " + e.Message);
                return null;
            }
        }

        internal static void CycleVariant(string cardID, int direction)
        {
            List<string> variants = GetVariants(cardID);
            if (variants.Count == 0)
                return;
            string current;
            ActiveMap.TryGetValue(cardID, out current);
            int idx = string.IsNullOrEmpty(current) ? -1 : variants.IndexOf(current);
            idx += direction;
            if (idx < -1)
                idx = variants.Count - 1;
            if (idx >= variants.Count)
                idx = -1;
            if (idx == -1)
                ActiveMap.Remove(cardID);
            else
                ActiveMap[cardID] = variants[idx];
        }

        internal static void RefreshDeckEditorThumbnails()
        {
            DeckEditorScript editor = UnityEngine.Object.FindFirstObjectByType<DeckEditorScript>();
            CardDatabaseScript db = CardDatabaseScript.Instance;
            if (editor == null || editor.lgo_CurrentDeck == null || db == null)
                return;
            foreach (GameObject go in editor.lgo_CurrentDeck)
            {
                if (go == null)
                    continue;
                CardLogicScript cls = go.GetComponent<CardLogicScript>();
                UnityEngine.UI.Image img = go.GetComponent<UnityEngine.UI.Image>();
                if (cls == null || cls.myCard.cardDef == null || img == null)
                    continue;
                Sprite sprite = db.GetCardImage(cls.myCard.cardDef.cardID, SpriteState.Thumbnail);
                if (sprite != null)
                    img.sprite = sprite;
            }
        }
    }
}
