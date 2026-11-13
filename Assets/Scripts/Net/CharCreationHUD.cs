// Net/CharCreationHUD.cs
// Editor de personagem do perfil do jogador (UI Toolkit, Resources/UI/Screens/CharCreation.uxml).
// Aberto pela tela de Perfil para criar um personagem novo ou editar um salvo. Não fala com a
// rede: devolve o resultado por OnConfirmed e quem chamou grava no PlayerProfile. O orçamento
// de pontos é fixo (PlayerProfile.AttributeBudget), igual em toda sala.

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

        /// <summary>Confirmou: recebe uma cópia do personagem pronto (orçamento exato).</summary>
        public Action<CharacterPreset> OnConfirmed { get; set; }
        /// <summary>Voltou sem salvar.</summary>
        public Action OnCancelled { get; set; }
        /// <summary>Personagem em edição (leitura, para testes).</summary>
        public CharacterPreset Editing => _editing;

        // ---- UI references -------------------------------------------------
        private Label _budgetLbl;
        private TextField _nameInput;
        private Label _spriteLbl;
        private VisualElement _spritePreview;
        private CharacterPreview3D _preview3D;
        private Label _weaponInfoLbl, _derivedLbl, _infoLbl;
        private string _infoDefault = "Passe o mouse sobre um atributo, a arma ou a aparência para ver o que faz.";
        private int _lookIdx;
        private Label _weaponLbl;
        private Label[] _attrValueLbls;
        private Button _confirmBtn;
        private Label _statusLbl;

        // ---- Índices de navegação -------------------------------------------
        private int _spriteIdx;
        private int _weaponIdx;

        private static readonly string[] AttrNames = { "STR", "VIT", "DEX", "AGI", "INT", "WIS" };

        // =========================================================================
        // Bind — liga os elementos nomeados do UXML à lógica
        // =========================================================================
        protected override void Bind()
        {
            _budget = PlayerProfile.AttributeBudget;

            // Personagem novo: pontos repartidos por igual, já válido; o jogador redistribui.
            float each = Mathf.Clamp(Mathf.Floor(_budget / 6f), CharacterConfig.AttrMin, CharacterConfig.AttrMax);
            _editing = new CharacterPreset
            {
                presetName = PlayerProfile.NextCharacterName(),
                spritePath = CharacterSpriteCatalog.Default,
                weaponId   = "Hatchet",
                stats      = new UnitStatBlock
                {
                    STR = each, VIT = each, DEX = each, AGI = each, INT = each, WIS = each,
                    Footprint = 3,
                    // AttackRange vem da arma (não 0 — senão a unidade nasce sem alcance)
                    AttackRange = WeaponCatalog.Get("Hatchet")?.range ?? 1
                }
            };

            var r = Root;

            _budgetLbl  = r.Q<Label>("budget-label");
            _nameInput  = r.Q<TextField>("name-input");
            _spriteLbl  = r.Q<Label>("sprite-label");
            _spritePreview = r.Q<VisualElement>("sprite-preview");
            _weaponInfoLbl = r.Q<Label>("weapon-info");
            _derivedLbl    = r.Q<Label>("derived-label");
            _infoLbl       = r.Q<Label>("info-label");
            if (_infoLbl != null) _infoLbl.text = _infoDefault;
            // Prévia 3D: o corpo que entra na batalha, com a arma na mão (antes: sprite 2D sem arma).
            if (_spritePreview != null && World3D.Has("Character"))
            {
                _preview3D = CharacterPreview3D.Create(320);
                _spritePreview.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_preview3D.Texture));
            }
            _weaponLbl  = r.Q<Label>("weapon-label");
            _statusLbl  = r.Q<Label>("status-label");
            _confirmBtn = r.Q<Button>("confirm-btn");

            if (_nameInput != null)
            {
                _nameInput.value = _editing.presetName;
                _nameInput.RegisterValueChangedCallback(evt => _editing.presetName = evt.newValue);
            }

            // Sprite picker
            r.Q<Button>("sprite-prev")?.RegisterCallback<ClickEvent>(_ =>
            {
                _lookIdx = (_lookIdx - 1 + CharacterLookCatalog.All.Length) % CharacterLookCatalog.All.Length;
                SyncSprite();
            });
            r.Q<Button>("sprite-next")?.RegisterCallback<ClickEvent>(_ =>
            {
                _lookIdx = (_lookIdx + 1) % CharacterLookCatalog.All.Length;
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
                Hover(r.Q<VisualElement>($"attr-row-{i}"), () => AttrHelp(idx));
            }
            Hover(r.Q<Button>("sprite-prev")?.parent, () => $"<b>Aparência — {CharacterLookCatalog.All[_lookIdx].displayName}</b>\n{CharacterLookCatalog.All[_lookIdx].blurb}\nÉ só visual: não muda nenhum número.");
            Hover(r.Q<Button>("weapon-prev")?.parent, WeaponHelp);

            // Confirmar / voltar
            _confirmBtn?.RegisterCallback<ClickEvent>(_ => OnConfirm());
            r.Q<Button>("back-btn")?.RegisterCallback<ClickEvent>(_ => OnCancelled?.Invoke());

            // Inicializar UI
            SyncSprite();
            SyncWeapon();
            RefreshBudgetDisplay();
            RefreshAttrDisplay();
        }

        private void OnDestroy()
        {
            if (_preview3D != null) Destroy(_preview3D.gameObject);
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
            RefreshDerived();
        }

        /// <summary>Aparência = corpo 3D (CharacterLookCatalog). O spritePath segue existindo para a lógica antiga.</summary>
        private void SyncSprite()
        {
            var all = CharacterLookCatalog.All;
            if (_lookIdx < 0 || _lookIdx >= all.Length) _lookIdx = 0;
            _editing.modelId = all[_lookIdx].modelId;
            _editing.spritePath = all[_lookIdx].spritePath;
            if (_spriteLbl != null) _spriteLbl.text = all[_lookIdx].displayName;
            RefreshPreview();
        }

        /// <summary>Corpo que a batalha vai usar para a escolha atual (aparência, ou a arma no modo automático).</summary>
        public string PreviewBodyModel()
        {
            if (!string.IsNullOrEmpty(_editing.modelId)) return _editing.modelId;
            return UnitModel3D.CharacterModelFor("", "", WeaponCatalog.Get(_editing.weaponId));
        }

        private void RefreshPreview()
        {
            if (_preview3D != null)
                _preview3D.Show(PreviewBodyModel(), _editing.weaponId, Tuning.Get().playerTeamColor);
            RefreshDerived();
        }

        // =========================================================================
        // Ficha e explicações (hover)
        // =========================================================================
        private void Hover(VisualElement el, Func<string> text)
        {
            if (el == null || _infoLbl == null) return;
            el.RegisterCallback<PointerEnterEvent>(_ => _infoLbl.text = text());
            el.RegisterCallback<PointerLeaveEvent>(_ => _infoLbl.text = _infoDefault);
        }

        /// <summary>Stats completos da escolha atual, com a arma aplicada pela mesma regra da batalha.</summary>
        public AttributeStats CurrentStats()
        {
            var st = _editing.stats.ToAttributeStats();
            WeaponDef.ApplyTo(st, WeaponCatalog.Get(_editing.weaponId), Tuning.Get());
            return st;
        }

        /// <summary>Texto do que um atributo faz, com os números do tuning em vigor e o efeito do próximo ponto.</summary>
        public string AttrHelp(int idx)
        {
            var T = Tuning.Get(); var F = AttributeStats.Formulas ?? T.statFormulas;
            var st = CurrentStats();
            switch (idx)
            {
                case 0: return $"<b>STR — Força</b>\nDano por tile das armas corpo a corpo: +{F.dmgPctPerAttr * 100:0.#}% por ponto.\nStamina máxima: +{T.staminaPerBodyPoint:0.##} por ponto.\nEscala a magia Física <b>Fúria</b>.";
                case 1: return $"<b>VIT — Vitalidade</b>\nVida: +{F.hpPerVIT:0.#} por ponto.\nDefesa: −1 de dano físico a cada {1f / Mathf.Max(0.01f, F.armorPerVIT):0.#} pontos, uma vez por golpe.\nBloquear soma metade da VIT. Stamina máxima: +{T.staminaPerBodyPoint:0.##} por ponto.";
                case 2: return $"<b>DEX — Destreza</b>\nAções mais rápidas: menos ticks para atacar e conjurar.\nAcerto +{F.hitPerDEX * 100:0.#}% e crítico +{F.critPerDEX * 100:0.#}% por ponto.\nDano por tile das armas à distância (arcos, cajados): +{F.dmgPctPerAttr * 100:0.#}% por ponto.\nStamina máxima: +{T.staminaPerBodyPoint:0.##} por ponto.";
                case 3: return $"<b>AGI — Agilidade</b>\nTiles por andada (até {T.maxTilesPerMove}).\nEsquiva +{F.dodgePerAGI * 100:0.#}% por ponto; cada esquiva tira {T.dodgeFracPerAGI * 100:0}% dos tiles do golpe por ponto (até {T.dodgeFracCap * 100:0}%; golpe de 1 tile: chance de errar).\nAcima de {T.agiRegenFrom}, a Stamina volta mais rápido.";
                case 4: return $"<b>INT — Inteligência</b>\nPotência das magias: +{T.spellPotPerINT:0.#} por ponto.\nAlcance das magias: +{T.spellRangePerINT:0.#} célula por ponto.";
                default: return $"<b>WIS — Sabedoria</b>\nMana máxima: +{F.manaPerWIS:0.#} por ponto.\nMana volta mais rápido a cada {T.wisRegenStep} pontos.\nDefesa contra magia: −{F.magicResistPerWIS} de dano mágico por ponto.";
            }
        }

        /// <summary>O que a arma escolhida faz: estilo, dano com os atributos atuais, cadência, alcance, bloqueio e magia.</summary>
        public string WeaponHelp()
        {
            var w = WeaponCatalog.Get(_editing.weaponId);
            if (w == null) return "Desarmado.";
            var T = Tuning.Get(); var F = AttributeStats.Formulas ?? T.statFormulas;
            var st = CurrentStats();
            string style;
            switch (UnitModel3D.StyleFor(w.id))
            {
                case UnitModel3D.AttackStyle.Chop:   style = "machado: golpe de cima para baixo"; break;
                case UnitModel3D.AttackStyle.Shoot:  style = "arco: dispara flechas à distância"; break;
                case UnitModel3D.AttackStyle.Thrust: style = "cajado: estocada e conduíte de magia"; break;
                default:                             style = "lâmina: corte cruzado"; break;
            }
            string scale = st.dexScalesDamage ? $"DEX +{st.DEX * F.dmgPctPerAttr * 100:0}%" : $"STR +{st.STR * F.dmgPctPerAttr * 100:0}%";
            int ticks = Mathf.Max(1, Mathf.RoundToInt(st.AttackTicks * T.tickDexFactor / (T.tickDexFactor + Mathf.Max(0f, st.DEX))));
            string magic = (w.spellPowerMult != 1f || w.spellRangeBonus != 0)
                ? $"\nConduíte: magias ×{w.spellPowerMult:0.##}" + (w.spellRangeBonus != 0 ? $", +{w.spellRangeBonus} de alcance" : "") + "."
                : "";
            return $"<b>{w.displayName}</b> — {style}\n" +
                   $"Forma: <b>{AttackShape.Describe(w)}</b> — pega {st.ShapeTiles} dos 9 tiles de um alvo quando centralizada nele\n" +
                   $"Dano por tile: {w.damage} {scale} = {st.DamagePerTile:0.#} → golpe centralizado <b>{st.PhysicalDamage}</b>\n" +
                   $"Cadência: {ticks} tick{(ticks == 1 ? "" : "s")} (peso {w.attackTicks}; a DEX encurta) → {st.PhysicalDamage / (float)ticks:0.#} de dano por tick\n" +
                   $"Alcance: {st.AttackRange} célula{(st.AttackRange == 1 ? "" : "s")} (uma unidade ocupa 3).  Bloqueio: +{w.blockHardness}.{magic}";
        }

        private void RefreshDerived()
        {
            var w = WeaponCatalog.Get(_editing.weaponId);
            var T = Tuning.Get();
            var st = CurrentStats();
            if (_weaponInfoLbl != null && w != null)
            {
                int tk = Mathf.Max(1, Mathf.RoundToInt(st.AttackTicks * T.tickDexFactor / (T.tickDexFactor + Mathf.Max(0f, st.DEX))));
                _weaponInfoLbl.text = $"{AttackShape.Describe(w)} · {st.DamagePerTile:0.#} por tile ({st.PhysicalDamage} centralizado) a cada {tk} tick{(tk == 1 ? "" : "s")} · alcance {st.AttackRange} · {(st.dexScalesDamage ? "DEX" : "STR")}" +
                                      (w.spellPowerMult != 1f ? $" · magia ×{w.spellPowerMult:0.##}" : "");
            }
            if (_derivedLbl == null) return;
            int emberDmg = Mathf.RoundToInt((4f * T.spellBaseMult + st.INT * T.spellPotPerINT) * (w != null ? w.spellPowerMult : 1f));
            int emberReach = Mathf.RoundToInt(4f * T.spellRangeMult + st.INT * T.spellRangePerINT) + (w != null ? w.spellRangeBonus : 0);
            _derivedLbl.text =
                $"<b>Vida</b> {st.MaxHP}    <b>Stamina</b> {st.MaxStamina}    <b>Mana</b> {st.MaxMana}\n" +
                $"<b>Golpe</b> {st.PhysicalDamage}    <b>Alcance</b> {st.AttackRange}    <b>Andada</b> {st.MoveStep}\n" +
                $"<b>Acerto</b> {st.HitChance * 100:0}%    <b>Crítico</b> {st.CritChance * 100:0}%    <b>Esquiva</b> {st.DodgeChance * 100:0}%\n" +
                $"<b>Defesa</b> −{st.PhysicalDefense}    <b>Def. mágica</b> −{st.MagicDefense}\n" +
                $"<b>Brasa</b> ~{emberDmg} de dano, alcance {emberReach}";
        }

        // =========================================================================
        // Editar um personagem salvo
        // =========================================================================
        /// <summary>Carrega um personagem do perfil para edição (chamar logo após o Spawn).</summary>
        public void Edit(CharacterPreset p)
        {
            if (p == null || p.stats == null) return;
            ApplyPreset(p);
            var title = Root?.Q<Label>("cc-title");
            if (title != null) title.text = "Editar Personagem";
        }

        /// <summary>Deep-copy de um preset para o estado em edição, atualizando a UI.</summary>
        private void ApplyPreset(CharacterPreset p)
        {
            _editing = new CharacterPreset
            {
                presetName = p.presetName,
                spritePath = p.spritePath,
                weaponId   = p.weaponId,
                modelId    = p.modelId ?? "",
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
            _lookIdx = CharacterLookCatalog.IndexOf(_editing.modelId);
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
            if (_weaponLbl != null) _weaponLbl.text = w.displayName;
            RefreshPreview();
        }

        // =========================================================================
        // Confirmar
        // =========================================================================
        private void OnConfirm()
        {
            if (SumAttrs() != _budget) return;
            _editing.presetName = (_editing.presetName ?? "").Trim();
            if (_editing.presetName.Length == 0) _editing.presetName = PlayerProfile.NextCharacterName();
            if (_editing.presetName.Length > PlayerProfile.MaxNameLength)
                _editing.presetName = _editing.presetName.Substring(0, PlayerProfile.MaxNameLength);

            // Garante que o AttackRange reflete a arma escolhida no envio
            var w = WeaponCatalog.Get(_editing.weaponId);
            if (w != null) _editing.stats.AttackRange = w.range;

            Debug.Log($"[Perfil] Personagem confirmado: {_editing.presetName} pts={SumAttrs()}/{_budget} arma={_editing.weaponId} aparência='{_editing.modelId}'");
            OnConfirmed?.Invoke(PlayerProfile.Clone(_editing));
        }

    }
}
