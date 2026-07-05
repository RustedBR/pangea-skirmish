// Net/CharCreationHUD.cs
// Overlay de criação de personagem para o modo multiplayer.
// Exibido sobre a cena Sandbox após a fase MapEditing, sem trocar de cena.
// Criado e destruído por RoomHUD ao detectar fase CharCreation.
//
// Decisão de arquitetura: overlay novo em vez de reaproveitar o MainMenuManager,
// pois este é fortemente acoplado à cena MainMenu (unit preview, preset list, etc).
// Este painel é enxuto: sprite picker, 6 steppers de atributo, arma, nome, confirmar.

using UnityEngine;
using UnityEngine.UI;

namespace PangeaSkirmish
{
    public class CharCreationHUD : MonoBehaviour
    {
        // ---- Estado em edição -----------------------------------------------
        private CharacterPreset _editing;
        private int _budget;

        // ---- UI references -------------------------------------------------
        private Text _titleLbl;
        private Text _budgetLbl;
        private Text _spriteLbl;
        private Text _weaponLbl;
        private InputField _nameInput;
        private Text[] _attrValueLbls;
        private Button _confirmBtn;
        private Text _statusLbl;

        // ---- Índices de navegação -------------------------------------------
        private int _spriteIdx;
        private int _weaponIdx;

        private Font _font;

        private static readonly string[] AttrNames = { "STR", "VIT", "DEX", "AGI", "INT", "WIS" };

