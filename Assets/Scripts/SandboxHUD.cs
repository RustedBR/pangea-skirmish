using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using PangeaSkirmish.UI;

namespace PangeaSkirmish
{
    /// <summary>
    /// HUD do editor de mapas (Modo Sandbox), em UI Toolkit.
    /// Migrado de UGUI → UI Toolkit: herda de PangeaScreen e carrega
    /// Resources/UI/Screens/Sandbox.uxml. A lógica de negócio (pincéis de
    /// terreno, painel de unidades, stats derivados, modos MP/local) foi
    /// preservada; só a camada de apresentação mudou.
    ///
    /// Mantém a mesma API pública usada por SandboxController:
    ///   Build/Init, SetMpMode, SetLocalContentMode, SetPhaseUI, ShowUnitEditor,
    ///   SetMapName, SetNoClassSelected, ShowToast, RebuildClassPalette.
    /// </summary>
    public class SandboxHUD : PangeaScreen
    {
        protected override string UxmlResource => "UI/Screens/Sandbox";

        private SandboxController _ctrl;

        // ── Referências da UI ──
        private TextField _mapNameInput;
        private Button _prevBtn, _nextBtn, _saveBtn, _menuBtn;
        private Label _phaseLabel;
        private VisualElement _terrainPanel, _unitsPanel;
        private ScrollView _brushList, _unitList;
        private VisualElement _statsPanel;
        private TextField _unitNameInput;
        private Label _weaponLabel;
        private Label _derivedLabel;
        private Label _toastLabel;
        private VisualElement _mpFooter;
        private Button _readyBtn;
        private Label _mpStatusLabel;

        // listas de entradas p/ highlight de selecionado
        private readonly List<VisualElement> _brushEntries = new List<VisualElement>();
        private readonly List<VisualElement> _classEntries = new List<VisualElement>();

        private SandboxController.Phase _currentPhase = SandboxController.Phase.Terrain;

        // ── Inicialização (substitui o antigo Build) ──
        public void Init(SandboxController ctrl)
        {
            _ctrl = ctrl;
        }

        protected override void Bind()
        {
            var r = Root;

            _mapNameInput = r.Q<TextField>("map-name");
            _mapNameInput?.RegisterValueChangedCallback(evt => _ctrl?.SetMapName(evt.newValue));

            _prevBtn = r.Q<Button>("prev-btn");
            _prevBtn?.RegisterCallback<ClickEvent>(_ =>
            {
                int p = (int)_currentPhase - 1;
                if (p >= 0) _ctrl?.SetPhase((SandboxController.Phase)p);
            });

            _nextBtn = r.Q<Button>("next-btn");
            _nextBtn?.RegisterCallback<ClickEvent>(_ =>
            {
                int p = (int)_currentPhase + 1;
                if (p <= 2) _ctrl?.SetPhase((SandboxController.Phase)p);
            });

            _phaseLabel = r.Q<Label>("phase-label");

            _saveBtn = r.Q<Button>("save-btn");
            _saveBtn?.RegisterCallback<ClickEvent>(_ => _ctrl?.SaveMap());

            _menuBtn = r.Q<Button>("menu-btn");
            _menuBtn?.RegisterCallback<ClickEvent>(_ => _ctrl?.BackToMenu());

            _terrainPanel = r.Q<VisualElement>("terrain-panel");
            _unitsPanel = r.Q<VisualElement>("units-panel");
            _brushList = r.Q<ScrollView>("brush-list");
            _unitList = r.Q<ScrollView>("unit-list");

            _statsPanel = r.Q<VisualElement>("stats-panel");
            _unitNameInput = r.Q<TextField>("unit-name");
            _unitNameInput?.RegisterValueChangedCallback(evt => _ctrl?.SetSelectedName(evt.newValue));
            _weaponLabel = r.Q<Label>("weapon-label");
            _derivedLabel = r.Q<Label>("derived-label");

            r.Q<Button>("weapon-cycle")?.RegisterCallback<ClickEvent>(_ => _ctrl?.CycleSelectedWeapon());
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                r.Q<Button>($"attr-minus-{i}")?.RegisterCallback<ClickEvent>(_ => _ctrl?.EditSelectedStat(AttrNames[idx], -1));
                r.Q<Button>($"attr-plus-{i}")?.RegisterCallback<ClickEvent>(_ => _ctrl?.EditSelectedStat(AttrNames[idx], +1));
            }
            r.Q<Button>("save-unit-btn")?.RegisterCallback<ClickEvent>(_ => _ctrl?.SaveSelectedAsPreset());
            r.Q<Button>("delete-unit-btn")?.RegisterCallback<ClickEvent>(_ => _ctrl?.DeleteSelectedUnit());

