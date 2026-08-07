using HarmonyLib;
using UnityEngine;

namespace LogPose
{
    internal static class AltArtPatches
    {
        // Central choke point: every card image (gameplay, deck editor deck list, previews)
        // resolves through here. If the active deck picked a variant for this card, serve it.
        [HarmonyPrefix, HarmonyPatch(typeof(CardDatabaseScript), "GetCardImage_Internal")]
        private static bool GetCardImage_Prefix(string defName, SpriteState spriteToLoad, ref Sprite __result)
        {
            Sprite sprite;
            if (AltArtManager.TryGetVariantSprite(defName, spriteToLoad, out sprite))
            {
                __result = sprite;
                return false;
            }
            return true;
        }

        // Deck editor: loading a deck activates its sidecar; saving a deck persists choices.
        [HarmonyPostfix, HarmonyPatch(typeof(DeckEditorScript), "LoadDeck")]
        private static void LoadDeck_Postfix(string sFile)
        {
            AltArtManager.LoadSidecar(sFile);
            AltArtManager.RefreshDeckEditorThumbnails();
        }

        [HarmonyPostfix, HarmonyPatch(typeof(DeckEditorScript), "SaveDeck")]
        private static void SaveDeck_Postfix(string sFile)
        {
            AltArtManager.SaveSidecar(sFile);
        }

        // Match start: the game reads the selected deck through LoadDeckGeneric — activate the
        // matching sidecar so variants show during play.
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), "LoadDeckGeneric")]
        private static void LoadDeckGeneric_Postfix(string sFile)
        {
            AltArtManager.LoadSidecar(sFile);
        }
    }
}
