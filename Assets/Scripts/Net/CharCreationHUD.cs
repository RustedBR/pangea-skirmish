// Net/CharCreationHUD.cs
// Overlay de criação de personagem para o modo multiplayer (em UI Toolkit).
// Exibido sobre a cena Sandbox após a fase MapEditing, sem trocar de cena.
// Criado e destruído por MpPhaseDirector ao detectar fase CharCreation.
//
// Herda de PangeaScreen e carrega o layout de Resources/UI/Screens/CharCreation.uxml.
// O layout espelha o editor de personagem do lobby (MainMenuManager): sidebar de
// personagens salvos (Nome / Classe), preview central (sprite + arma + stats derivados)
// e painel de customização (aparência/arma + steppers de atributo com texto de
// efeito derivado). O envio ao servidor MP é disparado por "Salvar".

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using PangeaSkirmish.UI;

namespace PangeaSkirmish
{
    public class CharCreationHUD : PangeaScreen
    {
        protected override string UxmlResource => "UI/Screens/CharCreation";

        // ---- Estado em edição -----------------------------------------------
        private CharacterPreset _editing;
        private int _budget;
        private bool _isNewPreset;
        private string _origName;

        // ---- UI references (sidebar) ---------------------------------------
        private VisualElement _presetListContent;
        private List<Button> _presetRows = new List<Button>();

        // ---- UI references (preview + customização) -------------------------
        private TextField _nameInput;
        private Label _classLabel;
        private Label _weaponLabel;
        private Image _classSpriteImg;
        private Image _weaponSpriteImg;
        private Label _previewText;
        private Label[] _attrLabels = new Label[6];
        private Label[] _valLabels = new Label[6];
        private Label _statusLbl;

        // ---- Índices de navegação -------------------------------------------
        private int _spriteIdx;
        private int _weaponIdx;

        private static readonly string[] AttrNames = { "STR", "VIT", "DEX", "AGI", "INT", "WIS" };

        // =========================================================================
        // Bind
        // =========================================================================
        protected override void Bind()
        {
            _budget = RuntimeMultiplayerSession.CurrentConfig.attributeBudget;

            var r = Root;

            _presetListContent = r.Q<VisualElement>("preset-list-content");
            r.Q<Button>("preset-new")?.RegisterCallback<ClickEvent>(_ => NewPreset());

            _nameInput  = r.Q<TextField>("name-input");
            _classLabel = r.Q<Label>("class-label");
            _weaponLabel = r.Q<Label>("weapon-label");
            _classSpriteImg = r.Q<Image>("class-sprite");
            _weaponSpriteImg = r.Q<Image>("weapon-sprite");
            _previewText = r.Q<Label>("preview-text");
            _statusLbl = r.Q<Label>("status-label");

            if (_nameInput != null)
                _nameInput.RegisterValueChangedCallback(evt => _editing.presetName = evt.newValue);

            r.Q<Button>("class-next")?.RegisterCallback<ClickEvent>(_ => { CycleClass(); });
            r.Q<Button>("weapon-next")?.RegisterCallback<ClickEvent>(_ => { CycleWeapon(); });

            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                r.Q<Button>($"attr-minus-{i}")?.RegisterCallback<ClickEvent>(_ => AdjustAttr(idx, -1));
                r.Q<Button>($"attr-plus-{i}")?.RegisterCallback<ClickEvent>(_ => AdjustAttr(idx, +1));
                _attrLabels[i] = r.Q<Label>($"attr-val-{i}");
                _valLabels[i] = r.Q<Label>($"attr-effect-{i}");
            }

            r.Q<Button>("btn-back")?.RegisterCallback<ClickEvent>(_ => OnBack());
            r.Q<Button>("btn-save")?.RegisterCallback<ClickEvent>(_ => { SavePreset(); OnConfirmMP(); });
            r.Q<Button>("btn-delete")?.RegisterCallback<ClickEvent>(_ => DeletePreset());

            if (RoomManager.Instance != null)
                RoomManager.Instance.OnCharacterRejected += OnRejected;

            RebuildPresetList();
            NewPreset();
        }

        private void OnDestroy()
        {
            if (RoomManager.Instance != null)
                RoomManager.Instance.OnCharacterRejected -= OnRejected;
        }

        // =========================================================================
        // Sidebar — personagens salvos (Nome / Classe, igual ao lobby)
        // =========================================================================
        private void RebuildPresetList()
        {
            if (_presetListContent == null) return;
            foreach (var row in _presetRows) row.RemoveFromHierarchy();
            _presetRows.Clear();

            var presets = CharacterStorage.LoadAll();
            foreach (var p in presets)
            {
                var preset = p;
                var row = new Button(() => LoadPreset(preset));
                row.AddToClassList("cc-preset-row");
                // Lobby: nome em destaque + classe menor à direita
                row.text = preset.presetName;
                var spriteDef = CharacterSpriteCatalog.GetByPath(preset.spritePath);
                var sub = new Label(spriteDef != null ? spriteDef.displayName : "");
                sub.AddToClassList("cc-preset-sub");
                row.Add(sub);
                _presetListContent.Add(row);
                _presetRows.Add(row);
            }
        }

