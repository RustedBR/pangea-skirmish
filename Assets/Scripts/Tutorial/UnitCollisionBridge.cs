using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Ponte de colisão real (GDD-028 fase 2): registra a ocupação das unidades no CollisionGrid
    /// do GridManager. Aditivo — chamado pelo director/round em pontos seguros, sem espalhar
    /// código no ciclo de vida da Unit (que é compartilhado com MP/sandbox).
    /// Substitui o FindObjectsOfType de FootprintsOverlapAny por consulta O(1) ao registry.
    /// </summary>
    public static class UnitCollisionBridge
    {
        /// <summary>Re-registra TODAS as unidades vivas no CollisionGrid do grid.</summary>
        public static void SyncAll(GridManager grid)
        {
            if (grid == null || grid.Collision == null) return;
            // CollisionGrid.SyncFrom recria walkable/zLevel (não mexe nos occupants além de Configure).
            grid.Collision.SyncFrom(grid);
            // Tracked (não AllUnits/_byId): inclui unidades do Sandbox/editor e testes,
            // que nunca passam pelo PlacementSync de rede (bug pego por CollisionTests).
            foreach (var u in UnitRegistry.Tracked)
            {
                if (u == null || u.IsDead) continue;
                if (u.Grid != null && u.Grid != grid) continue;
                grid.Collision.Occupy(u.anchor, u.stats.Footprint, u);
            }
            // Muralhas (Barreira) também são ocupantes reais — bloqueiam passagem e projéteis.
            var field = SpellField.Current;
            if (field != null)
                foreach (var z in field.Zones)
                    if (z.Kind == SpellEffect.Barrier)
                        foreach (var c in z.Cells)
                            grid.Collision.Occupy(c, 1, z);
        }

        /// <summary>True se um anchor de footprint está livre para mover (exceto a própria unidade).</summary>
        public static bool CanOccupy(GridManager grid, Vector2Int anchor, int footprint, Unit self)
        {
            if (grid == null || grid.Collision == null) return false;
            return grid.Collision.IsWalkableArea(anchor, footprint) &&
                   grid.Collision.IsFootprintFree(anchor, footprint, self);
        }
    }
}