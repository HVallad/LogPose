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

        internal static void Update()
        {
            if (!Plugin.CfgUiReskin.Value || Time.frameCount % 30 != 0)
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
            }
            catch { }
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
                RestyleText(tmp);
        }

        private static void RestyleImage(Image img)
        {
            string sprite = img.sprite != null ? img.sprite.name : null;
            string name = img.gameObject.name;

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
