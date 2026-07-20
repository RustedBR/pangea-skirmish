using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Camada unificada de Colisão & Ocupação do grid (Parte A da mecânica de sandbox).
    /// Fonte da verdade do que TRAVA o espaço de jogo:
    ///   - walkable[x,y] : false se a célula é void OU tile/estrutura sólida.
    ///   - zLevel[x,y]   : altura efetiva (tile + estrutura) — base para z-level jogável (Parte B).
    ///   - occupant[x,y] : unidade/estrutura que ocupa a célula (registry O(1), substitui
    ///                     o FindObjectsOfType usado em FootprintsOverlapAny).
    ///
    /// É data-only (sem render). O GridManager a alimenta a partir de _voidCells/_heights e
    /// as unidades/estruturas se registram ao nascer/mover/morrer. Os métodos legados
    /// (FootprintsOverlap, IsAnchorInBounds) continuam válidos; este grid só ADICIONA
    /// consulta O(1) e z-level real sem quebrar o combate existente.
    /// </summary>
    public class CollisionGrid
    {
        private int _w;
        private int _h;

        private bool[,] _walkable;
        private int[,]  _zLevel;
        private readonly Dictionary<Vector2Int, object> _occupants = new Dictionary<Vector2Int, object>();

        public int Width  => _w;
        public int Height => _h;

        public void Configure(int w, int h)
        {
            _w = w;
            _h = h;
            _walkable = new bool[w, h];
            _zLevel   = new int[w, h];
            _occupants.Clear();
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    _walkable[x, y] = true;
                    _zLevel[x, y]   = 0;
                }
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < _w && y < _h;

        // ── Walkable (mapa/void/sólido) ──
        public void SetWalkable(int x, int y, bool v)
        {
            if (!InBounds(x, y)) return;
            _walkable[x, y] = v;
        }

        public bool IsWalkable(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            return _walkable[x, y];
        }

        // ── z-level (altura efetiva) ──
        public void SetZLevel(int x, int y, int z)
        {
            if (!InBounds(x, y)) return;
            _zLevel[x, y] = z;
        }

        public int GetZLevel(int x, int y)
        {
            if (!InBounds(x, y)) return 0;
            return _zLevel[x, y];
        }

        // Δ de altura entre duas células (usado pelo pathfinding em z-level jogável, Parte B)
        public int HeightDelta(int x0, int y0, int x1, int y1)
        {
            return Mathf.Abs(GetZLevel(x0, y0) - GetZLevel(x1, y1));
        }

        // ── Ocupação (unidades/estruturas) — registry O(1) ──
        /// <summary>Marca todas as células do footprint como ocupadas por <paramref name="who"/>.</summary>
        public void Occupy(Vector2Int anchor, int footprint, object who)
        {
            int s = Mathf.Max(1, footprint);
            for (int dx = 0; dx < s; dx++)
                for (int dy = 0; dy < s; dy++)
                {
                    var c = new Vector2Int(anchor.x + dx, anchor.y + dy);
                    if (InBounds(c.x, c.y)) _occupants[c] = who;
                }
        }

        /// <summary>Libera todas as células do footprint (independente de quem ocupava).</summary>
        public void Release(Vector2Int anchor, int footprint)
        {
            int s = Mathf.Max(1, footprint);
            for (int dx = 0; dx < s; dx++)
                for (int dy = 0; dy < s; dy++)
                {
                    var c = new Vector2Int(anchor.x + dx, anchor.y + dy);
                    if (InBounds(c.x, c.y)) _occupants.Remove(c);
                }
        }

        /// <summary>Move a ocupação de um footprint para outro (re-registro atômico).</summary>
        public void Move(Vector2Int from, Vector2Int to, int footprint, object who)
        {
            Release(from, footprint);
            Occupy(to, footprint, who);
        }

        public object OccupantAt(int x, int y)
        {
            if (!InBounds(x, y)) return null;
            _occupants.TryGetValue(new Vector2Int(x, y), out var o);
            return o;
        }

        public bool IsOccupied(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            return _occupants.ContainsKey(new Vector2Int(x, y));
        }

        // True se QUALQUER footprint do tamanho size em (anchor) está livre de ocupantes
        // (exceto <paramref name="ignore"/>). O(1) por célula — substitui FindObjectsOfType.
        public bool IsFootprintFree(Vector2Int anchor, int size, object ignore = null)
        {
            int s = Mathf.Max(1, size);
            for (int dx = 0; dx < s; dx++)
                for (int dy = 0; dy < s; dy++)
                {
                    var c = new Vector2Int(anchor.x + dx, anchor.y + dy);
                    if (!InBounds(c.x, c.y)) return false;
                    if (_occupants.TryGetValue(c, out var o) && !ReferenceEquals(o, ignore))
                        return false;
                }
            return true;
        }

        // ── Reconstrói walkable/zLevel a partir do estado atual do GridManager ──
        public void SyncFrom(GridManager grid)
        {
            if (grid == null) return;
            Configure(grid.width, grid.height);
            for (int x = 0; x < grid.width; x++)
                for (int y = 0; y < grid.height; y++)
                {
                    // void → não caminhável
                    _walkable[x, y] = !grid.IsVoid(x, y);
                    // zLevel inicial = altura do tile (estruturas somam em AddStructure, Parte D)
                    _zLevel[x, y] = grid.GetHeight(x, y);
                }
        }

        // ── Estruturas (Parte D estende) ──
        // Marca uma célula como sólida (pisável=false) e soma z. Stub agora; a ferramenta
        // de estrutura do sandbox chamará isso.
        public void AddStructure(int x, int y, int structureZ, bool solid = true)
        {
            if (!InBounds(x, y)) return;
            _zLevel[x, y] += structureZ;
            if (solid) _walkable[x, y] = false;
        }
    }
}
