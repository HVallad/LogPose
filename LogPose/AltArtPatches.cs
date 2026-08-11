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

        // Match start: the game reads each deck through LoadDeckGeneric — player's first,
        // then the enemy's. Merge (not replace) so the second load can't wipe the first
        // deck's picks, and start solo matches from a clean map so nothing stale leaks in.
        [HarmonyPostfix, HarmonyPatch(typeof(GameplayLogicScript), "LoadDeckGeneric")]
        private static void LoadDeckGeneric_Postfix(string sFile)
        {
            AltArtManager.MergeSidecar(sFile);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GameplayLogicScript), "GameStartSolo")]
        private static void GameStartSolo_Prefix()
        {
            AltArtManager.ResetForMatch();
        }

        // While the alt-art page is open the editor's Physics2D card hover/click must be
        // suppressed — UI raycasts don't block it, so a click on a thumbnail would also
        // hit the deck-list card behind the overlay.
        [HarmonyPrefix, HarmonyPatch(typeof(DeckEditorScript), "HandleMouseClick")]
        private static bool DeckEditorClick_Prefix()
        {
            return !AltArtUI.PageOpen;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(DeckEditorScript), "HandleMouseHover")]
        private static bool DeckEditorHover_Prefix(DeckEditorScript __instance)
        {
            if (!AltArtUI.PageOpen)
                return true;
            __instance.go_FocusedObject = null;
            return false;
        }

        // Hold Shift while hovering a card and the enlarged preview shows the BASE art
        // instead of the selected variant — the original English card (with readable rules
        // text) is always one key away, without giving up the parallel art anywhere else.
        // ShowFocusedCard runs every frame, so releasing Shift restores the variant instantly.
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
            SwapPreviewToBase(__instance.img_CardPreview, cardID);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(DeckEditorScript), "ShowFocusedCard")]
        private static void DeckEditorPreview_Postfix(DeckEditorScript __instance)
        {
            if (__instance == null || __instance.go_FocusedObject == null)
                return;
            CardLogicScript cls = __instance.go_FocusedObject.GetComponent<CardLogicScript>();
            if (cls == null || cls.myCard.cardDef == null)
                return;
            SwapPreviewToBase(__instance.img_CardPreview, cls.myCard.cardDef.cardID);
        }

        private static void SwapPreviewToBase(UnityEngine.UI.Image img, string cardID)
        {
            try
            {
                if (!UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift)
                    && !UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift))
                    return;
                if (img == null || img.sprite == null || string.IsNullOrEmpty(cardID))
                    return;
                if (!AltArtManager.HasActiveVariant(cardID))
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
