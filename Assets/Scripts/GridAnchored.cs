using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Marca um GameObject solto (ghost de alcance, waypoint, cursor, overlay de
    /// efeito de tile) como ancorado a uma célula LÓGICA do grid.
    ///
    /// Por que existe: a rotação de vista Q/E é REINDEXAÇÃO LÓGICA
    /// (GridManager._viewOrientation) — a mesma célula lógica muda de posição
    /// física a cada giro. Objetos que guardam só a posição de mundo (todos eles
    /// nascem na raiz da cena, sem pai no grid) ficavam órfãos no slot antigo:
    /// girar durante o planejamento deixava o anel de alcance e os waypoints
    /// desenhados onde o mapa estava antes.
    ///
    /// Uso: GridAnchored.Attach(go, cell, footprint) no lugar de escrever
    /// transform.position à mão. GridManager.SetViewOrientation chama RefreshAll.
    /// </summary>
    [DisallowMultipleComponent]
    public class GridAnchored : MonoBehaviour
    {
        // Registro explícito (não via Awake/OnEnable): vários destes componentes
        // são adicionados a GameObjects desativados — ghosts nascem com
        // SetActive(false) — e Awake/OnEnable não rodariam neles.
        private static readonly List<GridAnchored> _all = new List<GridAnchored>();

        /// <summary>Célula LÓGICA à qual este objeto está preso.</summary>
        public Vector2Int Cell { get; private set; }

        /// <summary>Footprint usado no cálculo do centro (1 = uma célula).</summary>
        public int Footprint { get; private set; } = 1;

        /// <summary>
        /// Prende (ou reposiciona) o objeto na célula lógica dada e já o coloca na
        /// posição física correta da orientação de vista atual.
        /// </summary>
        public static GridAnchored Attach(GameObject go, Vector2Int cell, int footprint = 1)
        {
            if (go == null) return null;

            var a = go.GetComponent<GridAnchored>();
            if (a == null) a = go.AddComponent<GridAnchored>();

            a.Cell = cell;
            a.Footprint = Mathf.Max(1, footprint);
            if (!_all.Contains(a)) _all.Add(a);

            a.Refresh();
            return a;
        }

        /// <summary>Reposiciona a partir da célula lógica, na orientação de vista atual.</summary>
        public void Refresh()
        {
            var grid = ResolveGrid();
            if (grid == null) return;
            transform.position = grid.AnchorToWorldCenter(Cell, Footprint);
        }

        // GridManager.Instance é preenchido no Awake, que NÃO roda fora do Play mode
        // (nem em testes EditMode). Sem o fallback, um Attach feito antes do Awake —
        // ou em modo editor — deixaria o objeto parado na origem em vez da célula.
        private static GridManager ResolveGrid()
        {
            var grid = GridManager.Instance;
            if (grid == null) grid = FindAnyObjectByType<GridManager>();
            return grid;
        }

        /// <summary>Todos os objetos ancorados vivos (usado pelo efeito de giro).</summary>
        public static IReadOnlyList<GridAnchored> All
        {
            get
            {
                for (int i = _all.Count - 1; i >= 0; i--)
                    if (_all[i] == null) _all.RemoveAt(i);
                return _all;
            }
        }

        /// <summary>
        /// Reposiciona todos os objetos ancorados. Chamado por
        /// GridManager.SetViewOrientation depois de reposicionar os tiles.
        /// </summary>
        public static void RefreshAll()
        {
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                var a = _all[i];
                if (a == null) { _all.RemoveAt(i); continue; }
                a.Refresh();
            }
        }

        private void OnDestroy() => _all.Remove(this);
    }
}
