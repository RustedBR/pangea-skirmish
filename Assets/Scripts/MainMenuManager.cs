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
        private GameObject _editorPanel;
        private GameObject _mapSelectPanel;
        private RoomHUD _roomHUD;

        // Editor state
        private Transform _presetListContent;
        private readonly List<GameObject> _presetRows = new List<GameObject>();
        private CharacterPreset _editing;
        private bool _isNewPreset;
        private string _origName;

        private InputField _nameInput;
        private Text _classLabel;
        private Text _weaponLabel;
        private Text _previewText;
        private Image _classSpriteImg;
        private Image _weaponSpriteImg;
        private Coroutine _walkAnim;
        private List<Sprite> _walkFrames;
        private readonly Text[] _attrLabels = new Text[6];
        private readonly Text[] _valLabels = new Text[6];

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

        private static readonly string[] AttrNames = { "STR", "VIT", "DEX", "AGI", "INT", "WIS" };

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
            BuildEditorPanel(canvas.transform);
            BuildMapSelectPanel(canvas.transform);
            BuildMultiplayerHUD(canvas.transform);
            ShowMenu();
        }

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

        private void StopWalkAnim()
        {
            if (_walkAnim != null) { StopCoroutine(_walkAnim); _walkAnim = null; }
        }

        private void ShowMenu()
        {
            StopWalkAnim();
            _menuScreen.SetVisible(true);
            _optionsScreen?.SetVisible(false);
            _editorPanel.SetActive(false);
            _mapSelectPanel.SetActive(false);
            _roomHUD?.HideAll();
        }

        private void ShowMultiplayer()
        {
            _menuScreen.SetVisible(false);
            _editorPanel.SetActive(false);
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

        // -------------------------------------------------------- CHARACTER EDITOR

        private void BuildEditorPanel(Transform parent)
        {
            _editorPanel = MakeFullPanel(parent, "EditorPanel");
            _editorPanel.AddComponent<Image>().color = ThemePanelBg;

            MakeLabel(_editorPanel.transform, new Vector2(0, 455), new Vector2(800, 60), 40,
                Tuning.Get().uiTitleColor).text = "Criar Personagem";

            // --- Left: preset list ---
            var listBg = new GameObject("PresetList", typeof(RectTransform));
            listBg.transform.SetParent(_editorPanel.transform, false);
            var listRt = listBg.GetComponent<RectTransform>();
            listRt.anchorMin = listRt.anchorMax = listRt.pivot = new Vector2(0.5f, 0.5f);
            listRt.anchoredPosition = new Vector2(-490, -20);
            listRt.sizeDelta = new Vector2(240, 400);
            listBg.AddComponent<Image>().color = ThemeListBg;

            MakeLabel(listBg.transform, new Vector2(0, 180), new Vector2(220, 24), 14,
                ThemeTextDim).text = "Personagens salvos";

            var btnNew = MakeBtn(listBg.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 142), new Vector2(200, 28));
            btnNew.GetComponent<Image>().color = ThemeButtonBg;
            MakeLabel(btnNew.transform, Vector2.zero, new Vector2(200, 28), 15, ThemeText).text = "+ Novo";
            btnNew.onClick.AddListener(NewPreset);

            // Scroll view for preset list
            var svGo = new GameObject("ScrollView", typeof(RectTransform), typeof(RectMask2D));
            svGo.transform.SetParent(listBg.transform, false);
            var svRt = svGo.GetComponent<RectTransform>();
            svRt.anchorMin = svRt.anchorMax = svRt.pivot = new Vector2(0.5f, 0.5f);
            svRt.anchoredPosition = new Vector2(0, -25);
            svRt.sizeDelta = new Vector2(220, 290);

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(svRt, false);
            _presetListContent = cGo.transform;
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

            // --- Right: editor fields ---
            float lx = 220;
            float top = 250;

            _nameInput = MakeInputField(_editorPanel.transform, new Vector2(lx, top), new Vector2(300, 40), "Nome");

            // Appearance row
            MakeLabel(_editorPanel.transform, new Vector2(lx - 160, top - 60), new Vector2(70, 36), 16,
                ThemeText).text = "Aparência:";
            _classLabel = MakeLabel(_editorPanel.transform, new Vector2(lx - 10, top - 60), new Vector2(140, 36), 16,
                ThemeGold);
            _classLabel.alignment = TextAnchor.MiddleLeft;
            var btnClass = MakeBtn(_editorPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(lx + 80, top - 60), new Vector2(50, 32));
            btnClass.GetComponent<Image>().color = ThemeButtonBg;
            MakeLabel(btnClass.transform, Vector2.zero, new Vector2(50, 32), 16, ThemeText).text = "→";
            btnClass.onClick.AddListener(CycleClass);

            // Weapon row
            MakeLabel(_editorPanel.transform, new Vector2(lx - 160, top - 110), new Vector2(70, 36), 16,
                ThemeText).text = "Arma:";
            _weaponLabel = MakeLabel(_editorPanel.transform, new Vector2(lx - 10, top - 110), new Vector2(140, 36), 14,
                ThemeGold);
            _weaponLabel.alignment = TextAnchor.MiddleLeft;
            var btnWeap = MakeBtn(_editorPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(lx + 80, top - 110), new Vector2(50, 32));
            btnWeap.GetComponent<Image>().color = ThemeButtonBg;
            MakeLabel(btnWeap.transform, Vector2.zero, new Vector2(50, 32), 16, ThemeText).text = "→";
            btnWeap.onClick.AddListener(CycleWeapon);

            // Attribute rows
            for (int i = 0; i < 6; i++)
            {
                float y = top - 170 - i * 38;
                MakeLabel(_editorPanel.transform, new Vector2(lx - 100, y), new Vector2(70, 36), 16,
                    ThemeText).text = AttrNames[i];

                _attrLabels[i] = MakeLabel(_editorPanel.transform, new Vector2(lx - 20, y), new Vector2(40, 36), 18, Color.white);
                _attrLabels[i].name = AttrNames[i] + "Val";
                _attrLabels[i].alignment = TextAnchor.MiddleCenter;

                int idx = i;
                var btnMinus = MakeBtn(_editorPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(lx + 30, y), new Vector2(34, 32));
                btnMinus.GetComponent<Image>().color = ThemeButtonBg;
                MakeLabel(btnMinus.transform, Vector2.zero, new Vector2(34, 32), 20, ThemeText).text = "−";
                btnMinus.onClick.AddListener(() => { AdjustAttr(idx, -1); });

                var btnPlus = MakeBtn(_editorPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(lx + 70, y), new Vector2(34, 32));
                btnPlus.GetComponent<Image>().color = ThemeButtonBg;
                MakeLabel(btnPlus.transform, Vector2.zero, new Vector2(34, 32), 20, ThemeText).text = "+";
                btnPlus.onClick.AddListener(() => { AdjustAttr(idx, +1); });

                _valLabels[i] = MakeLabel(_editorPanel.transform, new Vector2(lx + 165, y), new Vector2(65, 36), 13,
                    ThemeTextDim);
                _valLabels[i].alignment = TextAnchor.MiddleLeft;
            }

            // Preview (centered between left list and right fields)
            var centerGo = new GameObject("CenterPreview", typeof(RectTransform));
            centerGo.transform.SetParent(_editorPanel.transform, false);
            var centerRt = centerGo.GetComponent<RectTransform>();
            centerRt.anchorMin = centerRt.anchorMax = centerRt.pivot = new Vector2(0.5f, 0.5f);
            centerRt.anchoredPosition = new Vector2(-160, 10);
            centerRt.sizeDelta = new Vector2(280, 190);

            var classImgGo = new GameObject("ClassSprite", typeof(RectTransform));
            classImgGo.transform.SetParent(centerGo.transform, false);
            var cImgRt = classImgGo.GetComponent<RectTransform>();
            cImgRt.anchorMin = cImgRt.anchorMax = cImgRt.pivot = new Vector2(0.5f, 0.5f);
            cImgRt.anchoredPosition = new Vector2(-55, 50);
            cImgRt.sizeDelta = new Vector2(64, 64);
            _classSpriteImg = classImgGo.AddComponent<Image>();
            _classSpriteImg.preserveAspect = true;

            var weapImgGo = new GameObject("WeaponSprite", typeof(RectTransform));
            weapImgGo.transform.SetParent(centerGo.transform, false);
            var wImgRt = weapImgGo.GetComponent<RectTransform>();
            wImgRt.anchorMin = wImgRt.anchorMax = wImgRt.pivot = new Vector2(0.5f, 0.5f);
            wImgRt.anchoredPosition = new Vector2(50, 50);
            wImgRt.sizeDelta = new Vector2(112, 112);
            _weaponSpriteImg = weapImgGo.AddComponent<Image>();
            _weaponSpriteImg.preserveAspect = true;

            _previewText = MakeLabel(centerGo.transform, new Vector2(0, -35), new Vector2(260, 120), 13,
                ThemeText);
            _previewText.alignment = TextAnchor.UpperCenter;

            // Save / Delete / Confirmar (ordem igual à referência foto 1)
            var btnSave = MakeBtn(_editorPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(lx - 165, top - 470), new Vector2(140, 36));
            btnSave.GetComponent<Image>().color = ThemeButtonBg;
            MakeLabel(btnSave.transform, Vector2.zero, new Vector2(140, 36), 15, ThemeText).text = "Salvar";
            btnSave.onClick.AddListener(SavePreset);

            var btnDel = MakeBtn(_editorPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(lx - 15, top - 470), new Vector2(140, 36));
            btnDel.GetComponent<Image>().color = ThemeButtonBg;
            MakeLabel(btnDel.transform, Vector2.zero, new Vector2(140, 36), 15, ThemeDanger).text = "Deletar";
            btnDel.onClick.AddListener(() => {
                if (_editing != null && !_isNewPreset)
                {
                    CharacterStorage.Delete(_origName);
                    NewPreset();
                    RebuildPresetList();
                }
            });

            var btnConfirm = MakeBtn(_editorPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(lx + 135, top - 470), new Vector2(140, 36));
            btnConfirm.GetComponent<Image>().color = ThemeAccent;
            MakeLabel(btnConfirm.transform, Vector2.zero, new Vector2(140, 36), 15, ThemeConfirmText).text = "Confirmar";
            btnConfirm.onClick.AddListener(ConfirmPreset);

            // Back
            var voltar = MakeBtn(_editorPanel.transform, new Vector2(0.5f, 0f), new Vector2(0, 35), new Vector2(120, 29));
            voltar.GetComponent<Image>().color = ThemeButtonBg;
            MakeLabel(voltar.transform, Vector2.zero, new Vector2(120, 29), 13, ThemeDanger).text = "← Voltar";
            voltar.onClick.AddListener(ShowMenu);
        }

        private void RebuildPresetList()
        {
            foreach (var r in _presetRows) Destroy(r);
            _presetRows.Clear();

            bool hasActive = RuntimeSelectedCharacter.Active != null;
            var presets = CharacterStorage.LoadAll();
            foreach (var p in presets)
            {
                var nameCopy = p.presetName;
                var preset = p;
                var row = MakeListEntry(_presetListContent, out var img);
                _presetRows.Add(row);

                bool isActive = !string.IsNullOrEmpty(nameCopy)
                    && hasActive && RuntimeSelectedCharacter.Active.presetName == nameCopy;
                img.color = isActive ? BtnActive : BtnNormal;

                string prefix = isActive ? "▶ " : "";
                // Lobby (referência foto 1): classe em destaque à esquerda, nome menor à direita
                var spriteDef = CharacterSpriteCatalog.GetByPath(preset.spritePath);
                string spriteName = spriteDef != null ? spriteDef.displayName : "";
                MakeLabel(row.transform, new Vector2(-38, 0), new Vector2(130, 24), 12,
                    ThemeText).text = prefix + spriteName;
                MakeLabel(row.transform, new Vector2(52, 0), new Vector2(50, 24), 10,
                    ThemeTextDim).text = preset.presetName;

                row.GetComponent<Button>().onClick.AddListener(() => {
                    RuntimeSelectedCharacter.Active = preset;
                    LoadPreset(preset, nameCopy);
                    RebuildPresetList();
                });
            }
        }

        private GameObject MakeListEntry(Transform parent, out Image img)
        {
            var go = new GameObject("Entry", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(200, 28);
            img = go.AddComponent<Image>();
            img.color = BtnNormal;
            go.AddComponent<Button>();
            return go;
        }

        private void NewPreset()
        {
            _isNewPreset = true;
            _origName = "";
            _editing = new CharacterPreset();
            SyncEditorToPreset();
            RefreshPreview();
        }

        private void LoadPreset(CharacterPreset preset, string originalName)
        {
            _isNewPreset = false;
            _origName = originalName;
            // Deep copy
            _editing = new CharacterPreset
            {
                presetName = preset.presetName,
                spritePath = preset.spritePath,
                weaponId = preset.weaponId,
                stats = new UnitStatBlock
                {
                    STR = preset.stats.STR, VIT = preset.stats.VIT,
                    DEX = preset.stats.DEX, AGI = preset.stats.AGI,
                    INT = preset.stats.INT, WIS = preset.stats.WIS,
                    Footprint = preset.stats.Footprint,
                    AttackRange = preset.stats.AttackRange,
                }
            };
            SyncEditorToPreset();
            RefreshPreview();
        }

        private void SyncEditorToPreset()
        {
            if (_nameInput != null) _nameInput.SetTextWithoutNotify(_editing.presetName);

            var spriteDef = CharacterSpriteCatalog.GetByPath(_editing.spritePath);
            if (_classLabel != null)
                _classLabel.text = spriteDef != null ? spriteDef.displayName : "Guerreiro";

            var w = WeaponCatalog.Get(_editing.weaponId);
            if (_weaponLabel != null)
                _weaponLabel.text = w != null ? $"{_editing.weaponId} ({w.damage}/{w.range})" : _editing.weaponId;

            float[] vals = { _editing.stats.STR, _editing.stats.VIT, _editing.stats.DEX,
                             _editing.stats.AGI, _editing.stats.INT, _editing.stats.WIS };
            for (int i = 0; i < 6; i++)
            {
                if (_attrLabels[i] != null) _attrLabels[i].text = vals[i].ToString("0.#");
                if (_valLabels[i] != null)
                {
                    var d = _editing.stats.ToAttributeStats();
                    d.WeaponDamage = WeaponDamageForCurrent();
                    _valLabels[i].text = AttrDerivedLabel(i, d);
                }
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

            // Load character walking animation (SE invertido)
            if (_classSpriteImg != null)
            {
                if (_walkAnim != null) { StopCoroutine(_walkAnim); _walkAnim = null; }
                _walkFrames = null;
                string path = !string.IsNullOrEmpty(_editing.spritePath)
                    ? _editing.spritePath
                    : CharacterSpriteCatalog.Default;
                var all = Resources.LoadAll<Sprite>(path);
                if (all != null && all.Length > 0)
                {
                    _walkFrames = new List<Sprite>();
                    for (int i = 0; ; i++)
                    {
                        string key = $"walkingSE_{i}";
                        bool found = false;
                        foreach (var s in all)
                            if (s.name == key) { _walkFrames.Add(s); found = true; break; }
                        if (!found) break;
                    }
                    if (_walkFrames.Count > 0)
                    {
                        _classSpriteImg.color = Color.white;
                        _classSpriteImg.transform.localScale = new Vector3(-1, 1, 1);
                        _classSpriteImg.sprite = _walkFrames[0];
                        _walkAnim = StartCoroutine(WalkAnimLoop());
                    }
                }
            }

            // Load weapon sprite (SE, first frame)
            if (_weaponSpriteImg != null && !string.IsNullOrEmpty(_editing.weaponId))
            {
                var w = WeaponCatalog.Get(_editing.weaponId);
                if (w != null)
                {
                    var all = Resources.LoadAll<Sprite>($"Sprites/TinyTactics/Weapons/{_editing.weaponId}attackSE");
                    Sprite s = null;
                    if (all != null && all.Length > 0)
                    {
                        string firstKey = $"{_editing.weaponId}attackSE_0";
                        foreach (var sp in all)
                            if (sp.name == firstKey) { s = sp; break; }
                        if (s == null) s = all[0];
                    }
                    if (s != null)
                    {
                        _weaponSpriteImg.sprite = s;
                        _weaponSpriteImg.color = Color.white;
                    }
                    else
                    {
                        _weaponSpriteImg.color = new Color(0, 0, 0, 0);
                    }
                }
            }
        }

        private IEnumerator WalkAnimLoop()
        {
            int frame = 0;
            int n = _walkFrames != null ? _walkFrames.Count : 0;
            if (n < 2) yield break;
            while (true)
            {
                frame = (frame + 1) % n;
                _classSpriteImg.sprite = _walkFrames[frame];
                yield return new WaitForSeconds(Tuning.Get().menuWalkAnimFrameDelay);
            }
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

        private float GetAttr(int idx) => idx switch
        {
            0 => _editing.stats.STR, 1 => _editing.stats.VIT,
            2 => _editing.stats.DEX, 3 => _editing.stats.AGI,
            4 => _editing.stats.INT, 5 => _editing.stats.WIS,
            _ => 1f
        };

        private void SetAttr(int idx, float v)
        {
            switch (idx)
            {
                case 0: _editing.stats.STR = v; break;
                case 1: _editing.stats.VIT = v; break;
                case 2: _editing.stats.DEX = v; break;
                case 3: _editing.stats.AGI = v; break;
                case 4: _editing.stats.INT = v; break;
                case 5: _editing.stats.WIS = v; break;
            }
        }

        private void CycleClass()
        {
            if (_editing == null) return;
            var all = CharacterSpriteCatalog.All;
            int idx = 0;
            for (int i = 0; i < all.Length; i++)
                if (all[i].resourcePath == _editing.spritePath) { idx = (i + 1) % all.Length; break; }
            _editing.spritePath = all[idx].resourcePath;
            SyncEditorToPreset();
            RefreshPreview();
        }

        private void CycleWeapon()
        {
            if (_editing == null) return;
            var all = WeaponCatalog.All();
            int idx = 0;
            for (int i = 0; i < all.Length; i++)
                if (all[i].id == _editing.weaponId) { idx = (i + 1) % all.Length; break; }
            _editing.weaponId = all[idx].id;
            SyncEditorToPreset();
            RefreshPreview();
        }

        private void SavePreset()
        {
            if (_editing == null) return;
            if (_nameInput != null && !string.IsNullOrWhiteSpace(_nameInput.text))
                _editing.presetName = _nameInput.text;
            if (string.IsNullOrWhiteSpace(_editing.presetName))
                _editing.presetName = "Personagem";

            if (!_isNewPreset && _origName != _editing.presetName)
                CharacterStorage.Delete(_origName);

            CharacterStorage.Save(_editing);
            RuntimeSelectedCharacter.Active = _editing;
            _isNewPreset = false;
            _origName = _editing.presetName;
            RebuildPresetList();
            SyncEditorToPreset();
            RefreshPreview();
        }

        // "Confirmar": salva e inicia a partida com este personagem (fluxo Play -> Criar)
        private void ConfirmPreset()
        {
            SavePreset();
            RuntimeSelectedCharacter.Active = _editing;
            SceneManager.LoadScene("Battle");
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
                sbHandle.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
                sbHandle.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
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