        // =========================================================================
        // Build
        // =========================================================================
        public void Build(Transform canvas)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
                    Footprint = 3, AttackRange = 0
                }
            };

            // Fundo escuro full-screen: cobre a cena atrás (sandbox/menu) e captura os cliques,
            // para o overlay de criação não ficar "flutuando" sobre o mapa.
            var dimmer = new GameObject("CharCreationDimmer", typeof(RectTransform), typeof(Image));
            dimmer.transform.SetParent(canvas, false);
            var dimRt = dimmer.GetComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero; dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero; dimRt.offsetMax = Vector2.zero;
            dimmer.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 0.94f);

            // Painel central (renderizado por cima do dimmer, pois é criado depois)
            var panel = MakePanel(canvas, "CharCreationPanel",
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 680),
                new Color(0.04f, 0.05f, 0.08f, 0.99f));

            float top = 310f;
            float lineH = 42f;

            // Título
            _titleLbl = MakeLabel(panel.transform, new Vector2(0, top), new Vector2(500, 40), 22,
                new Color(0.92f, 0.80f, 0.35f));
            _titleLbl.text = "Criar Personagem";
            top -= lineH;

            // Budget
            _budgetLbl = MakeLabel(panel.transform, new Vector2(0, top), new Vector2(500, 34), 16,
                new Color(0.75f, 0.90f, 0.75f));
            top -= lineH;

            // Nome
            MakeLabel(panel.transform, new Vector2(-140, top), new Vector2(120, 34), 15,
                Color.white).text = "Nome:";
            _nameInput = MakeInputField(panel.transform, new Vector2(90, top), new Vector2(240, 34),
                _editing.presetName, v => _editing.presetName = v);
            top -= lineH;

            // Sprite picker
            MakeLabel(panel.transform, new Vector2(-160, top), new Vector2(100, 34), 15,
                Color.white).text = "Aparência:";
            MakeBtn(panel.transform, new Vector2(-50, top), new Vector2(30, 30), "<",
                () => { _spriteIdx = (_spriteIdx - 1 + CharacterSpriteCatalog.All.Length) % CharacterSpriteCatalog.All.Length; SyncSprite(); });
            _spriteLbl = MakeLabel(panel.transform, new Vector2(50, top), new Vector2(130, 34), 15, Color.white);
            MakeBtn(panel.transform, new Vector2(150, top), new Vector2(30, 30), ">",
                () => { _spriteIdx = (_spriteIdx + 1) % CharacterSpriteCatalog.All.Length; SyncSprite(); });
            top -= lineH;

            // Arma picker
            MakeLabel(panel.transform, new Vector2(-160, top), new Vector2(100, 34), 15,
                Color.white).text = "Arma:";
            MakeBtn(panel.transform, new Vector2(-50, top), new Vector2(30, 30), "<",
                () => { var ws = WeaponCatalog.All(); _weaponIdx = (_weaponIdx - 1 + ws.Length) % ws.Length; SyncWeapon(); });
            _weaponLbl = MakeLabel(panel.transform, new Vector2(50, top), new Vector2(130, 34), 14, Color.white);
            MakeBtn(panel.transform, new Vector2(150, top), new Vector2(30, 30), ">",
                () => { var ws = WeaponCatalog.All(); _weaponIdx = (_weaponIdx + 1) % ws.Length; SyncWeapon(); });
            top -= lineH;

            // Atributos (6 steppers)
            _attrValueLbls = new Text[6];
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                MakeLabel(panel.transform, new Vector2(-170, top), new Vector2(80, 34), 15,
                    Color.white).text = AttrNames[i] + ":";
                MakeBtn(panel.transform, new Vector2(-60, top), new Vector2(28, 28), "-",
                    () => StepAttr(idx, -1));
                _attrValueLbls[i] = MakeLabel(panel.transform, new Vector2(0, top), new Vector2(50, 34), 15, Color.white);
                MakeBtn(panel.transform, new Vector2(60, top), new Vector2(28, 28), "+",
                    () => StepAttr(idx, +1));
                top -= 36f;
            }

            // Status / rejeição
            _statusLbl = MakeLabel(panel.transform, new Vector2(0, top), new Vector2(480, 34), 14,
                new Color(1f, 0.4f, 0.4f));
            top -= lineH;

            // Confirmar
            var confirmGo = MakePanelGo(panel.transform, "Confirm", new Vector2(0, top - 10),
                new Vector2(200, 44), new Color(0.18f, 0.40f, 0.20f));
            UiSkin.ApplyButtonSkin(confirmGo.GetComponent<Image>(), new Color(0.18f, 0.40f, 0.20f));
            MakeLabel(confirmGo.transform, Vector2.zero, new Vector2(200, 44), 18, Color.white).text = "Confirmar";
            _confirmBtn = confirmGo.AddComponent<Button>();
            _confirmBtn.onClick.AddListener(OnConfirm);

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
            _editing.stats = s;
        }

        private int SumAttrs()
        {
            var s = _editing.stats;
            return (int)(s.STR + s.VIT + s.DEX + s.AGI + s.INT + s.WIS);
        }

        private void RefreshBudgetDisplay()
        {
            int used = SumAttrs();
            bool ok = used <= _budget;
            if (_budgetLbl != null)
            {
                _budgetLbl.text = $"Pontos: {used}/{_budget}";
                _budgetLbl.color = ok ? new Color(0.75f, 0.90f, 0.75f) : new Color(1f, 0.4f, 0.4f);
            }
            if (_confirmBtn != null) _confirmBtn.interactable = ok;
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
        }

        private void SyncWeapon()
        {
            var ws = WeaponCatalog.All();
            if (_weaponIdx < 0 || _weaponIdx >= ws.Length) _weaponIdx = 0;
            _editing.weaponId = ws[_weaponIdx].id;
            if (_weaponLbl != null)
            {
                var w = ws[_weaponIdx];
                _weaponLbl.text = $"{w.displayName} (d{w.damage}/r{w.range})";
            }
        }

        // =========================================================================
        // Confirmar
        // =========================================================================
        private void OnConfirm()
        {
            if (SumAttrs() > _budget) return;
            if (string.IsNullOrWhiteSpace(_editing.presetName))
                _editing.presetName = RuntimeMultiplayerSession.PlayerName;

            string json = JsonUtility.ToJson(_editing);
            Debug.Log($"[MP] Personagem criado (enviando): {_editing.presetName} pts={SumAttrs()}/{_budget} arma={_editing.weaponId}");
            RoomManager.Instance?.SubmitCharacterServerRpc(json);

            // Salvar localmente para uso no GameBootstrap MP
            RuntimeMultiplayerSession.LocalCharacterPreset = _editing;

            if (_confirmBtn != null) _confirmBtn.interactable = false;
            if (_statusLbl != null) _statusLbl.text = "Aguardando confirmação...";
        }

        private void OnRejected(string reason)
        {
            if (_confirmBtn != null) _confirmBtn.interactable = true;
            if (_statusLbl != null) _statusLbl.text = $"Rejeitado: {reason}";
        }

        // =========================================================================
        // UI Helpers
        // =========================================================================
        private GameObject MakePanel(Transform canvas, string name, Vector2 anchor,
            Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = color;
            return go;
        }

        private GameObject MakePanelGo(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = color;
            return go;
        }

        private void MakeBtn(Transform parent, Vector2 pos, Vector2 size, string label,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.25f, 0.27f, 0.35f);
            UiSkin.ApplyButtonSkin(img, img.color);
            MakeLabel(go.transform, Vector2.zero, size, 16, Color.white).text = label;
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);
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
            t.supportRichText = false;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

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
            trt.offsetMin = new Vector2(8, 0); trt.offsetMax = new Vector2(-8, 0);
            var txt = textGo.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 16; txt.alignment = TextAnchor.MiddleLeft;
            txt.color = Color.white; txt.supportRichText = false;
            input.textComponent = txt;
            input.text = initial;
            input.onValueChanged.AddListener(onChanged);
            go.SetActive(true);
            return input;
        }
    }
}
