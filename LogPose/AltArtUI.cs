using System.Collections.Generic;
using UnityEngine;

namespace LogPose
{
    // Minimal IMGUI panel, opened with the configured key (default F6) while the deck editor is
    // open. Lists every card in the current deck that has variant art and lets you cycle
    // base -> variant(s) -> base. Thumbnails in the deck list refresh live.
    internal static class AltArtUI
    {
        private static bool _visible;
        private static Vector2 _scroll;
        private static Rect _windowRect = new Rect(40f, 40f, 440f, 520f);

        internal static void Update()
        {
            if (!Input.GetKeyDown(Plugin.CfgAltArtKey.Value))
                return;
            if (!_visible && Object.FindFirstObjectByType<DeckEditorScript>() == null)
                return;
            _visible = !_visible;
        }

        internal static void OnGUI()
        {
            if (!_visible)
                return;
            if (Object.FindFirstObjectByType<DeckEditorScript>() == null)
            {
                _visible = false;
                return;
            }
            _windowRect = GUILayout.Window(0x0A17A57, _windowRect, DrawWindow, "Alt Art Selector");
        }

        private static void DrawWindow(int id)
        {
            DeckEditorScript editor = Object.FindFirstObjectByType<DeckEditorScript>();
            var deckIds = new List<string>();
            if (editor != null && editor.lgo_CurrentDeck != null)
            {
                foreach (GameObject go in editor.lgo_CurrentDeck)
                {
                    if (go == null)
                        continue;
                    CardLogicScript cls = go.GetComponent<CardLogicScript>();
                    if (cls != null && cls.myCard.cardDef != null
                        && !deckIds.Contains(cls.myCard.cardDef.cardID))
                        deckIds.Add(cls.myCard.cardDef.cardID);
                }
            }

            GUILayout.Label("Cards in this deck with alternate art:");
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(400f));
            int shown = 0;
            foreach (string cardID in deckIds)
            {
                List<string> variants = AltArtManager.GetVariants(cardID);
                if (variants.Count == 0)
                    continue;
                shown++;
                GUILayout.BeginHorizontal();
                string current;
                AltArtManager.ActiveMap.TryGetValue(cardID, out current);
                string label = string.IsNullOrEmpty(current) ? "base" : current.TrimStart('_');
                GUILayout.Label(cardID, GUILayout.Width(115f));
                if (GUILayout.Button("<", GUILayout.Width(30f)))
                {
                    AltArtManager.CycleVariant(cardID, -1);
                    AltArtManager.RefreshDeckEditorThumbnails();
                }
                GUILayout.Label(label + "   (" + (variants.Count + 1) + " arts)", GUILayout.Width(170f));
                if (GUILayout.Button(">", GUILayout.Width(30f)))
                {
                    AltArtManager.CycleVariant(cardID, 1);
                    AltArtManager.RefreshDeckEditorThumbnails();
                }
                GUILayout.EndHorizontal();
            }
            if (shown == 0)
            {
                GUILayout.Label("No variant art found for the cards in this deck.\n\n" +
                    "Add images named like OP01-001_alt1.png (or official\n" +
                    "parallel names like OP01-001_p1.png) into\n" +
                    "OPTCGSim_Data\\StreamingAssets\\Cards\\<SET>\\ —\n" +
                    "or run tools\\Fetch-AltArts.ps1 to download official\n" +
                    "parallel arts. Then press Rescan.");
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rescan art folders"))
                AltArtManager.InvalidateVariantCache();
            if (GUILayout.Button("Save choices now"))
                AltArtManager.SaveSidecar();
            if (GUILayout.Button("Close"))
                _visible = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("Choices save automatically when you save the deck.");
            GUI.DragWindow();
        }
    }
}
