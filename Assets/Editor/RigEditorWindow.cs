using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PangeaSkirmish.Editor
{
    /// <summary>
    /// Editor de rig e animações (GDD-025, 2026-11-14) — só no Unity, fora do jogo.
    ///
    /// Como usar: entre em Play, abra Pangea Skirmish/Animation/Rig e animações, escolha o ator.
    /// Clique numa peça (Cabeça, Braço D, Arma…): ela fica selecionada na Hierarchy e na Scene, e você
    /// move/gira/escala com as ferramentas normais do Unity (W/E/R). "Salvar ajustes" grava no asset
    /// Resources/RigOffsets, que o jogo aplica em todo personagem com aquele modelo. Os botões de animação
    /// tocam o golpe/esquiva/etc. com o ajuste aplicado, e a velocidade deixa ver em câmera lenta.
    /// </summary>
    public class RigEditorWindow : EditorWindow
    {
        private static readonly (string part, string label)[] PartLabels =
        {
            ("Head", "Cabeça"), ("ArmR", "Braço direito"), ("ArmL", "Braço esquerdo"), ("LegR", "Perna direita"),
            ("LegL", "Perna esquerda"), ("Weapon", "Arma"), ("Offhand", "Mão esquerda (arma)"), ("Shield", "Escudo"),
            ("Tail", "Cauda"), ("LegFL", "Pata diant. esq."), ("LegFR", "Pata diant. dir."), ("LegBL", "Pata tras. esq."), ("LegBR", "Pata tras. dir."),
        };

        private AnimationStage _stage;
        private int _actor;
        private float _speed = 1f;
        private Vector2 _scroll;
        private string _status = "";

        [MenuItem(PangeaMenu.Animation + "Rig e animações")]
        public static void Open() => GetWindow<RigEditorWindow>("Rig e animações");

        private void OnDisable()
        {
            if (Application.isPlaying) Time.timeScale = 1f;
        }

        private UnitModel3D Model => _stage != null && _stage.Actor != null ? _stage.Actor.Model3D : null;

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("1) Entre em Play.  2) Abra o palco e escolha o ator.  3) Clique numa peça e use W/E/R na Scene.  4) Salvar ajustes.", MessageType.Info);
            if (!Application.isPlaying) { EditorGUILayout.HelpBox("O editor de rig só funciona em Play Mode.", MessageType.Warning); return; }

            if (_stage == null)
            {
                _stage = Object.FindFirstObjectByType<AnimationStage>();
                if (GUILayout.Button("Abrir palco", GUILayout.Height(30))) { _stage = AnimationStage.Create(512); _stage.SetActor(AnimationStage.Actors[_actor].id); }
                return;
            }

            EditorGUILayout.LabelField("Ator", EditorStyles.boldLabel);
            var names = new string[AnimationStage.Actors.Length];
            for (int i = 0; i < names.Length; i++) names[i] = AnimationStage.Actors[i].label;
            int a = EditorGUILayout.Popup(_actor, names);
            if (a != _actor || _stage.Actor == null) { _actor = a; _stage.SetActor(AnimationStage.Actors[_actor].id); }

            var m = Model;
            EditorGUILayout.LabelField("Modelo", m != null ? m.ModelName : "—");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Peças (clique para selecionar e mover na Scene)", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(260));
            if (m != null)
                foreach (var (part, label) in PartLabels)
                {
                    if (!m.Rigs.TryGetValue(part, out var rig) || rig == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    bool sel = Selection.activeTransform == rig;
                    var st = new GUIStyle(GUI.skin.button) { fontStyle = sel ? FontStyle.Bold : FontStyle.Normal, alignment = TextAnchor.MiddleLeft };
                    if (GUILayout.Button((sel ? "▶ " : "") + label, st)) { Selection.activeTransform = rig; SceneView.lastActiveSceneView?.FrameSelected(); }
                    bool changed = rig.localPosition.sqrMagnitude > 1e-8f || rig.localRotation != Quaternion.identity || rig.localScale != Vector3.one;
                    GUILayout.Label(changed ? "ajustado" : "", GUILayout.Width(60));
                    if (GUILayout.Button("Zerar", GUILayout.Width(52)))
                    { Undo.RecordObject(rig, "Zerar peça"); rig.localPosition = Vector3.zero; rig.localRotation = Quaternion.identity; rig.localScale = Vector3.one; }
                    EditorGUILayout.EndHorizontal();
                }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Animação", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            int col = 0;
            foreach (var clip in AnimationStage.Clips)
            {
                if (GUILayout.Button(clip.label)) PlayKeepingRig(clip.id);
                if (++col % 4 == 0) { EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); }
            }
            EditorGUILayout.EndHorizontal();
            float sp = EditorGUILayout.Slider("Velocidade", _speed, 0.05f, 1f);
            if (!Mathf.Approximately(sp, _speed)) { _speed = sp; Time.timeScale = _speed; }

            EditorGUILayout.Space();
            if (GUILayout.Button("Salvar ajustes deste modelo", GUILayout.Height(32))) Save();
            if (GUILayout.Button("Visualizador com notas (tela do jogo)")) PangeaSkirmish.UI.PangeaScreen.Spawn<PangeaSkirmish.UI.AnimViewerScreen>("AnimViewerScreen");
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);
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
                asset.Set(m.ModelName, kv.Key, kv.Value.localPosition, kv.Value.localEulerAngles, kv.Value.localScale);
                n++;
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            _status = $"Ajustes de {m.ModelName} salvos ({n} peças). Valem para todo personagem com esse modelo.";
        }

        private void OnInspectorUpdate() => Repaint();
    }
}
