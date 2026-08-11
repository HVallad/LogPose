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
            string rel = FindImageFolderRelative(cardID);
            if (rel == null)
                return null;
            string basePath = Path.Combine(Path.Combine(Application.streamingAssetsPath, rel), cardID + suffix);

            string path = null;
            if (state == SpriteState.Thumbnail && File.Exists(basePath + "_small.jpg"))
                path = basePath + "_small.jpg";
            else if (File.Exists(basePath + ".png"))
                path = basePath + ".png";
            else if (File.Exists(basePath + ".jpg"))
                path = basePath + ".jpg";
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
