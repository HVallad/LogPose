using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose.UI
{
    // Frame 2a's board chrome. Two layers:
    //  - an overlay canvas (sort 40) with the HUD bar (turn + phase pills + names/leader
    //    lines/life pips + adopted timers), the center-line "YOUR TURN" chip and the live
    //    DON/DECK/TRASH counters;
    //  - repositioned VANILLA objects on the game canvas: the combat log + every action
    //    button moves into the right rail (x 1104..1848), with a panel and a mat backdrop
    //    interleaved at the right sibling depths so cards and previews keep their natural
    //    z-order. The whole field slides left via SideField + the sc_Locations rewrite in
    //    BoardLayoutPatches.
    internal static class BoardHUD
    {
        private static readonly string[] Phases = { "Refresh", "Draw", "DON!!", "Main", "End" };
        private static GameObject _root;
        private static TextMeshProUGUI _turn;
        private static readonly Image[] _pills = new Image[5];
        private static readonly TextMeshProUGUI[] _pillLabels = new TextMeshProUGUI[5];
        private static GameplayLogicScript _gls;

        private static TextMeshProUGUI _oppName, _oppLeader, _plName, _plLeader;
        private static Transform _oppPips, _plPips;
        private static readonly int[] _maxLife = new int[2];
        private static TMP_Text _vanOppName, _vanPlName, _vanTurnCounter;

        // Center-line chip + per-side counters (overlay canvas). These sit over the mat,
        // whose position depends on the canvas width — RefreshCounters re-seats them.
        private static Image _centerChip;
        private static TextMeshProUGUI _centerLabel;
        private static Image _centerRule;
        private static RectTransform _centerChipRt;
        private static TextMeshProUGUI _donP, _deckP, _trashP, _donO, _deckO, _trashO;

        // Game-canvas chrome (scene objects die on reload; recreated when null).
        private static RectTransform _railPanel, _matChrome;

        private static int _lastAction = -9;
        private static float _fieldFromLeft = 572f;   // field center, from the left edge
        private static float _canvasW = 1920f;

        internal static void Update()
        {
            bool poll = Time.frameCount % 30 == 0;
            if (_gls != null && Plugin.CfgUiReskin.Value
                && _gls.e_CurrentState != GameplayState.MainMenu)
            {
                StackChoices();   // every frame: choice buttons restyle the moment they appear
                UpdateHandTuck(); // every frame: the fan follows the pointer's intent
            }
            if (!poll)
                return;
            if (!Plugin.CfgUiReskin.Value)
            {
                if (_root != null)
                    _root.SetActive(false);
                return;
            }
            if (_gls == null)
            {
                _gls = Object.FindFirstObjectByType<GameplayLogicScript>();
                if (_gls == null)
                {
                    if (_root != null)
                        _root.SetActive(false);
                    return;
                }
            }
            // Keep the location table re-zoned even at the menu: leaders and deck piles
            // are placed during game LOAD, before SetupBoardObjects, so the table must
            // already hold the design geometry by then. The field's home is computed
            // from the REAL canvas width so narrow aspects (3:2, 16:10) keep the mat
            // on-screen and clear of the right rail.
            if (_gls.cn_Canvas != null)
            {
                float w = ((RectTransform)_gls.cn_Canvas.transform).rect.width;
                if (w > 100f)
                {
                    // The field centers itself in everything left of the rail (620 + gaps).
                    _fieldFromLeft = Mathf.Clamp((w - 668f) * 0.5f, 380f, 700f);
                    BoardLayoutPatches.FieldShift = _fieldFromLeft - w * 0.5f;
                    _canvasW = w;
                }
            }
            BoardLayoutPatches.Rezone(_gls);
            bool inGameAtAll = _gls.e_CurrentState != GameplayState.MainMenu;
            if (inGameAtAll)
            {
                EnsureField();
                ImposeChrome(_gls);
            }
            if (_railPanel != null && _railPanel.gameObject.activeSelf != inGameAtAll)
                _railPanel.gameObject.SetActive(inGameAtAll);
            if (_matChrome != null && _matChrome.gameObject.activeSelf != inGameAtAll)
                _matChrome.gameObject.SetActive(inGameAtAll);

            bool inGame = inGameAtAll
                && _gls.gsv_CurrentGame != null
                && _gls.gsv_CurrentGame.iTurnNumber >= 1;
            if (inGame && _root == null)
                Build();
            if (_root == null)
                return;
            if (_root.activeSelf != inGame)
                _root.SetActive(inGame);
            if (inGame)
            {
                Refresh();
                RefreshSides();
                RefreshCounters();
            }
        }

        // Colorway switched at runtime: drop every cached sprite and the scene chrome so
        // they rebuild with the new tokens. The overlay root is kept — it holds the
        // adopted vanilla timer labels, which the game still writes to.
        internal static void InvalidateTheme()
        {
            _sprEnd = _sprEndHover = _sprEndPress = null;
            _sprSec = _sprSecHover = _sprSecPress = null;
            _sprDanger = _sprDangerHover = null;
            if (_railPanel != null)
            {
                Object.Destroy(_railPanel.gameObject);
                _railPanel = null;
            }
            if (_matChrome != null)
            {
                Object.Destroy(_matChrome.gameObject);
                _matChrome = null;
            }
            _matP = _matO = _glowP = _glowO = null;
        }

        // The fan raises while the pointer is near it (with hysteresis so it doesn't
        // flicker at the boundary) and always during mulligan; otherwise it tucks so
        // the DON!! band and piles stay readable. Also re-runs the hand layout when the
        // acting seat flips, so the solo dock follows whose turn it is.
        private static void UpdateHandTuck()
        {
            try
            {
                // Raise only from the very bottom edge so the DON!! strip above the hand
                // stays clickable while tucked; once raised, stay up until the pointer
                // leaves the hand region entirely.
                bool preGame = _gls.gsv_CurrentGame == null || _gls.gsv_CurrentGame.iTurnNumber < 1;
                float frac = Input.mousePosition.y / Mathf.Max(Screen.height, 1);
                bool raised = preGame
                    || (BoardLayoutPatches.HandRaised ? frac < 0.28f : frac < 0.08f);
                int action = _gls.gsv_CurrentGame != null ? _gls.gsv_CurrentGame.iPlayerAction : -1;
                if (raised == BoardLayoutPatches.HandRaised && action == _lastAction)
                    return;
                BoardLayoutPatches.HandRaised = raised;
                _lastAction = action;
                _gls.RefreshHandPositions();
            }
            catch { }
        }

        // ------------------------------------------------------------------ field ------

        private static Image _matP, _matO, _glowP, _glowO;

        private static void EnsureField()
        {
            try
            {
                if (_gls.cn_Canvas == null)
                    return;
                if (_matP == null || _matO == null)
                {
                    Transform side = _gls.cn_Canvas.transform.Find("SideField");
                    if (side == null)
                        return;
                    Transform p = side.Find("Player/PlayerPlaymat");
                    Transform o = side.Find("Opponent/OpponentPlaymat");
                    Transform gp = side.Find("Player/PlayerSideGlow");
                    Transform go = side.Find("Opponent/OpponentSideGlow");
                    _matP = p != null ? p.GetComponent<Image>() : null;
                    _matO = o != null ? o.GetComponent<Image>() : null;
                    _glowP = gp != null ? gp.GetComponent<Image>() : null;
                    _glowO = go != null ? go.GetComponent<Image>() : null;
                }
                Sprite matP = FieldMat.Get(false);
                Sprite matO = FieldMat.Get(true);
                if (_matP != null && matP != null && _matP.sprite != matP)
                    _matP.sprite = matP;
                if (_matO != null && matO != null)
                {
                    if (_matO.sprite != matO)
                        _matO.sprite = matO;
                    // The design mirrors the opponent half vertically; the texture is
                    // authored for that, so the vanilla 180-degree turn comes off.
                    if (_matO.transform.localEulerAngles.z != 0f)
                        _matO.transform.localRotation = Quaternion.identity;
                }
                Color glow = Theme.WithA(Theme.Accent, 0.78f);
                if (_glowP != null && _glowP.color != glow)
                    _glowP.color = glow;
                if (_glowO != null && _glowO.color != glow)
                    _glowO.color = glow;
            }
            catch { }
        }

        // ------------------------------------------------------- game-canvas chrome ---

        internal static void ImposeChrome(GameplayLogicScript gls)
        {
            if (!Plugin.CfgUiReskin.Value || gls == null || gls.cn_Canvas == null)
                return;
            try
            {
                Theme.Ensure();
                BoardLayoutPatches.Rezone(gls);
                Transform cn = gls.cn_Canvas.transform;
                float F = BoardLayoutPatches.FieldShift;

                Transform side = cn.Find("SideField");
                if (side != null)
                {
                    RectTransform srt = side as RectTransform;
                    if (srt != null && srt.anchoredPosition.x != F)
                        srt.anchoredPosition = new Vector2(F, srt.anchoredPosition.y);
                    if (_matChrome == null)
                    {
                        GameObject gc = W.Go("LogPoseMatChrome", cn);
                        Image im = gc.AddComponent<Image>();
                        im.sprite = UISprites.RoundedRect(64, 64, 14f,
                            Theme.WithA(Theme.Surface, 0.45f), Theme.WithA(Theme.Text, 0.10f), 1f, 18f);
                        im.type = Image.Type.Sliced;
                        im.raycastTarget = false;
                        _matChrome = gc.GetComponent<RectTransform>();
                    }
                    int target = side.GetSiblingIndex();
                    if (_matChrome.GetSiblingIndex() > target)
                        _matChrome.SetSiblingIndex(target);
                    C(_matChrome, F, 0f, 790f, 1044f);
                }

                Transform log = cn.Find("LogScrollView");
                if (log != null)
                {
                    if (_railPanel != null && _railPanel.sizeDelta.x != 620f)
                    {
                        Object.Destroy(_railPanel.gameObject);   // rebuilt at the new width
                        _railPanel = null;
                    }
                    if (_railPanel == null)
                    {
                        GameObject rp = W.Go("LogPoseRail", cn);
                        Image im = rp.AddComponent<Image>();
                        im.sprite = UISprites.RoundedRect(64, 64, 14f,
                            Theme.Surface, Theme.WithA(Theme.Text, 0.10f), 1f, 18f);
                        im.type = Image.Type.Sliced;
                        im.raycastTarget = false;
                        _railPanel = rp.GetComponent<RectTransform>();
                        W.Label(rp.transform, "COMBAT LOG", 24f, 20f, 300f, 20f, 12f,
                            Theme.WithA(Theme.Text, 0.55f), 600, TextAlignmentOptions.TopLeft, false, 0.12f);
                        W.Rule(rp.transform, 16f, 56f, 588f);
                    }
                    int target = log.GetSiblingIndex();
                    if (_railPanel.GetSiblingIndex() > target)
                        _railPanel.SetSiblingIndex(target);
                    // The rail hugs the RIGHT edge, slimmed to 620 so the field zone gets
                    // the lion's share of the width.
                    R(_railPanel, -334f, 153f, 620f, 566f);

                    RectTransform lrt = (RectTransform)log;
                    R(lrt, -334f, 122f, 592f, 496f);
                    Image li = log.GetComponent<Image>();
                    if (li != null && li.enabled)
                        li.enabled = false;
                    if (log.childCount > 0 && log.GetChild(0).childCount > 0)
                    {
                        RectTransform content = log.GetChild(0).GetChild(0) as RectTransform;
                        if (content != null)
                        {
                            if (content.sizeDelta.x != 572f)
                                content.sizeDelta = new Vector2(572f, content.sizeDelta.y);
                            for (int i = 0; i < content.childCount; i++)
                            {
                                RectTransform line = content.GetChild(i) as RectTransform;
                                if (line != null && line.sizeDelta.x != 552f)
                                    line.sizeDelta = new Vector2(552f, line.sizeDelta.y);
                            }
                        }
                    }
                }

                // Rail action area + relocated utilities (all vanilla objects; the game
                // re-writes their spots each game start, so this re-imposes every poll).
                // Rail-side objects anchor to the RIGHT edge (design-x minus 960) so a
                // narrower-than-16:9 canvas can't clip them; solo tools hug the LEFT.
                MoveBtn(cn, "BackToMain", -179f, -372f, 300f, 56f, 15f, BtnKind.Danger);
                MoveBtn(cn, "ReportBug", -489f, -372f, 300f, 56f, 13f, BtnKind.Secondary);
                MoveBtn(cn, "DownloadLog", -120f, 408f, 130f, 40f, 12f, BtnKind.Secondary);
                // Bottom utility row, left to right: save-state tools, cancel, sound.
                MoveEdge(cn, "CancelMatch", -270f, -462f, 170f, 48f, 1f);
                MoveEdge(cn, "Volume", -128f, -465f, 0f, 0f, 1f);
                MoveEdge(cn, "Music", -63f, -465f, 0f, 0f, 1f);
                RectTransform ss = MoveEdge(cn, "SaveState", -585f, -465f, 0f, 0f, 1f);
                if (ss != null && ss.localScale.x != 0.7f)
                    ss.localScale = new Vector3(0.7f, 0.7f, 1f);
                RectTransform ssb = MoveEdge(cn, "SaveStateButtons", -450f, -465f, 0f, 0f, 1f);
                if (ssb != null && ssb.localScale.x != 0.7f)
                    ssb.localScale = new Vector3(0.7f, 0.7f, 1f);
                MoveEdge(cn, "P0HandCount", 115f, -500f, 0f, 0f, 0f);
                Move(cn, "P1HandCount", F + 103f, 165f, 0f, 0f);      // beside the dock
                Move(cn, "ActionActor", F + 298f, 0f, 0f, 0f);        // resolving card, over the mat

                Transform guide = cn.Find("GuideText");
                if (guide != null)
                {
                    RectTransform grt = guide as RectTransform;
                    A(grt, 1f);
                    grt.anchoredPosition = new Vector2(-334f, -95f);
                    grt.sizeDelta = new Vector2(560f, 48f);
                    TMP_Text gt = guide.GetComponent<TMP_Text>();
                    if (gt != null)
                        gt.alignment = TextAlignmentOptions.Center;
                }

                Transform prev = cn.Find("CardPreview");
                if (prev != null)
                {
                    // Shrunk just enough that the action rows stay visible while hovering.
                    RectTransform prt = prev as RectTransform;
                    A(prt, 1f);
                    prt.anchoredPosition = new Vector2(-334f, 140f);
                    prt.sizeDelta = new Vector2(400f, 562f);
                    if (prev.GetSiblingIndex() != cn.childCount - 1)
                        prev.SetAsLastSibling();
                }
            }
            catch { }
        }

        private static void Move(Transform cn, string name, float x, float y, float w, float h)
        {
            Transform t = cn.Find(name);
            if (t == null)
                return;
            RectTransform rt = t as RectTransform;
            if (rt == null)
                return;
            rt.anchoredPosition = new Vector2(x, y);
            if (w > 0f)
                rt.sizeDelta = new Vector2(w, h);
        }

        // Re-anchor to a screen edge (ax: 0 = left, 1 = right) with a centered pivot.
        private static void A(RectTransform rt, float ax)
        {
            if (rt.anchorMin.x != ax || rt.anchorMin.y != 0.5f)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(ax, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
            }
        }

        private static RectTransform MoveEdge(Transform cn, string name, float x, float y,
            float w, float h, float ax)
        {
            Transform t = cn.Find(name);
            if (t == null)
                return null;
            RectTransform rt = t as RectTransform;
            if (rt == null)
                return null;
            A(rt, ax);
            rt.anchoredPosition = new Vector2(x, y);
            if (w > 0f)
                rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private static void R(RectTransform rt, float x, float y, float w, float h)
        {
            A(rt, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void MoveBtn(Transform cn, string name, float x, float y, float w, float h,
            float fontSize, BtnKind kind)
        {
            RectTransform rt = MoveEdge(cn, name, x, y, w, h, 1f);
            if (rt != null)
                StyleAsButton(rt.gameObject, w, h, fontSize, kind);
        }

        // ------------------------------------------------------------- choice stack ---

        private static Sprite _sprEnd, _sprEndHover, _sprEndPress;
        private static Sprite _sprSec, _sprSecHover, _sprSecPress;
        private static Sprite _sprDanger, _sprDangerHover;

        private static void EnsureButtonSprites()
        {
            if (_sprEnd != null)
                return;
            _sprEnd = UISprites.RoundedRect(64, 64, 14f, Theme.WithA(Theme.Accent, 0.12f), Theme.Accent, 1f, 18f);
            _sprEndHover = UISprites.RoundedRect(64, 64, 14f, Theme.WithA(Theme.Accent, 0.2f), Theme.Accent, 1f, 18f);
            _sprEndPress = UISprites.RoundedRect(64, 64, 14f, Theme.WithA(Theme.Accent, 0.28f), Theme.Accent400, 1f, 18f);
            _sprSec = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Text, 0.03f), Theme.WithA(Theme.Text, 0.16f), 1f, 12f);
            _sprSecHover = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Text, 0.09f), Theme.WithA(Theme.Text, 0.28f), 1f, 12f);
            _sprSecPress = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Text, 0.14f), Theme.WithA(Theme.Text, 0.36f), 1f, 12f);
            _sprDanger = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Danger, 0.04f), Theme.Danger, 1f, 12f);
            _sprDangerHover = UISprites.RoundedRect(48, 48, 8f, Theme.WithA(Theme.Danger, 0.16f), Theme.Danger, 1f, 12f);
        }

        private static void StackChoices()
        {
            try
            {
                GameObject end = null;
                List<GameObject> rest = null;
                CollectChoice(_gls.go_ChoiceButton1, ref end, ref rest);
                CollectChoice(_gls.go_ChoiceButton2, ref end, ref rest);
                CollectChoice(_gls.go_ChoiceButton3, ref end, ref rest);
                CollectChoice(_gls.go_ChoiceButton4, ref end, ref rest);
                if (end != null)
                    PlaceChoice(end, -334f, -280f, 620f, 104f, true);
                if (rest != null)
                    for (int i = 0; i < rest.Count; i++)
                        PlaceChoice(rest[i], (i % 2 == 0) ? -489f : -179f, -188f + (i / 2) * 78f, 300f, 56f, false);
            }
            catch { }
        }

        private static void CollectChoice(GameObject b, ref GameObject end, ref List<GameObject> rest)
        {
            if (b == null || !b.activeSelf)
                return;
            ChoiceButtonScript cbs = b.GetComponent<ChoiceButtonScript>();
            if (cbs != null && cbs.myType == ButtonChoiceType.EndTurn && end == null)
            {
                end = b;
                return;
            }
            if (rest == null)
                rest = new List<GameObject>();
            rest.Add(b);
        }

        private static void PlaceChoice(GameObject b, float x, float y, float w, float h, bool primary)
        {
            RectTransform rt = b.GetComponent<RectTransform>();
            A(rt, 1f);
            if (rt.anchoredPosition.x != x || rt.anchoredPosition.y != y)
                rt.anchoredPosition = new Vector2(x, y);
            if (rt.sizeDelta.x != w)
                rt.sizeDelta = new Vector2(w, h);
            StyleAsButton(b, w, h, primary ? 30f : 18f, primary ? BtnKind.Primary : BtnKind.Secondary);
        }

        internal static void StyleAsButton(GameObject b, float w, float h, float fontSize, BtnKind kind)
        {
            EnsureButtonSprites();
            Sprite normal, hover, press;
            Color labelCol;
            if (kind == BtnKind.Primary)
            { normal = _sprEnd; hover = _sprEndHover; press = _sprEndPress; labelCol = Theme.Accent300; }
            else if (kind == BtnKind.Danger)
            { normal = _sprDanger; hover = _sprDangerHover; press = _sprDangerHover; labelCol = Theme.Danger; }
            else
            { normal = _sprSec; hover = _sprSecHover; press = _sprSecPress; labelCol = Theme.Text; }

            Image img = b.GetComponent<Image>();
            if (img != null && img.sprite != normal)
            {
                img.sprite = normal;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                Button btn = b.GetComponent<Button>();
                if (btn != null)
                {
                    btn.transition = Selectable.Transition.SpriteSwap;
                    btn.spriteState = new UnityEngine.UI.SpriteState
                    {
                        highlightedSprite = hover,
                        pressedSprite = press,
                        selectedSprite = normal,
                        disabledSprite = normal
                    };
                }
            }
            TMP_Text txt = b.GetComponentInChildren<TMP_Text>(true);
            if (txt != null)
            {
                // Labels must never intercept clicks — and on STRETCH-anchored children
                // sizeDelta is a margin, not a size: setting an absolute size there made
                // the invisible label rect overhang the button and steal its neighbors'
                // clicks (the "buttons are offset" bug).
                if (txt.raycastTarget)
                    txt.raycastTarget = false;
                if (txt.enableAutoSizing)
                    txt.enableAutoSizing = false;
                if (txt.fontSize != fontSize)
                    txt.fontSize = fontSize;
                if (txt.color != labelCol)
                    txt.color = labelCol;
                RectTransform trt = txt.rectTransform;
                bool stretch = trt.anchorMin.x != trt.anchorMax.x;
                Vector2 want = stretch ? new Vector2(-16f, -8f) : new Vector2(w - 16f, h - 8f);
                if (trt.sizeDelta != want)
                    trt.sizeDelta = want;
            }
        }

        private static RectTransform C(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        // ------------------------------------------------------------ overlay build ---

        private static void Build()
        {
            Theme.Ensure();
            _root = new GameObject("LogPoseBoardHUD", typeof(Canvas), typeof(CanvasScaler));
            Object.DontDestroyOnLoad(_root);
            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            Transform t = _root.transform;
            GameObject chip = W.Go("Chip", t);
            RectTransform crt = chip.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = new Vector2(0f, -6f);
            crt.sizeDelta = new Vector2(560f, 44f);
            Image bg = chip.AddComponent<Image>();
            bg.sprite = UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Surface, 0.75f),
                Theme.WithA(Theme.Text, 0.12f), 1f, 9f);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;

            _turn = W.Label(chip.transform, "TURN 1", 16f, 12f, 90f, 22f, 13f, Theme.Text, 600,
                TextAlignmentOptions.MidlineLeft, false, 0.14f);

            float x = 118f;
            for (int i = 0; i < Phases.Length; i++)
            {
                GameObject pill = W.Go("Pill" + Phases[i], chip.transform);
                W.TL(pill, x, 6f, 80f, 32f);
                Image pi = pill.AddComponent<Image>();
                pi.type = Image.Type.Sliced;
                pi.raycastTarget = false;
                _pills[i] = pi;
                _pillLabels[i] = W.Label(pill.transform, Phases[i], 0f, 0f, 80f, 32f, 13f,
                    Theme.TextMuted, 500, TextAlignmentOptions.Center);
                W.Fill(_pillLabels[i].gameObject);
                x += 86f;
            }

            _oppName = W.Label(t, "Opponent", 24f, 10f, 300f, 24f, 16f, Theme.Text, 500);
            _oppLeader = W.Label(t, "", 24f, 36f, 300f, 18f, 12f, Theme.TextMuted, 400,
                TextAlignmentOptions.TopLeft, true);
            GameObject op = W.Go("OppPips", t);
            W.TL(op, 330f, 18f, 260f, 24f);
            _oppPips = op.transform;

            _plName = W.Label(t, "You", 1424f, 10f, 296f, 24f, 16f, Theme.Text, 500,
                TextAlignmentOptions.TopRight);
            _plLeader = W.Label(t, "", 1424f, 36f, 296f, 18f, 12f, Theme.TextMuted, 400,
                TextAlignmentOptions.TopRight, true);
            GameObject pp = W.Go("PlPips", t);
            W.TL(pp, 1250f, 18f, 260f, 24f);
            _plPips = pp.transform;

            // Center line + whose-turn chip over the gap between the halves (2a).
            float matTL = 1920f * _fieldFromLeft / _canvasW;   // mat center in HUD coords
            _centerRule = W.Rule(t, matTL - 355f, 540f, 710f);
            GameObject cc = W.Go("CenterChip", t);
            _centerChipRt = W.TL(cc, matTL - 90f, 528f, 180f, 25f);
            _centerChip = cc.AddComponent<Image>();
            _centerChip.sprite = UISprites.RoundedRect(24, 24, 6f, Theme.WithA(Theme.Ground, 0.92f),
                Theme.WithA(Theme.Accent, 0.5f), 1f, 7f);
            _centerChip.type = Image.Type.Sliced;
            _centerChip.raycastTarget = false;
            _centerLabel = W.Label(cc.transform, "YOUR TURN", 0f, 0f, 180f, 25f, 11f,
                Theme.Accent300, 600, TextAlignmentOptions.Center, false, 0.16f);
            W.Fill(_centerLabel.gameObject);

            // Live pile counters over the outer bands (art carries the placards; these
            // carry the numbers). TL y: player band label row 866, opponent 210.
            _donP = Counter(t, 577f, 854f, 130f, TextAlignmentOptions.MidlineRight);
            _deckP = Counter(t, 717f, 854f, 100f, TextAlignmentOptions.Center);
            _trashP = Counter(t, 827f, 854f, 100f, TextAlignmentOptions.Center);
            _donO = Counter(t, 577f, 202f, 130f, TextAlignmentOptions.MidlineRight);
            _deckO = Counter(t, 717f, 202f, 100f, TextAlignmentOptions.Center);
            _trashO = Counter(t, 827f, 202f, 100f, TextAlignmentOptions.Center);

            _maxLife[0] = _maxLife[1] = 0;
            AdoptVanillaLabels();
            Plugin.Log.LogInfo("Board HUD built.");
        }

        private static TextMeshProUGUI Counter(Transform t, float x, float y, float w,
            TextAlignmentOptions align)
        {
            TextMeshProUGUI c = W.Label(t, "", x, y, w, 22f, 12f,
                Theme.WithA(Theme.Text, 0.6f), 600, align, true, 0.08f);
            c.enableWordWrapping = false;
            return c;
        }

        private static void RefreshCounters()
        {
            try
            {
                // The mat's on-screen position depends on the canvas width; keep the
                // mat-anchored overlay items seated over it.
                float hudX = 1920f * _fieldFromLeft / _canvasW;
                SetX(_centerRule != null ? _centerRule.rectTransform : null, hudX - 355f);
                SetX(_centerChipRt, hudX - 90f);
                SetX(_donP.rectTransform, hudX + 5f);
                SetX(_deckP.rectTransform, hudX + 145f);
                SetX(_trashP.rectTransform, hudX + 255f);
                SetX(_donO.rectTransform, hudX + 5f);
                SetX(_deckO.rectTransform, hudX + 145f);
                SetX(_trashO.rectTransform, hudX + 255f);

                if (_gls.Lps_Players == null || _gls.Lps_Players.Count < 2)
                    return;
                CounterTexts(0, _donP, _deckP, _trashP);
                CounterTexts(1, _donO, _deckO, _trashO);
            }
            catch { }
        }

        private static void SetX(RectTransform rt, float x)
        {
            if (rt != null && rt.anchoredPosition.x != x)
                rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
        }

        private static void CounterTexts(int seat, TMP_Text don, TMP_Text deck, TMP_Text trash)
        {
            PlayerState ps = _gls.Lps_Players[seat];
            int total = ps.Lgo_MyDonCostArea != null ? ps.Lgo_MyDonCostArea.Count : 0;
            int active = 0;
            if (ps.Lgo_MyDonCostArea != null)
                foreach (GameObject g in ps.Lgo_MyDonCostArea)
                {
                    if (g == null)
                        continue;
                    CardLogicScript cls = g.GetComponent<CardLogicScript>();
                    if (cls != null && !cls.myCard.bTapped)
                        active++;
                }
            don.text = total > 0 ? active + " / " + total + " ACTIVE" : "";
            deck.text = "DECK " + (ps.Lgo_MyDeck != null ? ps.Lgo_MyDeck.Count : 0);
            trash.text = "TRASH " + (ps.Lgo_MyTrash != null ? ps.Lgo_MyTrash.Count : 0);
        }

        // -------------------------------------------------------- vanilla label adopt --

        private static void AdoptVanillaLabels()
        {
            try
            {
                Transform side = _gls.cn_Canvas != null ? _gls.cn_Canvas.transform.Find("SideField") : null;
                if (side != null)
                {
                    Transform on = side.Find("Opponent/OpponentSideName");
                    Transform pn = side.Find("Player/PlayerSideName");
                    _vanOppName = on != null ? on.GetComponent<TMP_Text>() : null;
                    _vanPlName = pn != null ? pn.GetComponent<TMP_Text>() : null;
                    if (_vanOppName != null) _vanOppName.alpha = 0f;
                    if (_vanPlName != null) _vanPlName.alpha = 0f;

                    Transform ot = side.Find("Opponent/OpponentTimer");
                    Transform pt2 = side.Find("Player/PlayerTimer");
                    if (ot != null) PinTimer(ot, new Vector2(600f, -14f));
                    if (pt2 != null) PinTimer(pt2, new Vector2(1236f, -14f));
                }
                Transform tc = _gls.cn_Canvas != null ? _gls.cn_Canvas.transform.Find("Turn Counter") : null;
                _vanTurnCounter = tc != null ? tc.GetComponent<TMP_Text>() : null;
                if (_vanTurnCounter != null)
                    _vanTurnCounter.alpha = 0f;
            }
            catch { }
        }

        private static void PinTimer(Transform timer, Vector2 pos)
        {
            timer.SetParent(_root.transform, false);
            RectTransform rt = timer.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(100f, 30f);
            TMP_Text txt = timer.GetComponent<TMP_Text>();
            if (txt != null)
            {
                txt.fontSize = 17f;
                txt.color = Theme.Accent300;
                txt.alignment = TextAlignmentOptions.MidlineLeft;
            }
        }

        private static void RefreshSides()
        {
            try
            {
                if (_vanOppName != null && !string.IsNullOrEmpty(_vanOppName.text))
                    _oppName.text = _vanOppName.text;
                if (_vanPlName != null && !string.IsNullOrEmpty(_vanPlName.text))
                    _plName.text = _vanPlName.text;
                if (_gls.Lps_Players != null && _gls.Lps_Players.Count >= 2)
                {
                    _plLeader.text = LeaderId(0);
                    _oppLeader.text = LeaderId(1);
                    RefreshPips(_plPips, 0);
                    RefreshPips(_oppPips, 1);
                }
            }
            catch { }
        }

        private static string LeaderId(int seat)
        {
            var lgo = _gls.Lps_Players[seat].Lgo_MyLeader;
            if (lgo == null || lgo.Count == 0 || lgo[0] == null)
                return "";
            CardLogicScript cls = lgo[0].GetComponent<CardLogicScript>();
            if (cls == null || cls.myCard.cardDef == null)
                return "";
            string name = cls.myCard.cardDef.characterName;
            string id = cls.myCard.cardDef.cardID;
            return string.IsNullOrEmpty(name) ? id : name + " · " + id;
        }

        private static void RefreshPips(Transform holder, int seat)
        {
            int life = _gls.Lps_Players[seat].Lgo_MyLifeDeck != null ? _gls.Lps_Players[seat].Lgo_MyLifeDeck.Count : 0;
            if (life > _maxLife[seat])
                _maxLife[seat] = life;
            int max = Mathf.Clamp(_maxLife[seat], life, 10);
            if (holder.childCount != max)
            {
                for (int i = holder.childCount - 1; i >= 0; i--)
                    Object.Destroy(holder.GetChild(i).gameObject);
                for (int i = 0; i < max; i++)
                {
                    GameObject pip = W.Go("Pip" + i, holder);
                    W.TL(pip, i * 16f, 0f, 12f, 20f);
                    Image im = pip.AddComponent<Image>();
                    im.sprite = UISprites.RoundedRect(12, 20, 3f, Color.white, Color.clear, 0f, 0f);
                    im.raycastTarget = false;
                }
            }
            bool mirror = holder == _oppPips;
            for (int i = 0; i < holder.childCount; i++)
            {
                Image im = holder.GetChild(i).GetComponent<Image>();
                int idx = mirror ? i : holder.childCount - 1 - i;
                im.color = idx < life ? Theme.Accent400 : Theme.Edge;
            }
        }

        private static void Refresh()
        {
            try
            {
                string turnText = _gls.text_TurnCount != null ? (_gls.text_TurnCount.text ?? "") : "";
                string digits = "";
                foreach (char ch in turnText)
                    if (char.IsDigit(ch))
                        digits += ch;
                _turn.text = "TURN " + (digits.Length > 0 ? digits : "1");

                int active = PhaseIndex(_gls.e_CurrentState);
                for (int i = 0; i < _pills.Length; i++)
                {
                    bool on = i == active;
                    _pills[i].sprite = on
                        ? UISprites.RoundedRect(32, 32, 8f, Theme.WithA(Theme.Accent, 0.16f), Theme.Accent, 1f, 9f)
                        : UISprites.RoundedRect(32, 32, 8f, Color.clear, Color.clear, 0f, 9f);
                    _pillLabels[i].color = on ? Theme.Accent300 : Theme.TextMuted;
                }

                if (_centerLabel != null && _gls.gsv_CurrentGame != null)
                {
                    bool mine = _gls.gsv_CurrentGame.iPlayerAction == 0;
                    _centerLabel.text = mine ? "YOUR TURN" : "OPPONENT'S TURN";
                    _centerLabel.color = mine ? Theme.Accent300 : Theme.TextMuted;
                }
            }
            catch { }
        }

        private static int PhaseIndex(GameplayState s)
        {
            switch (s)
            {
                case GameplayState.PlayerTurn_Start:
                case GameplayState.PlayerTurn_StartWait:
                case GameplayState.PlayerTurn_Untap:
                    return 0;
                case GameplayState.PlayerTurn_DrawCard:
                    return 1;
                case GameplayState.PlayerTurn_DrawDon:
                    return 2;
                case GameplayState.EndingTurn:
                case GameplayState.EndTurnTrashingFilm:
                case GameplayState.EndTurnEqualDon:
                    return 4;
                default:
                    return 3;
            }
        }
    }
}
