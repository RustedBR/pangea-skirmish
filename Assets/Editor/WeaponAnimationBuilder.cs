using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PangeaSkirmish.EditorTools
{
    /// <summary>
    /// Gera os assets de animação das armas (overlay canvas-aligned) a partir dos
    /// spritesheets em Resources/Sprites/TinyTactics/Weapons ({id}attackSE/NE.png, 6 frames 32x48):
    ///  - {id}_AttackSE/NE.anim — 6 keyframes de sprite @15fps (0.4s, hold no último frame)
    ///  - {id}_CarrySE/NE.anim  — frame 0 estático (pose de descanso p/ idle/walk)
    ///  - WeaponOverlayBase.controller — estados CarrySE (default), CarryNE, AttackSE, AttackNE
    ///  - {id}Overlay.overrideController — AnimatorOverrideController por arma (carregado
    ///    em runtime via Resources.Load("Animations/Weapons/{id}Overlay") pelo WeaponOverlay)
    ///
    /// Re-executável e NÃO destrutivo: em clips existentes só os keyframes de SPRITE são
    /// reescritos — curvas de posição/rotação (feitas na janela Weapon Anim Editor) são
    /// preservadas. Controller e overrides só são criados se não existirem.
    /// Rodar de novo quando novas armas ganharem spritesheet (ShortBow, LongBow, etc).
    /// </summary>
    public static class WeaponAnimationBuilder
    {
        private const string SpritesRoot = "Assets/Resources/Sprites/TinyTactics/Weapons";
        private const string OutRoot     = "Assets/Resources/Animations/Weapons";
        private const float  Fps         = 15f;
        private static readonly string[] Dirs = { "SE", "NE" };

        private static EditorCurveBinding BindActive() => new EditorCurveBinding
        {
            path = "", type = typeof(WeaponSortDriver), propertyName = "_activeFrame"
        };

        [MenuItem("Pangea/Gerar Animações de Armas")]
        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Animations"))
                AssetDatabase.CreateFolder("Assets/Resources", "Animations");
            if (!AssetDatabase.IsValidFolder(OutRoot))
                AssetDatabase.CreateFolder("Assets/Resources/Animations", "Weapons");

            // Ids das armas = arquivos {id}attackSE.png presentes na pasta de sprites.
            var ids = Directory.GetFiles(SpritesRoot, "*attackSE.png")
                .Select(Path.GetFileNameWithoutExtension)
                .Select(n => n.Substring(0, n.Length - "attackSE".Length))
                .OrderBy(s => s)
                .ToList();

            var clips = new Dictionary<string, AnimationClip>();
            var readyIds = new List<string>();

            foreach (var id in ids)
            {
                bool ok = true;
                foreach (var dir in Dirs)
                {
                    var frames = LoadFrames($"{SpritesRoot}/{id}attack{dir}.png");
                    if (frames.Count == 0)
                    {
                        Debug.LogWarning($"[WeaponAnimationBuilder] {id}attack{dir}.png sem sub-sprites — pulando {id}.");
                        ok = false;
                        break;
                    }

                    // Preserva _Active curve do clip existente (editada pelo Weapon Anim Editor)
                    bool[] active = ReadActiveCurve($"{OutRoot}/{id}_Attack{dir}.anim", frames.Count);
                    if (active != null)
                    {
                        int actCount = 0;
                        for (int i = 0; i < active.Length; i++) if (active[i]) actCount++;
                        Debug.Log($"[WeaponAnimationBuilder] {id}_{dir}: _Active preservado = [{string.Join(",", active.Select(a => a ? "1" : "0"))}] ({actCount} ativos)");
                    }
                    else
                    {
                        Debug.Log($"[WeaponAnimationBuilder] {id}_{dir}: sem _Active curve — todos os {frames.Count} frames ativos");
                    }
                    clips[$"{id}_Attack{dir}"] = WriteClip(frames, hold: true,
                        $"{OutRoot}/{id}_Attack{dir}.anim", active);
                    // Carry usa só o primeiro frame ativo
                    int firstActive = active != null ? System.Array.IndexOf(active, true) : 0;
                    if (firstActive < 0) firstActive = 0;
                    clips[$"{id}_Carry{dir}"]  = WriteClip(new List<Sprite> { frames[firstActive] },
                        hold: false, $"{OutRoot}/{id}_Carry{dir}.anim", null);
                }
                if (ok) readyIds.Add(id);
            }

            if (readyIds.Count == 0)
            {
                Debug.LogError("[WeaponAnimationBuilder] Nenhuma arma com sprites encontrada — nada gerado.");
                return;
            }

            // Controller base: os estados usam os clips da primeira arma; as demais entram
            // como override. Os nomes dos estados são os que o WeaponOverlay dá Play().
            // Só é criado se não existir (preserva ajustes manuais no controller).
            string refId = readyIds[0];
            string ctrlPath = $"{OutRoot}/WeaponOverlayBase.controller";
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            if (ctrl == null)
            {
                ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
                var sm = ctrl.layers[0].stateMachine;

                AnimatorState AddState(string name, string clipKey, Vector3 pos)
                {
                    var st = sm.AddState(name, pos);
                    st.motion = clips[clipKey];
                    return st;
                }

                var carrySE = AddState("CarrySE", $"{refId}_CarrySE", new Vector3(260, 0, 0));
                AddState("CarryNE",  $"{refId}_CarryNE",  new Vector3(260, 60, 0));
                AddState("AttackSE", $"{refId}_AttackSE", new Vector3(260, 140, 0));
                AddState("AttackNE", $"{refId}_AttackNE", new Vector3(260, 200, 0));
                sm.defaultState = carrySE;
            }

            // Um AnimatorOverrideController por arma (inclusive a de referência, para o
            // runtime carregar sempre pelo mesmo padrão de nome). Só cria os que faltam.
            foreach (var id in readyIds)
            {
                string aocPath = $"{OutRoot}/{id}Overlay.overrideController";
                if (AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(aocPath) != null) continue;
                var aoc = new AnimatorOverrideController(ctrl) { name = $"{id}Overlay" };
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                aoc.GetOverrides(pairs);
                for (int i = 0; i < pairs.Count; i++)
                {
                    // Nome do clip base = "{refId}_{sufixo}" → troca para o clip "{id}_{sufixo}".
                    string suffix = pairs[i].Key.name.Substring(refId.Length + 1);
                    pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, clips[$"{id}_{suffix}"]);
                }
                aoc.ApplyOverrides(pairs);
                AssetDatabase.CreateAsset(aoc, aocPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[WeaponAnimationBuilder] Gerado: {readyIds.Count} armas ({string.Join(", ", readyIds)}), " +
                      $"{clips.Count} clips + controller base + overrides em {OutRoot}.");
        }

        /// <summary>Sub-sprites do sheet ordenados pelo índice numérico do sufixo (_0, _1, ...).</summary>
        private static List<Sprite> LoadFrames(string assetPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .OrderBy(s =>
                {
                    int us = s.name.LastIndexOf('_');
                    return us >= 0 && int.TryParse(s.name.Substring(us + 1), out int n) ? n : 0;
                })
                .ToList();
        }

        /// <summary>
        /// Lê a curva _Active de um clip existente para preservar quais frames estão ativos.
        /// Retorna null se a curva não existir (significa que todos os frames estão ativos).
        /// </summary>
        private static bool[] ReadActiveCurve(string clipPath, int totalFrames)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null) return null;
            var curve = AnimationUtility.GetEditorCurve(clip, BindActive());
            if (curve == null) return null;
            var active = new bool[totalFrames];
            for (int k = 0; k < totalFrames; k++)
            {
                float t = k / Fps + 0.0005f;
                active[k] = curve.Evaluate(t) > 0.5f;
            }
            return active;
        }

        /// <summary>
        /// Escreve os keyframes de sprite no clip do path. Se o clip já existe, SÓ a curva
        /// de sprite é substituída — curvas de posição/rotação (Weapon Anim Editor) ficam.
        /// Se active != null, só escreve frames onde active[i] == true e grava a curva _Active.
        /// </summary>
        private static AnimationClip WriteClip(List<Sprite> frames, bool hold, string path, bool[] active)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool isNew = clip == null;
            if (isNew) clip = new AnimationClip();
            clip.frameRate = Fps;

            // Determina quais frames escrever
            var activeFrames = new List<int>();
            if (active != null)
            {
                for (int i = 0; i < frames.Count && i < active.Length; i++)
                    if (active[i]) activeFrames.Add(i);
            }
            else
            {
                for (int i = 0; i < frames.Count; i++)
                    activeFrames.Add(i);
            }
            if (activeFrames.Count == 0)
            {
                // Fallback: escreve todos se nenhum estiver marcado como ativo
                activeFrames.Clear();
                for (int i = 0; i < frames.Count; i++)
                    activeFrames.Add(i);
            }

            // Sprite keyframes — só dos frames ativos
            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = "",
                propertyName = "m_Sprite"
            };
            int n = activeFrames.Count;
            var keys = new ObjectReferenceKeyframe[hold ? n + 1 : n];
            for (int i = 0; i < n; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / Fps, value = frames[activeFrames[i]] };
            if (hold)
                keys[n] = new ObjectReferenceKeyframe { time = n / Fps, value = frames[activeFrames[n - 1]] };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            // Curva _Active — metadata para o Weapon Anim Editor
            if (active != null)
            {
                var activeCurve = new AnimationCurve();
                for (int k = 0; k < frames.Count; k++)
                    activeCurve.AddKey(new Keyframe(k / Fps, active[k] ? 1f : 0f,
                        float.PositiveInfinity, float.PositiveInfinity));
                AnimationUtility.SetEditorCurve(clip, BindActive(), activeCurve);
            }

            if (isNew)
            {
                clip.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(clip, path);
            }
            else
            {
                EditorUtility.SetDirty(clip);
            }
            return clip;
        }
    }
}
