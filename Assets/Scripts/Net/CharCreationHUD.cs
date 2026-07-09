// Net/CharCreationHUD.cs
// Overlay de criação de personagem para o modo multiplayer (em UI Toolkit).
// Exibido sobre a cena Sandbox após a fase MapEditing, sem trocar de cena.
// Criado e destruído por MpPhaseDirector ao detectar fase CharCreation.
//
// Migrado de UGUI → UI Toolkit: a classe herda de PangeaScreen e carrega
// o layout de Resources/UI/Screens/CharCreation.uxml. A lógica de negócio (steppers
// de atributo, budget, sprite/arma picker, submit) foi preservada; só a camada de
// apresentação mudou. O nome da classe foi mantido para não quebrar MpPhaseDirector.

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

        // ---- Modo local (sala loopback solo do menu principal) --------------
        // Em vez de enviar ao RoomManager, salva em CharacterStorage e volta ao menu.
        public bool LocalContentMode { get; set; } = false;

        // ---- UI references -------------------------------------------------
        private Label _budgetLbl;
        private TextField _nameInput;
        private Label _spriteLbl;
        private Image _spritePreview;
        private Label _weaponLbl;
        private Label[] _attrValueLbls;
        private Button _confirmBtn;
        private Label _statusLbl;
        private DropdownField _presetDropdown;
        private Button _presetLoadBtn;
        private List<CharacterPreset> _savedPresets = new List<CharacterPreset>();

        // ---- Índices de navegação -------------------------------------------
        private int _spriteIdx;
        private int _weaponIdx;
        private Sprite[] _spriteFramesCache; // cache do LoadAll do sprite atual

        private static readonly string[] AttrNames = { "STR", "VIT", "DEX", "AGI", "INT", "WIS" };

        // =========================================================================
        // Bind — liga os elementos nomeados do UXML à lógica
        // =========================================================================
        protected override void Bind()
        {
            _budget = RuntimeMultiplayerSession.CurrentConfig.attributeBudget;

            // Inicializar preset com valores mínimos
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
                    // AttackRange vem da arma (não 0 — senão a unidade nasce sem alcance)
                    AttackRange = WeaponCatalog.Get("Hatchet")?.range ?? 1
                }
            };

            var r = Root;

            _budgetLbl  = r.Q<Label>("budget-label");
            _nameInput  = r.Q<TextField>("name-input");
            _spriteLbl  = r.Q<Label>("sprite-label");
            _spritePreview = r.Q<Image>("sprite-preview");
            _weaponLbl  = r.Q<Label>("weapon-label");
            _statusLbl  = r.Q<Label>("status-label");
            _confirmBtn = r.Q<Button>("confirm-btn");
            _presetDropdown = r.Q<DropdownField>("preset-dropdown");
            _presetLoadBtn  = r.Q<Button>("preset-load");

            if (_nameInput != null)
            {
                _nameInput.value = _editing.presetName;
                _nameInput.RegisterValueChangedCallback(evt => _editing.presetName = evt.newValue);
            }

            // Personagens salvos (carregar no MP)
            PopulateSavedPresets();
            _presetLoadBtn?.RegisterCallback<ClickEvent>(_ => LoadSelectedPreset());

            // Sprite picker
            r.Q<Button>("sprite-prev")?.RegisterCallback<ClickEvent>(_ =>
            {
                _spriteIdx = (_spriteIdx - 1 + CharacterSpriteCatalog.All.Length) % CharacterSpriteCatalog.All.Length;
                SyncSprite();
            });
            r.Q<Button>("sprite-next")?.RegisterCallback<ClickEvent>(_ =>
            {
                _spriteIdx = (_spriteIdx + 1) % CharacterSpriteCatalog.All.Length;
                SyncSprite();
            });

            // Arma picker
            r.Q<Button>("weapon-prev")?.RegisterCallback<ClickEvent>(_ =>
            {
                var ws = WeaponCatalog.All();
                _weaponIdx = (_weaponIdx - 1 + ws.Length) % ws.Length;
                SyncWeapon();
            });
            r.Q<Button>("weapon-next")?.RegisterCallback<ClickEvent>(_ =>
            {
                var ws = WeaponCatalog.All();
                _weaponIdx = (_weaponIdx + 1) % ws.Length;
                SyncWeapon();
            });

            // Atributos (6 steppers)
            _attrValueLbls = new Label[6];
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                r.Q<Button>($"attr-minus-{i}")?.RegisterCallback<ClickEvent>(_ => StepAttr(idx, -1));
                r.Q<Button>($"attr-plus-{i}")?.RegisterCallback<ClickEvent>(_ => StepAttr(idx, +1));
                _attrValueLbls[i] = r.Q<Label>($"attr-val-{i}");
            }

            // Confirmar
            _confirmBtn?.RegisterCallback<ClickEvent>(_ => OnConfirm());

            // Inicializar UI
            SyncSprite();
            SyncWeapon();
            RefreshBudgetDisplay();
            RefreshAttrDisplay();

            // Escutar rejeição do servidor
            if (RoomManager.Instance != null)
                RoomManager.Instance.OnCharacterRejected += OnRejected;
        }

        private void OnDestroy()
        {
            if (RoomManager.Instance != null)
                RoomManager.Instance.OnCharacterRejected -= OnRejected;
        }

        // =========================================================================
        // Lógica de atributos
        // =========================================================================
        private void StepAttr(int idx, float delta)
        {
            float[] vals = GetAttrArray();
            float min = CharacterConfig.AttrMin;
            float max = CharacterConfig.AttrMax;

            float newVal = Mathf.Clamp(vals[idx] + delta, min, max);
            float sumOthers = 0;
            for (int i = 0; i < 6; i++) if (i != idx) sumOthers += vals[i];

            // Não ultrapassar budget
            if (sumOthers + newVal > _budget) return;

            SetAttr(idx, newVal);
            RefreshBudgetDisplay();
            RefreshAttrDisplay();
        }

        private float[] GetAttrArray()
        {
            var s = _editing.stats;
            return new[] { s.STR, s.VIT, s.DEX, s.AGI, s.INT, s.WIS };
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

        private int SumAttrs()
        {
            var s = _editing.stats;
            return (int)(s.STR + s.VIT + s.DEX + s.AGI + s.INT + s.WIS);
        }

        private void RefreshBudgetDisplay()
        {
            int used = SumAttrs();
            // Exige gastar TODOS os pontos (design de "distribuir o budget")
            bool ok = used == _budget;
            if (_budgetLbl != null)
            {
                _budgetLbl.text = $"Pontos: {used}/{_budget}";
                _budgetLbl.style.color = ok
                    ? new StyleColor(new Color(0.35f, 0.82f, 0.54f))
                    : new StyleColor(new Color(1f, 0.40f, 0.40f));
            }
            if (_confirmBtn != null) _confirmBtn.SetEnabled(ok);
        }

        private void RefreshAttrDisplay()
        {
            var vals = GetAttrArray();
            for (int i = 0; i < 6; i++)
                if (_attrValueLbls[i] != null) _attrValueLbls[i].text = ((int)vals[i]).ToString();
        }

        private void SyncSprite()
        {
            var all = CharacterSpriteCatalog.All;
            if (_spriteIdx < 0 || _spriteIdx >= all.Length) _spriteIdx = 0;
            _editing.spritePath = all[_spriteIdx].resourcePath;
            if (_spriteLbl != null) _spriteLbl.text = all[_spriteIdx].displayName;

            // Preview do sprite (frame walkingSE_0) — cacheia o LoadAll pra não reload a cada nav
            if (_spritePreview != null)
            {
                _spriteFramesCache = Resources.LoadAll<Sprite>(_editing.spritePath);
                Sprite preview = null;
                if (_spriteFramesCache != null && _spriteFramesCache.Length > 0)
                {
                    foreach (var s in _spriteFramesCache)
                        if (s.name == "walkingSE_0") { preview = s; break; }
                    if (preview == null) preview = _spriteFramesCache[0];
                }
                _spritePreview.image = SpriteToTexture(preview);
            }
        }

        // =========================================================================
        // Personagens salvos (carregar no MP)
        // =========================================================================
        private void PopulateSavedPresets()
        {
            if (_presetDropdown == null) return;
            _savedPresets = CharacterStorage.LoadAll();
            var names = new List<string> { "(novo)" };
            foreach (var p in _savedPresets) names.Add(p.presetName);
            _presetDropdown.choices = names;
            _presetDropdown.value = names[0];
            _presetDropdown.index = 0;
        }

        private void LoadSelectedPreset()
        {
            if (_presetDropdown == null || _presetDropdown.index <= 0) return;
            int i = _presetDropdown.index - 1;
            if (i < 0 || i >= _savedPresets.Count) return;

            var p = _savedPresets[i];
            ApplyPreset(p);

            if (_statusLbl != null) _statusLbl.text = $"Carregado: {p.presetName}";
        }

        /// <summary>Deep-copy de um preset para o estado em edição, atualizando a UI.</summary>
        private void ApplyPreset(CharacterPreset p)
        {
            _editing = new CharacterPreset
            {
                presetName = p.presetName,
                spritePath = p.spritePath,
                weaponId   = p.weaponId,
                stats      = new UnitStatBlock
                {
                    STR = p.stats.STR, VIT = p.stats.VIT,
                    DEX = p.stats.DEX, AGI = p.stats.AGI,
                    INT = p.stats.INT, WIS = p.stats.WIS,
                    Footprint = p.stats.Footprint,
                    AttackRange = p.stats.AttackRange
                }
            };

            // Sincronizar índices de navegação (sprite/weapon) com o preset carregado
            var spriteDef = CharacterSpriteCatalog.GetByPath(_editing.spritePath);
            _spriteIdx = spriteDef != null ? System.Array.IndexOf(CharacterSpriteCatalog.All, spriteDef) : 0;
            if (_spriteIdx < 0) _spriteIdx = 0;
            var ws = WeaponCatalog.All();
            _weaponIdx = 0;
            for (int k = 0; k < ws.Length; k++) if (ws[k].id == _editing.weaponId) { _weaponIdx = k; break; }

            if (_nameInput != null) _nameInput.SetValueWithoutNotify(_editing.presetName);
            SyncSprite();
            SyncWeapon();
            RefreshBudgetDisplay();
            RefreshAttrDisplay();
        }

        private void SyncWeapon()
        {
            var ws = WeaponCatalog.All();
            if (_weaponIdx < 0 || _weaponIdx >= ws.Length) _weaponIdx = 0;
            var w = ws[_weaponIdx];
            _editing.weaponId = w.id;
            // AttackRange da unidade = range da arma (senão nasce sem alcance de ataque)
            _editing.stats.AttackRange = w.range;
            if (_weaponLbl != null)
                _weaponLbl.text = $"{w.displayName} (d{w.damage}/r{w.range})";
        }

        // =========================================================================
        // Confirmar
        // =========================================================================
        private void OnConfirm()
        {
            if (SumAttrs() != _budget) return;
            if (string.IsNullOrWhiteSpace(_editing.presetName))
                _editing.presetName = RuntimeMultiplayerSession.PlayerName;

            // Garante que o AttackRange reflete a arma escolhida no envio
            var w = WeaponCatalog.Get(_editing.weaponId);
            if (w != null) _editing.stats.AttackRange = w.range;

            string json = JsonUtility.ToJson(_editing);
            Debug.Log($"[MP] Personagem criado (enviando): {_editing.presetName} pts={SumAttrs()}/{_budget} arma={_editing.weaponId} range={_editing.stats.AttackRange}");

            if (LocalContentMode)
            {
                // Modo local (offline, sala loopback solo): salva o preset no disco
                // e fecha a sessão, voltando ao menu principal.
                CharacterStorage.Save(_editing);
                RuntimeMultiplayerSession.LocalCharacterPreset = _editing;
                LocalContentLauncher.FinishAndReturnToMenu();
                return;
            }

            RoomManager.Instance?.SubmitCharacterServerRpc(json);

            // Salvar localmente para uso no GameBootstrap MP
            RuntimeMultiplayerSession.LocalCharacterPreset = _editing;

            if (_confirmBtn != null) _confirmBtn.SetEnabled(false);
            if (_statusLbl != null) _statusLbl.text = "Aguardando confirmação...";
        }

        private void OnRejected(string reason)
        {
            if (_confirmBtn != null) _confirmBtn.SetEnabled(true);
            if (_statusLbl != null) _statusLbl.text = $"Rejeitado: {reason}";
        }

        // Recorta a região do sprite do atlas para uma Texture2D legível (WebGL-safe).
        // Usa RenderTexture + ReadPixels para contornar atlas comprimido (ASTC/DXT5)
        // que quebra o GetPixels direto em WebGL/mobile.
        private static Texture2D SpriteToTexture(Sprite sprite)
        {
            if (sprite == null) return null;
            int w = Mathf.RoundToInt(sprite.rect.width);
            int h = Mathf.RoundToInt(sprite.rect.height);
            if (w <= 0 || h <= 0) return null;

            var src = sprite.texture;
            // Cópia 1:1 do retângulo do sprite para um RT (formato RGBA32, sempre legível)
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            var prevActive = RenderTexture.active;
            Graphics.Blit(src, rt, new Vector2(1f, 1f), new Vector2(-sprite.rect.x, -sprite.rect.y));
            RenderTexture.active = rt;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }
    }
}
