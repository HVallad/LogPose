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

        // Japanese-text parallels stay on the board, but the enlarged hover preview swaps to
        // the base English card — the readable rules text is always one hover away.
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), "ShowFocusedCard")]
        private static void GameplayPreview_Postfix(GameplayLogicScript __instance)
        {
            if (__instance == null)
                return;
            string cardID = null;
            if (!string.IsNullOrEmpty(__instance.s_FocusedChatPreview))
                cardID = __instance.s_FocusedChatPreview;
            else if (__instance.go_FocusedObject != null)
            {
                CardLogicScript cls = __instance.go_FocusedObject.GetComponent<CardLogicScript>();
                if (cls != null && cls.myCard.bFaceUp && cls.myCard.cardDef != null)
                    cardID = cls.myCard.cardDef.cardID;
            }
            SwapPreviewToEnglish(__instance.img_CardPreview, cardID);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(DeckEditorScript), "ShowFocusedCard")]
        private static void DeckEditorPreview_Postfix(DeckEditorScript __instance)
        {
            if (__instance == null || __instance.go_FocusedObject == null)
                return;
            CardLogicScript cls = __instance.go_FocusedObject.GetComponent<CardLogicScript>();
            if (cls == null || cls.myCard.cardDef == null)
                return;
            SwapPreviewToEnglish(__instance.img_CardPreview, cls.myCard.cardDef.cardID);
        }

        private static void SwapPreviewToEnglish(UnityEngine.UI.Image img, string cardID)
        {
            try
            {
                if (!Plugin.CfgEnglishPreviewForJpArts.Value)
                    return;
                if (img == null || img.sprite == null || string.IsNullOrEmpty(cardID))
                    return;
                if (!AltArtManager.IsActiveVariantJapanese(cardID))
                    return;
                AltArtManager.BypassVariant = true;
                try
                {
                    Sprite baseSprite = CardDatabaseScript.Instance.GetCardImage(cardID, SpriteState.Full);
                    if (baseSprite != null)
                        img.sprite = baseSprite;
                }
                finally
                {
                    AltArtManager.BypassVariant = false;
                }
            }
            catch
            {
                // never break the hover path
            }
        }
    }
}
