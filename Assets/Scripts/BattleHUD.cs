using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace PangeaSkirmish
{
    public struct LogEntry
    {
        public string displayText;
        public string tooltipText;
        public Team   team;
        public Unit   unit; // unidade associada (câmera + inspect ao clicar)
    }

    /// <summary>
    /// UI do combate — tema "Final Fantasy Tactics" (janelas azuis com moldura clara):
    ///   Topo          — fase e timer
    ///   Esquerda      — histórico de batalha (seções recolhíveis por round)
    ///   Inferior esq. — status do personagem (retrato + HP + PA/PAB + atributos)
    ///   Centro inf.   — fila de ações (ordem) com incremento
    ///   Inferior dir. — menu de comandos vertical (Mover / Atacar / Confirmar)
    /// </summary>
    public class BattleHUD : MonoBehaviour
    {
        private const float HUNDO = 38f; // linha de Desfazer/Limpar

        private Font   _font;
        private Sprite _fftSprite; // gradiente azul escuro das janelas
        private Canvas _canvas;

        // ── Topo ────────────────────────────
        private Text _phaseText;
        private Text _timerText;
        private Text _cameraModeText;

        // ── Esquerda: histórico de batalha ───
        private ScrollRect    _logScrollRect;
        private RectTransform _logContentRT;

        private readonly List<LogEntry> _logEntries = new List<LogEntry>();

        private sealed class LogLine
        {
            public GameObject go;
            public int   entryIdx;   // índice em _logEntries; -1 = header ou spacer
            public bool  isHeader;
            public int   sectionIdx;
            public Image hoverBg;
            public Unit  unit;
        }
        private readonly List<LogLine> _logLines = new List<LogLine>();

        private sealed class LogSection
        {
            public bool expanded = true;
            public Text chevron;
            public readonly List<int> lineIndices = new List<int>();
        }
        private readonly List<LogSection> _sections = new List<LogSection>();

        public Action<Unit> OnLogLineClicked;
        public Action<int> OnSeqChipToggled; // toggle BAP no chip clicado
        public Action OnUndoClicked;
        public Action OnClearClicked;

        // Tooltip
        private GameObject _tooltipGo;
        private Text       _tooltipTxt;
        private int        _hoveredLineIdx = -1;

        // ── Status do personagem ─────────────
        private Unit          _inspectedUnit;
        private GameObject    _statusContent;
        private Image         _portraitImg;
        private GameObject    _hpBarBg;
        private RectTransform _hpBarFillRT;
        private Image         _hpBarFillImg;
        private Text _unitNameText;
        private Text _unitHPText;
        private Text _apText;
        private Text _unitStatsText;
        private Text _unitHintText;

        // ── Menu de comandos ─────────────────
        private GameObject _actionBar;
        private Button _moveButton;
        private Image  _moveImg;
        private Text   _moveLabel;
        private Button _confirmButton;

        // ── Fila de sequência de ações ───────
        private GameObject _seqBar;
        private RectTransform _seqContentRT;
        private readonly List<GameObject> _seqChips = new List<GameObject>();
        private readonly List<Image> _seqChipImages = new List<Image>();
        private readonly List<Color> _seqChipBaseColors = new List<Color>();
        private int _selectedSeqIndex = -1;

        // ── Prompt de bônus ─────────────────
        private GameObject _promptPanel;
        private Text   _promptText;
        private Text   _bonusTimerText;
        private Button _simButton;
        private Button _naoButton;

        // ── Tela de fim ──────────────────────
        private GameObject _endPanel;
        private Text _endText;

        // ── Cores ── (as principais vêm do GameTuning, seção "HUD: tema")
        private static Color CorNormal     => Tuning.Get().hudButtonNormalColor;
        private static Color CorAtivo      => Tuning.Get().hudButtonActiveColor;
        private static Color CorConfirmado => Tuning.Get().hudButtonConfirmedColor;
        private static Color CorDisabled   => Tuning.Get().hudButtonDisabledColor;
        private static readonly Color CorConfirmBtn = new Color(0.14f, 0.42f, 0.22f);
        private static Color CorPlayer     => Tuning.Get().logPlayerColor;
        private static Color CorEnemy      => Tuning.Get().logEnemyColor;
        private static Color CorSystem     => Tuning.Get().logSystemColor;
        private static Color CorHover      => Tuning.Get().logHoverColor;
        private static readonly Color CorRoundHdr   = new Color(0.10f, 0.16f, 0.34f, 1.00f);
        private static Color CorChipMove   => Tuning.Get().seqChipMoveColor;
        private static Color CorChipAtk    => Tuning.Get().seqChipAttackColor;
        private static readonly Color CorChipBonus  = new Color(1.00f, 0.95f, 0.45f, 1.00f); // contraste: amarelo claro
        private static Color CorChipMoveBn => Tuning.Get().seqChipMoveBonusColor; // move + BAP: roxo
        private static Color CorChipAtkBn  => Tuning.Get().seqChipAttackBonusColor; // atk + BAP: laranja

        // Tema FFT (azul escuro)
        private static Color CorFFTtop    => Tuning.Get().windowGradientTopColor;
        private static Color CorFFTbot    => Tuning.Get().windowGradientBottomColor;
        private static Color CorFFTBorda  => Tuning.Get().windowBorderColor;
        private static readonly Color CorFFTTitulo = new Color(0.74f, 0.85f, 1.00f);
        private static readonly Color CorFFTTitBg  = new Color(0.16f, 0.24f, 0.46f, 1.00f);

        // ─────────────────────────────────────
        public void Build(Canvas canvas)
        {
            _canvas = canvas;
            _font   = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildFFTSprite();
            var root = canvas.transform;

            BuildTopBar(root);
            BuildBattleLog(root);
            BuildTooltip(root);
            BuildStatusPanel(root);
            BuildCommandMenu(root);
            BuildActionSequenceBar(root);
            BuildPromptPanel(root);
            BuildEndPanel(root);
            BuildCameraIndicator(root);
        }

        // ── TEMA FFT ─────────────────────────
        private void BuildFFTSprite()
        {
            const int h = 48;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / (h - 1); // y=0 base, y=h-1 topo
                tex.SetPixel(0, y, Color.Lerp(CorFFTbot, CorFFTtop, t));
            }
            tex.Apply();
            _fftSprite = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Aplica o visual de janela FFT (gradiente azul + moldura clara) a um GameObject.</summary>
        // Painéis com a moldura pixel-art gerada aplicada, p/ permitir reaplicar ao vivo (RefreshUiSkin).
        // "go" próprio vira só a MÁSCARA (forma arredondada, invisível) + o Mask; o gradiente
        // real mora no filho "Fill" e é recortado por ela — senão o preenchimento continua
        // um retângulo quadrado por trás do anel decorativo (o canto "parece quadrado").
        private readonly List<Image> _windowFrames = new List<Image>();
        private readonly List<Image> _windowMasks  = new List<Image>();

        private Image StyleFFTWindow(GameObject go, bool border = true)
        {
            var T = Tuning.Get();

            // Fill: o gradiente FFT em si, num filho stretch, clipado pela máscara do pai.
            var fillTr = go.transform.Find("Fill");
            GameObject fillGo;
            if (fillTr == null)
            {
                fillGo = new GameObject("Fill", typeof(RectTransform));
                fillGo.transform.SetParent(go.transform, false);
                fillGo.transform.SetAsFirstSibling();
                var frt = fillGo.GetComponent<RectTransform>();
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            }
            else fillGo = fillTr.gameObject;
            var fillImg = fillGo.GetComponent<Image>();
            if (fillImg == null) fillImg = fillGo.AddComponent<Image>();
            fillImg.sprite = _fftSprite;
            fillImg.type   = Image.Type.Simple;
            fillImg.color  = Color.white;
            fillImg.raycastTarget = false;

            // Imagem do "go" raiz: só a forma arredondada (shape da máscara), sem gradiente.
            var maskImg = go.GetComponent<Image>();
            if (maskImg == null) maskImg = go.AddComponent<Image>();
            var maskComp = go.GetComponent<Mask>();

            if (T.windowFrameEnabled)
            {
                maskImg.sprite = UiSkin.GeneratedRoundedMask(T.windowFrameCorner);
                maskImg.type   = Image.Type.Sliced;
                maskImg.color  = Color.white;
                if (maskComp == null) maskComp = go.AddComponent<Mask>();
                maskComp.enabled = true;
                maskComp.showMaskGraphic = false; // a forma em si fica invisível, só recorta o Fill
                if (!_windowMasks.Contains(maskImg)) _windowMasks.Add(maskImg);
            }
            else
            {
                maskImg.sprite = null;
                maskImg.type   = Image.Type.Simple;
                maskImg.color  = new Color(0, 0, 0, 0); // sem máscara, "go" fica invisível (Fill mostra o quadrado normal)
                if (maskComp != null) maskComp.enabled = false;
            }

            if (border)
            {
                var ol = go.GetComponent<Outline>();
                if (ol == null) ol = go.AddComponent<Outline>();

                var frameTr = go.transform.Find("Frame");
                if (T.windowFrameEnabled)
                {
                    // A moldura nova (contorno nítido) substitui a sombra diagonal antiga do Outline.
                    ol.enabled = false;
                    Image frameImg;
                    if (frameTr == null)
                    {
                        var frameGo = new GameObject("Frame", typeof(RectTransform));
                        frameGo.transform.SetParent(go.transform, false);
                        var frt = frameGo.GetComponent<RectTransform>();
                        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
                        frameImg = frameGo.AddComponent<Image>();
                        frameImg.raycastTarget = false;
                        _windowFrames.Add(frameImg);
                    }
                    else
                    {
                        frameTr.gameObject.SetActive(true);
                        frameImg = frameTr.GetComponent<Image>();
                    }
                    frameImg.transform.SetAsLastSibling(); // sempre por cima do Fill
                    frameImg.sprite = UiSkin.GeneratedWindowFrame(T.windowFrameCorner, T.windowFrameBorderPx,
                        T.windowFrameBorderColor, T.windowFrameHighlightColor);
                    frameImg.type  = Image.Type.Sliced;
                    frameImg.color = Color.white;
                }
                else
                {
                    ol.enabled = true;
                    ol.effectColor    = CorFFTBorda;
                    ol.effectDistance = new Vector2(2f, -2f);
                    if (frameTr != null) frameTr.gameObject.SetActive(false);
                }
            }
            return maskImg;
        }

        /// <summary>Reaplica a moldura/máscara de painel (raio/cor) em todos os painéis já criados.</summary>
        private void RefreshWindowFrames()
        {
            var T = Tuning.Get();
            _windowFrames.RemoveAll(f => f == null);
            _windowMasks.RemoveAll(m => m == null);

            foreach (var maskImg in _windowMasks)
            {
                var maskComp = maskImg.GetComponent<Mask>();
                if (T.windowFrameEnabled)
                {
                    maskImg.sprite = UiSkin.GeneratedRoundedMask(T.windowFrameCorner);
                    maskImg.type   = Image.Type.Sliced;
                    maskImg.color  = Color.white;
                    if (maskComp == null) maskComp = maskImg.gameObject.AddComponent<Mask>();
                    maskComp.enabled = true;
                    maskComp.showMaskGraphic = false;
                }
                else
                {
                    maskImg.sprite = null;
                    maskImg.color  = new Color(0, 0, 0, 0);
                    if (maskComp != null) maskComp.enabled = false;
                }
            }

            foreach (var frameImg in _windowFrames)
            {
                var ol = frameImg.transform.parent.GetComponent<Outline>();
                if (T.windowFrameEnabled)
                {
                    if (ol != null) ol.enabled = false;
                    frameImg.gameObject.SetActive(true);
                    frameImg.sprite = UiSkin.GeneratedWindowFrame(T.windowFrameCorner, T.windowFrameBorderPx,
                        T.windowFrameBorderColor, T.windowFrameHighlightColor);
                    frameImg.type = Image.Type.Sliced;
                }
                else
                {
                    frameImg.gameObject.SetActive(false);
                    if (ol != null) { ol.enabled = true; ol.effectColor = CorFFTBorda; ol.effectDistance = new Vector2(2f, -2f); }
                }
            }
        }

        // ── TOPO: fase + timer ───────────────
        private void BuildTopBar(Transform root)
        {
            // 15% menor que o original (420×60 → 357×51)
            var win = new GameObject("TopBar", typeof(RectTransform));
            win.transform.SetParent(root, false);
            var rt = win.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -10);
            rt.sizeDelta = new Vector2(357, 51);
            StyleFFTWindow(win);

            // Fase: lado esquerdo, centralizado verticalmente
            _phaseText = MakeText(win.transform, "Phase", new Vector2(0f, 0.5f),
                new Vector2(14, 0), new Vector2(240, 43), 15, TextAnchor.MiddleLeft);
            _phaseText.fontStyle = FontStyle.Bold;

            // Timer: lado direito, mesmo Y da fase
            _timerText = MakeText(win.transform, "Timer", new Vector2(1f, 0.5f),
                new Vector2(-14, 0), new Vector2(95, 43), 17, TextAnchor.MiddleRight);
            _timerText.color = Tuning.Get().timerNormalColor;
            _timerText.fontStyle = FontStyle.Bold;
        }

        // ── INDICADOR DE MODO CÂMERA ────────
        private void BuildCameraIndicator(Transform root)
        {
            var go = new GameObject("CameraMode", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-10, -10);
            rt.sizeDelta = new Vector2(110, 24);

            var img = go.AddComponent<Image>();
            img.sprite = _fftSprite;
            img.color = new Color(1f, 1f, 1f, Tuning.Get().cameraIndicatorBgAlpha);

            _cameraModeText = MakeText(go.transform, "Auto", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(110, 24), 11, TextAnchor.MiddleCenter);
            _cameraModeText.color = Tuning.Get().cameraAutoColor;
            _cameraModeText.fontStyle = FontStyle.Bold;

            // Tooltip no hover
            var trigger = go.AddComponent<EventTrigger>();
            var enterEntry = new EventTrigger.Entry();
            enterEntry.eventID = EventTriggerType.PointerEnter;
            enterEntry.callback.AddListener(_ => ShowCameraTooltip());
            trigger.triggers.Add(enterEntry);

            var exitEntry = new EventTrigger.Entry();
            exitEntry.eventID = EventTriggerType.PointerExit;
            exitEntry.callback.AddListener(_ => HideCameraTooltip());
            trigger.triggers.Add(exitEntry);
        }

        public void SetCameraMode(CameraMode mode)
        {
            if (_cameraModeText == null) return;
            bool isAuto = mode == CameraMode.Auto;
            _cameraModeText.text = isAuto ? "Auto" : "Manual";
            var T = Tuning.Get();
            _cameraModeText.color = isAuto ? T.cameraAutoColor : T.cameraManualColor;
        }

        private void ShowCameraTooltip()
        {
            if (_tooltipGo == null || _tooltipTxt == null) return;
            _tooltipGo.SetActive(true);
            _tooltipTxt.text = "Câmera automática segue as ações.\n" +
                               $"Arrastar/zoom ativa modo Manual ({Tuning.Get().camManualTimeout:0.#}s).";
            var rt = _tooltipGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-10, -40);
            rt.sizeDelta = new Vector2(220, 36);
        }

        private void HideCameraTooltip()
        {
            if (_tooltipGo != null) _tooltipGo.SetActive(false);
        }

        // ── ESQUERDA: histórico ──────────────
        private void BuildBattleLog(Transform root)
        {
            var panel = new GameObject("BattleLogPanel", typeof(RectTransform));
            panel.transform.SetParent(root, false);
            var prt = panel.GetComponent<RectTransform>();
            // Estica verticalmente: do topo (abaixo da TopBar) até acima do painel de status,
            // deixando uma margem. Assim o log encolhe/cresce com a tela sem invadir o status.
            prt.anchorMin = new Vector2(0, 0);
            prt.anchorMax = new Vector2(0, 1);
            prt.pivot     = new Vector2(0, 1);
            // Base = topo do painel UNIDADE (12 de margem + a altura dele, que é dinâmica)
            // + um respiro visível, pra nunca encostar nele por mais que ele cresça.
            const float gapAboveStatus = 16f;
            float logBottom = 12f + StatusPanelHeight() + gapAboveStatus;
            prt.offsetMin = new Vector2(12, logBottom);
            prt.offsetMax = new Vector2(312, -82);  // largura 300 · topo a 82 do alto
            StyleFFTWindow(panel).raycastTarget = true;

            MakeTituloPainel(panel.transform, "HISTÓRICO DE BATALHA");

            _logScrollRect = panel.AddComponent<ScrollRect>();
            _logScrollRect.horizontal      = false;
            _logScrollRect.vertical        = true;
            _logScrollRect.scrollSensitivity = Tuning.Get().logScrollSensitivity;
            _logScrollRect.movementType    = ScrollRect.MovementType.Clamped;
            _logScrollRect.inertia         = false;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(panel.transform, false);
            var vpRT = viewportGo.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = new Vector2(6, 6);
            vpRT.offsetMax = new Vector2(-6, -(TitleZoneHeight() + 4f));
            viewportGo.AddComponent<RectMask2D>();
            _logScrollRect.viewport = vpRT;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            _logContentRT = contentGo.GetComponent<RectTransform>();
            _logContentRT.anchorMin = Vector2.zero;
            _logContentRT.anchorMax = new Vector2(1, 0);
            _logContentRT.pivot     = new Vector2(0.5f, 0);
            _logContentRT.anchoredPosition = Vector2.zero;
            _logContentRT.sizeDelta = Vector2.zero;

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment       = TextAnchor.UpperLeft;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight   = true;
            vlg.childControlWidth    = true;
            vlg.spacing = 0;
            vlg.padding = new RectOffset(4, 4, 2, 2);
            contentGo.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            _logScrollRect.content = _logContentRT;
        }

        private void BuildTooltip(Transform root)
        {
            _tooltipGo = new GameObject("LogTooltip", typeof(RectTransform));
            _tooltipGo.transform.SetParent(root, false);
            var ttRT = _tooltipGo.GetComponent<RectTransform>();
            ttRT.anchorMin = Vector2.zero;
            ttRT.anchorMax = Vector2.zero;
            ttRT.pivot     = new Vector2(0, 1);
            ttRT.sizeDelta = new Vector2(260, 40);
            StyleFFTWindow(_tooltipGo);

            var tgo = new GameObject("Tip", typeof(RectTransform));
            tgo.transform.SetParent(_tooltipGo.transform, false);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(6, 3);
            trt.offsetMax = new Vector2(-6, -3);
            _tooltipTxt = tgo.AddComponent<Text>();
            _tooltipTxt.font                = _font;
            _tooltipTxt.fontSize            = 12;
            _tooltipTxt.color               = new Color(0.92f, 0.94f, 1f);
            _tooltipTxt.horizontalOverflow  = HorizontalWrapMode.Wrap;
            _tooltipTxt.verticalOverflow    = VerticalWrapMode.Overflow;
            _tooltipTxt.supportRichText     = true;
            _tooltipTxt.raycastTarget       = false;

            _tooltipGo.SetActive(false);
        }

        // ── INFERIOR ESQUERDA: status ────────
        /// <summary>Altura do painel UNIDADE — cresce com a zona do título/moldura pra caber o
        /// conteúdo sem cortar embaixo (180 é o mínimo original, calibrado pro título antigo).
        /// Extraído p/ o painel de HISTÓRICO poder posicionar seu rodapé sem se sobrepor.</summary>
        private static float StatusPanelHeight()
        {
            float statusBaseY = TitleZoneHeight() + 8f;
            return Mathf.Max(180f, statusBaseY + 94f + 56f + 12f + FrameBottomPad());
        }

        private void BuildStatusPanel(Transform root)
        {
            var panel = new GameObject("StatusPanel", typeof(RectTransform));
            panel.transform.SetParent(root, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0, 0);
            prt.pivot = new Vector2(0, 0);
            prt.anchoredPosition = new Vector2(12, 12);
            prt.sizeDelta = new Vector2(360, StatusPanelHeight());
            StyleFFTWindow(panel);

            MakeTituloPainel(panel.transform, "UNIDADE");

            _statusContent = new GameObject("Content", typeof(RectTransform));
            _statusContent.transform.SetParent(panel.transform, false);
            var crt = _statusContent.GetComponent<RectTransform>();
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            // Pequeno respiro nas bordas pra o conteúdo não encostar na moldura arredondada.
            crt.offsetMin = new Vector2(6, 6);
            crt.offsetMax = new Vector2(-6, -6);
            var inner = _statusContent.transform;
            float baseY = TitleZoneHeight() + 8f;

            // retrato (sprite da unidade) com moldura
            var portGo = new GameObject("Portrait", typeof(RectTransform));
            portGo.transform.SetParent(inner, false);
            var portRT = portGo.GetComponent<RectTransform>();
            portRT.anchorMin = portRT.anchorMax = new Vector2(0, 1);
            portRT.pivot = new Vector2(0, 1);
            portRT.anchoredPosition = new Vector2(12, -baseY);
            portRT.sizeDelta = new Vector2(70, 86);
            var portBg = portGo.AddComponent<Image>();
            portBg.color = new Color(0.04f, 0.07f, 0.16f, 1f);
            var portOl = portGo.AddComponent<Outline>();
            portOl.effectColor = CorFFTBorda;
            portOl.effectDistance = new Vector2(1.5f, -1.5f);

            var portImgGo = new GameObject("Sprite", typeof(RectTransform));
            portImgGo.transform.SetParent(portGo.transform, false);
            var piRT = portImgGo.GetComponent<RectTransform>();
            piRT.anchorMin = Vector2.zero; piRT.anchorMax = Vector2.one;
            piRT.offsetMin = new Vector2(4, 4); piRT.offsetMax = new Vector2(-4, -4);
            _portraitImg = portImgGo.AddComponent<Image>();
            _portraitImg.preserveAspect = true;
            _portraitImg.raycastTarget = false;

            _unitNameText = MakeText(inner, "UnitName", new Vector2(0, 1),
                new Vector2(92, -(baseY + 2f)), new Vector2(254, 26), 17, TextAnchor.MiddleLeft);
            _unitNameText.fontStyle = FontStyle.Bold;

            _hpBarBg = new GameObject("HPBarBg", typeof(RectTransform));
            _hpBarBg.transform.SetParent(inner, false);
            var bgRT = _hpBarBg.GetComponent<RectTransform>();
            bgRT.anchorMin = bgRT.anchorMax = new Vector2(0, 1);
            bgRT.pivot = new Vector2(0, 1);
            bgRT.anchoredPosition = new Vector2(92, -(baseY + 30f));
            bgRT.sizeDelta = new Vector2(248, 14);
            var hpBgImg = _hpBarBg.AddComponent<Image>();
            hpBgImg.color = new Color(0.04f, 0.07f, 0.16f);
            {
                var T = Tuning.Get();
                var pillSpr = UiSkin.SliceSliced(T.uiSheetPath, T.worldBarPillRect, T.worldBarPillBorder);
                if (pillSpr != null) { hpBgImg.sprite = pillSpr; hpBgImg.type = Image.Type.Sliced; }
            }

            var hpFill = new GameObject("HPFill", typeof(RectTransform));
            hpFill.transform.SetParent(_hpBarBg.transform, false);
            _hpBarFillRT = hpFill.GetComponent<RectTransform>();
            _hpBarFillRT.anchorMin = Vector2.zero;
            _hpBarFillRT.anchorMax = Vector2.one;
            _hpBarFillRT.offsetMin = Vector2.zero;
            _hpBarFillRT.offsetMax = Vector2.zero;
            _hpBarFillImg = hpFill.AddComponent<Image>();
            _hpBarFillImg.color = Tuning.Get().hpBarHighColor;

            _unitHPText = MakeText(inner, "HPText", new Vector2(0, 1),
                new Vector2(92, -(baseY + 46f)), new Vector2(248, 16), 11, TextAnchor.MiddleLeft);
            _unitHPText.color = new Color(0.80f, 0.85f, 0.92f);

            _apText = MakeText(inner, "APDisplay", new Vector2(0, 1),
                new Vector2(92, -(baseY + 66f)), new Vector2(248, 18), 13, TextAnchor.MiddleLeft);
            _apText.color = new Color(1f, 0.9f, 0.55f);

            _unitStatsText = MakeText(inner, "Stats", new Vector2(0, 1),
                new Vector2(12, -(baseY + 94f)), new Vector2(336, 56), 11, TextAnchor.UpperLeft);
            _unitStatsText.color = new Color(0.85f, 0.88f, 0.95f);

            _unitHintText = MakeText(inner, "Hint", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(300, 80), 13, TextAnchor.MiddleCenter);
            _unitHintText.fontStyle = FontStyle.Italic;
            _unitHintText.color = new Color(0.50f, 0.58f, 0.74f);
            _unitHintText.text = "Clique numa unidade para inspecionar";

            SetInfoVisible(false);
        }

        private void SetInfoVisible(bool visible)
        {
            _portraitImg.transform.parent.gameObject.SetActive(visible);
            _unitNameText.gameObject.SetActive(visible);
            _hpBarBg.SetActive(visible);
            _unitHPText.gameObject.SetActive(visible);
            _apText.gameObject.SetActive(visible);
            _unitStatsText.gameObject.SetActive(visible);
            _unitHintText.gameObject.SetActive(!visible);
        }

        // Referências para novos botões de ações bônus
        private Button _attackUnitButton;
        private Image  _attackUnitImg;
        private Text   _attackUnitLabel;
        private Button _attackTileButton;
        private Image  _attackTileImg;
        private Text   _attackTileLabel;
        private Button _powerStrikeButton;
        private Image  _powerStrikeImg;
        private Text   _powerStrikeLabel;
        private Button _quickStepButton;
        private Image  _quickStepImg;
        private Text   _quickStepLabel;

        // ── Botões de magia ──────────────────
        private Button _magicButton;
        private Image  _magicImg;
        private Text   _magicLabel;
        private Button _concentrateButton;
        private Image  _concentrateImg;
        private Text   _concentrateLabel;

        // Botões de cancelamento
        private Button _undoButton;
        private Image  _undoImg;
        private Text   _undoLabel;
        private Button _clearButton;
        private Image  _clearImg;
        private Text   _clearLabel;

        // ── Menu cascata ─────────────────────
        private Stack<GameObject> _menuStack = new Stack<GameObject>();
        private GameObject _mainMenuPanel;
        private GameObject _actionsMenuPanel;
        private GameObject _attackMenuPanel;
        private GameObject _magicMenuPanel;
        private GameObject _spellTypeMenuPanel;
        private GameObject _bonusMenuPanel;
        private GameObject _breadcrumbBar;
        private Text _breadcrumbText;
        private readonly List<Button> _magicElementButtons = new List<Button>();
        private readonly List<Image> _magicElementImages = new List<Image>();
        private readonly List<Text> _magicElementLabels = new List<Text>();
        private System.Action<SpellElement> _magicElementClick;

        // Botão Incremento (novo)
        private Button _incrementButton;
        private Image  _incrementImg;
        private Text   _incrementLabel;

        // Botão Mirar (novo)
        private Button _aimButton;
        private Image  _aimImg;
        private Text   _aimLabel;

        // SpellType submenu
        private Button _spellSelfButton;
        private Image  _spellSelfImg;
        private Text   _spellSelfLabel;
        private Button _spellUnitButton;
        private Image  _spellUnitImg;
        private Text   _spellUnitLabel;
        private Button _spellTileButton;
        private Image  _spellTileImg;
        private Text   _spellTileLabel;
        private System.Action<string> _spellTypeClick;

        // Stepper de mana (escolha livre de quanta mana injetar na conjuração)
        private GameObject _manaStepperPanel;
        private Button _manaMinusButton;
        private Button _manaPlusButton;
        private Button _manaCastButton;
        private Button _manaBackButton;
        private Text   _manaValueText;
        private Text   _manaPotencyText;

        // ── INFERIOR DIREITA: menu de comandos (cascata)
        private void BuildCommandMenu(Transform parent)
        {
            const float BTN_H   = 38f;
            const float BTN_GAP = 4f;
            // Zona reservada pro título no topo do painel (margem + altura do título de 22px).
            // Precisa acompanhar a margem usada em MakeTituloPainel, senão os botões dos
            // submenus (Ações/Ataque/Magia/etc.) começam a se sobrepor ao título.
            float TITLE_H = TitleZoneHeight();
            // Respiro embaixo pra o último botão não ficar sob a espessura da moldura
            // (contorno+realce+raio do canto) — mesma ideia do TITLE_H, mas pro rodapé.
            float BOTTOM_PAD = FrameBottomPad();
            const float HCON    = 42f;
            const float BR_H    = 22f;

            // Alturas dos painéis
            float mainH       = TITLE_H + 3 * BTN_H + 2 * BTN_GAP + 8f + HCON + 4f + HUNDO + BOTTOM_PAD;
            float subH        = TITLE_H + 2 * BTN_H + 1 * BTN_GAP + 8f + BOTTOM_PAD;
            float magicH      = TITLE_H + 6 * BTN_H + 5 * BTN_GAP + 8f + BOTTOM_PAD;
            float spellTypeH  = TITLE_H + 3 * BTN_H + 2 * BTN_GAP + 8f + BOTTOM_PAD;
            float bonusH      = TITLE_H + 3 * BTN_H + 2 * BTN_GAP + 8f + BOTTOM_PAD;
            float manaStepH   = TITLE_H + 26f + (HUNDO - 4f) + 22f + 2 * BTN_H + 5 * BTN_GAP + 8f + BOTTOM_PAD;
            float maxSub      = Mathf.Max(Mathf.Max(magicH, manaStepH), Mathf.Max(spellTypeH, bonusH));
            float totalH      = Mathf.Max(mainH, maxSub) + BR_H;

            _actionBar = new GameObject("CommandMenu", typeof(RectTransform));
            _actionBar.transform.SetParent(parent, false);
            var rt = _actionBar.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(1, 0);
            rt.anchoredPosition = new Vector2(-12, 37);
            rt.sizeDelta = new Vector2(232, totalH);

            // ── Breadcrumb (topo, oculto por padrão) ──
            _breadcrumbBar = new GameObject("BreadcrumbBar", typeof(RectTransform));
            _breadcrumbBar.transform.SetParent(_actionBar.transform, false);
            var brt = _breadcrumbBar.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0, 1);
            brt.anchorMax = new Vector2(1, 1);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(0, BR_H);
            _breadcrumbBar.AddComponent<Image>().color = CorFFTTitBg;
            _breadcrumbText = MakeText(_breadcrumbBar.transform, "BreadText",
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220, BR_H),
                11, TextAnchor.MiddleCenter);
            _breadcrumbText.color = CorFFTTitulo;
            _breadcrumbBar.SetActive(false);

            float yOff = -BR_H; // offset abaixo do breadcrumb

            // ── MAIN MENU ──
            _mainMenuPanel = CreateMenuPanel(_actionBar.transform, "MainMenu", yOff, mainH);
            MakeTituloPainel(_mainMenuPanel.transform, "COMANDOS");

            float y = -(TITLE_H + BTN_GAP);
            var btnAcoes = MakeMenuButton(_mainMenuPanel.transform, "BtnAcoes", y,
                out _, out var lblAcoes);
            lblAcoes.text = "1 - Ações";
            btnAcoes.onClick.AddListener(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); ShowSubMenu(_actionsMenuPanel, "Ações"); });
            y -= BTN_H + BTN_GAP;

            var btnBonus = MakeMenuButton(_mainMenuPanel.transform, "BtnBonusMain", y,
                out _, out var lblBonus);
            lblBonus.text = "2 - Bônus";
            btnBonus.onClick.AddListener(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); ShowSubMenu(_bonusMenuPanel, "Bônus"); });
            y -= BTN_H + BTN_GAP;

            var btnMagia = MakeMenuButton(_mainMenuPanel.transform, "BtnMagiaMain", y,
                out _, out var lblMagia);
            lblMagia.text = "3 - Magia";
            btnMagia.onClick.AddListener(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); ShowSubMenu(_magicMenuPanel, "Magia"); });
            y -= BTN_H + 8f;

            // Confirmar
            var confirmGo = new GameObject("ConfirmButton", typeof(RectTransform));
            confirmGo.transform.SetParent(_mainMenuPanel.transform, false);
            var crt = confirmGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = new Vector2(0, y);
            crt.sizeDelta = new Vector2(-16, HCON);
            var confirmImg = confirmGo.AddComponent<Image>();
            ApplyButtonFrame(confirmImg, CorConfirmBtn);
            var cOl = confirmGo.AddComponent<Outline>();
            cOl.effectColor = new Color(0.55f, 0.85f, 0.62f);
            cOl.effectDistance = new Vector2(1.5f, -1.5f);
            _confirmButton = confirmGo.AddComponent<Button>();
            MakeText(confirmGo.transform, "Label", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(200, 36), 15, TextAnchor.MiddleCenter).text = "Confirmar plano";
            y -= HCON + 4f;

            // Undo / Clear
            _undoButton = MakeInlineButton(_mainMenuPanel.transform, "BtnUndo", "Desfazer",
                y, -54f, new Color(0.50f, 0.35f, 0.12f), out _undoImg, out _undoLabel);
            _clearButton = MakeInlineButton(_mainMenuPanel.transform, "BtnClear", "Limpar",
                y,  54f, new Color(0.52f, 0.18f, 0.18f), out _clearImg, out _clearLabel);

            // ── ACTIONS MENU ──
            _actionsMenuPanel = CreateMenuPanel(_actionBar.transform, "ActionsMenu", yOff, subH);
            MakeTituloPainel(_actionsMenuPanel.transform, "AÇÕES");
            y = -(TITLE_H + BTN_GAP);
            _moveButton = MakeMenuButton(_actionsMenuPanel.transform, "BtnMover", y,
                out _moveImg, out _moveLabel);
            _moveLabel.text = "1 - Mover";
            y -= BTN_H + BTN_GAP;
            var btnAtkSub = MakeMenuButton(_actionsMenuPanel.transform, "BtnAtacar", y,
                out _, out var lblAtkSub);
            lblAtkSub.text = "2 - Atacar";
            btnAtkSub.onClick.AddListener(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); ShowSubMenu(_attackMenuPanel, "Ações > Atacar"); });

            // ── ATTACK MENU ──
            _attackMenuPanel = CreateMenuPanel(_actionBar.transform, "AttackMenu", yOff, subH);
            MakeTituloPainel(_attackMenuPanel.transform, "ATACAR");
            y = -(TITLE_H + BTN_GAP);
            _attackUnitButton = MakeMenuButton(_attackMenuPanel.transform, "BtnAtacarUnidade", y,
                out _attackUnitImg, out _attackUnitLabel);
            _attackUnitLabel.text = "1 - Atacar Unidade";
            y -= BTN_H + BTN_GAP;
            _attackTileButton = MakeMenuButton(_attackMenuPanel.transform, "BtnAtacarTile", y,
                out _attackTileImg, out _attackTileLabel);
            _attackTileLabel.text = "2 - Atacar Tile";

            // ── MAGIC MENU ──
            _magicMenuPanel = CreateMenuPanel(_actionBar.transform, "MagicMenu", yOff, magicH);
            MakeTituloPainel(_magicMenuPanel.transform, "MAGIA");
            y = -(TITLE_H + BTN_GAP);
            _magicElementButtons.Clear();
            _magicElementImages.Clear();
            _magicElementLabels.Clear();
            var elements = new (SpellElement elem, string label)[] {
                (SpellElement.Physical, "1 - Físico"),
                (SpellElement.Magic,    "2 - Mágico"),
                (SpellElement.Fire,     "3 - Fogo"),
                (SpellElement.Water,    "4 - Água"),
                (SpellElement.Air,      "5 - Ar"),
                (SpellElement.Earth,    "6 - Terra"),
            };
            foreach (var (element, label) in elements)
            {
                var eBtn = MakeMenuButton(_magicMenuPanel.transform, $"BtnElem_{element}", y,
                    out var eImg, out var eLbl);
                eLbl.text = label;
                ApplyButtonFrame(eImg, SpellBook.ElementColor(element));
                var captured = element;
                eBtn.onClick.AddListener(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); OnMagicElementClick(captured); });
                _magicElementButtons.Add(eBtn);
                _magicElementImages.Add(eImg);
                _magicElementLabels.Add(eLbl);
                y -= BTN_H + BTN_GAP;
            }

            // ── SPELL TYPE MENU (Self / Unidade / Tile) ──
            _spellTypeMenuPanel = CreateMenuPanel(_actionBar.transform, "SpellTypeMenu", yOff, spellTypeH);
            MakeTituloPainel(_spellTypeMenuPanel.transform, "ALVO DA MAGIA");
            y = -(TITLE_H + BTN_GAP);
            _spellSelfButton = MakeMenuButton(_spellTypeMenuPanel.transform, "BtnSpellSelf", y,
                out _spellSelfImg, out _spellSelfLabel);
            _spellSelfLabel.text = "1 - Self";
            y -= BTN_H + BTN_GAP;
            _spellUnitButton = MakeMenuButton(_spellTypeMenuPanel.transform, "BtnSpellUnit", y,
                out _spellUnitImg, out _spellUnitLabel);
            _spellUnitLabel.text = "2 - Unidade";
            y -= BTN_H + BTN_GAP;
            _spellTileButton = MakeMenuButton(_spellTypeMenuPanel.transform, "BtnSpellTile", y,
                out _spellTileImg, out _spellTileLabel);
            _spellTileLabel.text = "3 - Tile";

            // ── MANA STEPPER MENU (quanta mana injetar) ──
            _manaStepperPanel = CreateMenuPanel(_actionBar.transform, "ManaStepperMenu", yOff, manaStepH);
            MakeTituloPainel(_manaStepperPanel.transform, "MANA");
            y = -(TITLE_H + BTN_GAP);
            _manaValueText = MakeText(_manaStepperPanel.transform, "ManaValue",
                new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(200, 26), 18, TextAnchor.MiddleCenter);
            _manaValueText.color = Tuning.Get().manaTextColor;
            _manaValueText.text = "1 MP";
            y -= 26f + BTN_GAP;
            _manaMinusButton = MakeInlineButton(_manaStepperPanel.transform, "BtnManaMinus", "−",
                y, -54f, new Color(0.28f, 0.40f, 0.62f), out _, out _);
            _manaPlusButton = MakeInlineButton(_manaStepperPanel.transform, "BtnManaPlus", "+",
                y, 54f, new Color(0.28f, 0.40f, 0.62f), out _, out _);
            y -= (HUNDO - 4f) + BTN_GAP;
            _manaPotencyText = MakeText(_manaStepperPanel.transform, "ManaPotency",
                new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(200, 22), 13, TextAnchor.MiddleCenter);
            _manaPotencyText.text = "Potência: 0";
            y -= 22f + BTN_GAP;
            _manaCastButton = MakeMenuButton(_manaStepperPanel.transform, "BtnManaCast", y,
                out var castImg, out var castLbl);
            ApplyButtonFrame(castImg, CorConfirmBtn);
            castLbl.text = "Conjurar";
            y -= BTN_H + BTN_GAP;
            _manaBackButton = MakeMenuButton(_manaStepperPanel.transform, "BtnManaBack", y,
                out _, out var backLbl);
            backLbl.text = "Voltar";

            // ── BONUS MENU ──
            _bonusMenuPanel = CreateMenuPanel(_actionBar.transform, "BonusMenu", yOff, bonusH);
            MakeTituloPainel(_bonusMenuPanel.transform, "AÇÕES BÔNUS");
            y = -(TITLE_H + BTN_GAP);
            _concentrateButton = MakeMenuButton(_bonusMenuPanel.transform, "BtnConcentrar", y,
                out _concentrateImg, out _concentrateLabel);
            _concentrateLabel.text = "1 - Concentrar";
            y -= BTN_H + BTN_GAP;
            _incrementButton = MakeMenuButton(_bonusMenuPanel.transform, "BtnIncremento", y,
                out _incrementImg, out _incrementLabel);
            _incrementLabel.text = "2 - Incremento";
            y -= BTN_H + BTN_GAP;
            _aimButton = MakeMenuButton(_bonusMenuPanel.transform, "BtnMirar", y,
                out _aimImg, out _aimLabel);
            _aimLabel.text = "3 - Mirar";

            // ── Estado inicial ──
            _actionsMenuPanel.SetActive(false);
            _attackMenuPanel.SetActive(false);
            _magicMenuPanel.SetActive(false);
            _spellTypeMenuPanel.SetActive(false); // antes ficava ativo e sobrepunha o menu
            _manaStepperPanel.SetActive(false);
            _bonusMenuPanel.SetActive(false);
            _mainMenuPanel.SetActive(true);
            _menuStack.Clear();

            _actionBar.SetActive(false);
        }

        /// <summary>PainelFFT ancorado ao topo, com altura fixa.</summary>
        private GameObject CreateMenuPanel(Transform parent, string name, float yTop, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0, yTop);
            r.sizeDelta = new Vector2(0, height);
            StyleFFTWindow(go);
            return go;
        }

        /// <summary>Sub-painel FFT esticado na largura do container, posicionado pelo topo (yTop ≤ 0).</summary>
        private GameObject MakeSubPanel(Transform parent, string name, float yTop, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, yTop);
            rt.sizeDelta = new Vector2(0, height); // largura = container
            StyleFFTWindow(go);
            return go;
        }

        // Registro dos botões skinados p/ permitir reaplicar a moldura ao vivo (ver RefreshUiSkin).
        private readonly List<(Image img, Color baseColor)> _skinnedButtons = new List<(Image, Color)>();

        /// <summary>Fundo cápsula 9-slice do BDragon1727 p/ botões; sem toggle/asset, deixa o Image com a cor sólida original.</summary>
        private void ApplyButtonFrame(Image img, Color baseColor)
        {
            // RemoveAll+Add (não só Exists-check) pra permitir recolorir um botão já skinado
            // (ex.: chips de elemento sobrescrevem a cor de MakeMenuButton) sem deixar o
            // registro de refresh preso na cor antiga.
            _skinnedButtons.RemoveAll(t => t.img == img);
            _skinnedButtons.Add((img, baseColor));

            UiSkin.ApplyButtonSkin(img, baseColor);
        }

        /// <summary>
        /// Reaplica a skin de botão (moldura/tint/flip) em todos os botões já criados, lendo
        /// os valores ATUAIS do GameTuning. Use durante o Play (botão direito no componente
        /// BattleHUD no Inspector → "Refresh UI Skin") pra iterar sem recarregar a cena.
        /// </summary>
        [ContextMenu("Refresh UI Skin")]
        private void RefreshUiSkin()
        {
            _skinnedButtons.RemoveAll(t => t.img == null);
            // Cópia: ApplyButtonFrame mexe em _skinnedButtons (remove+add), não dá pra iterar a lista original.
            foreach (var (img, baseColor) in _skinnedButtons.ToArray())
                ApplyButtonFrame(img, baseColor);
            RefreshWindowFrames();
        }

        /// <summary>
        /// Compensa a cor de estado (Ativo/Confirmado/Normal/Desabilitado etc.) da mesma forma
        /// que ApplyButtonFrame, pra usar sempre que um botão já skinado tiver a cor trocada
        /// depois de criado (highlight de picking/seleção). Sem isso o refresh de estado
        /// reintroduzia a cor crua e escurecia o botão de novo.
        /// </summary>
        private static Color SkinTint(Color c)
        {
            var T = Tuning.Get();
            return T.uiButtonSkinEnabled ? Color.Lerp(c, Color.white, T.uiButtonFrameTintLerp) : c;
        }

        private Button MakeMenuButton(Transform parent, string name, float y,
            out Image img, out Text label)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(206, 38);
            img = go.AddComponent<Image>();
            ApplyButtonFrame(img, CorNormal);
            var ol = go.AddComponent<Outline>();
            ol.effectColor = CorFFTBorda; ol.effectDistance = new Vector2(1f, -1f);
            var btn = go.AddComponent<Button>();
            // Centralizado: combina com o formato de pílula da moldura (texto encostado à
            // esquerda ficava estranho dentro de um botão com pontas arredondadas).
            label = MakeText(go.transform, "Label", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(190, 34), 15, TextAnchor.MiddleCenter);
            return btn;
        }

        // ── CENTRO INF.: fila de ações ───────
        private void BuildActionSequenceBar(Transform parent)
        {
            _seqBar = new GameObject("ActionSeqBar", typeof(RectTransform));
            _seqBar.transform.SetParent(parent, false);
            var rt = _seqBar.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 12f);
            rt.sizeDelta = new Vector2(470, 60);
            StyleFFTWindow(_seqBar);

            var label = MakeText(_seqBar.transform, "SeqLabel", new Vector2(0f, 1f),
                new Vector2(12, -6), new Vector2(120, 18), 12, TextAnchor.MiddleLeft);
            label.text  = "Ordem das ações";
            label.color = CorFFTTitulo;

            var contentGo = new GameObject("SeqContent", typeof(RectTransform));
            contentGo.transform.SetParent(_seqBar.transform, false);
            _seqContentRT = contentGo.GetComponent<RectTransform>();
            _seqContentRT.anchorMin = new Vector2(0f, 0f);
            _seqContentRT.anchorMax = new Vector2(0f, 0f);
            _seqContentRT.pivot     = new Vector2(0f, 0f);
            _seqContentRT.anchoredPosition = new Vector2(12, 8);
            _seqContentRT.sizeDelta = new Vector2(440, 26);
            var hlg = contentGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment       = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth    = true;
            hlg.childControlHeight   = true;
            hlg.spacing = 4;

            _seqBar.SetActive(false);
        }

        public void SetActionSequence(List<ActionType> seq, List<ScheduledAction> scheduled = null)
        {
            foreach (var c in _seqChips) if (c != null) Destroy(c);
            _seqChips.Clear();
            _seqChipImages.Clear();
            _seqChipBaseColors.Clear();

            if (seq == null || seq.Count == 0)
            {
                _selectedSeqIndex = -1;
                _seqBar.SetActive(false);
                return;
            }
            if (_selectedSeqIndex >= seq.Count) _selectedSeqIndex = -1;

            _seqBar.SetActive(true);
            for (int i = 0; i < seq.Count; i++)
            {
                int idx = i; // local copy para evitar closure issues
                bool isAtk = seq[i] == ActionType.Attack;
                bool isSpell = seq[i] == ActionType.Spell;
                bool isConc = seq[i] == ActionType.Concentrate;
                bool isBonus = scheduled != null && i < scheduled.Count && scheduled[i].IsBonus;
                var chip = new GameObject($"Chip{i}", typeof(RectTransform));
                chip.transform.SetParent(_seqContentRT, false);
                Color baseCol;
                if (isSpell)
                    baseCol = Tuning.Get().seqChipSpellColor;
                else if (isConc)
                    baseCol = Tuning.Get().seqChipConcentrateColor;
                else
                    baseCol = isBonus ? (isAtk ? CorChipAtkBn : CorChipMoveBn) : (isAtk ? CorChipAtk : CorChipMove);
                var img = chip.AddComponent<Image>();
                ApplyButtonFrame(img, baseCol);
                var le = chip.AddComponent<LayoutElement>();
                le.preferredHeight = 24;
                le.preferredWidth  = 90;

                var t = MakeText(chip.transform, "Lbl", new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(86, 22), 12, TextAnchor.MiddleCenter);
                string mark = isBonus ? "◆ " : "";
                t.text      = $"{mark}{i + 1}  {(isSpell ? "Magia" : isAtk ? "Atacar" : isConc ? "Concentrar" : "Mover")}";
                t.color     = Color.white;
                t.fontStyle = FontStyle.Bold;

                // Adicionar Button para seleção de chip
                var btn = chip.AddComponent<Button>();
                btn.onClick.AddListener(() => OnSeqChipClicked(idx));

                _seqChips.Add(chip);
                _seqChipImages.Add(img);
                _seqChipBaseColors.Add(baseCol);
            }
            RefreshSeqChipHighlight();
        }

        public void HideActionSequence()
        {
            foreach (var c in _seqChips) if (c != null) Destroy(c);
            _seqChips.Clear();
            _seqChipImages.Clear();
            _seqChipBaseColors.Clear();
            _selectedSeqIndex = -1;
            if (_seqBar != null) _seqBar.SetActive(false);
        }

        private void OnSeqChipClicked(int index)
        {
            if (index < 0 || index >= _seqChips.Count) return;
            AudioManager.I?.Play(AudioManager.I.sfxUIClick);
            // Toggle BAP no chip (se tiver callback registrado)
            if (OnSeqChipToggled != null)
                OnSeqChipToggled.Invoke(index);
            _selectedSeqIndex = (_selectedSeqIndex == index) ? -1 : index;
            RefreshSeqChipHighlight();
        }

        private void RefreshSeqChipHighlight()
        {
            var T = Tuning.Get();
            // Se a moldura 9-slice estiver ativa, o "descanso" já é o tint compensado
            // (ApplyButtonFrame); sem isso o highlight reintroduzia a cor crua e escurecia
            // o chip de novo assim que o usuário clicava nele.
            bool skinned = T.uiButtonSkinEnabled;
            for (int i = 0; i < _seqChipImages.Count; i++)
            {
                if (_seqChipImages[i] == null) continue;
                Color resting = skinned ? Color.Lerp(_seqChipBaseColors[i], Color.white, T.uiButtonFrameTintLerp) : _seqChipBaseColors[i];
                _seqChipImages[i].color = (i == _selectedSeqIndex)
                    ? Color.Lerp(resting, Color.white, T.seqChipSelectedLighten)
                    : resting;
            }
        }

        public int SelectedSeqIndex => _selectedSeqIndex;
        public void SelectSeqIndex(int idx) => _selectedSeqIndex = idx;

        // ── BIND / ESTADO DOS BOTÕES ─────────
        private Button MakeInlineButton(Transform parent, string name, string label,
            float yTop, float xCenter, Color color, out Image img, out Text txt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(xCenter, yTop);
            rt.sizeDelta = new Vector2(102, HUNDO - 4);
            img = go.AddComponent<Image>();
            ApplyButtonFrame(img, color);
            var ol = go.AddComponent<Outline>();
            ol.effectColor = CorFFTBorda;
            ol.effectDistance = new Vector2(1f, -1f);
            var btn = go.AddComponent<Button>();
            txt = MakeText(go.transform, "Label", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(100, HUNDO - 6), 13, TextAnchor.MiddleCenter);
            txt.text = label;
            return btn;
        }

        public void BindConfirm(Action a) { BindButton(_confirmButton, a); }
        public void BindMove(Action a)    { BindButton(_moveButton,    a); }
        public void BindAttackUnit(Action a)  { BindButton(_attackUnitButton,  a); }
        public void BindAttackTile(Action a)  { BindButton(_attackTileButton,  a); }
        public void BindPowerStrike(Action a) { BindButton(_powerStrikeButton, a); }
        public void BindQuickStep(Action a)   { BindButton(_quickStepButton,   a); }
        public void BindMagic(Action a)       { BindButton(_magicButton, a); }
        public void BindConcentrate(Action a) { BindButton(_concentrateButton, a); }
        public void BindIncrement(Action a)   { BindButton(_incrementButton,   a); }
        public void BindAim(Action a)         { BindButton(_aimButton,         a); }
        public void BindUndo(Action a)        { BindButton(_undoButton,   a); }
        public void BindClear(Action a)       { BindButton(_clearButton,  a); }
        public void BindMagicElement(System.Action<SpellElement> a) { _magicElementClick = a; }
        public void BindSpellSelf(Action a) { BindButton(_spellSelfButton, a); }
        public void BindSpellUnit(Action a) { BindButton(_spellUnitButton, a); }
        public void BindSpellTile(Action a) { BindButton(_spellTileButton, a); }
        public void BindManaMinus(Action a) { BindButton(_manaMinusButton, a); }
        public void BindManaPlus(Action a)  { BindButton(_manaPlusButton, a); }
        public void BindManaCast(Action a)  { BindButton(_manaCastButton, a); }
        public void BindManaBack(Action a)  { BindButton(_manaBackButton, a); }

        public GameObject SpellTypeMenuPanel => _spellTypeMenuPanel;
        public GameObject ManaStepperPanel   => _manaStepperPanel;

        /// <summary>Atualiza o texto de mana e o preview de alcance + potência do stepper.</summary>
        public void SetManaPreview(int mana, int max, int potency, int range)
        {
            if (_manaValueText != null)   _manaValueText.text   = $"{mana} / {max} MP";
            if (_manaPotencyText != null) _manaPotencyText.text = $"Alcance: {range}  •  Potência: {potency}";
            if (_manaMinusButton != null) _manaMinusButton.interactable = mana > 1;
            if (_manaPlusButton != null)  _manaPlusButton.interactable  = mana < max;
        }
        private static void BindButton(Button b, Action a)
        {
            if (b == null) return;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => { AudioManager.I?.Play(AudioManager.I.sfxUIClick); a?.Invoke(); });
        }

        // ── NAVEGAÇÃO CASCATA ──────────────────
        public void ShowMainMenu()
        {
            _actionsMenuPanel.SetActive(false);
            _attackMenuPanel.SetActive(false);
            _magicMenuPanel.SetActive(false);
            _spellTypeMenuPanel.SetActive(false);
            _manaStepperPanel.SetActive(false);
            _bonusMenuPanel.SetActive(false);
            _mainMenuPanel.SetActive(true);
            _menuStack.Clear();
            UpdateBreadcrumb("");
        }

        public void ShowSubMenu(GameObject panel, string path)
        {
            // Esconde o painel atual visível
            foreach (var go in _menuStack)
                if (go.activeSelf) go.SetActive(false);
            _mainMenuPanel.SetActive(false);
            _menuStack.Push(panel);
            panel.SetActive(true);
            UpdateBreadcrumb(path);
        }

        public void GoBack()
        {
            if (_menuStack.Count == 0) return;
            var current = _menuStack.Pop();
            current.SetActive(false);
            if (_menuStack.Count > 0)
                _menuStack.Peek().SetActive(true);
            else
                _mainMenuPanel.SetActive(true);
            UpdateBreadcrumb(GetCurrentPath());
        }

        private string GetCurrentPath()
        {
            if (_menuStack.Count == 0) return "";
            var parts = new List<string>();
            foreach (var go in _menuStack)
            {
                if (go == _actionsMenuPanel) parts.Add("Ações");
                else if (go == _attackMenuPanel) parts.Add("Atacar");
                else if (go == _magicMenuPanel) parts.Add("Magia");
                else if (go == _spellTypeMenuPanel) parts.Add("Alvo");
                else if (go == _manaStepperPanel) parts.Add("Mana");
                else if (go == _bonusMenuPanel) parts.Add("Bônus");
            }
            parts.Reverse();
            return string.Join(" > ", parts);
        }

        private void UpdateBreadcrumb(string path)
        {
            if (_breadcrumbBar != null)
                _breadcrumbBar.SetActive(!string.IsNullOrEmpty(path));
            if (_breadcrumbText != null)
                _breadcrumbText.text = path;
        }

        public void OnMagicElementClick(SpellElement element)
        {
            _magicElementClick?.Invoke(element);
        }

        public GameObject MagicMenuPanel => _magicMenuPanel;

        public void SetMoveState(bool canAdd, int count, bool isPicking)
        {
            bool hasAny = count > 0;
            _moveButton.interactable = canAdd || hasAny || isPicking;
            _moveImg.color  = SkinTint(isPicking ? CorAtivo : hasAny ? CorConfirmado : canAdd ? CorNormal : CorDisabled);
            _moveLabel.text = (isPicking ? "▶ " : "") + "1 - Mover" + (count > 0 ? $" x{count}" : "");
        }

        public void SetAttackUnitState(bool canAdd, int count, bool isPicking = false)
        {
            bool hasAny = count > 0;
            _attackUnitButton.interactable = canAdd || hasAny || isPicking;
            _attackUnitImg.color  = SkinTint(isPicking ? CorAtivo : hasAny ? CorConfirmado : canAdd ? CorNormal : CorDisabled);
            _attackUnitLabel.text = (isPicking ? "▶ " : "") + "1 - Atacar Unidade" + (count > 0 ? $" x{count}" : "");
        }

        public void SetAttackTileState(bool canAdd, int count, bool isPicking = false)
        {
            _attackTileButton.interactable = canAdd || isPicking;
            _attackTileImg.color  = SkinTint(isPicking ? CorAtivo : canAdd ? CorNormal : CorDisabled);
            _attackTileLabel.text = (isPicking ? "▶ " : "") + "2 - Atacar Tile";
        }



        public void SetMagicState(bool canAdd, bool isPicking = false)
        {
            for (int i = 0; i < _magicElementButtons.Count; i++)
            {
                _magicElementButtons[i].interactable = canAdd || isPicking;
                if (isPicking)
                    _magicElementImages[i].color = SkinTint(CorAtivo);
                else if (canAdd)
                    _magicElementImages[i].color = SkinTint(SpellBook.ElementColor((SpellElement)(i + 1)));
                else
                    _magicElementImages[i].color = SkinTint(CorDisabled);
            }
        }

        public void SetConcentrateState(bool canAdd, bool isPicking = false)
        {
            _concentrateButton.interactable = canAdd || isPicking;
            _concentrateImg.color  = SkinTint(isPicking ? CorAtivo : canAdd ? CorNormal : CorDisabled);
            _concentrateLabel.text = (isPicking ? "▶ " : "") + "1 - Concentrar" + (isPicking ? "..." : "");
        }

        public void SetIncrementState(bool canAdd, bool isPicking = false)
        {
            _incrementButton.interactable = canAdd || isPicking;
            _incrementImg.color  = SkinTint(isPicking ? CorAtivo : canAdd ? CorNormal : CorDisabled);
            _incrementLabel.text = (isPicking ? "▶ " : "") + "2 - Incremento" + (isPicking ? "..." : "");
        }

        public void SetAimState(bool canAdd, bool isPicking = false)
        {
            if (_aimButton == null) return;
            _aimButton.interactable = canAdd || isPicking;
            _aimImg.color  = SkinTint(isPicking ? CorAtivo : canAdd ? CorNormal : CorDisabled);
            _aimLabel.text = (isPicking ? "▶ " : "") + "3 - Mirar" + (isPicking ? "..." : "");
        }

        // ── VISIBILIDADE ─────────────────────
        public void SetActionBarVisible(bool v, Unit u = null)
        {
            _actionBar.SetActive(v);
            if (v)
            {
                ShowMainMenu();
                if (u != null) UpdateAPDisplay(u);
            }
        }

        public void UpdateAPDisplay(Unit u)
        {
            _apText.text = $"<b>PA</b> {u.remainingAP}/{u.stats.ActionPoints}   " +
                           $"<b>PAB</b> {u.remainingBAP}/{u.stats.BonusActionPoints}";
        }

        // ── TEXTOS PÚBLICOS ─────────────────
        public void SetPhase(string s)          => _phaseText.text = s;
        public void SetTimer(float sec)         => _timerText.text = sec.ToString("0.0") + "s";
        public void SetTimerWarning(bool warn)  => _timerText.color = warn ? Tuning.Get().timerWarningColor : Tuning.Get().timerNormalColor;
        public void HideTimer()                 => _timerText.text = "";
        public void SetTimerVisible(bool v)     => _timerText.gameObject.SetActive(v);
        public void SetConfirmVisible(bool v)   => _confirmButton?.gameObject.SetActive(v);
        public bool IsPromptVisible             => _promptPanel.activeSelf;

        // ── INFO DA UNIDADE ──────────────────
        public void ShowUnitInfo(Unit u)
        {
            _inspectedUnit = u;
            RefreshUnitInfo();
        }

        public void RefreshUnitInfo() => ApplyUnitInfo(_inspectedUnit);

        private void ApplyUnitInfo(Unit u)
        {
            if (u == null) { SetInfoVisible(false); return; }
            SetInfoVisible(true);

            var s = u.stats;
            _portraitImg.sprite = u.CurrentSprite;
            _portraitImg.color  = u.CurrentSprite != null ? Color.white : new Color(1, 1, 1, 0);

            string team = u.team == Team.Player
                ? $"<color=#{ColorUtility.ToHtmlStringRGB(CorPlayer)}>Aliado</color>"
                : $"<color=#{ColorUtility.ToHtmlStringRGB(CorEnemy)}>Inimigo</color>";
            _unitNameText.text = $"{u.unitName}  <size=11>{team}</size>";
            if (u.IsDead) _unitNameText.text += "  <color=#888>+</color>";

            float ratio = (float)u.currentHP / Mathf.Max(1, s.MaxHP);
            _hpBarFillRT.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            var Thp = Tuning.Get();
            _hpBarFillImg.color = ratio > Thp.hpBarYellowThreshold ? Thp.hpBarHighColor
                                : ratio > Thp.hpBarRedThreshold    ? Thp.hpBarMidColor
                                                                   : Thp.hpBarLowColor;
            _unitHPText.text = $"HP  {u.currentHP} / {s.MaxHP}";
            _apText.text = $"<b>PA</b> {u.remainingAP}/{s.ActionPoints}   <b>PAB</b> {u.remainingBAP}/{s.BonusActionPoints}";

            var buffs = StatusEffectSystem.Summary(u.statusEffects);
            _unitStatsText.text =
                $"<b>STR</b> {s.STR:0.#}  <b>VIT</b> {s.VIT:0.#}  <b>DEX</b> {s.DEX:0.#}  <b>AGI</b> {s.AGI:0.#}\n" +
                $"<b>MOV</b> {s.MoveBudget}  <b>INI</b> {s.Initiative}  <b>ATQ</b> {s.PhysicalDamage}  <b>ALC</b> {s.AttackRange}\n" +
                $"<b>MANA</b> {u.currentMana}/{s.MaxMana}  <b>CONC</b> {u.plannedConcentrations}" +
                (string.IsNullOrEmpty(buffs) ? "" : $"\n<color=#FFD700><b>BUFFS</b> {buffs}</color>");
            _unitNameText.text = $"{u.unitName}  <size=11>{team}</size>  <color=#{ColorUtility.ToHtmlStringRGB(Tuning.Get().manaTextColor)}>MP {u.currentMana}</color>";
        }

        // ── LOG DE BATALHA ───────────────────
        public void LogAction(string line, Unit unit = null)
        {
            LogAction(new LogEntry
            {
                displayText = line,
                tooltipText = line,
                team        = unit != null ? unit.team : Team.Player,
                unit        = unit,
            });
        }

        public void LogAction(LogEntry entry)
        {
            int idx = _logEntries.Count;
            _logEntries.Add(entry);
            AppendLineUI(entry, idx);
            Canvas.ForceUpdateCanvases();
            if (_logScrollRect != null)
                _logScrollRect.verticalNormalizedPosition = 0f;
            Debug.Log("[Battle] " + entry.tooltipText);
        }

        public void LogRound(int round)
        {
            if (_sections.Count > 0)
                AppendSpacerUI();

            int sIdx = _sections.Count;
            var section = new LogSection();
            _sections.Add(section);

            var hGo = new GameObject($"RoundHdr_{round}", typeof(RectTransform));
            hGo.transform.SetParent(_logContentRT, false);
            var hRT = hGo.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0, 0);
            hRT.anchorMax = new Vector2(1, 0);
            hRT.pivot = new Vector2(0.5f, 0);
            hGo.AddComponent<Image>().color = CorRoundHdr;
            hGo.AddComponent<LayoutElement>().preferredHeight = 22;

            var tGo = new GameObject("HdrText", typeof(RectTransform));
            tGo.transform.SetParent(hGo.transform, false);
            var tRT = tGo.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = new Vector2(8, 0);
            tRT.offsetMax = Vector2.zero;
            section.chevron = tGo.AddComponent<Text>();
            section.chevron.font               = _font;
            section.chevron.fontSize           = 12;
            section.chevron.fontStyle          = FontStyle.Bold;
            section.chevron.color              = CorFFTTitulo;
            section.chevron.text               = $"v  Round {round}";
            section.chevron.alignment          = TextAnchor.MiddleLeft;
            section.chevron.horizontalOverflow = HorizontalWrapMode.Overflow;
            section.chevron.verticalOverflow   = VerticalWrapMode.Overflow;
            section.chevron.raycastTarget      = false;

            _logLines.Add(new LogLine
            {
                go = hGo, entryIdx = -1, isHeader = true,
                sectionIdx = sIdx, hoverBg = null, unit = null,
            });

            Canvas.ForceUpdateCanvases();
            if (_logScrollRect != null)
                _logScrollRect.verticalNormalizedPosition = 0f;
        }

        private void AppendSpacerUI()
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(_logContentRT, false);
            go.AddComponent<LayoutElement>().preferredHeight = 5;

            int sIdx = _sections.Count - 1;
            var ll = new LogLine { go = go, entryIdx = -1, isHeader = false, sectionIdx = sIdx, hoverBg = null, unit = null };
            int li = _logLines.Count;
            _logLines.Add(ll);
            if (sIdx >= 0) _sections[sIdx].lineIndices.Add(li);
        }

        private void AppendLineUI(LogEntry entry, int entryIdx)
        {
            int sIdx = _sections.Count - 1;

            var go = new GameObject("LogLine", typeof(RectTransform));
            go.transform.SetParent(_logContentRT, false);

            var bg = go.AddComponent<Image>();
            bg.color = Color.clear;
            bg.raycastTarget = false;

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment        = TextAnchor.UpperLeft;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0;
            vlg.padding = new RectOffset(4, 4, 1, 1);

            var tGo = new GameObject("LineText", typeof(RectTransform));
            tGo.transform.SetParent(go.transform, false);
            var txt = tGo.AddComponent<Text>();
            txt.font               = _font;
            txt.fontSize           = 13;
            txt.color              = LogColor(entry.team);
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow   = VerticalWrapMode.Overflow;
            txt.supportRichText    = true;
            txt.text               = entry.displayText;
            txt.raycastTarget      = false;

            var ll = new LogLine
            {
                go = go, entryIdx = entryIdx, isHeader = false,
                sectionIdx = sIdx, hoverBg = bg, unit = entry.unit,
            };
            int li = _logLines.Count;
            _logLines.Add(ll);
            if (sIdx >= 0) _sections[sIdx].lineIndices.Add(li);
        }

        private Color LogColor(Team team) => team switch
        {
            Team.Player => CorPlayer,
            Team.Enemy  => CorEnemy,
            _           => CorSystem,
        };

        public void Log(string line) => Debug.Log("[Battle] " + line);

        // ── UPDATE: hover + clique ───────────
        private void Update()
        {
            if (UnityEngine.InputSystem.Mouse.current == null) return;
            var pos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();

            bool insideLog = _logScrollRect != null &&
                RectTransformUtility.RectangleContainsScreenPoint(
                    _logScrollRect.viewport, pos, _canvas.worldCamera);

            if (!insideLog) { SetHoverLine(-1); HideTooltip(); return; }

            int found = -1;
            for (int i = 0; i < _logLines.Count; i++)
            {
                var ll = _logLines[i];
                if (ll.go == null || !ll.go.activeInHierarchy) continue;
                var rt = ll.go.GetComponent<RectTransform>();
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, pos, _canvas.worldCamera))
                { found = i; break; }
            }

            SetHoverLine(found);

            if (found >= 0 && !_logLines[found].isHeader && _logLines[found].entryIdx >= 0)
                ShowTooltip(_logLines[found].entryIdx, pos);
            else
                HideTooltip();

            MoveTooltip(pos);

            if (UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame && found >= 0)
            {
                var ll = _logLines[found];
                if (ll.isHeader)
                    ToggleSection(ll.sectionIdx);
                else if (ll.unit != null)
                    OnLogLineClicked?.Invoke(ll.unit);
            }
        }

        private void SetHoverLine(int newIdx)
        {
            if (_hoveredLineIdx == newIdx) return;
            if (_hoveredLineIdx >= 0 && _hoveredLineIdx < _logLines.Count)
                if (_logLines[_hoveredLineIdx].hoverBg != null)
                    _logLines[_hoveredLineIdx].hoverBg.color = Color.clear;
            _hoveredLineIdx = newIdx;
            if (newIdx >= 0 && newIdx < _logLines.Count)
                if (_logLines[newIdx].hoverBg != null)
                    _logLines[newIdx].hoverBg.color = CorHover;
        }

        private void ToggleSection(int sIdx)
        {
            if (sIdx < 0 || sIdx >= _sections.Count) return;
            var s = _sections[sIdx];
            s.expanded = !s.expanded;

            foreach (int li in s.lineIndices)
                if (li < _logLines.Count && _logLines[li].go != null)
                    _logLines[li].go.SetActive(s.expanded);

            if (s.chevron != null)
            {
                string t = s.chevron.text;
                if (t.Length > 0 && (t[0] == 'v' || t[0] == '>'))
                    s.chevron.text = (s.expanded ? "v" : ">") + t.Substring(1);
            }

            Canvas.ForceUpdateCanvases();
        }

        // ── Tooltip ──────────────────────────
        private void ShowTooltip(int entryIdx, Vector2 screenPos)
        {
            if (entryIdx < 0 || entryIdx >= _logEntries.Count) return;
            string tip = _logEntries[entryIdx].tooltipText;
            if (string.IsNullOrEmpty(tip)) return;

            _tooltipTxt.text = tip;
            _tooltipGo.SetActive(true);

            Canvas.ForceUpdateCanvases();
            float h = Mathf.Max(36, _tooltipTxt.preferredHeight + 8);
            _tooltipGo.GetComponent<RectTransform>().sizeDelta = new Vector2(260, h);
        }

        private void HideTooltip()
        {
            if (_tooltipGo != null) _tooltipGo.SetActive(false);
        }

        private void MoveTooltip(Vector2 screenPos)
        {
            if (!_tooltipGo.activeSelf) return;
            float x = screenPos.x + 20;
            float y = screenPos.y - 10;
            if (x + 270 > Screen.width)  x = screenPos.x - 270;
            if (y < 0) y = 0;
            if (y > Screen.height) y = Screen.height;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvas.transform as RectTransform, new Vector2(x, y),
                _canvas.worldCamera, out local);
            _tooltipGo.GetComponent<RectTransform>().anchoredPosition = local;
        }

        // ── PROMPT DE BÔNUS ─────────────────
        public void ShowPrompt(string text)
        {
            _promptText.text = text;
            _bonusTimerText.text = "";
            _simButton.gameObject.SetActive(false);
            _naoButton.gameObject.SetActive(false);
            _promptPanel.SetActive(true);
        }

        public void ShowBonusPrompt(string text, Action onSim, Action onNao)
        {
            _promptText.text = text;
            _bonusTimerText.text = "";
            _simButton.gameObject.SetActive(true);
            _naoButton.gameObject.SetActive(true);
            _simButton.onClick.RemoveAllListeners();
            _naoButton.onClick.RemoveAllListeners();
            _simButton.onClick.AddListener(() => onSim?.Invoke());
            _naoButton.onClick.AddListener(() => onNao?.Invoke());
            _promptPanel.SetActive(true);
        }

        public void UpdateBonusTimer(float sec)
            => _bonusTimerText.text = sec > 0f ? sec.ToString("0.0") + "s" : "";

        public void HidePrompt() => _promptPanel.SetActive(false);

        public void ShowEndScreen(string msg)
        {
            _endText.text = msg;
            _endPanel.SetActive(true);
        }

        // ── HELPERS ─────────────────────────
        /// <summary>
        /// Margem entre o topo do painel e o título (ver MakeTituloPainel): grande o
        /// suficiente pra o título ficar fora da curva do canto arredondado da moldura.
        /// </summary>
        private static float TitleTopMargin()
        {
            var T = Tuning.Get();
            return T.windowFrameEnabled ? T.windowFrameCorner + 3f : 2f;
        }

        /// <summary>Altura total da zona reservada pro título (margem + 22px do título em si).
        /// Usada tanto por MakeTituloPainel quanto pelas fórmulas de altura dos painéis de
        /// comando (BuildCommandMenu) — as duas precisam concordar ou o conteúdo se sobrepõe.</summary>
        private static float TitleZoneHeight() => TitleTopMargin() + 22f;

        /// <summary>Respiro extra a reservar no rodapé dos painéis, pra o último elemento não
        /// ficar embaixo da espessura da moldura (contorno+realce+raio do canto).</summary>
        private static float FrameBottomPad()
        {
            var T = Tuning.Get();
            return T.windowFrameEnabled ? T.windowFrameCorner + T.windowFrameBorderPx + 4f : 0f;
        }

        private void MakeTituloPainel(Transform parent, string titulo)
        {
            float margin = TitleTopMargin();

            var go = new GameObject("Titulo", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -margin);
            rt.sizeDelta = new Vector2(-margin * 2f, 22);
            go.AddComponent<Image>().color = CorFFTTitBg;

            var tgo = new GameObject("TituloTxt", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tgo.AddComponent<Text>();
            t.font = _font; t.fontSize = 11; t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = CorFFTTitulo;
            t.text = titulo; t.raycastTarget = false;
        }

        private Text MakeText(Transform parent, string name, Vector2 anchor, Vector2 pos,
                              Vector2 size, int fontSize, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = fontSize; t.alignment = align;
            t.color = Color.white; t.supportRichText = true; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow   = VerticalWrapMode.Overflow;
            return t;
        }

        private void BuildPromptPanel(Transform parent)
        {
            // Mesmo local do CommandMenu (canto inferior direito)
            _promptPanel = new GameObject("PromptPanel", typeof(RectTransform));
            _promptPanel.transform.SetParent(parent, false);
            var rt = _promptPanel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-12, 12);
            rt.sizeDelta = new Vector2(230, 188);
            StyleFFTWindow(_promptPanel);

            MakeTituloPainel(_promptPanel.transform, "COMANDOS");
            float promptBaseY = TitleZoneHeight() + 8f;

            _bonusTimerText = MakeText(_promptPanel.transform, "BonusTimer",
                new Vector2(0.5f, 1f), new Vector2(0, -promptBaseY), new Vector2(210, 24),
                15, TextAnchor.MiddleCenter);
            _bonusTimerText.color = new Color(1f, 0.55f, 0.55f);

            _promptText = MakeText(_promptPanel.transform, "PromptText",
                new Vector2(0.5f, 1f), new Vector2(0, -(promptBaseY + 28f)), new Vector2(210, 56),
                14, TextAnchor.MiddleCenter);
            _promptText.color = new Color(1f, 0.92f, 0.6f);

            // Botões lado a lado dentro do painel
            float promptBtnY = promptBaseY + 98f;
            _simButton = MakePromptButton(_promptPanel.transform, "BtnSim", "Sim",
                new Vector2(-55, -promptBtnY), new Color(0.16f, 0.50f, 0.22f));
            _naoButton = MakePromptButton(_promptPanel.transform, "BtnNao", "Nao",
                new Vector2( 55, -promptBtnY), new Color(0.52f, 0.16f, 0.16f));

            _promptPanel.SetActive(false);
        }

        private Button MakePromptButton(Transform parent, string name, string label,
                                        Vector2 offset, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(96, 40);
            go.AddComponent<Image>().color = color;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = CorFFTBorda; ol.effectDistance = new Vector2(1f, -1f);
            var btn = go.AddComponent<Button>();
            MakeText(go.transform, "Label", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(88, 36), 16, TextAnchor.MiddleCenter).text = label;
            return btn;
        }

        private void BuildEndPanel(Transform parent)
        {
            _endPanel = new GameObject("EndPanel", typeof(RectTransform));
            _endPanel.transform.SetParent(parent, false);
            var rt = _endPanel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _endPanel.AddComponent<Image>().color = new Color(0f, 0f, 0f, Tuning.Get().endScreenDimAlpha);

            var win = new GameObject("EndWin", typeof(RectTransform));
            win.transform.SetParent(_endPanel.transform, false);
            var wrt = win.GetComponent<RectTransform>();
            wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 0.5f);
            wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(820, 240);
            StyleFFTWindow(win);

            // Texto em cima
            _endText = MakeText(win.transform, "EndText", new Vector2(0.5f, 0.5f),
                new Vector2(0, 45), new Vector2(780, 100), 40, TextAnchor.MiddleCenter);

            // Botão "Voltar ao Menu" — largo, embaixo do texto
            var menuGo = new GameObject("BtnMenu", typeof(RectTransform));
            menuGo.transform.SetParent(win.transform, false);
            var mrt = menuGo.GetComponent<RectTransform>();
            mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 0.5f);
            mrt.anchoredPosition = new Vector2(0, -65);
            mrt.sizeDelta = new Vector2(260, 54);
            menuGo.AddComponent<Image>().color = new Color(0.16f, 0.42f, 0.22f);
            var mol = menuGo.AddComponent<Outline>();
            mol.effectColor = CorFFTBorda; mol.effectDistance = new Vector2(1.5f, -1.5f);
            var menuBtn = menuGo.AddComponent<Button>();
            MakeText(menuGo.transform, "Label", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(250, 48), 20, TextAnchor.MiddleCenter).text = "Voltar ao Menu";
            menuBtn.onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));

            _endPanel.SetActive(false);
        }

        // ── MULTIPLAYER: aguardando posicionamento ───────────────────────────
        private GameObject _mpWaitingOverlay;

        public void ShowWaitingForPlacement()
        {
            if (_mpWaitingOverlay != null) return;

            var canvas = GetComponentInParent<Canvas>() ?? FindAnyObjectByType<Canvas>();
            if (canvas == null) return;

            _mpWaitingOverlay = new GameObject("MpWaitingOverlay", typeof(RectTransform));
            _mpWaitingOverlay.transform.SetParent(canvas.transform, false);
            var rt = _mpWaitingOverlay.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 60);
            rt.sizeDelta = new Vector2(500, 50);

            var bg = _mpWaitingOverlay.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.04f, 0.05f, 0.08f, 0.88f);

            var go = new GameObject("Lbl", typeof(RectTransform));
            go.transform.SetParent(_mpWaitingOverlay.transform, false);
            var lrt = go.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var txt = go.AddComponent<UnityEngine.UI.Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 20;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = new Color(0.92f, 0.80f, 0.35f);
            txt.text = "Aguardando jogadores posicionarem...";
            txt.raycastTarget = false;
            _mpWaitingText = txt;
        }

        private UnityEngine.UI.Text _mpWaitingText;
        /// <summary>Atualiza o texto do overlay de posicionamento (se visível).</summary>
        public void SetWaitingText(string s)
        {
            if (_mpWaitingText != null) _mpWaitingText.text = s;
        }

        public void HideWaitingForPlacement()
        {
            if (_mpWaitingOverlay != null)
            {
                Destroy(_mpWaitingOverlay);
                _mpWaitingOverlay = null;
            }
        }
    }
}
