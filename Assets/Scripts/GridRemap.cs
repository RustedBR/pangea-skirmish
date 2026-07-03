using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    public static class GridRemap
    {
        public static void InsertLineAndRemap(
            GridManager grid, bool column, int index, int copyFrom,
            MapData newMap, List<string> log, TileEffectManager tileFx)
        {
            var oldUnits = Object.FindObjectsOfType<Unit>();
            foreach (var u in oldUnits)
            {
                Vector2Int a = u.anchor;
                if (column)
                {
                    if (a.x >= index) a.x += 1;
                }
                else
                {
                    if (a.y >= index) a.y += 1;
                }
                u.anchor = a;
            }

            grid.RebuildFromMap(newMap);

            if (tileFx != null)
            {
                tileFx.RemapCells(old =>
                {
                    if (column)
                        return old.x >= index ? new Vector2Int(old.x + 1, old.y) : old;
                    else
                        return old.y >= index ? new Vector2Int(old.x, old.y + 1) : old;
                });
            }

            string msg = $"Grid expandido {(column ? "col" : "linha")} {index}";
            if (log != null) log.Add(msg);
        }

        public static bool UnitAffectedByRemap(Vector2Int anchor, bool column, int index)
        {
            return column ? anchor.x >= index : anchor.y >= index;
        }
    }
}