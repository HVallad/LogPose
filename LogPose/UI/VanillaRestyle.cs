using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose.UI
{
    // Converts the vanilla parchment UI to the redesign language IN PLACE: every screen
    // is built from a tiny sprite vocabulary (buttonLong_beige, panel_beige, Background,
    // UISprite, Checkmark, wood BG), so swapping by sprite name + retinting text restyles
    // the whole game without touching any layout or behaviour. Runs continuously so
    // late-spawned elements (lobby rows, choice buttons) get picked up too.
    internal static class VanillaRestyle
    {
        private static readonly HashSet<int> Done = new HashSet<int>();

        internal static void Update(bool force = false)
        {
            if (!Plugin.CfgUiReskin.Value || (!force && Time.frameCount % 30 != 0))
                return;
            Theme.Ensure();
            try
            {
                foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (c.name.StartsWith("LogPose") || c.name == "OPBountyCanvas")
                        continue;
                    foreach (Graphic g in c.GetComponentsInChildren<Graphic>(true))
                    {
                        if (g == null || !Done.Add(g.GetInstanceID()))
                            continue;
                        Restyle(g);
                    }
                }
                // Scrollbar ColorBlocks re-stamp their parchment tint onto the themed
                // handle sprite (the brown thumb) — sweep every active scrollbar until
                // its tint is white. Not one-shot: the one-shot visit can hit these
                // while their hierarchy is inactive.
                foreach (Scrollbar sb in Object.FindObjectsByType<Scrollbar>(FindObjectsSortMode.None))
                    FixScrollbarTint(sb);
            }
            catch { }
        }

        private static void FixScrollbarTint(Scrollbar sb)
        {
            if (sb == null)
                return;
            if (sb.colors.normalColor != Color.white)
            {
                ColorBlock cb = sb.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(0.9f, 0.88f, 1f, 1f);
                cb.pressedColor = new Color(0.8f, 0.77f, 1f, 1f);
                cb.selectedColor = Color.white;
                sb.colors = cb;
                if (sb.targetGraphic != null)
                    sb.targetGraphic.CrossFadeColor(Color.white, 0f, true, true);
            }
        }

        private static void Restyle(Graphic g)
        {
            Image img = g as Image;
            if (img != null)
            {
                RestyleImage(img);
                return;
            }
            TMP_Text tmp = g as TMP_Text;
            if (tmp != null)
            {
                RestyleText(tmp);
                return;
            }
            // Vanilla toggle labels are LEGACY Text at #323232 — invisible on the dark
            // theme (all eleven settings toggles, the private-lobby checkboxes). The
            // TMP-only pass missed them: same gap that hid the editor chips in 1.0.9.
            Text legacy = g as Text;
            if (legacy != null)
            {
                Color c = legacy.color;
                if (c.a > 0.5f && c.r < 0.45f && c.g < 0.45f && c.b < 0.45f)
                    legacy.color = Theme.Text;
            }
        }

        private static void RestyleImage(Image img)
        {
            string sprite = img.sprite != null ? img.sprite.name : null;
            string name = img.gameObject.name;

            // Dropdown popups: the prefab Template (and any live "Dropdown List" clone)
            // ships as a WHITE panel with near-white item rows — unreadable once the
            // item labels are retinted light. Styling the inactive Template means every
            // popup opens dark from the first frame (clones copy the styled state).
            if (name == "Template" || name == "Dropdown List")
            {
                img.sprite = UISprites.RoundedRect(48, 48, 8f, Theme.Surface, Theme.EdgeModal, 1f, 12f);
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                return;
            }
            if (name == "Item Background")
            {
                img.sprite = null;
                img.color = Theme.WithA(Theme.Text, 0.05f);
                return;
            }
            // Scrollbars ship spriteless and get their color from the vanilla
            // parchment ColorBlock — the brown thumb in the lobby list. Dark track,
            // accent thumb, and the tint retargeted to plain white.
            if (name == "Scrollbar Vertical" || name == "Scrollbar Horizontal")
            {
                img.sprite = UISprites.RoundedRect(16, 16, 4f, Theme.WithA(Theme.Text, 0.05f), Color.clear, 0f, 5f);
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                return;
            }
            if (name == "Handle")
            {
                img.sprite = UISprites.RoundedRect(16, 16, 4f, Theme.WithA(Theme.Accent, 0.5f), Color.clear, 0f, 5f);
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                // NOTE: the parameterless GetComponentInParent misses INACTIVE parents —
                // the lobby scrollbar is inactive at boot when this one-shot visit runs,
                // so the ColorBlock is also fixed by the per-poll sweep in Update.
                Scrollbar sb = img.GetComponentInParent<Scrollbar>(true);
                if (sb != null)
                    FixScrollbarTint(sb);
                return;
            }

            // The wood table background -> flat ground.
            if (name == "BG" && img.rectTransform.rect.width > 1200f)
            {
                img.sprite = null;
                img.color = Theme.Ground;
                return;
            }
            if (sprite == "buttonLong_beige")
            {
                img.sprite = UISprites.RoundedRect(48, 48, 8f, Theme.Surface, Theme.WithA(Theme.Text, 0.16f), 1f, 12f);
                img.type = Image.Type.Sliced;
                KeepTint(img);
                Button b = img.GetComponent<Button>();
                if (b != null && b.targetGraphic == img)
                {
                    b.transition = Selectable.Transition.SpriteSwap;
                    b.spriteState = new UnityEngine.UI.SpriteState
                    {
                        highlightedSprite = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Accent, 0.12f), Theme.Accent, 1f, 12f),
                        pressedSprite = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Accent, 0.22f), Theme.Accent400, 1f, 12f),
                        selectedSprite = img.sprite,
                        disabledSprite = img.sprite
                    };
                }
                return;
            }
            if (sprite == "panel_beige")
            {
                img.sprite = UISprites.RoundedRect(64, 64, 14f, Theme.Surface, Theme.Edge, 1f, 18f);
                img.type = Image.Type.Sliced;
                KeepTint(img);
                return;
            }
            if (sprite == "Background")
            {
                img.sprite = UISprites.RoundedRect(64, 64, 14f, Theme.Ground, Theme.Edge, 1f, 18f);
                img.type = Image.Type.Sliced;
                KeepTint(img);
                return;
            }
            if (sprite == "UISprite")
            {
                bool toggle = img.GetComponentInParent<Toggle>() != null;
                img.sprite = toggle
                    ? UISprites.RoundedRect(32, 32, 8f, Theme.SurfaceRaised, Theme.WithA(Theme.Text, 0.16f), 1f, 9f)
                    : UISprites.RoundedRect(32, 32, 8f, Theme.SurfaceRaised, Theme.WithA(Theme.Text, 0.12f), 1f, 9f);
                img.type = Image.Type.Sliced;
                KeepTint(img);
                return;
            }
            if (sprite == "Checkmark")
            {
                img.color = Theme.Accent;
                return;
            }
            if (sprite == "DropdownArrow")
            {
                img.color = Theme.TextMuted;
                return;
            }
            // Icon glyphs drawn in near-black for parchment — retint for the dark theme.
            if (sprite == "audioOn" || sprite == "audioOff" || sprite == "musicOn" || sprite == "musicOff")
                img.color = Theme.Text;
        }

        // Beige sprites were often tinted (grey disabled tabs, translucent panels).
        // Preserve only the alpha of the old tint; the new sprites carry their own color.
        private static void KeepTint(Image img)
        {
            Color c = img.color;
            img.color = new Color(1f, 1f, 1f, c.a);
        }

        private static void RestyleText(TMP_Text tmp)
        {
            if (tmp.font != null && tmp.font.name.StartsWith("LogPose"))
                return;
            // Pure display labels must never raycast: the invisible (alpha-0) side-name
            // rects sat over the lobby browser's format chips and ate their clicks.
            string n = tmp.gameObject.name;
            if (tmp.raycastTarget && (n == "OpponentSideName" || n == "PlayerSideName"
                || n == "OpponentTimer" || n == "PlayerTimer" || n == "Turn Counter"
                || n == "Mismatch Indicator" || n == "Version Number" || n == "GuideText"))
                tmp.raycastTarget = false;
            bool mono = tmp.gameObject.name.Contains("Timer") || tmp.gameObject.name.Contains("Version");
            TMP_FontAsset f = mono ? UIFonts.Mono : UIFonts.Sans;
            if (f != null)
                tmp.font = f;
            Color c = tmp.color;
            // Dark-on-parchment text becomes light-on-dark; already-light text stays.
            bool dark = c.r < 0.5f && c.g < 0.5f && c.b < 0.5f;
            Color target = Theme.Text;
            tmp.color = new Color(target.r, target.g, target.b, c.a);
            if (!dark && (c.r > 0.95f && c.g > 0.6f && c.b < 0.5f))
                tmp.color = new Color(c.r, c.g, c.b, c.a);   // keep warm highlight colors (warnings etc.)
        }
    }
}
