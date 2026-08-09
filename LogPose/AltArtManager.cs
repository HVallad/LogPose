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
            string rel = FindImageFolderRelative(cardID);
            if (rel == null)
                return false;
            string basePath = Path.Combine(Path.Combine(Application.streamingAssetsPath, rel), cardID + suffix);

            string path = null;
            if (state == SpriteState.Thumbnail && File.Exists(basePath + "_small.jpg"))
                path = basePath + "_small.jpg";
            else if (File.Exists(basePath + ".png"))
                path = basePath + ".png";
            else if (File.Exists(basePath + ".jpg"))
                path = basePath + ".jpg";
            if (path == null)
                return false;

            string key = path + "|" + state;
            if (SpriteCache.TryGetValue(key, out sprite) && sprite != null)
                return true;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(bytes))
                    return false;
                sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(tex.width / 2f, tex.height / 2f), 100f, 0u, SpriteMeshType.FullRect);
                SpriteCache[key] = sprite;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("AltArt: failed loading " + path + ": " + e.Message);
                sprite = null;
                return false;
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
