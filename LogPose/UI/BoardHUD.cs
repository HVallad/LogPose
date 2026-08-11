using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose.UI
{
    // 2a's top-center HUD: TURN counter + five phase pills, floating in the empty strip
    // above the playmat. Display-only (no raycast targets), driven off the game state
    // each poll; hides itself outside of games.
    internal static class BoardHUD
    {
        private static readonly string[] Phases = { "Refresh", "Draw", "DON!!", "Main", "End" };
        private static GameObject _root;
        private static TextMeshProUGUI _turn;
        private static readonly Image[] _pills = new Image[5];
        private static readonly TextMeshProUGUI[] _pillLabels = new TextMeshProUGUI[5];
        private static GameplayLogicScript _gls;

        // 2a HUD side groups: names + leader ids + live life pips at the bar's edges.
        private static TextMeshProUGUI _oppName, _oppLeader, _plName, _plLeader;
        private static Transform _oppPips, _plPips;
        private static readonly int[] _maxLife = new int[2];
        private static TMP_Text _vanOppName, _vanPlName, _vanTurnCounter;

        internal static void Update()
        {
            if (Time.frameCount % 30 != 0)
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
            bool inGameAtAll = _gls.e_CurrentState != GameplayState.MainMenu;
            if (inGameAtAll)
                EnsureField();   // swap the mat from the first frame (mulligan included)
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
            }
        }

        // The game re-assigns the playmat sprites at every game start (leader-color
        // playsheets), so the design mat is re-asserted each poll rather than swapped once.
        private static Image _matP, _matO, _glowP, _glowO;

        private static void EnsureField()
        {
            try
            {
                Sprite mat = FieldMat.Get();
                if (mat == null || _gls.cn_Canvas == null)
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
                if (_matP != null && _matP.sprite != mat)
                    _matP.sprite = mat;
                if (_matO != null && _matO.sprite != mat)
                    _matO.sprite = mat;
                Color glow = Theme.WithA(Theme.Accent, 0.78f);
                if (_glowP != null && _glowP.color != glow)
                    _glowP.color = glow;
                if (_glowO != null && _glowO.color != glow)
                    _glowO.color = glow;
            }
            catch { }
        }

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

            // Side groups (2a): opponent left, player right, names over leader ids, pips beside.
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
            W.TL(pp, 1156f, 18f, 260f, 24f);
            _plPips = pp.transform;

            _maxLife[0] = _maxLife[1] = 0;
            AdoptVanillaLabels();
            Plugin.Log.LogInfo("Board HUD built.");
        }

        // The vanilla name labels keep receiving text from the game; mirror them into the
        // bar and hide the originals (plus the redundant floating turn counter). Timers are
        // reparented into the bar so the timed-lobby clock writes keep landing.
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
