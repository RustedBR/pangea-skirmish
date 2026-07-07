using UnityEditor;
using UnityEngine;

namespace PangeaSkirmish.Editor
{
    /// <summary>
    /// Testes de animação de ataque com o jogo RODANDO (Play mode):
    ///  - Pangea Skirmish/Debug/Attack Test/Golpe SE|NE|SW|NW — toca o golpe na unidade selecionada
    ///    (clique na unidade na Hierarchy; sem seleção usa a primeira unidade do Player).
    ///  - Pangea Skirmish/Debug/Attack Test/Câmera Lenta — alterna timeScale 0.25x/1x para ver o golpe frame a frame.
    /// Atalho principal: Ctrl+Shift+T = Golpe SE.
    /// </summary>
    public static class AttackTester
    {
        private const float SlowScale = 0.25f;
        private const string SlowMoMenu = PangeaMenu.Debug + "Attack Test/Câmera Lenta (0.25x) %#y";

        [MenuItem(PangeaMenu.Debug + "Attack Test/Golpe SE %#t")]
        private static void GolpeSE() => Fire(new Vector3( 2f, -1f, 0f), "SE");

        [MenuItem(PangeaMenu.Debug + "Attack Test/Golpe NE")]
        private static void GolpeNE() => Fire(new Vector3( 2f,  1f, 0f), "NE");

        [MenuItem(PangeaMenu.Debug + "Attack Test/Golpe SW")]
        private static void GolpeSW() => Fire(new Vector3(-2f, -1f, 0f), "SW");

        [MenuItem(PangeaMenu.Debug + "Attack Test/Golpe NW")]
        private static void GolpeNW() => Fire(new Vector3(-2f,  1f, 0f), "NW");

        [MenuItem(SlowMoMenu)]
        private static void ToggleSlowMo()
        {
            Time.timeScale = Mathf.Approximately(Time.timeScale, 1f) ? SlowScale : 1f;
            Menu.SetChecked(SlowMoMenu, !Mathf.Approximately(Time.timeScale, 1f));
            Debug.Log($"[AttackTester] timeScale = {Time.timeScale}");
        }

        private static void Fire(Vector3 delta, string label)
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[AttackTester] Entre em Play primeiro (cena Battle ou Sandbox).");
                return;
            }

            var unit = PickUnit();
            if (unit == null)
            {
                Debug.LogWarning("[AttackTester] Nenhuma unidade encontrada — selecione uma na Hierarchy.");
                return;
            }

            unit.StopAllCoroutines(); // evita golpes sobrepostos de testes anteriores
            unit.StartCoroutine(unit.PlayAttackAnim(unit.transform.position + delta));
            Debug.Log($"[AttackTester] Golpe {label} em '{unit.unitName}' (arma: {unit.weaponId})");
        }

        /// <summary>Unidade selecionada na Hierarchy; sem seleção, a primeira unidade viva do Player.</summary>
        private static Unit PickUnit()
        {
            if (Selection.activeGameObject != null)
            {
                var sel = Selection.activeGameObject.GetComponentInParent<Unit>();
                if (sel != null && !sel.IsDead) return sel;
            }
            foreach (var u in Object.FindObjectsByType<Unit>())
                if (u.team == Team.Player && !u.IsDead) return u;
            foreach (var u in Object.FindObjectsByType<Unit>())
                if (!u.IsDead) return u;
            return null;
        }
    }
}