            _toastLabel = r.Q<Label>("toast");

            _mpFooter = r.Q<VisualElement>("mp-footer");
            _readyBtn = r.Q<Button>("ready-btn");
            _mpStatusLabel = r.Q<Label>("mp-status");

            BuildBrushEntries();
            RebuildClassPalette();
            SetPhaseUI(_currentPhase);
        }

        private static readonly string[] AttrNames = { "STR", "VIT", "DEX", "AGI", "INT", "WIS" };

        // ── Pincéis de terreno ──
        private void BuildBrushEntries()
        {
            if (_brushList == null) return;
            _brushList.Clear();
            _brushEntries.Clear();

            var brushes = TilePalette.Brushes;
            for (int i = 0; i < brushes.Length; i++)
            {
                var brush = brushes[i];
                var entry = MakeEntry(brush.name);
                if (_ctrl != null && _ctrl.Tiles != null)
                {
                    var sp = _ctrl.Tiles.GetTile(brush.spriteName);
                    if (sp != null)
                    {
                        var icon = new Image { image = sp.texture, scaleMode = ScaleMode.ScaleToFit };
                        icon.AddToClassList("sb-entry-icon");
                        entry.Insert(0, icon);
                    }
                }
                int idx = i;
                entry.RegisterCallback<ClickEvent>(_ =>
                {
                    HighlightList(_brushEntries, idx);
                    _ctrl?.SetBrush(brush);
                });
                _brushList.Add(entry);
                _brushEntries.Add(entry);
            }
        }

        // ── Sprites + presets de unidade ──
        public void RebuildClassPalette()
        {
            if (_unitList == null) return;
            _unitList.Clear();
            _classEntries.Clear();

            var sprites = CharacterSpriteCatalog.All;
            var presets = CharacterStorage.LoadAll();
            int idx = 0;

            foreach (var spriteDef in sprites)
            {
                var entry = MakeEntry(spriteDef.displayName);
                int local = idx;
                string path = spriteDef.resourcePath;
                entry.RegisterCallback<ClickEvent>(_ =>
                {
                    HighlightList(_classEntries, local);
                    _ctrl?.SetSprite(path);
                });
                _unitList.Add(entry);
                _classEntries.Add(entry);
                idx++;
            }

            foreach (var preset in presets)
            {
                var entry = MakeEntry("★ " + preset.presetName);
                int local = idx;
                var pst = preset;
                entry.RegisterCallback<ClickEvent>(_ =>
                {
                    HighlightList(_classEntries, local);
                    _ctrl?.SetPreset(pst);
                });
                _unitList.Add(entry);
                _classEntries.Add(entry);
                idx++;
            }

            if (_classEntries.Count > 0) HighlightList(_classEntries, 0);
        }

        private VisualElement MakeEntry(string label)
        {
            var entry = new VisualElement();
            entry.AddToClassList("sb-entry");
            var lbl = new Label(label) { name = "entry-label" };
            lbl.AddToClassList("sb-entry-label");
            entry.Add(lbl);
            return entry;
        }

        // ── API pública (chamada pelo controller) ──

        public void SetPhaseUI(SandboxController.Phase p)
        {
            _currentPhase = p;
            if (_terrainPanel != null) _terrainPanel.style.display = (p == SandboxController.Phase.Terrain) ? DisplayStyle.Flex : DisplayStyle.None;
            if (_unitsPanel != null)  _unitsPanel.style.display  = (p != SandboxController.Phase.Terrain) ? DisplayStyle.Flex : DisplayStyle.None;

            if (_phaseLabel != null)
            {
                _phaseLabel.text = p switch
                {
                    SandboxController.Phase.Terrain => "1. Terreno",
                    SandboxController.Phase.Allies  => "2. Aliados",
                    SandboxController.Phase.Enemies => "3. Inimigos",
                    _ => _phaseLabel.text
                };
            }
        }

