using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose.UI
{
    // Approved menu direction "1c": persistent top bar, hero, three destination cards
    // over a lit field, utility band. Rendered as an opaque overlay canvas above the
    // vanilla menu; every action drives the vanilla flow by invoking the original
    // buttons' onClick, so game updates that rename methods can't break the wiring.
    internal static class MainMenuUI
    {
        private static GameObject _root;
        private static HostJoinScript _hjs;
        private static TextMeshProUGUI _updatePill;
        private static Button _updateBtn;
        private static readonly Dictionary<string, Button> Vanilla = new Dictionary<string, Button>();

        // Runs EVERY frame: the visibility work is cheap once wired, and anything on a
        // slower cadence shows the vanilla menu for a beat on every transition (the
        // "old skin flash"). `force` (scene just loaded) re-runs the lookups instantly.
        internal static void Update(bool force = false)
        {
            if (!Plugin.CfgUiReskin.Value)
            {
                if (_root != null)
                    _root.SetActive(false);
                return;
            }
            if (_hjs == null)
            {
                // Another scene (deck selector) — the overlay yields. Search again only
                // on cadence or the moment a scene finishes loading.
                if (_root != null && _root.activeSelf)
                    _root.SetActive(false);
                if (!force && Time.frameCount % 30 != 0)
                    return;
                _hjs = UnityEngine.Object.FindFirstObjectByType<HostJoinScript>();
                if (_hjs == null)
                    return;
                if (_root != null)
                    FindVanillaButtons();   // menu scene reloaded — old button refs died with it
            }
            if (_hjs.go_SoloVSelf == null)
                return;

            // Stay visible under the match-history modal — 2f dims the menu behind a scrim.
            bool menuShown = _hjs.go_SoloVSelf.activeInHierarchy;
            if (menuShown && _root == null)
            {
                try { Build(); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Menu reskin failed, falling back to vanilla: " + e);
                    Plugin.CfgUiReskin.Value = false;
                    return;
                }
            }
            if (_root != null)
            {
                if (_root.activeSelf != menuShown)
                    _root.SetActive(menuShown);
                if (menuShown && Time.frameCount % 30 == 0)
                    RefreshUpdatePill();
            }
        }

        private static void Build()
        {
            Theme.Ensure();
            TMP_Text donor = _hjs.go_SoloVSelf.GetComponentInChildren<TMP_Text>(true);
            if (donor != null)
                UIFonts.SetDonor(donor.font);
            FindVanillaButtons();

            _root = new GameObject("LogPoseMenu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;   // match width: the 1920 design always fits horizontally

            Transform t = _root.transform;

            // --- field ------------------------------------------------------------
            GameObject ground = W.Go("Ground", t);
            W.Fill(ground);
            Image g = ground.AddComponent<Image>();
            g.color = Theme.Ground;

            GameObject glow = W.Go("HeroGlow", t);
            W.TL(glow, -24f, 147f, 1200f, 700f);
            Image glowImg = glow.AddComponent<Image>();
            glowImg.sprite = UISprites.Glow(Theme.HeroGlow, 0.65f);
            glowImg.raycastTarget = false;

            GameObject grad = W.Go("HeroGrad", t);
            W.TL(grad, 0f, 0f, 1920f, 560f);
            Image gradImg = grad.AddComponent<Image>();
            gradImg.sprite = UISprites.VGradient(Theme.WithA(Theme.HeroTop, 0.45f), Theme.WithA(Theme.HeroTop, 0f));
            gradImg.raycastTarget = false;

            // --- top bar ----------------------------------------------------------
            W.Label(t, "OPTCGSim", 72f, 36f, 300f, 30f, 20f, Theme.Text, 500);
            W.Tag(t, "LogPose " + Plugin.VERSION, 196f, 36f, false, outline: true);

            _updateBtn = W.Btn(t, "", 1300f, 24f, 220f, 48f, BtnKind.Primary, () => UpdateCheck.Trigger(), 15f);
            _updatePill = _updateBtn.GetComponentInChildren<TextMeshProUGUI>();
            _updateBtn.gameObject.SetActive(false);

            string player = PlayerName();
            if (!string.IsNullOrEmpty(player))
            {
                W.Panel(t, "Avatar", 1648f, 30f, 36f, 36f, 8f, Theme.SurfaceRaised, Color.clear, 0f);
                W.Label(t, player, 1696f, 38f, 180f, 24f, 15f, Theme.Text, 500);
            }
            W.Btn(t, "≡", 1800f, 24f, 48f, 48f, BtnKind.Secondary, () => Invoke("Settings"), 22f);

            // --- hero -------------------------------------------------------------
            W.Label(t, "SET SAIL", 72f, 150f, 600f, 22f, 13f, Theme.Accent400, 600,
                TextAlignmentOptions.TopLeft, false, 0.18f);
            W.Label(t, "Where to today?", 72f, 178f, 1200f, 100f, 72f, Theme.Text, 500);

            // --- destination cards -----------------------------------------------
            BuildMultiplayerCard(t, 72f, 376f, 696f, 392f);
            BuildDeckCard(t, 792f, 376f, 516f, 392f);
            BuildSoloCard(t, 1332f, 376f, 516f, 392f);

            // --- utility band (anchored to the window bottom so no aspect clips it) --
            float rowW = 450f;
            UtilityRow(t, 72f, "⏱", "Match history", MatchMeta(), OpenMatchHistory);
            UtilityRow(t, 72f + rowW + 16f, "▦", "Alt arts", AltArtMeta(), () => Invoke("Deck Editor"));
            UtilityRow(t, 72f + 2f * (rowW + 16f), "▶", "Replays", "Step through any logged game", OpenMatchHistory);

            GameObject div = W.Go("BandDiv", t);
            W.BL(div, 1490f, 86f, 1f, 44f);
            Image divImg = div.AddComponent<Image>();
            divImg.color = Theme.WithA(Theme.Text, 0.14f);
            divImg.raycastTarget = false;

            Button opb = W.Btn(t, "Open OPBounty", 0f, 0f, 170f, 48f, BtnKind.Secondary, () => Invoke("OPBounty"), 15f);
            W.BL(opb.gameObject, 1516f, 84f, 170f, 48f);
            Button quit = W.Btn(t, "Quit", 0f, 0f, 90f, 48f, BtnKind.Secondary, Application.Quit, 15f);
            W.BL(quit.gameObject, 1702f, 84f, 90f, 48f);
            TextMeshProUGUI ver = W.Label(t, VersionString(), 0f, 0f, 110f, 20f, 12f,
                Theme.WithA(Theme.Text, 0.4f), 400, TextAlignmentOptions.BottomLeft, true);
            W.BL(ver.gameObject, 1808f, 98f, 110f, 20f);

            // Quiet links keep the vanilla surfaces the design left out reachable.
            QuietLink(t, 72f, "Patch notes", () => Invoke("Patch Notes"));
            QuietLink(t, 192f, "Help", () => Invoke("Help"));
            QuietLink(t, 262f, "Credits", () => Invoke("Credits"));
            QuietLink(t, 352f, "Sign out", () => Invoke("Sign Out"));
            QuietLink(t, 452f, "Colorway · " + ColorwayName(), ToggleColorway);

            Plugin.Log.LogInfo("Menu reskin built (" + Vanilla.Count + " vanilla buttons wired).");
        }

        private static void BuildMultiplayerCard(Transform t, float x, float y, float w, float h)
        {
            GameObject card = W.Go("CardMultiplayer", t);
            W.TL(card, x, y, w, h);
            Image bg = card.AddComponent<Image>();
            bg.sprite = UISprites.RoundedRectVGradient((int)w, (int)h, 14f,
                Theme.WithA(Theme.Accent, 0.16f), Theme.WithA(Theme.Surface, 0.92f), Theme.Accent, 1f);

            W.Label(card.transform, "PLAY NOW", 32f, 32f, 300f, 20f, 12f, Theme.Accent400, 600,
                TextAlignmentOptions.TopLeft, false, 0.1f);
            W.Label(card.transform, "Multiplayer", 32f, 56f, 500f, 54f, 40f, Theme.Text, 500);
            W.Label(card.transform, "Queue Standard or OP17, jump into Unlimited, or host a private lobby.",
                32f, 118f, 420f, 60f, 16f, Theme.TextMuted, 400);

            GameObject tags = W.Go("Tags", card.transform);
            W.TL(tags, 32f, 236f, w - 64f, 26f);
            HorizontalLayoutGroup lg = tags.AddComponent<HorizontalLayoutGroup>();
            lg.spacing = 8f;
            lg.childAlignment = TextAnchor.MiddleLeft;
            lg.childForceExpandWidth = lg.childForceExpandHeight = false;
            foreach (string f in new[] { "Standard", "OP17", "Extra Regulation", "Unlimited", "Korean", "Private" })
                W.Tag(tags.transform, f, 0f, 0f, false);

            W.Btn(card.transform, "Browse lobbies", 32f, h - 88f, 346f, 56f, BtnKind.Primary, () => Invoke("Multiplayer"), 16f);
        }

        private static void BuildDeckCard(Transform t, float x, float y, float w, float h)
        {
            Image card = W.Panel(t, "CardDecks", x, y, w, h, 14f, Theme.Surface, Theme.Edge);
            Transform ct = card.transform;
            W.Label(ct, "BUILD", 32f, 32f, 300f, 20f, 12f, Theme.TextMuted, 600, TextAlignmentOptions.TopLeft, false, 0.1f);
            W.Label(ct, "Deck editor", 32f, 58f, 400f, 44f, 32f, Theme.Text, 500);
            W.Label(ct, DeckMeta(), 32f, 108f, 400f, 24f, 16f, Theme.TextMuted, 400);

            Image ph = W.Panel(ct, "Thumbs", 32f, 148f, w - 64f, 116f, 8f,
                Theme.WithA(Theme.Ground, 0.4f), Theme.WithA(Theme.Text, 0.12f));
            W.Label(ph.transform, "Leader thumbnails of\nrecent decks", 0f, 34f, w - 64f, 48f, 13f,
                Theme.WithA(Theme.Text, 0.35f), 400, TextAlignmentOptions.Center);

            W.Btn(ct, "Open deck editor", 32f, h - 80f, w - 64f, 48f, BtnKind.Secondary, () => Invoke("Deck Editor"), 16f);
        }

        private static void BuildSoloCard(Transform t, float x, float y, float w, float h)
        {
            Image card = W.Panel(t, "CardSolo", x, y, w, h, 14f, Theme.Surface, Theme.Edge);
            Transform ct = card.transform;
            W.Label(ct, "PRACTICE", 32f, 32f, 300f, 20f, 12f, Theme.TextMuted, 600, TextAlignmentOptions.TopLeft, false, 0.1f);
            W.Label(ct, "Solo play", 32f, 58f, 400f, 44f, 32f, Theme.Text, 500);
            W.Label(ct, "Pilot both sides, or resume a saved state.", 32f, 108f, 420f, 24f, 16f, Theme.TextMuted, 400);

            W.Btn(ct, "Start match", 32f, h - 144f, w - 64f, 48f, BtnKind.Primary, () => Invoke("Solo"), 16f);
            W.Btn(ct, "Load state from clipboard", 32f, h - 80f, w - 64f, 48f, BtnKind.Secondary, () => Invoke("Clipboard"), 15f);
        }

        private static void UtilityRow(Transform t, float x, string icon, string title, string meta, Action onClick)
        {
            GameObject row = W.Go("Row" + title, t);
            W.BL(row, x, 72f, 450f, 72f);
            Image img = row.AddComponent<Image>();
            img.sprite = UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Surface, 0.7f),
                Theme.WithA(Theme.Text, 0.1f), 1f, 9f);
            img.type = Image.Type.Sliced;

            Button b = row.AddComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.SpriteSwap;
            b.spriteState = new UnityEngine.UI.SpriteState
            {
                highlightedSprite = UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Accent, 0.08f),
                    Theme.WithA(Theme.Accent, 0.4f), 1f, 9f),
                pressedSprite = UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Accent, 0.16f),
                    Theme.Accent, 1f, 9f)
            };
            b.onClick.AddListener(() => onClick());

            W.Label(row.transform, icon, 20f, 22f, 28f, 28f, 20f, Theme.Accent, 400, TextAlignmentOptions.Center);
            W.Label(row.transform, title, 60f, 13f, 340f, 24f, 17f, Theme.Text, 500);
            W.Label(row.transform, meta, 60f, 39f, 370f, 20f, 13f, Theme.TextMuted, 400);
        }

        private static void QuietLink(Transform t, float x, string text, Action onClick)
        {
            Button b = W.Btn(t, text, 0f, 0f, text.Length * 9f + 24f, 34f, BtnKind.Secondary, onClick, 13f);
            W.BL(b.gameObject, x, 20f, text.Length * 9f + 24f, 34f);
            Image img = b.GetComponent<Image>();
            img.sprite = UISprites.RoundedRect(32, 32, 8f, Color.clear, Color.clear, 0f, 9f);
            b.GetComponentInChildren<TextMeshProUGUI>().color = Theme.WithA(Theme.Text, 0.45f);
        }

        // --- wiring ---------------------------------------------------------------

        private static string _gameVersion;

        private static void FindVanillaButtons()
        {
            Vanilla.Clear();
            foreach (TMP_Text txt in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string s = (txt.text ?? "").Trim();
                if (s.Length >= 3 && s.Length <= 16 && char.IsDigit(s[0]) && s.Contains(".") &&
                    txt.GetComponentInParent<Button>() == null)
                {
                    _gameVersion = s;
                    break;
                }
            }
            // Menu buttons all live under the main-menu canvas; a scene-wide scan matches
            // same-labelled buttons on other screens. OPBounty floats on its own canvas.
            List<Button> pool = new List<Button>();
            if (_hjs.go_MainCanvas != null)
                pool.AddRange(_hjs.go_MainCanvas.GetComponentsInChildren<Button>(true));
            pool.AddRange(UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            foreach (Button b in pool)
            {
                TMP_Text txt = b.GetComponentInChildren<TMP_Text>(true);
                if (txt == null)
                    continue;
                string s = txt.text ?? "";
                Match(s, "Multiplayer", "Multiplayer", b);
                Match(s, "Deck Editor", "Deck Editor", b);
                Match(s, "Solo v Self", "Solo", b);
                Match(s, "Patch Notes", "Patch Notes", b);
                Match(s, "Settings", "Settings", b);
                if (s.Trim() == "Help") Match(s, "Help", "Help", b);
                Match(s, "Credits", "Credits", b);
                Match(s, "Sign Out", "Sign Out", b);
                Match(s, "OPBounty", "OPBounty", b);
                Match(s, "From Clipboard", "Clipboard", b);
            }
            foreach (var kv in Vanilla)
            {
                string calls = "";
                for (int i = 0; i < kv.Value.onClick.GetPersistentEventCount(); i++)
                {
                    UnityEngine.Object target = kv.Value.onClick.GetPersistentTarget(i);
                    calls += (target != null ? target.GetType().Name : "null") + "."
                        + kv.Value.onClick.GetPersistentMethodName(i) + " ";
                }
                Plugin.Log.LogInfo("Menu wire: '" + kv.Key + "' -> " + PathOf(kv.Value.transform)
                    + " calls: " + calls.Trim());
            }
        }

        private static void Match(string text, string needle, string key, Button b)
        {
            if (!Vanilla.ContainsKey(key) && text.Contains(needle))
                Vanilla[key] = b;
        }

        private static string PathOf(Transform t)
        {
            string s = t.name;
            while (t.parent != null && s.Length < 90)
            {
                t = t.parent;
                s = t.name + "/" + s;
            }
            return s;
        }

        private static void Invoke(string key)
        {
            Button b;
            if (Vanilla.TryGetValue(key, out b) && b != null)
                b.onClick.Invoke();
            else
                Plugin.Log.LogWarning("Menu reskin: no vanilla button for '" + key + "'.");
        }

        private static void OpenMatchHistory()
        {
            Replay.MatchHistoryUI.Open(_hjs);
        }

        private static string ColorwayName()
        {
            string v = Plugin.CfgUiColorway.Value;
            return v != null && v.Trim().ToLowerInvariant() == "batsu" ? "Batsu" : "Nocturne";
        }

        // Live-switches the LogPose-built surfaces (menu, mats, board chrome). Screens the
        // in-place restyler already converted keep the old accent until the next launch —
        // the one-way sprite swap can't be re-keyed.
        private static void ToggleColorway()
        {
            Plugin.CfgUiColorway.Value = ColorwayName() == "Batsu" ? "Nocturne" : "Batsu";
            BoardHUD.InvalidateTheme();
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        private static void RefreshUpdatePill()
        {
            bool show = UpdateCheck.Offering;
            if (_updateBtn != null && _updateBtn.gameObject.activeSelf != show)
                _updateBtn.gameObject.SetActive(show);
            if (show && _updatePill != null)
                _updatePill.text = UpdateCheck.OfferText;
        }

        // --- data -----------------------------------------------------------------

        private static string PlayerName()
        {
            foreach (string key in new[] { "PlayerName", "Nickname", "UserName" })
            {
                string v = PlayerPrefs.GetString(key, "");
                if (!string.IsNullOrEmpty(v))
                    return v;
            }
            return null;
        }

        private static string DeckMeta()
        {
            try
            {
                int n = Directory.GetFiles(Path.Combine(Paths.GameRootPath, "Decks"), "*.deck").Length;
                return n + (n == 1 ? " deck saved" : " decks saved");
            }
            catch { return "Build and tune decks"; }
        }

        private static string AltArtMeta()
        {
            try
            {
                int n = Directory.GetFiles(Path.Combine(Paths.GameRootPath, "Decks"), "*.arts.json").Length;
                return n > 0 ? n + (n == 1 ? " deck customised" : " decks customised") : "Pick parallel art per deck";
            }
            catch { return "Pick parallel art per deck"; }
        }

        private static string MatchMeta()
        {
            try
            {
                string dir = Path.Combine(Paths.GameRootPath, "CombatLogs", "AutoSaved");
                int n = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.rz1").Length : 0;
                return n > 0 ? n + " recorded games" : "Browse recorded games";
            }
            catch { return "Browse recorded games"; }
        }

        private static string VersionString()
        {
            return string.IsNullOrEmpty(_gameVersion) ? Application.version : _gameVersion;
        }
    }
}
