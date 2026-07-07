using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PangeaSkirmish.Editor
{
    /// <summary>
    /// Pangea Skirmish → Animation → Weapon Anim Editor — posiciona a arma frame a frame VENDO
    /// personagem e arma juntos, com zoom de pixel. Salva curvas de posição/rotação (stepped) direto
    /// nos clips {id}_Attack{SE,NE}.anim que o jogo usa (nó filho "Anim" do WeaponOverlay).
    ///
    /// Controles: arrastar arma com o mouse • setas = 1px (Shift = 4px) • Q/E = girar ±5°
    /// • ,/. = frame anterior/próximo • scroll no canvas = zoom.
    /// O gerador (Pangea Skirmish/Animation/Gerar Animações de Armas) preserva essas curvas.
    /// </summary>
    public class WeaponAnimEditorWindow : EditorWindow
    {
        private const string ClipsRoot  = "Assets/Resources/Animations/Weapons";
        private const string CharsRes   = "Sprites/TinyTactics/Characters";
        private const string WeaponsRes = "Sprites/TinyTactics/Weapons";
        private const string WeaponsDir = "Assets/Resources/Sprites/TinyTactics/Weapons";
        private const float Fps = 15f;
        private const int PPU = 32;
        private const int Frames = 6;

        private static readonly string[] Classes = { "fighter", "mage", "cleric" };
        private static readonly string[] Dirs = { "SE", "NE" };
        private static readonly string[] FrameNames =
            { "0 charging", "1 attack_0", "2 attack_1", "3 attack_2", "4 attack_3", "5 release" };

        private string[] _weaponIds = new string[0];
        private int _weaponIdx, _classIdx, _dirIdx;
        private int _frame;
        private float _zoom = 12f;
        private float _gameZoom = 5f; // densidade aproximada da câmera de batalha (~5 px de tela por px de arte)
        private bool _onion = true;

        private Sprite[] _body = new Sprite[Frames];
        private Sprite[] _weapon = new Sprite[Frames];
        private Vector2[] _pos = new Vector2[Frames];
        private float[] _rot = new float[Frames];
        private float[] _scale = new float[Frames];
        private bool[] _behind = new bool[Frames];
        private int[] _wframe = new int[Frames]; // qual sprite da arma em cada frame do personagem
        private bool[] _active = new bool[Frames]; // frame ativo/inativo (skip se false)
        private bool _dirty;

        private static readonly string[] WFrameLabels =
            { "0 (wind-up)", "1 (vazio na arte)", "2 (erguido)", "3 (vazio na arte)", "4 (golpe)", "5 (impacto)" };

        [MenuItem(PangeaMenu.Animation + "Weapon Anim Editor")]
        public static void Open()
        {
            var w = GetWindow<WeaponAnimEditorWindow>("Weapon Anim");
            w.minSize = new Vector2(460, 700);
        }

        private void OnEnable()
        {
            _weaponIds = Directory.Exists(WeaponsDir)
                ? Directory.GetFiles(WeaponsDir, "*attackSE.png")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Select(n => n.Substring(0, n.Length - "attackSE".Length))
                    .OrderBy(s => s).ToArray()
                : new string[0];
            LoadAll();
        }

        // ------------------------------------------------------------------ DADOS

        private string WeaponId => _weaponIds.Length > 0 ? _weaponIds[_weaponIdx] : null;
        private string Dir => Dirs[_dirIdx];
        private string ClipPath => $"{ClipsRoot}/{WeaponId}_Attack{Dir}.anim";

        private static EditorCurveBinding Bind(string prop) => new EditorCurveBinding
        {
            path = "", type = typeof(Transform), propertyName = prop
        };

        private static EditorCurveBinding BindSort() => new EditorCurveBinding
        {
            path = "", type = typeof(WeaponSortDriver), propertyName = "sortOffset"
        };

        private static EditorCurveBinding BindSprite() => new EditorCurveBinding
        {
            path = "", type = typeof(SpriteRenderer), propertyName = "m_Sprite"
        };

        private static EditorCurveBinding BindActive() => new EditorCurveBinding
        {
            path = "", type = typeof(WeaponSortDriver), propertyName = "_activeFrame"
        };

        private int ActiveCount => _active.Count(a => a);

        private void LoadAll()
        {
            for (int k = 0; k < Frames; k++)
            {
                _pos[k] = Vector2.zero; _rot[k] = 0f; _scale[k] = 1f; _behind[k] = false;
                _wframe[k] = k; _body[k] = null; _weapon[k] = null; _active[k] = true;
            }
            if (WeaponId == null) return;

            var chars = Resources.LoadAll<Sprite>($"{CharsRes}/{Classes[_classIdx]}");
            Sprite C(string n) => chars.FirstOrDefault(s => s.name == n);
            _body[0] = C($"charging{Dir}_0");
            for (int i = 0; i < 4; i++) _body[1 + i] = C($"attack{Dir}_{i}");
            _body[5] = C($"release{Dir}_0");

            var weap = Resources.LoadAll<Sprite>($"{WeaponsRes}/{WeaponId}attack{Dir}")
                .OrderBy(s =>
                {
                    int us = s.name.LastIndexOf('_');
                    return us >= 0 && int.TryParse(s.name.Substring(us + 1), out int n) ? n : 0;
                }).ToArray();
            for (int k = 0; k < Frames && k < weap.Length; k++) _weapon[k] = weap[k];

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip != null)
            {
                var cx = AnimationUtility.GetEditorCurve(clip, Bind("m_LocalPosition.x"));
                var cy = AnimationUtility.GetEditorCurve(clip, Bind("m_LocalPosition.y"));
                var cz = AnimationUtility.GetEditorCurve(clip, Bind("localEulerAnglesRaw.z"));
                var cs = AnimationUtility.GetEditorCurve(clip, Bind("m_LocalScale.x"));
                var co = AnimationUtility.GetEditorCurve(clip, BindSort());
                var ca = AnimationUtility.GetEditorCurve(clip, BindActive());
                for (int k = 0; k < Frames; k++)
                {
                    float t = k / Fps + 0.0005f;
                    _pos[k] = new Vector2(cx?.Evaluate(t) ?? 0f, cy?.Evaluate(t) ?? 0f);
                    _rot[k] = cz?.Evaluate(t) ?? 0f;
                    _scale[k] = cs?.Evaluate(t) ?? 1f;
                    _behind[k] = (co?.Evaluate(t) ?? 1f) < 0f;
                    _active[k] = ca != null ? ca.Evaluate(t) > 0.5f : true;
                }

                // Mapeamento de sprite da arma por frame (curva de object reference)
                var okeys = AnimationUtility.GetObjectReferenceCurve(clip, BindSprite());
                if (okeys != null)
                    foreach (var key in okeys)
                    {
                        int k = Mathf.RoundToInt(key.time * Fps);
                        if (k < 0 || k >= Frames || !(key.value is Sprite sp)) continue;
                        int us = sp.name.LastIndexOf('_');
                        if (us >= 0 && int.TryParse(sp.name.Substring(us + 1), out int idx))
                            _wframe[k] = Mathf.Clamp(idx, 0, Frames - 1);
                    }
            }
            _dirty = false;
            if (WeaponId != null)
            {
                string activeStr = string.Join(",", _active.Select(a => a ? "1" : "0"));
                Debug.Log($"[WeaponAnimEditor] LoadAll {WeaponId}_{Dir}: _active=[{activeStr}], {ActiveCount} ativos");
            }
            Repaint();
        }

        private void SaveToClip()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null)
            {
                EditorUtility.DisplayDialog("Weapon Anim Editor",
                    $"Clip não encontrado:\n{ClipPath}\n\nRode Pangea Skirmish/Animation/Gerar Animações de Armas primeiro.", "OK");
                return;
            }

            int activeCount = ActiveCount;
            if (activeCount == 0)
            {
                EditorUtility.DisplayDialog("Weapon Anim Editor",
                    "Nenhum frame ativo — não é possível salvar clip vazio.", "OK");
                return;
            }

            // Curvas stepped para posição/rotação/escala — só dos frames ativos
            AnimationCurve SteppedActive(System.Func<int, float> val)
            {
                var c = new AnimationCurve();
                int idx = 0;
                for (int k = 0; k < Frames; k++)
                {
                    if (!_active[k]) continue;
                    c.AddKey(new Keyframe(idx / Fps, val(k), float.PositiveInfinity, float.PositiveInfinity));
                    idx++;
                }
                return c;
            }

            AnimationUtility.SetEditorCurve(clip, Bind("m_LocalPosition.x"), SteppedActive(k => _pos[k].x));
            AnimationUtility.SetEditorCurve(clip, Bind("m_LocalPosition.y"), SteppedActive(k => _pos[k].y));
            AnimationUtility.SetEditorCurve(clip, Bind("m_LocalPosition.z"), SteppedActive(k => 0f));
            AnimationUtility.SetEditorCurve(clip, Bind("localEulerAnglesRaw.x"), SteppedActive(k => 0f));
            AnimationUtility.SetEditorCurve(clip, Bind("localEulerAnglesRaw.y"), SteppedActive(k => 0f));
            AnimationUtility.SetEditorCurve(clip, Bind("localEulerAnglesRaw.z"), SteppedActive(k => _rot[k]));
            AnimationUtility.SetEditorCurve(clip, Bind("m_LocalScale.x"), SteppedActive(k => _scale[k]));
            AnimationUtility.SetEditorCurve(clip, Bind("m_LocalScale.y"), SteppedActive(k => _scale[k]));
            AnimationUtility.SetEditorCurve(clip, Bind("m_LocalScale.z"), SteppedActive(k => 1f));
            AnimationUtility.SetEditorCurve(clip, BindSort(), SteppedActive(k => _behind[k] ? -1f : 1f));

            // Curva _Active: 1.0 nos frames ativos, 0.0 nos inativos (metadata para LoadAll)
            var activeCurve = new AnimationCurve();
            for (int k = 0; k < Frames; k++)
                activeCurve.AddKey(new Keyframe(k / Fps, _active[k] ? 1f : 0f, float.PositiveInfinity, float.PositiveInfinity));
            AnimationUtility.SetEditorCurve(clip, BindActive(), activeCurve);

            // Sprite da arma por frame — só frames ativos, com hold do último
            var skeys = new List<ObjectReferenceKeyframe>();
            int spriteIdx = 0;
            for (int k = 0; k < Frames; k++)
            {
                if (!_active[k]) continue;
                skeys.Add(new ObjectReferenceKeyframe { time = spriteIdx / Fps, value = _weapon[_wframe[k]] });
                spriteIdx++;
            }
            // Hold: repete o último frame ativo no tempo final
            skeys.Add(new ObjectReferenceKeyframe { time = spriteIdx / Fps, value = _weapon[_wframe[LastActiveIndex()]] });
            AnimationUtility.SetObjectReferenceCurve(clip, BindSprite(), skeys.ToArray());

            clip.frameRate = Fps;
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            _dirty = false;
            string activeStr = string.Join(",", _active.Select(a => a ? "1" : "0"));
            Debug.Log($"[WeaponAnimEditor] Salvo {WeaponId}_Attack{Dir}: _active=[{activeStr}], {activeCount} frames, clip length={activeCount / Fps:F3}s");
            ShowNotification(new GUIContent($"Salvo: {activeCount} frames → {WeaponId}_Attack{Dir}.anim"));
        }

        private int LastActiveIndex()
        {
            for (int k = Frames - 1; k >= 0; k--)
                if (_active[k]) return k;
            return 0;
        }

        /// <summary>
        /// Insere um frame duplicando o estado do frame atual na posição _frame+1,
        /// deslocando todos os seguintes para a direita. O último frame é perdido.
        /// </summary>
        private void AddFrameAt(int at)
        {
            // desloca tudo à direita a partir de at+1
            for (int k = Frames - 1; k > at; k--)
            {
                _pos[k] = _pos[k - 1];
                _rot[k] = _rot[k - 1];
                _scale[k] = _scale[k - 1];
                _behind[k] = _behind[k - 1];
                _wframe[k] = _wframe[k - 1];
                _active[k] = _active[k - 1];
            }
            // insere cópia do frame atual na posição at+1
            int ins = at + 1;
            _pos[ins] = _pos[at];
            _rot[ins] = _rot[at];
            _scale[ins] = _scale[at];
            _behind[ins] = _behind[at];
            _wframe[ins] = _wframe[at];
            _active[ins] = true;
            _frame = ins;
        }

        // ------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            if (_weaponIds.Length == 0)
            {
                EditorGUILayout.HelpBox("Nenhum spritesheet de arma encontrado em " + WeaponsDir, MessageType.Warning);
                return;
            }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            _weaponIdx = EditorGUILayout.Popup("Arma", _weaponIdx, _weaponIds);
            _classIdx  = EditorGUILayout.Popup("Corpo (referência)", _classIdx, Classes);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            _dirIdx = EditorGUILayout.Popup("Direção", _dirIdx, Dirs);
            _onion  = EditorGUILayout.ToggleLeft("Fantasma do frame anterior", _onion, GUILayout.Width(200));
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                if (_dirty && !EditorUtility.DisplayDialog("Weapon Anim Editor",
                        "Há ajustes não salvos. Descartar?", "Descartar", "Voltar"))
                { /* mantém seleção anterior visualmente na próxima repaint — simples */ }
                LoadAll();
            }

            // Timeline: indicador visual de frames ativos/inativos
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Timeline", GUILayout.Width(60));
            for (int k = 0; k < Frames; k++)
            {
                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = _active[k] ? new Color(0.5f, 1f, 0.5f) : new Color(0.4f, 0.4f, 0.4f);
                if (GUILayout.Button(k.ToString(), GUILayout.Width(28),
                    _frame == k ? GUILayout.Height(26) : GUILayout.Height(20)))
                {
                    _frame = k;
                    GUI.FocusControl(null);
                }
                GUI.backgroundColor = oldBg;
            }
            EditorGUILayout.EndHorizontal();

            // Botões de frame: toggle, adicionar, remover
            EditorGUILayout.BeginHorizontal();
            var toggleLabel = _active[_frame] ? $"Frame {_frame}: ATIVO" : $"Frame {_frame}: SKIPADO";
            if (GUILayout.Button(toggleLabel, GUILayout.Width(130)))
            {
                _active[_frame] = !_active[_frame];
                _dirty = true;
            }
            GUI.enabled = ActiveCount < Frames;
            if (GUILayout.Button("+ Adicionar Frame", GUILayout.Width(130)))
            {
                AddFrameAt(_frame);
                _dirty = true;
            }
            GUI.enabled = ActiveCount > 1;
            if (GUILayout.Button("- Remover Frame", GUILayout.Width(130)))
            {
                _active[_frame] = false;
                // move para o próximo ativo se existir
                for (int k = _frame + 1; k < Frames; k++)
                    if (_active[k]) { _frame = k; break; }
                _dirty = true;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            _frame = EditorGUILayout.IntSlider(new GUIContent("Frame"), _frame, 0, Frames - 1);
            string frameLabel = _active[_frame]
                ? $"{FrameNames[_frame]} ({_wframe[_frame]})"
                : $"{FrameNames[_frame]} — SKIPADO";
            EditorGUILayout.LabelField(" ", frameLabel, EditorStyles.boldLabel);
            _zoom = EditorGUILayout.Slider("Zoom", _zoom, 4f, 24f);
            _gameZoom = EditorGUILayout.Slider("Preview jogo (zoom)", _gameZoom, 1.5f, 8f);

            DrawCanvas();

            EditorGUI.BeginChangeCheck();
            _pos[_frame] = EditorGUILayout.Vector2Field("Posição (un; 1px = 0.03125)", _pos[_frame]);
            _rot[_frame] = EditorGUILayout.Slider("Rotação (°)", _rot[_frame], -180f, 180f);
            _scale[_frame] = EditorGUILayout.Slider("Escala", _scale[_frame], 0.25f, 2f);
            _behind[_frame] = EditorGUILayout.Toggle("Atrás do personagem", _behind[_frame]);
            _wframe[_frame] = EditorGUILayout.Popup("Frame da arma", _wframe[_frame], WFrameLabels);
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _frame > 0;
            if (GUILayout.Button("Copiar frame anterior"))
            {
                _pos[_frame] = _pos[_frame - 1]; _rot[_frame] = _rot[_frame - 1];
                _scale[_frame] = _scale[_frame - 1]; _behind[_frame] = _behind[_frame - 1];
                _wframe[_frame] = _wframe[_frame - 1];
                _dirty = true;
            }
            GUI.enabled = true;
            if (GUILayout.Button("Escala p/ todos os frames"))
            { for (int k = 0; k < Frames; k++) _scale[k] = _scale[_frame]; _dirty = true; }
            if (GUILayout.Button("Resetar frame"))
            {
                _pos[_frame] = Vector2.zero; _rot[_frame] = 0f; _scale[_frame] = 1f;
                _behind[_frame] = false; _wframe[_frame] = _frame; _dirty = true;
            }
            if (GUILayout.Button("Resetar TUDO"))
            {
                if (EditorUtility.DisplayDialog("Weapon Anim Editor",
                        "Voltar TODOS os frames ao padrão do kit (escala 1, sem offset, todos ativos)?", "Resetar", "Cancelar"))
                {
                    for (int k = 0; k < Frames; k++)
                    {
                        _pos[k] = Vector2.zero; _rot[k] = 0f; _scale[k] = 1f;
                        _behind[k] = false; _wframe[k] = k; _active[k] = true;
                    }
                    _dirty = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = _dirty ? new Color(0.6f, 1f, 0.6f) : Color.white;
            if (GUILayout.Button(_dirty ? "SALVAR NO CLIP *" : "Salvar no clip", GUILayout.Height(30))) SaveToClip();
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("Recarregar", GUILayout.Height(30), GUILayout.Width(100))) LoadAll();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Arraste a arma no canvas • Setas = 1px (Shift = 4px) • Q/E = girar ±5° • Z/X = escala ∓5% • " +
                "B = frente/atrás • , . = trocar frame\n" +
                "FRAMES: clique nos números da timeline para selecionar. Botão verde = frame ativo, cinza = skipado.\n" +
                "Use \"+ Adicionar Frame\" para duplicar o frame atual (insere depois dele).\n" +
                "Use \"- Remover Frame\" para desativar o frame (fica vermelho no canvas, não entra no clip).\n" +
                "Frames 1 e 3 da arma são vazios na arte — desative-os se não precisar.\n" +
                "Clip salva só frames ativos: ex. 5 ativos = clip de 5/15s.\n" +
                "Curvas salvas valem para TODAS as classes que usarem esta arma; o flip SW/NW espelha sozinho.\n" +
                "OBS: rodar \"Gerar Animações de Armas\" reseta o mapeamento de frame da arma para o padrão " +
                "(posição/rotação/escala são preservadas) — re-salve aqui depois, se regenerar.",
                MessageType.Info);

            HandleKeys();
        }

        private void DrawCanvas()
        {
            float boxW = 32 * _zoom, boxH = 48 * _zoom;
            Rect area = GUILayoutUtility.GetRect(boxW + 24, boxH + 24);
            EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.16f));
            Vector2 origin = new Vector2(area.center.x - boxW / 2f, area.center.y - boxH / 2f);

            // contorno do canvas 32x48 compartilhado
            var outline = new Rect(origin.x, origin.y, boxW, boxH);
            Handles.color = new Color(1f, 1f, 1f, 0.15f);
            Handles.DrawSolidRectangleWithOutline(outline, Color.clear, new Color(1f, 1f, 1f, 0.2f));

            bool isActive = _active[_frame];

            // Frame inativo: overlay vermelho translúcido + texto "SKIPADO"
            if (!isActive)
            {
                EditorGUI.DrawRect(area, new Color(0.8f, 0.2f, 0.2f, 0.15f));
                GUI.color = new Color(1f, 0.4f, 0.4f, 0.9f);
                GUI.Label(new Rect(area.center.x - 40, area.center.y - 8, 80, 16),
                    "SKIPADO", EditorStyles.boldLabel);
                GUI.color = Color.white;
            }

            // ordem de desenho: fantasma → arma atrás → corpo → arma na frente
            if (_onion && _frame > 0 && _weapon[_wframe[_frame - 1]] != null)
                DrawWeapon(_frame - 1, origin, 0.3f);

            float weaponAlpha = isActive ? 1f : 0.3f;
            if (_behind[_frame] && _weapon[_wframe[_frame]] != null)
                DrawWeapon(_frame, origin, weaponAlpha);

            // corpo: 32x32 encostado na BASE do canvas (bottom-aligned)
            if (_body[_frame] != null)
                DrawSprite(_body[_frame], new Rect(origin.x, origin.y + 16 * _zoom, 32 * _zoom, 32 * _zoom), 1f);

            if (!_behind[_frame] && _weapon[_wframe[_frame]] != null)
                DrawWeapon(_frame, origin, weaponAlpha);

            // Mini preview "tamanho no jogo" — overlay no canto superior direito do canvas
            float mz = _gameZoom;
            var mini = new Rect(area.xMax - (32 * mz + 10) - 6, area.y + 6, 32 * mz + 10, 48 * mz + 24);
            EditorGUI.DrawRect(mini, new Color(0.09f, 0.13f, 0.09f, 0.95f));
            GUI.Label(new Rect(mini.x + 3, mini.y + 2, mini.width - 6, 14), "jogo", EditorStyles.miniLabel);
            Vector2 mo = new Vector2(mini.center.x - 16 * mz, mini.y + 18);
            float miniAlpha = isActive ? 1f : 0.3f;
            if (_behind[_frame] && _weapon[_wframe[_frame]] != null)
                DrawWeapon(_frame, mo, miniAlpha, mz);
            if (_body[_frame] != null)
                DrawSprite(_body[_frame], new Rect(mo.x, mo.y + 16 * mz, 32 * mz, 32 * mz), 1f);
            if (!_behind[_frame] && _weapon[_wframe[_frame]] != null)
                DrawWeapon(_frame, mo, miniAlpha, mz);

            // input do mouse
            Event e = Event.current;
            if (area.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    _pos[_frame].x += e.delta.x / (_zoom * PPU);
                    _pos[_frame].y -= e.delta.y / (_zoom * PPU);
                    _dirty = true; Repaint(); e.Use();
                }
                else if (e.type == EventType.ScrollWheel)
                {
                    _zoom = Mathf.Clamp(_zoom - e.delta.y * 0.5f, 4f, 24f);
                    Repaint(); e.Use();
                }
            }
        }

        private void DrawWeapon(int k, Vector2 origin, float alpha) => DrawWeapon(k, origin, alpha, _zoom);

        private void DrawWeapon(int k, Vector2 origin, float alpha, float zoom)
        {
            // posição em unidades → pixels de tela (y invertido: GUI cresce para baixo)
            Vector2 offPx = new Vector2(_pos[k].x * PPU * zoom, -_pos[k].y * PPU * zoom);
            // pivot do sprite = centro do canvas 32x48; a escala expande ao redor do pivot
            Vector2 pivot = new Vector2(origin.x + 16 * zoom + offPx.x, origin.y + 24 * zoom + offPx.y);
            float w = 32 * zoom * _scale[k], h = 48 * zoom * _scale[k];
            var rect = new Rect(pivot.x - w / 2f, pivot.y - h / 2f, w, h);

            Matrix4x4 m = GUI.matrix;
            // z+ no mundo = anti-horário visual; GUI (y p/ baixo) roda horário → nega
            GUIUtility.RotateAroundPivot(-_rot[k], pivot);
            DrawSprite(_weapon[_wframe[k]], rect, alpha);
            GUI.matrix = m;
        }

        private static void DrawSprite(Sprite s, Rect rect, float alpha)
        {
            // s.rect = retângulo COMPLETO do sub-sprite no atlas. NÃO usar textureRect:
            // ele é o retângulo justo dos pixels opacos e AMPLIA/recentra a arte no preview
            // (foi a causa da arma parecer gigante na janela e minúscula no jogo).
            var tr = s.rect;
            var uv = new Rect(tr.x / s.texture.width, tr.y / s.texture.height,
                              tr.width / s.texture.width, tr.height / s.texture.height);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(rect, s.texture, uv);
            GUI.color = old;
        }

        private void HandleKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;
            float px = (e.shift ? 4f : 1f) / PPU;
            bool used = true;
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow:  _pos[_frame].x -= px; _dirty = true; break;
                case KeyCode.RightArrow: _pos[_frame].x += px; _dirty = true; break;
                case KeyCode.UpArrow:    _pos[_frame].y += px; _dirty = true; break;
                case KeyCode.DownArrow:  _pos[_frame].y -= px; _dirty = true; break;
                case KeyCode.Q: _rot[_frame] += 5f; _dirty = true; break;
                case KeyCode.E: _rot[_frame] -= 5f; _dirty = true; break;
                case KeyCode.Z: _scale[_frame] = Mathf.Clamp(_scale[_frame] - 0.05f, 0.25f, 2f); _dirty = true; break;
                case KeyCode.X: _scale[_frame] = Mathf.Clamp(_scale[_frame] + 0.05f, 0.25f, 2f); _dirty = true; break;
                case KeyCode.B: _behind[_frame] = !_behind[_frame]; _dirty = true; break;
                case KeyCode.Comma:  _frame = Mathf.Max(0, _frame - 1); break;
                case KeyCode.Period: _frame = Mathf.Min(Frames - 1, _frame + 1); break;
                default: used = false; break;
            }
            if (used) { Repaint(); e.Use(); }
        }
    }
}
