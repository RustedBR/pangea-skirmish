using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish.UI
{
    /// <summary>
    /// Perfil do jogador (Resources/UI/Screens/Profile.uxml): o nome e a lista de personagens
    /// salvos, com o que está em uso. É a "identidade" definida antes de entrar em Coop ou PvP.
    /// Criar e editar abrem o CharCreationHUD por cima; tudo grava no PlayerProfile.
    /// </summary>
    public class ProfileScreen : PangeaScreen
    {
        protected override string UxmlResource => "UI/Screens/Profile";

        /// <summary>Voltar ao menu.</summary>
        public Action OnBack;
        /// <summary>Seguir para o multiplayer (só chamado com o perfil pronto).</summary>
        public Action OnContinue;

        private TextField _nameInput;
        private ScrollView _list;
        private Label _notice, _selName, _selInfo;
        private Button _editBtn, _deleteBtn, _continueBtn, _newBtn;
        private VisualElement _preview;
        private CharacterPreview3D _preview3D;
        private CharCreationHUD _editor;

        protected override void Bind()
        {
            var r = Root;
            _nameInput   = r.Q<TextField>("name-input");
            _list        = r.Q<ScrollView>("char-list");
            _notice      = r.Q<Label>("notice");
            _selName     = r.Q<Label>("sel-name");
            _selInfo     = r.Q<Label>("sel-info");
            _preview     = r.Q<VisualElement>("preview");
            _newBtn      = r.Q<Button>("btn-new");
            _editBtn     = r.Q<Button>("btn-edit");
            _deleteBtn   = r.Q<Button>("btn-delete");
            _continueBtn = r.Q<Button>("btn-continue");

            if (_nameInput != null)
            {
                _nameInput.SetValueWithoutNotify(PlayerProfile.Name);
                _nameInput.RegisterValueChangedCallback(e => { PlayerProfile.SetName(e.newValue); RefreshState(); });
            }
            if (_newBtn != null) _newBtn.clicked += () => OpenEditor(-1);
            if (_editBtn != null) _editBtn.clicked += () => OpenEditor(PlayerProfile.Data.selected);
            if (_deleteBtn != null) _deleteBtn.clicked += () => { PlayerProfile.Remove(PlayerProfile.Data.selected); Rebuild(); };
            r.Q<Button>("btn-back").clicked += () => OnBack?.Invoke();
            if (_continueBtn != null) _continueBtn.clicked += () => { if (PlayerProfile.IsReady) OnContinue?.Invoke(); };
            Rebuild();
        }

        private void OnDestroy()
        {
            if (_preview3D != null) Destroy(_preview3D.gameObject);
            if (_editor != null) Destroy(_editor.gameObject);
        }

        /// <summary>Mostra a tela; <paramref name="notice"/> explica por que ela abriu (ex.: falta personagem).</summary>
        public void Show(string notice = "")
        {
            SetVisible(true);
            if (_notice != null) _notice.text = notice ?? "";
            if (_nameInput != null) _nameInput.SetValueWithoutNotify(PlayerProfile.Name);
            Rebuild();
        }

        public void Hide()
        {
            SetVisible(false);
            if (_preview3D != null) { Destroy(_preview3D.gameObject); _preview3D = null; }
        }

        private void Rebuild()
        {
            if (_list == null) return;
            _list.Clear();
            var chars = PlayerProfile.Data.characters;
            if (chars.Count == 0)
            {
                var empty = new Label("Nenhum personagem ainda. Clique em \"+ Novo\" para criar o primeiro.");
                empty.AddToClassList("pf-empty");
                _list.Add(empty);
            }
            for (int i = 0; i < chars.Count; i++)
            {
                int idx = i;
                var c = chars[i];
                bool sel = idx == PlayerProfile.Data.selected;
                var row = new VisualElement { name = "char-row-" + idx };
                row.AddToClassList("pf-row");
                if (sel) row.AddToClassList("pf-row--selected");
                var text = new VisualElement(); text.AddToClassList("pf-row-text");
                var name = new Label(c.presetName); name.AddToClassList("pf-row-name");
                var sub = new Label(RoomHUD.DescribeCharacter(c).Split('\n')[0]); sub.AddToClassList("pf-row-sub");
                text.Add(name); text.Add(sub); row.Add(text);
                if (sel) { var tag = new Label("EM USO"); tag.AddToClassList("pf-row-tag"); row.Add(tag); }
                row.RegisterCallback<ClickEvent>(_ => { PlayerProfile.Select(idx); Rebuild(); });
                _list.Add(row);
            }
            RefreshState();
        }

        private void RefreshState()
        {
            var c = PlayerProfile.Selected;
            _editBtn?.SetEnabled(c != null);
            _deleteBtn?.SetEnabled(c != null);
            _newBtn?.SetEnabled(PlayerProfile.Count < PlayerProfile.MaxCharacters);
            _continueBtn?.SetEnabled(PlayerProfile.IsReady);
            if (_notice != null && PlayerProfile.IsReady) _notice.text = "";
            else if (_notice != null && string.IsNullOrEmpty(_notice.text)) _notice.text = PlayerProfile.MissingText;

            if (_selName != null) _selName.text = c != null ? c.presetName : "—";
            if (_selInfo != null) _selInfo.text = c != null ? Sheet(c) : "Sem personagem em uso.";

            if (_preview == null) return;
            if (c == null || !World3D.Has("Character") || !IsVisible)
            {
                _preview.style.backgroundImage = StyleKeyword.None;
                return;
            }
            if (_preview3D == null)
            {
                _preview3D = CharacterPreview3D.Create(320);
                _preview.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_preview3D.Texture));
            }
            string body = !string.IsNullOrEmpty(c.modelId) ? c.modelId : UnitModel3D.CharacterModelFor("", "", WeaponCatalog.Get(c.weaponId));
            _preview3D.Show(body, c.weaponId, Tuning.Get().playerTeamColor);
        }

        private bool IsVisible => Root != null && Root.style.display != DisplayStyle.None;

        /// <summary>Ficha resumida do personagem em uso, com a arma aplicada como na batalha.</summary>
        public static string Sheet(CharacterPreset c)
        {
            var st = c.stats.ToAttributeStats();
            WeaponDef.ApplyTo(st, WeaponCatalog.Get(c.weaponId), Tuning.Get());
            return RoomHUD.DescribeCharacter(c) + "\n" +
                   $"Vida {st.MaxHP} · Estâmina {st.MaxStamina} · Mana {st.MaxMana}\n" +
                   $"Golpe {st.PhysicalDamage} · Alcance {st.AttackRange} · Andada {st.MoveStep}";
        }

        // ------------------------------------------------------------ editor de personagem

        /// <summary>Abre o editor: index &lt; 0 cria um personagem novo; senão edita o salvo.</summary>
        public CharCreationHUD OpenEditor(int index)
        {
            if (_editor != null) return _editor;
            var existing = index >= 0 && index < PlayerProfile.Count ? PlayerProfile.Data.characters[index] : null;
            Hide();
            _editor = Spawn<CharCreationHUD>("CharCreationHUD");
            _editor.Document.sortingOrder = 1000;
            if (existing != null) _editor.Edit(existing);
            _editor.OnConfirmed = preset =>
            {
                int at = PlayerProfile.Put(existing != null ? index : -1, preset);
                Debug.Log($"[Perfil] personagem salvo no índice {at}: {preset.presetName}");
                CloseEditor();
            };
            _editor.OnCancelled = CloseEditor;
            return _editor;
        }

        private void CloseEditor()
        {
            if (_editor != null) { Destroy(_editor.gameObject); _editor = null; }
            Show("");
        }
    }
}
