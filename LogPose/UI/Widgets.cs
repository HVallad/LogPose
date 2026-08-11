using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LogPose.UI
{
    internal enum BtnKind { Primary, Secondary, Danger }

    // uGUI builders working in the mockups' coordinate space: 1920x1080 reference,
    // origin at the TOP-LEFT, y growing downward (README values drop straight in).
    internal static class W
    {
        internal static GameObject Go(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        internal static RectTransform TL(GameObject go, float x, float y, float w, float h)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        internal static RectTransform BL(GameObject go, float x, float yFromBottom, float w, float h)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(x, yFromBottom);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        internal static RectTransform Fill(GameObject go)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        internal static Image Panel(Transform parent, string name, float x, float y, float w, float h,
            float radius, Color fill, Color edge, float edgeW = 1f)
        {
            GameObject go = Go(name, parent);
            TL(go, x, y, w, h);
            Image img = go.AddComponent<Image>();
            img.sprite = UISprites.RoundedRect(64, 64, radius, fill, edge, edgeW, radius + 4f);
            img.type = Image.Type.Sliced;
            return img;
        }

        internal static TextMeshProUGUI Label(Transform parent, string text, float x, float y, float w, float h,
            float size, Color color, int weight = 400, TextAlignmentOptions align = TextAlignmentOptions.TopLeft,
            bool mono = false, float trackingEm = 0f)
        {
            GameObject go = Go("Label", parent);
            TL(go, x, y, w, h);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = mono ? UIFonts.Mono : (weight >= 600 ? UIFonts.SansSemi : UIFonts.Sans);
            if (weight >= 600 && tmp.font == UIFonts.Sans)
                tmp.fontStyle = FontStyles.Bold;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.characterSpacing = trackingEm * 100f;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            tmp.text = text;
            return tmp;
        }

        internal static Button Btn(Transform parent, string label, float x, float y, float w, float h,
            BtnKind kind, Action onClick, float fontSize = 16f)
        {
            GameObject go = Go("Btn" + label, parent);
            TL(go, x, y, w, h);
            Image img = go.AddComponent<Image>();
            Sprite normal, hover, pressed;
            Color labelCol;
            if (kind == BtnKind.Primary)
            {
                normal  = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Accent, 0f), Theme.Accent, 1f, 12f);
                hover   = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Accent, 0.12f), Theme.Accent, 1f, 12f);
                pressed = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Accent, 0.22f), Theme.Accent400, 1f, 12f);
                labelCol = Theme.Accent300;
            }
            else if (kind == BtnKind.Danger)
            {
                normal  = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Danger, 0f), Theme.Danger, 1f, 12f);
                hover   = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Danger, 0.14f), Theme.Danger, 1f, 12f);
                pressed = hover;
                labelCol = Theme.Danger;
            }
            else
            {
                normal  = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Text, 0f), Theme.WithA(Theme.Text, 0.16f), 1f, 12f);
                hover   = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Text, 0.07f), Theme.WithA(Theme.Text, 0.28f), 1f, 12f);
                pressed = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Text, 0.14f), Theme.WithA(Theme.Text, 0.36f), 1f, 12f);
                labelCol = Theme.Text;
            }
            img.sprite = normal;
            img.type = Image.Type.Sliced;

            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.SpriteSwap;
            UnityEngine.UI.SpriteState ss = new UnityEngine.UI.SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = pressed,
                selectedSprite = normal,
                disabledSprite = normal
            };
            b.spriteState = ss;
            b.onClick.AddListener(() =>
            {
                if (onClick != null)
                    onClick();
                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(null);
            });

            TextMeshProUGUI tmp = Label(go.transform, label, 0f, 0f, w, h, fontSize, labelCol, 500,
                TextAlignmentOptions.Center);
            Fill(tmp.gameObject);
            return b;
        }

        // Small pill tag. Width fits the text (estimated from TMP preferred values on the
        // next layout pass via ContentSizeFitter).
        internal static RectTransform Tag(Transform parent, string text, float x, float y, bool accent, bool outline = false)
        {
            GameObject go = Go("Tag" + text, parent);
            RectTransform rt = TL(go, x, y, 10f, 24f);
            Image img = go.AddComponent<Image>();
            if (outline)
                img.sprite = UISprites.RoundedRect(24, 24, 6f, Theme.WithA(Theme.Accent, 0f), Theme.Accent, 1f, 7f);
            else
                img.sprite = UISprites.RoundedRect(24, 24, 6f, accent ? Theme.DonActiveFill : Theme.Edge, Color.clear, 0f, 7f);
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;

            HorizontalLayoutGroup lg = go.AddComponent<HorizontalLayoutGroup>();
            lg.padding = new RectOffset(10, 10, 4, 4);
            lg.childAlignment = TextAnchor.MiddleCenter;
            lg.childForceExpandWidth = lg.childForceExpandHeight = false;
            ContentSizeFitter fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TextMeshProUGUI tmp = Label(go.transform, text, 0f, 0f, 10f, 16f, 12f,
                outline || accent ? Theme.Accent300 : Theme.Text, 600, TextAlignmentOptions.Center);
            tmp.enableWordWrapping = false;
            return rt;
        }

        internal static Image Rule(Transform parent, float x, float y, float w)
        {
            GameObject go = Go("Rule", parent);
            TL(go, x, y, w, 1f);
            Image img = go.AddComponent<Image>();
            img.sprite = UISprites.RuleFade(Theme.WithA(Theme.Text, 0.16f));
            img.raycastTarget = false;
            return img;
        }
    }
}
