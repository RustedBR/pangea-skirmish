using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PangeaSkirmish
{
    public class MainMenuManager : MonoBehaviour
    {
        private Font _font;
        private UI.MainMenuScreen _menuScreen;
        private UI.OptionsScreen _optionsScreen;
        private GameObject _mapSelectPanel;
        private RoomHUD _roomHUD;

        private static Color BtnNormal => Tuning.Get().uiButtonNormalColor;
        private static Color BtnActive => Tuning.Get().uiButtonActiveColor;

        // ══ Cores do tema (PangeaTheme.uss / GameTuning) — mantém constância visual ══
        private static Color ThemePanelBg       => new Color(0.039f, 0.063f, 0.157f, 0.92f); // pg-panel
        private static Color ThemeListBg        => new Color(0.04f, 0.06f, 0.16f, 0.85f);
        private static Color ThemeText          => new Color(0.933f, 0.949f, 1.0f);           // pg-text
        private static Color ThemeTextDim       => new Color(0.624f, 0.690f, 0.816f);         // pg-text-dim
        private static Color ThemeAccent        => new Color(0.416f, 0.663f, 1.0f);           // pg-accent (azul primário)
        private static Color ThemeGold          => Tuning.Get().uiTitleColor;                 // pg-gold
        private static Color ThemeDanger        => new Color(1.0f, 0.353f, 0.353f);           // pg-danger (vermelho)
        private static Color ThemePanelBorder   => new Color(0.165f, 0.227f, 0.416f);         // pg-panel-border
        private static Color ThemeButtonBg      => new Color(0.071f, 0.102f, 0.220f, 0.96f);  // pg-panel-bg-2 (botão normal)
        private static Color ThemeConfirmText   => new Color(0.03f, 0.055f, 0.13f);            // texto escuro p/ botão primário

        private void Start()
        {
            if (AudioManager.I == null) new GameObject("AudioManager", typeof(AudioManager));
            AudioManager.I?.PlayMusic(AudioManager.I.bgmMenu);
            if (RuntimeTuning.Active == null)
            {
                var src = Resources.Load<GameTuning>("GameTuning");
                if (src != null) RuntimeTuning.Active = src;
            }
            AttributeStats.Formulas = (RuntimeTuning.Active ?? new GameTuning()).statFormulas;

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Tuning.Get().menuBackgroundColor;
            cam.orthographic = true;

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<InputSystemUIInputModule>();
            }

            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BuildMenuPanel(canvas.transform);
            BuildMapSelectPanel(canvas.transform);
            BuildMultiplayerHUD(canvas.transform);
            ShowMenu();

#if UNITY_WEBGL
            // AUTO-DIAG: no browser, dispara o fluxo de Host sozinho após 2s para
            // capturar os logs [MP-DIAG] no <pre id="mpdiag"> e identificar o abort.
            Debug.Log("[MP-DIAG][MainMenu] AUTO-DIAG WebGL: agendando OnClickCreateRoom em 2s");
            Invoke("AutoDiagMp", 2f);
#endif
        }

#if UNITY_WEBGL
        private void AutoDiagMp()
        {
            MpDiag.Log("MainMenu", "AUTO-DIAG WebGL: ShowMultiplayer + OnClickCreateRoom");
            ShowMultiplayer();
            if (_roomHUD != null) _roomHUD.OnClickCreateRoom();
        }
#endif

        // ---------------------------------------------------------------- MENU

        private void BuildMenuPanel(Transform parent)
        {
            // Menu principal migrado para UI Toolkit (Resources/UI/Screens/MainMenu.uxml).
            // Jogo 100% multiplayer: os antigos botões single-player ("Começar o Jogo" e
            // "Modo Sandbox") saíram do menu. As ações "Criar Mapa" e "Criar Personagem"
            // abrem uma sala loopback SOLO (LocalContentLauncher) que carrega a cena real
            // de edição offline — fiel à experiência multiplayer.
            _menuScreen = UI.PangeaScreen.Spawn<UI.MainMenuScreen>("MainMenuScreen");
            _menuScreen.OnMultiplayer     = ShowMultiplayer;
            _menuScreen.OnCreateMap       = StartLocalMapEditor;
            _menuScreen.OnCreateCharacter = StartLocalCharEditor;
            _menuScreen.OnOptions         = ShowOptions;
            _menuScreen.OnQuit            = () => Application.Quit();

            _optionsScreen = UI.PangeaScreen.Spawn<UI.OptionsScreen>("OptionsScreen");
            _optionsScreen.OnBack = ShowMenu;
            _optionsScreen.SetVisible(false);
        }

        private void ShowOptions()
        {
            _menuScreen.SetVisible(false);
            _optionsScreen.SetVisible(true);
        }

        private void ShowMapSelect()
        {
            _menuScreen.SetVisible(false);
            RebuildMapSelectPanel();
            _mapSelectPanel.SetActive(true);
        }

        private void StartLocalMapEditor()
        {
            _menuScreen.SetVisible(false);
            LocalContentLauncher.Launch("map");
        }

        private void StartLocalCharEditor()
        {
            _menuScreen.SetVisible(false);
            LocalContentLauncher.Launch("char");
        }

        private void ShowMenu()
        {
            _menuScreen.SetVisible(true);
            _optionsScreen?.SetVisible(false);
            _mapSelectPanel.SetActive(false);
            _roomHUD?.HideAll();
        }

        private void ShowMultiplayer()
        {
            _menuScreen.SetVisible(false);
            _mapSelectPanel.SetActive(false);
            _roomHUD?.ShowLobbyPanel();
        }

        private void BuildMultiplayerHUD(Transform canvasTransform)
        {
            var go = new GameObject("RoomHUD", typeof(RectTransform));
            go.transform.SetParent(canvasTransform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            _roomHUD = go.AddComponent<RoomHUD>();
            _roomHUD.Init(canvasTransform, _font);
            _roomHUD.OnBackToMenu += ShowMenu;
        }

        // ---------------------------------------------------------------- MAP SELECT

        private void BuildMapSelectPanel(Transform parent)
        {
            _mapSelectPanel = MakeFullPanel(parent, "MapSelectPanel");
            _mapSelectPanel.AddComponent<Image>().color = ThemePanelBg;

            MakeLabel(_mapSelectPanel.transform, new Vector2(0, 455), new Vector2(800, 60), 40,
                Tuning.Get().uiTitleColor).text = "Escolher Mapa";
            RebuildMapSelectPanel();
        }

        private void RebuildMapSelectPanel()
        {
            foreach (Transform child in _mapSelectPanel.transform)
            {
                if (child.name != "Lbl" && child.name != "Viewport")
                    Destroy(child.gameObject);
            }

            var vpTransform = _mapSelectPanel.transform.Find("Viewport");
            RectTransform viewportRt;
            RectTransform contentRt;
            Transform contentTransform;

            if (vpTransform == null)
            {
                var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
                vpGo.transform.SetParent(_mapSelectPanel.transform, false);
                viewportRt = vpGo.GetComponent<RectTransform>();
                viewportRt.anchorMin = new Vector2(0.5f, 0.5f);
                viewportRt.anchorMax = new Vector2(0.5f, 0.5f);
                viewportRt.pivot = new Vector2(0.5f, 0.5f);
                viewportRt.sizeDelta = new Vector2(440, 490);
                viewportRt.anchoredPosition = new Vector2(0, -10);
                vpGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);

                var cGo = new GameObject("Content", typeof(RectTransform));
                cGo.transform.SetParent(viewportRt, false);
                contentRt = cGo.GetComponent<RectTransform>();
                contentRt.anchorMin = new Vector2(0, 1);
                contentRt.anchorMax = new Vector2(1, 1);
                contentRt.pivot = new Vector2(0.5f, 1);
                contentRt.anchoredPosition = Vector2.zero;
                contentRt.sizeDelta = new Vector2(0, 0);
                contentTransform = cGo.transform;

                var vlg = cGo.AddComponent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = true;
                vlg.spacing = 6;
                vlg.padding = new RectOffset(6, 6, 4, 4);
                cGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.MinSize;

                var sr = vpGo.AddComponent<ScrollRect>();
                sr.viewport = viewportRt;
                sr.content = contentRt;
                sr.vertical = true;
                sr.horizontal = false;
                sr.movementType = ScrollRect.MovementType.Clamped;
                sr.inertia = true;
                sr.decelerationRate = 0.05f;

                var sbGo = new GameObject("Scrollbar", typeof(RectTransform));
                sbGo.transform.SetParent(viewportRt, false);
                var sbRt = sbGo.GetComponent<RectTransform>();
                sbRt.anchorMin = new Vector2(1, 0);
                sbRt.anchorMax = new Vector2(1, 1);
                sbRt.pivot = new Vector2(1, 0.5f);
                sbRt.sizeDelta = new Vector2(10, 0);
                sbRt.anchoredPosition = Vector2.zero;
                var sb = sbGo.AddComponent<Scrollbar>();
                sb.direction = Scrollbar.Direction.TopToBottom;
                sbGo.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.30f);
                var sbHandle = new GameObject("Handle", typeof(RectTransform));
                sbHandle.transform.SetParent(sbGo.transform, false);
                sbHandle.GetComponent<RectTransform>().anchorMin = Vector2.zero;
                sbHandle.GetComponent<RectTransform>().anchorMax = Vector2.one;
                sbHandle.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
                sbHandle.AddComponent<Image>().color = new Color(0.45f, 0.45f, 0.50f);
                sb.handleRect = sbHandle.GetComponent<RectTransform>();
                sr.verticalScrollbar = sb;
                sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            }
            else
            {
                viewportRt = vpTransform.GetComponent<RectTransform>();
                contentTransform = vpTransform.Find("Content");
                contentRt = contentTransform.GetComponent<RectTransform>();
                foreach (Transform c in contentTransform) Destroy(c.gameObject);
            }

            var mapNames = MapStorage.ListMapNames();

            var btnDefault = MakeBtn(contentTransform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(420, 48));
            btnDefault.GetComponent<Image>().color = ThemeButtonBg;
            MakeLabel(btnDefault.transform, Vector2.zero, new Vector2(420, 48), 24,
                ThemeText).text = "▶ Batalha Padrão";
            btnDefault.onClick.AddListener(() =>
            {
                RuntimeMap.Selected = null;
                SceneManager.LoadScene("Battle");
            });

            foreach (var mapName in mapNames)
            {
                var nameCopy = mapName;
                var row = new GameObject("MapRow", typeof(RectTransform));
                row.transform.SetParent(contentTransform, false);
                row.GetComponent<RectTransform>().sizeDelta = new Vector2(420, 48);
                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 6;
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;

                var play = MakeBtn(row.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250, 48));
                play.GetComponent<Image>().color = ThemeButtonBg;
                play.gameObject.AddComponent<LayoutElement>().preferredWidth = 250;
                MakeLabel(play.transform, Vector2.zero, new Vector2(240, 44), 22, ThemeText).text = "▶ " + nameCopy;
                play.onClick.AddListener(() =>
                {
                    RuntimeMap.Selected = MapStorage.Load(nameCopy);
                    SceneManager.LoadScene("Battle");
                });

                var edit = MakeBtn(row.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80, 48));
                edit.GetComponent<Image>().color = ThemeButtonBg;
                edit.gameObject.AddComponent<LayoutElement>().preferredWidth = 80;
                MakeLabel(edit.transform, Vector2.zero, new Vector2(76, 44), 18, ThemeText).text = "Editar";
                edit.onClick.AddListener(() =>
                {
                    RuntimeSandbox.MapToEdit = MapStorage.Load(nameCopy);
                    SceneManager.LoadScene("Sandbox");
                });

                var del = MakeBtn(row.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70, 48));
                del.GetComponent<Image>().color = ThemeButtonBg;
                del.gameObject.AddComponent<LayoutElement>().preferredWidth = 70;
                MakeLabel(del.transform, Vector2.zero, new Vector2(66, 44), 18, ThemeDanger).text = "Apagar";
                del.onClick.AddListener(() =>
                {
                    MapStorage.Delete(nameCopy);
                    RebuildMapSelectPanel();
                });
            }

            var btnBack = MakeBtn(_mapSelectPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0, 60), new Vector2(220, 55));
            btnBack.GetComponent<Image>().color = ThemeButtonBg;
            MakeLabel(btnBack.transform, Vector2.zero, new Vector2(220, 55), 24,
                ThemeDanger).text = "← Voltar";
            btnBack.onClick.AddListener(ShowMenu);
        }

        // ---------------------------------------------------------------- HELPERS

        private GameObject MakeFullPanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go;
        }

        private void MakeMenuBtn(Transform parent, string label, Vector2 pos, Action onClick)
        {
            var btn = MakeBtn(parent, new Vector2(0.5f, 0.5f), pos, new Vector2(370, 65));
            var img = btn.GetComponent<Image>();
            var baseColor = ThemeButtonBg;
            UiSkin.ApplyButtonSkin(img, baseColor);
            MakeLabel(btn.transform, Vector2.zero, new Vector2(370, 65), 28, ThemeText).text = label;
            btn.onClick.AddListener(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); onClick?.Invoke(); });
        }

        private Button MakeBtn(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = BtnNormal;
            return go.AddComponent<Button>();
        }

        private Text MakeLabel(Transform parent, Vector2 pos, Vector2 size, int fontSize, Color color)
        {
            var go = new GameObject("Lbl", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private InputField MakeInputField(Transform parent, Vector2 pos, Vector2 size, string initial)
        {
            var go = new GameObject("InputField", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.20f);
            var input = go.AddComponent<InputField>();

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10, 0); trt.offsetMax = new Vector2(-10, 0);
            var txt = textGo.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 18; txt.alignment = TextAnchor.MiddleLeft;
            txt.color = Color.white; txt.supportRichText = false;

            input.textComponent = txt;
            input.text = initial;
            go.SetActive(true);
            return input;
        }
    }
}