        public void ShowUnitEditor(UnitPlacement p)
        {
            if (_statsPanel == null) return;
            if (p == null) { _statsPanel.style.display = DisplayStyle.None; return; }
            _statsPanel.style.display = DisplayStyle.Flex;

            if (_unitNameInput != null) _unitNameInput.SetValueWithoutNotify(p.displayName);

            if (_weaponLabel != null)
            {
                var w = WeaponCatalog.Get(p.weaponId);
                _weaponLabel.text = w != null ? w.displayName : "Nenhuma";
            }

            string[] names = { "STR", "VIT", "DEX", "AGI", "INT", "WIS" };
            float[] values = { p.stats.STR, p.stats.VIT, p.stats.DEX, p.stats.AGI, p.stats.INT, p.stats.WIS };
            for (int i = 0; i < 6; i++)
            {
                var val = Root?.Q<Label>($"attr-val-{i}");
                if (val != null) val.text = values[i].ToString("0.#");
            }

            if (_derivedLabel != null)
            {
                var d = p.stats.ToAttributeStats();
                var w = WeaponCatalog.Get(p.weaponId);
                var t = RuntimeTuning.Active ?? Resources.Load<GameTuning>("GameTuning");
                d.WeaponDamage = w != null ? w.damage : (t != null ? t.unarmedDamage : 1);
                _derivedLabel.text = $"HP {d.MaxHP}   Mov {d.MoveBudget}   PA {d.ActionPoints}   PAB {d.BonusActionPoints}\n" +
                                     $"ATQ {d.PhysicalDamage}   Ini {d.Initiative}\n" +
                                     $"Acierto {d.HitChance * 100:F0}%   Esquiva {d.DodgeChance * 100:F0}%   Crítico {d.CritChance * 100:F0}%\n" +
                                     $"Def.Física {d.PhysicalDefense}   Def.Mágica {d.MagicDefense}";
            }
        }

        public void SetMapName(string name)
        {
            if (_mapNameInput != null) _mapNameInput.SetValueWithoutNotify(name);
        }

        public void SetNoClassSelected()
        {
            foreach (var e in _classEntries) e.RemoveFromClassList("sb-entry--active");
        }

        public void ShowToast(string msg)
        {
            if (_toastLabel != null) _toastLabel.text = msg;
        }

        // ── Modo local (sala loopback solo) ──
        public void SetLocalContentMode(bool local)
        {
            if (!local) return;
            if (_phaseLabel != null) _phaseLabel.text = "Criar Mapa (Offline)";
            // mantém Salvar + Menu (já presentes na top bar)
        }

        // ── Modo multiplayer ──
        public void SetMpMode(bool mp)
        {
            if (!mp) return;

            // Em MP não há fases de unidade nem salvar (o mapa vai para a sala)
            if (_unitsPanel != null) _unitsPanel.style.display = DisplayStyle.None;
            if (_statsPanel != null) _statsPanel.style.display = DisplayStyle.None;
            if (_saveBtn != null)    _saveBtn.style.display = DisplayStyle.None;
            if (_nextBtn != null)    _nextBtn.style.display = DisplayStyle.None;
            if (_prevBtn != null)    _prevBtn.style.display = DisplayStyle.None;

            if (_phaseLabel != null) _phaseLabel.text = "Mapa Colaborativo";

            if (_mpFooter != null) _mpFooter.style.display = DisplayStyle.Flex;
            _readyBtn?.RegisterCallback<ClickEvent>(_ =>
            {
                _ctrl?.SetReadyMap();
                if (_readyBtn != null) _readyBtn.SetEnabled(false);
                ShowMpWaiting(true);
            });
        }

        public void ShowMpWaiting(bool show)
        {
            if (_mpStatusLabel != null)
                _mpStatusLabel.text = show ? "Aguardando os demais..." : "";
        }

        // ── Helpers ──
        private void HighlightList(List<VisualElement> entries, int activeIdx)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null) continue;
                if (i == activeIdx) entries[i].AddToClassList("sb-entry--active");
                else entries[i].RemoveFromClassList("sb-entry--active");
            }
        }

        /// <summary>Expõe o painel raiz do UIDocument para o SandboxController checar
        /// IsOverButton em UI Toolkit (panel.Pick).</summary>
        public IPanel UiPanel => Root?.panel;
    }
}