        private void NewPreset()
        {
            _isNewPreset = true;
            _origName = "";
            _editing = new CharacterPreset
            {
                presetName = RuntimeMultiplayerSession.PlayerName,
                spritePath = CharacterSpriteCatalog.Default,
                weaponId   = "Hatchet",
                stats      = new UnitStatBlock
                {
                    STR = CharacterConfig.AttrMin, VIT = CharacterConfig.AttrMin,
                    DEX = CharacterConfig.AttrMin, AGI = CharacterConfig.AttrMin,
                    INT = CharacterConfig.AttrMin, WIS = CharacterConfig.AttrMin,
                    Footprint = 3,
                    AttackRange = WeaponCatalog.Get("Hatchet")?.range ?? 1
                }
            };
            SyncEditorToPreset();
            RefreshPreview();
        }

        private void LoadPreset(CharacterPreset preset)
        {
            _isNewPreset = false;
            _origName = preset.presetName;
            _editing = new CharacterPreset
            {
                presetName = preset.presetName,
                spritePath = preset.spritePath,
                weaponId   = preset.weaponId,
                stats      = new UnitStatBlock
                {
                    STR = preset.stats.STR, VIT = preset.stats.VIT,
                    DEX = preset.stats.DEX, AGI = preset.stats.AGI,
                    INT = preset.stats.INT, WIS = preset.stats.WIS,
                    Footprint = preset.stats.Footprint,
                    AttackRange = preset.stats.AttackRange
                }
            };
            SyncEditorToPreset();
            RefreshPreview();
        }

        // =========================================================================
        // Customização
        // =========================================================================
        private void CycleClass()
        {
            var all = CharacterSpriteCatalog.All;
            _spriteIdx = (_spriteIdx + 1) % all.Length;
            _editing.spritePath = all[_spriteIdx].resourcePath;
            SyncEditorToPreset();
            RefreshPreview();
        }

        private void CycleWeapon()
        {
            var ws = WeaponCatalog.All();
            _weaponIdx = (_weaponIdx + 1) % ws.Length;
            _editing.weaponId = ws[_weaponIdx].id;
            SyncEditorToPreset();
            RefreshPreview();
        }

        private void AdjustAttr(int idx, int delta)
        {
            if (_editing == null) return;
            float v = GetAttr(idx) + delta;
            v = Mathf.Clamp(v, CharacterConfig.AttrMin, CharacterConfig.AttrMax);
            SetAttr(idx, v);
            SyncEditorToPreset();
            RefreshPreview();
        }

        private float GetAttr(int idx)
        {
            var s = _editing.stats;
            return idx switch
            {
                0 => s.STR, 1 => s.VIT, 2 => s.DEX,
                3 => s.AGI, 4 => s.INT, 5 => s.WIS,
                _ => 0
            };
        }

        private void SetAttr(int idx, float val)
        {
            var s = _editing.stats;
            switch (idx)
            {
                case 0: s.STR = val; break;
                case 1: s.VIT = val; break;
                case 2: s.DEX = val; break;
                case 3: s.AGI = val; break;
                case 4: s.INT = val; break;
                case 5: s.WIS = val; break;
            }
        }

        private void SyncEditorToPreset()
        {
            if (_nameInput != null) _nameInput.SetValueWithoutNotify(_editing.presetName);

            var spriteDef = CharacterSpriteCatalog.GetByPath(_editing.spritePath);
            if (_classLabel != null)
                _classLabel.text = spriteDef != null ? spriteDef.displayName : "Guerreiro";

            var w = WeaponCatalog.Get(_editing.weaponId);
            if (_weaponLabel != null)
                _weaponLabel.text = w != null ? $"{w.displayName} ({w.damage}/{w.range})" : _editing.weaponId;

            float[] vals = { _editing.stats.STR, _editing.stats.VIT, _editing.stats.DEX,
                             _editing.stats.AGI, _editing.stats.INT, _editing.stats.WIS };
            var d = _editing.stats.ToAttributeStats();
            d.WeaponDamage = WeaponDamageForCurrent();
            for (int i = 0; i < 6; i++)
            {
                if (_attrLabels[i] != null) _attrLabels[i].text = vals[i].ToString("0.#");
                if (_valLabels[i] != null) _valLabels[i].text = AttrDerivedLabel(i, d);
            }
        }

        private int WeaponDamageForCurrent()
        {
            var w = WeaponCatalog.Get(_editing.weaponId);
            if (w != null) return w.damage;
            var t = RuntimeTuning.Active;
            return t != null ? t.unarmedDamage : 1;
        }

