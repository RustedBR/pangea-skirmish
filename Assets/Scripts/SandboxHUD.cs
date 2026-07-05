using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PangeaSkirmish
{
    /// <summary>
    /// UI do editor de mapas (Modo Sandbox). Barra superior + painel lateral por fase
    /// (pincéis de terreno / classes+presets de unidade) + painel de stats à direita.
    /// </summary>
    public class SandboxHUD : MonoBehaviour
    {
        private SandboxController _ctrl;
        private Font _font;

        private GameObject _terrainPanel;
        private GameObject _unitsPanel;
        private GameObject _statsPanel;
        private Text _phaseLabel;
        private Text _toastLabel;
        private InputField _mapNameInput;
        private InputField _unitNameInput;

        // Botões de pincel / classe (para destaque do selecionado)
        private readonly List<Image> _brushImgs = new List<Image>();
        private readonly List<Image> _classImgs = new List<Image>();

        // Tema compartilhado com o menu (GameTuning, seção Menu & Sandbox)
        private static Color BtnNormal => Tuning.Get().uiButtonNormalColor;
        private static Color BtnActive => Tuning.Get().uiButtonActiveColor;
        private static Color PanelBg   => Tuning.Get().uiPanelBgColor;

        public void Build(Transform canvas, SandboxController ctrl)
        {
            _ctrl = ctrl;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BuildTopBar(canvas);

            _terrainPanel = MakePanel(canvas, "TerrainPanel", new Vector2(0, 0.5f),
                new Vector2(10, -80), new Vector2(250, 320), PanelBg);
            BuildBrushButtons(_terrainPanel.transform);

            _unitsPanel = MakePanel(canvas, "UnitsPanel", new Vector2(0, 0.5f),
                new Vector2(10, -30), new Vector2(250, 200), PanelBg);
            BuildUnitsPanelContent(true);

            _statsPanel = MakePanel(canvas, "StatsPanel", new Vector2(1, 0.5f),
                new Vector2(-10, 0), new Vector2(320, 600), new Color(0.05f, 0.06f, 0.09f, 0.94f));
            BuildStatsPanel(_statsPanel.transform);

            // Toast (inferior-central)
            var toastGo = new GameObject("Toast", typeof(RectTransform));
            toastGo.transform.SetParent(canvas, false);
            var toastRt = toastGo.GetComponent<RectTransform>();
            toastRt.anchorMin = toastRt.anchorMax = toastRt.pivot = new Vector2(0.5f, 0f);
            toastRt.anchoredPosition = new Vector2(0, 30);
            toastRt.sizeDelta = new Vector2(600, 40);
            _toastLabel = MakeLabel(toastGo.transform, Vector2.zero, new Vector2(600, 40), 18,
                Tuning.Get().toastTextColor);
            _toastLabel.text = "";

            HighlightList(_brushImgs, 0);
            _statsPanel.SetActive(false);
        }

        // ── BARRA SUPERIOR ───────────────────
        private void BuildTopBar(Transform canvas)
        {
            var topBar = new GameObject("TopBar", typeof(RectTransform));
            topBar.transform.SetParent(canvas, false);
            var rt = topBar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, 0);
            rt.sizeDelta = new Vector2(0, 80);
            var topBarImg = topBar.AddComponent<Image>();
            topBarImg.color = new Color(0.05f, 0.06f, 0.08f, 0.95f);
            topBarImg.raycastTarget = false;

            _mapNameInput = MakeInputField(topBar.transform, new Vector2(-820, -10), new Vector2(240, 44),
                "Novo Mapa", v => _ctrl.SetMapName(v));

            _prevBtn = MakeTopButton(topBar.transform, "◀ Voltar", new Vector2(-560, -10), new Vector2(110, 44),
                new Color(0.25f, 0.25f, 0.35f), () =>
                {
                    int p = (int)_currentPhase - 1;
                    if (p >= 0) _ctrl?.SetPhase((SandboxController.Phase)p);
                });
            _nextBtn = MakeTopButton(topBar.transform, "Próxima ▶", new Vector2(-440, -10), new Vector2(120, 44),
                new Color(0.25f, 0.25f, 0.35f), () =>
                {
                    int p = (int)_currentPhase + 1;
                    if (p <= 2) _ctrl?.SetPhase((SandboxController.Phase)p);
                });

            _phaseLabel = MakeLabel(topBar.transform, new Vector2(0, -10), new Vector2(300, 44), 22,
                new Color(0.92f, 0.80f, 0.35f));
            _phaseLabel.alignment = TextAnchor.MiddleCenter;
            _phaseLabel.text = "1. Terreno";

            _saveBtn = MakeTopButton(topBar.transform, "Salvar", new Vector2(760, -10), new Vector2(120, 44),
                new Color(0.18f, 0.40f, 0.20f), () => _ctrl?.SaveMap());
            MakeTopButton(topBar.transform, "Menu", new Vector2(890, -10), new Vector2(110, 44),
                new Color(0.40f, 0.16f, 0.16f), () => _ctrl?.BackToMenu());
        }

        // ── PINCÉIS DE TERRENO (scroll via ScrollRect + VerticalLayoutGroup) ──
        private void BuildBrushButtons(Transform parent)
        {
            float panelW = 250f;
            float viewH = 280f;

            // Título (fora do scroll)
            MakeLabel(parent, new Vector2(0, 140), new Vector2(panelW - 20, 24), 14,
                new Color(0.70f, 0.74f, 0.82f)).text = "Pincéis de terreno";

            // ScrollView (Image de fundo + RectMask2D)
            var svGo = new GameObject("BrushScroll", typeof(RectTransform), typeof(RectMask2D));
            svGo.transform.SetParent(parent, false);
            var svRt = svGo.GetComponent<RectTransform>();
            svRt.anchorMin = svRt.anchorMax = svRt.pivot = new Vector2(0.5f, 0.5f);
            svRt.anchoredPosition = new Vector2(0, -25);
            svRt.sizeDelta = new Vector2(panelW - 20, viewH);
            var svBg = svGo.AddComponent<Image>();
            svBg.color = new Color(0.10f, 0.11f, 0.14f, 0.95f);

            // Content (anchor top, VerticalLayoutGroup + ContentSizeFitter)
            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(svRt, false);
            var cRt = cGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0, 1);
            cRt.anchorMax = new Vector2(1, 1);
            cRt.pivot = new Vector2(0.5f, 1);
            cRt.anchoredPosition = Vector2.zero;
            cRt.sizeDelta = new Vector2(0, 0);

            var vlg = cGo.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 3;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            cGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.MinSize;

            // ScrollRect
            var sr = svGo.AddComponent<ScrollRect>();
            sr.viewport = svRt;
            sr.content = cRt;
            sr.vertical = true;
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;

            // Scrollbar
            var sbGo = new GameObject("Scrollbar", typeof(RectTransform));
            sbGo.transform.SetParent(svRt, false);
            var sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(1, 0);
            sbRt.anchorMax = new Vector2(1, 1);
            sbRt.pivot = new Vector2(1, 0.5f);
            sbRt.sizeDelta = new Vector2(8, 0);
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

            // Entradas
            var brushes = TilePalette.Brushes;
            for (int i = 0; i < brushes.Length; i++)
            {
                var brush = brushes[i];
                var go = MakeListEntry(cGo.transform, 0, out var img);
                _brushImgs.Add(img);

                var le = go.AddComponent<LayoutElement>();
                le.preferredHeight = 38;
                le.preferredWidth = panelW - 30;

                if (_ctrl != null && _ctrl.Tiles != null)
                {
                    var sp = _ctrl.Tiles.GetTile(brush.tileIndex);
                    if (sp != null)
                    {
                        var iconGo = new GameObject("Icon", typeof(RectTransform));
                        iconGo.transform.SetParent(go.transform, false);
                        var iconRt = iconGo.GetComponent<RectTransform>();
                        iconRt.anchorMin = iconRt.anchorMax = iconRt.pivot = new Vector2(0f, 0.5f);
                        iconRt.anchoredPosition = new Vector2(8, 0);
                        iconRt.sizeDelta = new Vector2(30, 30);
                        var iconImg = iconGo.AddComponent<Image>();
                        iconImg.sprite = sp;
                        iconImg.preserveAspect = true;
                        iconImg.raycastTarget = false;
                    }
                }

                MakeLabel(go.transform, new Vector2(20, 0), new Vector2(170, 36), 16,
                    new Color(0.85f, 0.88f, 0.92f)).text = brush.name;

                int idx = i;
                go.GetComponent<Button>().onClick.AddListener(() =>
                    { HighlightList(_brushImgs, idx); _ctrl?.SetBrush(brush); });
            }
        }

        // ── SPRITES + PRESETS DE UNIDADE ─────
        private void BuildUnitsPanelContent(bool highlightFirst)
        {
            // Limpa conteúdo anterior
            foreach (Transform c in _unitsPanel.transform) Destroy(c.gameObject);
            _classImgs.Clear();

            var sprites = CharacterSpriteCatalog.All;
            var presets = CharacterStorage.LoadAll();
            int total = sprites.Length + presets.Count;

            // Redimensiona o painel conforme a quantidade de entradas
            float panelH = 60 + total * 44 + 10;
            _unitsPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(250, panelH);
            float top = panelH * 0.5f;

            MakeLabel(_unitsPanel.transform, new Vector2(0, top - 22), new Vector2(230, 26), 15,
                new Color(0.70f, 0.74f, 0.82f)).text = "Aparência / Personagem";

            float y = top - 56;
            int idx = 0;

            foreach (var spriteDef in sprites)
            {
                var go = MakeListEntry(_unitsPanel.transform, y, out var img);
                _classImgs.Add(img);
                MakeLabel(go.transform, Vector2.zero, new Vector2(220, 36), 16,
                    new Color(0.85f, 0.88f, 0.92f)).text = spriteDef.displayName;
                int local = idx;
                string path = spriteDef.resourcePath;
                go.GetComponent<Button>().onClick.AddListener(() =>
                    { HighlightList(_classImgs, local); _ctrl?.SetSprite(path); });
                y -= 44; idx++;
            }

            foreach (var preset in presets)
            {
                var go = MakeListEntry(_unitsPanel.transform, y, out var img);
                _classImgs.Add(img);
                MakeLabel(go.transform, Vector2.zero, new Vector2(220, 36), 15,
                    new Color(0.80f, 0.92f, 0.80f)).text = "★ " + preset.presetName;
                int local = idx;
                var pst = preset;
                go.GetComponent<Button>().onClick.AddListener(() =>
                    { HighlightList(_classImgs, local); _ctrl?.SetPreset(pst); });
                y -= 44; idx++;
            }

            if (highlightFirst && _classImgs.Count > 0) HighlightList(_classImgs, 0);
        }

        public void RebuildClassPalette() => BuildUnitsPanelContent(false);

        // ── PAINEL DE STATS ──────────────────
        private void BuildStatsPanel(Transform parent)
        {
            _unitNameInput = MakeInputField(parent, new Vector2(0, 220), new Vector2(280, 40),
                "Unidade", v => _ctrl.SetSelectedName(v));

            // Linha de arma
            var weaponRow = new GameObject("WeaponRow", typeof(RectTransform));
            weaponRow.transform.SetParent(parent, false);
            var wrt = weaponRow.GetComponent<RectTransform>();
            wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.anchoredPosition = new Vector2(0, 175);
            wrt.sizeDelta = new Vector2(290, 36);

            MakeLabel(weaponRow.transform, new Vector2(-115, 0), new Vector2(70, 32), 16,
                new Color(0.78f, 0.80f, 0.85f)).text = "Arma:";

            var weaponLbl = MakeLabel(weaponRow.transform, new Vector2(-10, 0), new Vector2(120, 32), 16,
                new Color(1f, 0.92f, 0.6f));
            weaponLbl.name = "WeaponLabel";
            weaponLbl.text = "";

            var btnCycleWeapon = MakeBtn(weaponRow.transform, new Vector2(0.5f, 0.5f), new Vector2(95, 0),
                new Vector2(50, 32), new Color(0.25f, 0.42f, 0.65f));
            MakeLabel(btnCycleWeapon.transform, Vector2.zero, new Vector2(50, 32), 16, Color.white).text = "→";
            btnCycleWeapon.onClick.AddListener(() => _ctrl?.CycleSelectedWeapon());

            string[] names = { "STR", "VIT", "DEX", "AGI", "INT", "WIS" };
            float y = 120;
            for (int i = 0; i < 6; i++)
            {
                BuildAttrRow(parent, names[i], y);
                y -= 40;
            }

            var derivedLbl = MakeLabel(parent, new Vector2(0, -125), new Vector2(290, 140), 14,
                new Color(0.65f, 0.78f, 0.92f));
            derivedLbl.alignment = TextAnchor.UpperLeft;
            derivedLbl.name = "DerivedLabel";
            derivedLbl.text = "";

            var btnSave = MakeBtn(parent, new Vector2(0.5f, 0.5f), new Vector2(0, -290),
                new Vector2(250, 40), new Color(0.20f, 0.34f, 0.50f));
            MakeLabel(btnSave.transform, Vector2.zero, new Vector2(250, 36), 16, Color.white).text = "★ Salvar personagem";
            btnSave.onClick.AddListener(() => _ctrl?.SaveSelectedAsPreset());

            var btnDelete = MakeBtn(parent, new Vector2(0.5f, 0.5f), new Vector2(0, -340),
                new Vector2(250, 40), new Color(0.45f, 0.18f, 0.18f));
            MakeLabel(btnDelete.transform, Vector2.zero, new Vector2(250, 36), 16, Color.white).text = "Remover unidade";
            btnDelete.onClick.AddListener(() => _ctrl?.DeleteSelectedUnit());
        }

        private void BuildAttrRow(Transform parent, string attrName, float y)
        {
            var row = new GameObject(attrName + "Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(290, 36);

            MakeLabel(row.transform, new Vector2(-115, 0), new Vector2(70, 32), 16,
                new Color(0.78f, 0.80f, 0.85f)).text = attrName;

            var valText = MakeLabel(row.transform, new Vector2(-40, 0), new Vector2(50, 32), 18, Color.white);
            valText.name = "Val";
            valText.alignment = TextAnchor.MiddleCenter;

            var btnMinus = MakeBtn(row.transform, new Vector2(0.5f, 0.5f), new Vector2(55, 0),
                new Vector2(36, 32), new Color(0.45f, 0.18f, 0.18f));
            MakeLabel(btnMinus.transform, Vector2.zero, new Vector2(36, 32), 20, Color.white).text = "−";
            btnMinus.onClick.AddListener(() => _ctrl?.EditSelectedStat(attrName, -1));

            var btnPlus = MakeBtn(row.transform, new Vector2(0.5f, 0.5f), new Vector2(100, 0),
                new Vector2(36, 32), new Color(0.18f, 0.38f, 0.18f));
            MakeLabel(btnPlus.transform, Vector2.zero, new Vector2(36, 32), 20, Color.white).text = "+";
            btnPlus.onClick.AddListener(() => _ctrl?.EditSelectedStat(attrName, +1));
        }

        // ── API pública (chamada pelo controller) ──
        private SandboxController.Phase _currentPhase = SandboxController.Phase.Terrain;

        public void SetPhaseUI(SandboxController.Phase p)
        {
            _currentPhase = p;
            _terrainPanel?.SetActive(p == SandboxController.Phase.Terrain);
            _unitsPanel?.SetActive(p != SandboxController.Phase.Terrain);

            switch (p)
            {
                case SandboxController.Phase.Terrain: _phaseLabel.text = "1. Terreno";  break;
                case SandboxController.Phase.Allies:  _phaseLabel.text = "2. Aliados";  break;
                case SandboxController.Phase.Enemies: _phaseLabel.text = "3. Inimigos"; break;
            }
        }

        public void ShowUnitEditor(UnitPlacement p)
        {
            if (p == null) { _statsPanel?.SetActive(false); return; }
            _statsPanel?.SetActive(true);

            if (_unitNameInput != null) _unitNameInput.SetTextWithoutNotify(p.displayName);

            // Exibir arma atual
            var weaponLbl = _statsPanel?.transform.Find("WeaponRow/WeaponLabel")?.GetComponent<Text>();
            if (weaponLbl != null)
            {
                var w = WeaponCatalog.Get(p.weaponId);
                weaponLbl.text = w != null ? w.displayName : "Nenhuma";
            }

            string[] names = { "STR", "VIT", "DEX", "AGI", "INT", "WIS" };
            float[] values = { p.stats.STR, p.stats.VIT, p.stats.DEX, p.stats.AGI, p.stats.INT, p.stats.WIS };
            for (int i = 0; i < 6; i++)
            {
                var row = _statsPanel?.transform.Find(names[i] + "Row");
                var valText = row != null ? row.Find("Val")?.GetComponent<Text>() : null;
                if (valText != null) valText.text = values[i].ToString("0.#");
            }

            var derived = _statsPanel?.transform.Find("DerivedLabel")?.GetComponent<Text>();
            if (derived != null)
            {
                var d = p.stats.ToAttributeStats();
                // Aplicar dano da arma aos stats derivados para preview
                // Espelha Unit.EquipWeapon: desarmado usa unarmedDamage (não o weaponBase)
                var w = WeaponCatalog.Get(p.weaponId);
                var t = RuntimeTuning.Active ?? Resources.Load<GameTuning>("GameTuning");
                d.WeaponDamage = w != null ? w.damage : (t != null ? t.unarmedDamage : 1);
                derived.text = $"HP {d.MaxHP}   Mov {d.MoveBudget}   PA {d.ActionPoints}   PAB {d.BonusActionPoints}\n" +
                               $"ATQ {d.PhysicalDamage}   Ini {d.Initiative}\n" +
                               $"Acierto {d.HitChance * 100:F0}%   Esquiva {d.DodgeChance * 100:F0}%   Crítico {d.CritChance * 100:F0}%\n" +
                               $"Def.Física {d.PhysicalDefense}   Def.Mágica {d.MagicDefense}";
            }
        }

        public void SetMapName(string name)
        {
            if (_mapNameInput != null) _mapNameInput.SetTextWithoutNotify(name);
        }

        public void SetNoClassSelected()
        {
            for (int i = 0; i < _classImgs.Count; i++)
                if (_classImgs[i] != null) _classImgs[i].color = BtnNormal;
        }

        public void ShowToast(string msg)
        {
            if (_toastLabel != null) _toastLabel.text = msg;
        }

        // ── HELPERS ──────────────────────────
        private void HighlightList(List<Image> imgs, int activeIdx)
        {
            for (int i = 0; i < imgs.Count; i++)
                if (imgs[i] != null) imgs[i].color = (i == activeIdx) ? BtnActive : BtnNormal;
        }

        /// <summary>Cria uma linha-botão padrão (230×38) centrada em x, no y dado.</summary>
        private GameObject MakeListEntry(Transform parent, float y, out Image img)
        {
            var go = new GameObject("Entry", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(230, 38);
            img = go.AddComponent<Image>();
            img.color = BtnNormal;
            go.AddComponent<Button>();
            return go;
        }

        /// <summary>InputField legacy criado com segurança: GameObject desativado durante a
        /// montagem para o OnEnable só rodar depois de textComponent atribuído.</summary>
        private InputField MakeInputField(Transform parent, Vector2 pos, Vector2 size,
            string initial, UnityEngine.Events.UnityAction<string> onChanged)
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
            input.onValueChanged.AddListener(onChanged);
            go.SetActive(true);
            return input;
        }

        private GameObject MakePanel(Transform canvas, string name, Vector2 anchor,
            Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        private Button MakeTopButton(Transform parent, string label, Vector2 pos, Vector2 size,
            Color color, UnityEngine.Events.UnityAction onClick)
        {
            var btn = MakeBtn(parent, new Vector2(0.5f, 0.5f), pos, size, color);
            UiSkin.ApplyButtonSkin(btn.GetComponent<Image>(), color);
            MakeLabel(btn.transform, Vector2.zero, size, 18, Color.white).text = label;
            btn.onClick.AddListener(onClick);
            return btn;
        }

        private Button MakeBtn(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = color;
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

        // ── MODO MULTIPLAYER ──────────────────
        private Text _mpStatusLabel;
        private Button _readyBtn;
        private Button _saveBtn, _nextBtn, _prevBtn;

        /// <summary>
        /// Adapta a HUD para o modo MP: oculta fases Allies/Enemies e Save,
        /// exibe botão "Pronto" e label de espera.
        /// </summary>
        public void SetMpMode(bool mp)
        {
            if (!mp) return;

            // Ocultar painel de unidades (fases Allies/Enemies não existem em MP)
            if (_unitsPanel != null) _unitsPanel.SetActive(false);
            if (_statsPanel  != null) _statsPanel.SetActive(false);

            // Em MP não há Salvar (o mapa vai para a sala) nem navegação de fases
            // (só existe a fase Terreno) — esconder esses botões.
            if (_saveBtn != null) _saveBtn.gameObject.SetActive(false);
            if (_nextBtn != null) _nextBtn.gameObject.SetActive(false);
            if (_prevBtn != null) _prevBtn.gameObject.SetActive(false);

            // Substituir label de fase
            if (_phaseLabel != null) _phaseLabel.text = "Mapa Colaborativo";

            // Botão "Finalizar Mapa" na vaga que era do Salvar (visível na barra superior).
            if (_readyBtn == null)
            {
                var canvas = transform.root; // canvas raiz
                var topBar = canvas.Find("TopBar");
                if (topBar != null)
                {
                    var readyCol = new Color(0.18f, 0.40f, 0.20f);
                    _readyBtn = MakeBtn(topBar, new Vector2(0.5f, 0.5f),
                        new Vector2(760, -10), new Vector2(170, 44), readyCol);
                    UiSkin.ApplyButtonSkin(_readyBtn.GetComponent<Image>(), readyCol);
                    MakeLabel(_readyBtn.transform, Vector2.zero, new Vector2(170, 44), 16, Color.white).text = "Finalizar Mapa";
                    _readyBtn.onClick.AddListener(() =>
                    {
                        _ctrl?.SetReadyMap();
                        if (_readyBtn != null) _readyBtn.interactable = false;
                        ShowMpWaiting(true);
                    });

                    // Label "Aguardando os demais…" (inicialmente oculto)
                    _mpStatusLabel = MakeLabel(topBar, new Vector2(600, -10),
                        new Vector2(190, 44), 15,
                        new Color(0.80f, 0.80f, 0.35f));
                    _mpStatusLabel.text = "";
                }
            }
        }

        public void ShowMpWaiting(bool show)
        {
            if (_mpStatusLabel != null)
                _mpStatusLabel.text = show ? "Aguardando os demais..." : "";
        }
    }
}
