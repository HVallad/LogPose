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
            bool inGame = _gls.e_CurrentState != GameplayState.MainMenu
                && _gls.gsv_CurrentGame != null
                && _gls.gsv_CurrentGame.iTurnNumber >= 1;
            if (inGame && _root == null)
                Build();
            if (_root == null)
                return;
            if (_root.activeSelf != inGame)
                _root.SetActive(inGame);
            if (inGame)
                Refresh();
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
            Plugin.Log.LogInfo("Board HUD built.");
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