        private string AttrDerivedLabel(int idx, AttributeStats d)
        {
            return idx switch
            {
                0 => $"dano {d.PhysicalDamage}",
                1 => $"HP {d.MaxHP}  def {d.PhysicalDefense}",
                2 => $"crit {Mathf.RoundToInt(d.CritChance * 100)}%  PAB {d.BonusActionPoints}",
                3 => $"esq {Mathf.RoundToInt(d.DodgeChance * 100)}%  mov {d.MoveBudget}  PA {d.ActionPoints}",
                4 => $"dano {d.MagicDamage}",
                5 => $"mana {d.MaxMana}  res {d.MagicDefense}",
                _ => ""
            };
        }

        // =========================================================================
        // Preview (sprite + arma + stats derivados) — igual ao lobby
        // =========================================================================
        private void RefreshPreview()
        {
            if (_previewText == null) return;
            var d = _editing.stats.ToAttributeStats();
            d.WeaponDamage = WeaponDamageForCurrent();
            _previewText.text = $"HP: {d.MaxHP}\n" +
                                $"Mana: {d.MaxMana}\n" +
                                $"Ataque: {d.PhysicalDamage}\n" +
                                $"Mágico: {d.MagicDamage}\n" +
                                $"Iniciativa: {d.Initiative}\n" +
                                $"Movimento: {d.MoveBudget}\n" +
                                $"PA: {d.ActionPoints}  PAB: {d.BonusActionPoints}\n" +
                                $"Precisão: {Mathf.RoundToInt(d.HitChance * 100)}%\n" +
                                $"Crítico: {Mathf.RoundToInt(d.CritChance * 100)}%\n" +
                                $"Defesa: {d.PhysicalDefense}\n" +
                                $"Resistência: {d.MagicDefense}";

            // Sprite do personagem (frame walkingSE_0) — usa .sprite direto (UI Toolkit)
            if (_classSpriteImg != null)
            {
                string path = !string.IsNullOrEmpty(_editing.spritePath)
                    ? _editing.spritePath : CharacterSpriteCatalog.Default;
                var all = Resources.LoadAll<Sprite>(path);
                Sprite s = null;
                if (all != null && all.Length > 0)
                {
                    foreach (var sp in all) if (sp.name == "walkingSE_0") { s = sp; break; }
                    if (s == null) s = all[0];
                }
                _classSpriteImg.sprite = s;
                _classSpriteImg.style.display = s != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Sprite da arma (primeiro frame attackSE) — igual ao lobby
            if (_weaponSpriteImg != null && !string.IsNullOrEmpty(_editing.weaponId))
            {
                var w = WeaponCatalog.Get(_editing.weaponId);
                Sprite ws = null;
                if (w != null)
                {
                    var all = Resources.LoadAll<Sprite>($"Sprites/TinyTactics/Weapons/{_editing.weaponId}attackSE");
                    if (all != null && all.Length > 0)
                    {
                        string firstKey = $"{_editing.weaponId}attackSE_0";
                        foreach (var sp in all) if (sp.name == firstKey) { ws = sp; break; }
                        if (ws == null) ws = all[0];
                    }
                }
                _weaponSpriteImg.sprite = ws;
                _weaponSpriteImg.style.display = ws != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // =========================================================================
        // Ações
        // =========================================================================
        private void OnBack()
        {
            if (_statusLbl != null) _statusLbl.text = "";
        }

        private void SavePreset()
        {
            if (_editing == null) return;
            if (!_isNewPreset && !string.IsNullOrWhiteSpace(_origName) && _origName != _editing.presetName)
                CharacterStorage.Delete(_origName);
            CharacterStorage.Save(_editing);
            _isNewPreset = false;
            _origName = _editing.presetName;
            RebuildPresetList();
            if (_statusLbl != null) _statusLbl.text = $"Salvo: {_editing.presetName}";
        }

        private void DeletePreset()
        {
            if (_editing == null || _isNewPreset) return;
            if (!string.IsNullOrEmpty(_origName))
            {
                CharacterStorage.Delete(_origName);
                NewPreset();
                RebuildPresetList();
            }
        }

        // Envia o personagem ao servidor MP (chamado por "Salvar")
        private void OnConfirmMP()
        {
            if (SumAttrs() != _budget) return;
            if (string.IsNullOrWhiteSpace(_editing.presetName))
                _editing.presetName = RuntimeMultiplayerSession.PlayerName;

            var w = WeaponCatalog.Get(_editing.weaponId);
            if (w != null) _editing.stats.AttackRange = w.range;

            string json = JsonUtility.ToJson(_editing);
            Debug.Log($"[MP] Personagem criado (enviando): {_editing.presetName} pts={SumAttrs()}/{_budget} arma={_editing.weaponId} range={_editing.stats.AttackRange}");
            RoomManager.Instance?.SubmitCharacterServerRpc(json);
            RuntimeMultiplayerSession.LocalCharacterPreset = _editing;

            if (_statusLbl != null) _statusLbl.text = "Aguardando confirmação...";
        }

        private void OnRejected(string reason)
        {
            if (_statusLbl != null) _statusLbl.text = $"Rejeitado: {reason}";
        }

        private int SumAttrs()
        {
            var s = _editing.stats;
            return (int)(s.STR + s.VIT + s.DEX + s.AGI + s.INT + s.WIS);
        }
    }
}
