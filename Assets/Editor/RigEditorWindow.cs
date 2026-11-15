using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PangeaSkirmish.Editor
{
    /// <summary>
    /// Editor de rig e animações (GDD-025; refeito em 2026-11-15) — só no Unity, fora do jogo.
    ///
    /// Um clique em "Abrir palco" entra em Play (sem maximizar a Game view) e monta o palco. O boneco
    /// aparece na própria janela: arrastar gira a câmera, a roda dá zoom. Escolha uma peça na lista e
    /// ajuste posição, rotação e escala por números (arrastar o rótulo X/Y/Z também ajusta).
    /// "Salvar ajustes" grava em Resources/RigOffsets, que o jogo aplica em todo personagem com aquele
    /// modelo. Os botões de animação tocam o golpe/esquiva/etc. com o ajuste, e a velocidade deixa ver
    /// em câmera lenta.
    /// </summary>
    public class RigEditorWindow : EditorWindow
    {
        private static readonly (string part, string label)[] PartLabels =
        {
            ("Head", "Cabeça"), ("ArmR", "Braço direito"), ("ArmL", "Braço esquerdo"), ("LegR", "Perna direita"),
            ("LegL", "Perna esquerda"), ("Weapon", "Arma"), ("Offhand", "Mão esquerda (arma)"), ("Shield", "Escudo"),
            ("Tail", "Cauda"), ("LegFL", "Pata diant. esq."), ("LegFR", "Pata diant. dir."), ("LegBL", "Pata tras. esq."), ("LegBR", "Pata tras. dir."),
        };

        private const string AutoStartKey = "PangeaRig.AutoStart";
        private const string PlayBehaviorKey = "PangeaRig.PlayBehavior";
        private const float SideWidth = 340f;

        private AnimationStage _stage;
        private int _actor;
        private int _body;
        private int _offhand;
        private float _speed = 1f;
        private string _part = "Weapon";
        private Vector2 _scroll;
        private string _status = "";
        private bool _dragging;

        [MenuItem(PangeaMenu.Animation + "Rig e animações")]
        public static void Open()
        {
            var w = GetWindow<RigEditorWindow>("Rig e animações");
            w.minSize = new Vector2(760f, 520f);
        }

        // ── Play sem maximizar e palco automático ────────────────────────────────────────────

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        private static void OnPlayMode(PlayModeStateChange st)
        {
            if (st == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(AutoStartKey, false))
            {
                SessionState.SetBool(AutoStartKey, false);
                Unmaximize();
                var w = GetWindow<RigEditorWindow>("Rig e animações");
                w.EnsureStage();
                w.Focus();
            }
            if (st == PlayModeStateChange.EnteredEditMode) RestorePlayBehavior();
        }

        private static void StartPlayWithStage()
        {
            SessionState.SetBool(AutoStartKey, true);
            ForcePlayFocused();
            EditorApplication.isPlaying = true;
        }

        /// <summary>A Game view maximizada esconde esta janela: tira qualquer janela do maximizado.</summary>
        private static void Unmaximize()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (w != null && w.maximized) w.maximized = false;
        }

        /// <summary>"Play Maximized" da Game view vira "Play Focused" enquanto o editor de rig usa o Play (volta depois).</summary>
        private static void ForcePlayFocused()
        {
            try
            {
                var gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                var prop = gvType?.GetProperty("enterPlayModeBehavior", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (prop == null) return;
                foreach (var gv in Resources.FindObjectsOfTypeAll(gvType))
                {
                    var cur = prop.GetValue(gv);
                    if (cur.ToString() != "PlayMaximized") continue;
                    SessionState.SetString(PlayBehaviorKey, cur.ToString());
                    prop.SetValue(gv, System.Enum.Parse(prop.PropertyType, "PlayFocused"));
                }
            }
            catch (System.Exception e) { Debug.LogWarning("Rig: não consegui trocar o Play Maximized: " + e.Message); }
        }

        private static void RestorePlayBehavior()
        {
            string keep = SessionState.GetString(PlayBehaviorKey, "");
            if (keep == "") return;
            SessionState.EraseString(PlayBehaviorKey);
            try
            {
                var gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                var prop = gvType?.GetProperty("enterPlayModeBehavior", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (prop == null) return;
                foreach (var gv in Resources.FindObjectsOfTypeAll(gvType)) prop.SetValue(gv, System.Enum.Parse(prop.PropertyType, keep));
            }
            catch { }
        }

        private void EnsureStage()
        {
            if (!Application.isPlaying) return;
            if (_stage == null) _stage = Object.FindFirstObjectByType<AnimationStage>();
            if (_stage == null)
            {
                _stage = AnimationStage.Create(768);
                _stage.SetView(AnimationStage.View.Free);
                _stage.SetActor(AnimationStage.Actors[_actor].id);
            }
        }

        private void OnEnable() => EditorApplication.update += Tick;
        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            if (Application.isPlaying) Time.timeScale = 1f;
        }
        private void Tick() { if (Application.isPlaying && _stage != null) Repaint(); }

        private UnitModel3D Model => _stage != null && _stage.Actor != null ? _stage.Actor.Model3D : null;

        // ── Interface ────────────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.HelpBox("O palco roda dentro do jogo. Um clique entra em Play (sem maximizar) e já abre o boneco aqui.", MessageType.Info);
                if (GUILayout.Button("▶  Abrir palco de animação", GUILayout.Height(40))) StartPlayWithStage();
                GUILayout.FlexibleSpace();
                return;
            }
            EnsureStage();
            if (_stage == null) return;

            EditorGUILayout.BeginHorizontal();
            DrawPreview();
            EditorGUILayout.BeginVertical(GUILayout.Width(SideWidth));
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSide();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreview()
        {
            var area = GUILayoutUtility.GetRect(200f, 200f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(area, new Color(0.13f, 0.15f, 0.22f));
            var tex = _stage.Texture;
            float side = Mathf.Min(area.width, area.height - 28f);
            var r = new Rect(area.x + (area.width - side) * 0.5f, area.y + 28f + (area.height - 28f - side) * 0.5f, side, side);
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, false);

            // Vistas
            var bar = new Rect(area.x + 6f, area.y + 4f, area.width - 12f, 22f);
            GUILayout.BeginArea(bar);
            EditorGUILayout.BeginHorizontal();
            void ViewBtn(string label, AnimationStage.View v)
            {
                bool on = _stage.CurrentView == v;
                if (GUILayout.Toggle(on, label, EditorStyles.miniButton, GUILayout.Width(64)) && !on) _stage.SetView(v);
            }
            ViewBtn("Livre", AnimationStage.View.Free);
            ViewBtn("Jogo", AnimationStage.View.Game);
            ViewBtn("Lado", AnimationStage.View.Side);
            ViewBtn("Frente", AnimationStage.View.Front);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Recentrar", EditorStyles.miniButton, GUILayout.Width(72)))
            { _stage.Yaw = -20f; _stage.Pitch = 20f; _stage.Zoom = 1f; _stage.SetView(AnimationStage.View.Free); }
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUI.Label(new Rect(area.x + 8f, area.yMax - 20f, area.width, 18f), "Arraste para girar · roda para zoom", EditorStyles.miniLabel);

            // Câmera: arrastar gira (passa para a vista livre), roda dá zoom.
            var e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { _dragging = true; e.Use(); }
            else if (e.type == EventType.MouseUp) _dragging = false;
            else if (e.type == EventType.MouseDrag && _dragging)
            {
                if (_stage.CurrentView != AnimationStage.View.Free) _stage.SetView(AnimationStage.View.Free);
                _stage.Yaw += e.delta.x * 0.5f;
                _stage.Pitch = Mathf.Clamp(_stage.Pitch + e.delta.y * 0.4f, -80f, 85f);
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && area.Contains(e.mousePosition))
            {
                _stage.Zoom = Mathf.Clamp(_stage.Zoom * (e.delta.y > 0 ? 0.9f : 1.1f), 0.4f, 5f);
                e.Use();
            }
        }

        private void DrawSide()
        {
            EditorGUILayout.LabelField("Ator", EditorStyles.boldLabel);
            var names = new string[AnimationStage.Actors.Length];
            for (int i = 0; i < names.Length; i++) names[i] = AnimationStage.Actors[i].label;
            int a = EditorGUILayout.Popup(_actor, names);
            if (a != _actor || _stage.Actor == null) { _actor = a; _stage.SetActor(AnimationStage.Actors[_actor].id); }
            if (AnimationStage.Actors[_actor].id != "Fera")
            {
                var bodies = new string[AnimationStage.Bodies.Length];
                for (int i = 0; i < bodies.Length; i++) bodies[i] = AnimationStage.Bodies[i] == "" ? "Corpo padrão da arma" : AnimationStage.Bodies[i];
                int b = EditorGUILayout.Popup("Corpo", _body, bodies);
                if (b != _body) { _body = b; _stage.SetBody(AnimationStage.Bodies[_body]); }
                if (!Equipment.IsTwoHanded(AnimationStage.Actors[_actor].id))
                {
                    var offs = AnimationStage.Offhands;
                    var ol = new string[offs.Length];
                    for (int i = 0; i < ol.Length; i++) ol[i] = offs[i].label;
                    int o = EditorGUILayout.Popup("Mão esquerda", Mathf.Clamp(_offhand, 0, offs.Length - 1), ol);
                    if (o != _offhand) { _offhand = o; _stage.SetOffhand(offs[_offhand].id); }
                }
                else EditorGUILayout.LabelField("Mão esquerda", "— (arma de 2 mãos)");
            }
            var m = Model;
            EditorGUILayout.LabelField("Modelo", m != null ? m.ModelName : "—");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Peça", EditorStyles.boldLabel);
            if (m != null)
            {
                var have = new List<(string part, string label)>();
                foreach (var pl in PartLabels) if (m.Rigs.TryGetValue(pl.part, out var t) && t != null) have.Add(pl);
                if (have.Count > 0 && have.FindIndex(x => x.part == _part) < 0) _part = have[0].part;
                const int cols = 2;
                for (int i = 0; i < have.Count; i += cols)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int j = i; j < Mathf.Min(i + cols, have.Count); j++)
                    {
                        var (part, label) = have[j];
                        var rig = m.Rigs[part];
                        bool changed = rig.localPosition.sqrMagnitude > 1e-8f || rig.localRotation != Quaternion.identity || rig.localScale != Vector3.one;
                        bool on = _part == part;
                        if (GUILayout.Toggle(on, label + (changed ? " •" : ""), EditorStyles.miniButton, GUILayout.Height(22)) && !on) _part = part;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (m.Rigs.TryGetValue(_part, out var sel) && sel != null)
                {
                    string key = m.RigKey(_part), src = m.RigSource(_part);
                    string info = key == m.ModelName ? $"Este ajuste vale para todo {m.ModelName}."
                        : src == key ? $"Ajuste próprio de {key}."
                        : src == "" ? $"{key}: pose padrão (nenhum corpo tem ajuste desta arma)."
                        : $"{key}: espelhado de {src}, seguindo o braço. Mexa e salve para ter ajuste próprio.";
                    EditorGUILayout.HelpBox(info, MessageType.None);
                    DrawPieceFields(sel);
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Animação", EditorStyles.boldLabel);
            const int clipCols = 3;
            for (int i = 0; i < AnimationStage.Clips.Length; i += clipCols)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + clipCols, AnimationStage.Clips.Length); j++)
                {
                    var clip = AnimationStage.Clips[j];
                    if (GUILayout.Button(clip.label, GUILayout.Height(22))) PlayKeepingRig(clip.id);
                }
                EditorGUILayout.EndHorizontal();
            }
            float sp = EditorGUILayout.Slider("Velocidade", _speed, 0.05f, 1f);
            if (!Mathf.Approximately(sp, _speed)) { _speed = sp; Time.timeScale = _speed; }

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Salvar ajustes deste modelo", GUILayout.Height(32))) Save();
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);
        }

        /// <summary>Posição, rotação e escala da peça por números; o rótulo X/Y/Z arrasta o valor.</summary>
        private void DrawPieceFields(Transform rig)
        {
            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            var pos = EditorGUILayout.Vector3Field("Posição", rig.localPosition);
            var eul = EditorGUILayout.Vector3Field("Rotação (graus)", WrapEuler(rig.localEulerAngles));
            var scl = EditorGUILayout.Vector3Field("Escala", rig.localScale);
            float uni = EditorGUILayout.Slider("Escala única", (scl.x + scl.y + scl.z) / 3f, 0.2f, 3f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(rig, "Ajustar peça");
                rig.localPosition = pos;
                rig.localRotation = Quaternion.Euler(eul);
                float avg = (scl.x + scl.y + scl.z) / 3f;
                rig.localScale = Mathf.Abs(uni - avg) > 1e-4f && avg > 1e-4f ? scl * (uni / avg) : scl;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Zerar posição")) { Undo.RecordObject(rig, "Zerar"); rig.localPosition = Vector3.zero; }
            if (GUILayout.Button("Zerar rotação")) { Undo.RecordObject(rig, "Zerar"); rig.localRotation = Quaternion.identity; }
            if (GUILayout.Button("Zerar escala")) { Undo.RecordObject(rig, "Zerar"); rig.localScale = Vector3.one; }
            EditorGUILayout.EndHorizontal();
        }

        private static bool IsMirroredUntouched(RigOffsets asset, UnitModel3D m, string part, Transform rig)
        {
            string key = m.RigKey(part);
            int plus = key.IndexOf('+');
            if (plus < 0) return false;
            string item = key.Substring(plus + 1);
            string def = part == "Shield" ? "Tank" : UnitModel3D.DefaultBodyFor(item);
            asset.ResolveAccessory(m.ModelName, item, part, part == "Weapon" ? "ArmR" : "ArmL", def, out var p, out var r, out var sc);
            return (rig.localPosition - p).sqrMagnitude < 1e-6f && Quaternion.Angle(rig.localRotation, r) < 0.05f && (rig.localScale - sc).sqrMagnitude < 1e-6f;
        }

        private static Vector3 WrapEuler(Vector3 e)
        {
            float W(float a) { a %= 360f; return a > 180f ? a - 360f : a < -180f ? a + 360f : a; }
            return new Vector3(W(e.x), W(e.y), W(e.z));
        }

        /// <summary>Recriar o ator para tocar a animação perderia os ajustes ainda não salvos: guarda e repõe.</summary>
        private void PlayKeepingRig(string clip)
        {
            var keep = Snapshot();
            _stage.ResetActor();
            Restore(keep);
            _stage.Play(clip);
        }

        private Dictionary<string, (Vector3 p, Quaternion r, Vector3 s)> Snapshot()
        {
            var d = new Dictionary<string, (Vector3, Quaternion, Vector3)>();
            if (Model != null) foreach (var kv in Model.Rigs) if (kv.Value != null) d[kv.Key] = (kv.Value.localPosition, kv.Value.localRotation, kv.Value.localScale);
            return d;
        }

        private void Restore(Dictionary<string, (Vector3 p, Quaternion r, Vector3 s)> d)
        {
            if (Model == null) return;
            foreach (var kv in Model.Rigs)
                if (kv.Value != null && d.TryGetValue(kv.Key, out var t)) { kv.Value.localPosition = t.p; kv.Value.localRotation = t.r; kv.Value.localScale = t.s; }
        }

        private void Save()
        {
            var m = Model;
            if (m == null) { _status = "Sem modelo no palco."; return; }
            var asset = RigOffsets.Get();
            if (asset == null)
            {
                asset = CreateInstance<RigOffsets>();
                AssetDatabase.CreateAsset(asset, "Assets/Resources/RigOffsets.asset");
                RigOffsets.SetCache(asset);
            }
            Undo.RecordObject(asset, "Salvar ajustes de rig");
            int n = 0;
            foreach (var kv in m.Rigs)
            {
                if (kv.Value == null) continue;
                string key = m.RigKey(kv.Key);
                // Acessório espelhado de outro corpo e não mexido: não vira ajuste próprio (continua seguindo a origem).
                if (key != m.ModelName && asset.Find(key, kv.Key) == null && IsMirroredUntouched(asset, m, kv.Key, kv.Value)) continue;
                asset.Set(key, kv.Key, kv.Value.localPosition, kv.Value.localEulerAngles, kv.Value.localScale);
                n++;
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            string arma = m.RigKey("Weapon") != m.ModelName ? $" Arma: {m.RigKey("Weapon")}." : "";
            _status = $"Ajustes de {m.ModelName} salvos ({n} peças). Corpo vale para todo {m.ModelName};{arma}";
        }
    }
}
