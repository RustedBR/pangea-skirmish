using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    public enum TileEffectKind { Fire, Water, Wind, ManaOrb }

    [Serializable]
    public class TileEffect
    {
        public TileEffectKind Kind;
        public Vector2Int Cell;
        public int Potency;
        public Vector2Int Direction;
        public int RoundsLeft;
        public string PreviousTerrainName;

        [NonSerialized] public SpriteRenderer Overlay;

        public bool IsExpired => RoundsLeft == 0;
    }

    public class TileEffectManager : MonoBehaviour
    {
        private GridManager _grid;
        private Camera _cam;
        private readonly Dictionary<Vector2Int, TileEffect> _effects = new Dictionary<Vector2Int, TileEffect>();
        private Transform _overlayRoot;

        public void Setup(GridManager grid, Camera cam)
        {
            _grid = grid;
            _cam = cam;
            _overlayRoot = new GameObject("TileOverlays").transform;
            _overlayRoot.SetParent(transform);
        }

        public TileEffect GetAt(Vector2Int cell)
        {
            _effects.TryGetValue(cell, out var fx);
            return fx;
        }

        public void Apply(TileEffectKind kind, Vector2Int cell, int potency, Vector2Int direction, int casterFootprint)
        {
            var existing = GetAt(cell);
            if (existing != null)
            {
                if (kind == TileEffectKind.Fire && existing.Kind == TileEffectKind.Fire)
                {
                    existing.Potency += potency;
                    existing.RoundsLeft = Mathf.Max(existing.RoundsLeft, Tuning.Get().fireTileDurationRounds);
                    return;
                }
                if (kind == TileEffectKind.Water && existing.Kind == TileEffectKind.Fire)
                {
                    RemoveAt(cell);
                    return;
                }
                if (kind == TileEffectKind.Fire && existing.Kind == TileEffectKind.Water)
                {
                    RemoveAt(cell);
                    return;
                }
                RemoveAt(cell);
            }

            var fx = new TileEffect
            {
                Kind = kind,
                Cell = cell,
                Potency = potency,
                Direction = direction,
                RoundsLeft = kind == TileEffectKind.Fire ? Tuning.Get().fireTileDurationRounds
                          : kind == TileEffectKind.Wind ? Tuning.Get().windTileDurationRounds
                          : kind == TileEffectKind.ManaOrb ? -1 : 1,
                PreviousTerrainName = _grid != null && cell.x >= 0 && cell.x < _grid.width && cell.y >= 0 && cell.y < _grid.height
                    ? _grid.GetTerrainName(cell.x, cell.y) : "",
            };

            if (kind == TileEffectKind.Water)
            {
                _grid.SetCell(cell.x, cell.y, Tuning.Get().waterTileName, 0, "", false);
            }

            _effects[cell] = fx;
            CreateOverlay(fx);
        }

        private void RemoveAt(Vector2Int cell)
        {
            if (_effects.TryGetValue(cell, out var fx))
            {
                if (fx.Overlay != null) Destroy(fx.Overlay.gameObject);
                _effects.Remove(cell);
            }
        }

        private void CreateOverlay(TileEffect fx)
        {
            var go = new GameObject($"Overlay_{fx.Cell.x}_{fx.Cell.y}");
            go.transform.SetParent(_overlayRoot);
            go.transform.position = _grid.AnchorToWorldCenter(fx.Cell, 1);
            // Migração XY→XZ (2026-07-20): overlay de tile é topo → deita no XZ.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BuildFallbackTileSprite(fx.Kind);
            sr.sortingOrder = (fx.Cell.x + fx.Cell.y) * 4 + 3;
            sr.color = new Color(1f, 1f, 1f, Tuning.Get().tileEffectOverlayAlpha);
            fx.Overlay = sr;
        }

        private static Sprite _sharedFireOverlay, _sharedWaterOverlay, _sharedWindOverlay, _sharedOrbOverlay;

        private static Sprite BuildFallbackTileSprite(TileEffectKind kind)
        {
            if (_sharedFireOverlay == null) _sharedFireOverlay = MakeColoredCircle(new Color(1f, 0.3f, 0f));
            if (_sharedWaterOverlay == null) _sharedWaterOverlay = MakeColoredCircle(new Color(0.1f, 0.4f, 1f));
            if (_sharedWindOverlay == null) _sharedWindOverlay = MakeColoredCircle(new Color(0.7f, 0.9f, 1f));
            if (_sharedOrbOverlay == null) _sharedOrbOverlay = MakeColoredCircle(new Color(0.5f, 0f, 1f));
            switch (kind)
            {
                case TileEffectKind.Fire: return _sharedFireOverlay;
                case TileEffectKind.Water: return _sharedWaterOverlay;
                case TileEffectKind.Wind: return _sharedWindOverlay;
                case TileEffectKind.ManaOrb: return _sharedOrbOverlay;
                default: return _sharedFireOverlay;
            }
        }

        private static Sprite MakeColoredCircle(Color c)
        {
            int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            float cx = size / 2f, cy = size / 2f, r = size / 2f - 1;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - cx), dy = Mathf.Abs(y - cy);
                bool inside = (dx * dx + dy * dy) <= r * r;
                tex.SetPixel(x, y, inside ? c : Color.clear);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16);
        }

        public void InjectMana(Vector2Int cell, int amount)
        {
            var existing = GetAt(cell);
            if (existing != null)
            {
                existing.Potency += amount;
                return;
            }
            Apply(TileEffectKind.ManaOrb, cell, amount, Vector2Int.zero, 0);
        }

        public IEnumerator OnUnitMoved(Unit u, Vector2Int fromAnchor, List<string> log)
        {
            var path = GetCellsBetween(fromAnchor, u.anchor);
            bool tookFireDamage = false;
            foreach (var cell in path)
            {
                var fx = GetAt(cell);
                if (fx == null) continue;
                if (fx.Kind == TileEffectKind.Fire && !tookFireDamage)
                {
                    int dmg = Mathf.RoundToInt(fx.Potency * Tuning.Get().fireTileDamageFactor);
                    if (dmg > 0)
                    {
                        bool dead = u.currentHP <= dmg;
                        u.TakeDamage(dmg);
                        tookFireDamage = true;
                        if (log != null) log.Add($"{u.unitName} sofreu {dmg} de dano de fogo em {cell}");
                        if (!u.IsDead) continue;
                        break;
                    }
                }
                if (fx.Kind == TileEffectKind.ManaOrb && cell == u.anchor)
                {
                    u.currentMana = Mathf.Min(u.currentMana + fx.Potency, u.stats.MaxMana);
                    if (log != null) log.Add($"{u.unitName} pegou orbe de mana +{fx.Potency}");
                    RemoveAt(cell);
                }
            }

            var finalFx = GetAt(u.anchor);
            if (finalFx != null && finalFx.Kind == TileEffectKind.Wind)
            {
                int pushTiles = Tuning.Get().windPushTiles;
                Vector2Int pushDest = u.anchor + finalFx.Direction * pushTiles;
                var footprint = u.stats.Footprint;
                if (_grid.IsAnchorInBounds(pushDest, footprint) && !_grid.IsVoid(pushDest.x, pushDest.y))
                {
                    u.anchor = pushDest;
                    u.SnapToAnchor();
                    if (log != null) log.Add($"{u.unitName} empurrado pelo vento para {pushDest}");
                }
            }

            yield break;
        }

        public IEnumerator EndOfRoundTick(List<Unit> units, List<string> log)
        {
            foreach (var fx in new List<TileEffect>(_effects.Values))
            {
                if (fx.Kind == TileEffectKind.Fire)
                {
                    foreach (var u in units)
                    {
                        if (u.IsDead) continue;
                        if (GridManager.FootprintsOverlap(u.anchor, u.stats.Footprint, fx.Cell, 1))
                        {
                            int dmg = Mathf.RoundToInt(fx.Potency * Tuning.Get().fireTileDamageFactor);
                            u.TakeDamage(dmg);
                            if (log != null) log.Add($"{u.unitName} sofreu {dmg} de dano de fogo");
                        }
                    }
                }

                if (fx.RoundsLeft > 0) fx.RoundsLeft--;
                if (fx.RoundsLeft == 0 && fx.Kind != TileEffectKind.ManaOrb)
                {
                    if (!string.IsNullOrEmpty(fx.PreviousTerrainName) && fx.Kind == TileEffectKind.Water)
                    {
                        _grid.SetCell(fx.Cell.x, fx.Cell.y, fx.PreviousTerrainName, 0, "", false);
                    }
                    RemoveAt(fx.Cell);
                }
            }
            yield break;
        }

        public void RemapCells(Func<Vector2Int, Vector2Int> map)
        {
            var remapped = new Dictionary<Vector2Int, TileEffect>();
            foreach (var kv in _effects)
            {
                var newCell = map(kv.Key);
                if (newCell.x >= 0)
                {
                    kv.Value.Cell = newCell;
                    remapped[newCell] = kv.Value;
                    if (kv.Value.Overlay != null)
                        kv.Value.Overlay.transform.position = _grid.AnchorToWorldCenter(newCell, 1);
                }
                else
                {
                    if (kv.Value.Overlay != null) Destroy(kv.Value.Overlay.gameObject);
                }
            }
            _effects.Clear();
            foreach (var kv in remapped) _effects[kv.Key] = kv.Value;
        }

        public void ClearAll()
        {
            foreach (var fx in _effects.Values)
            {
                if (fx.Overlay != null) Destroy(fx.Overlay.gameObject);
            }
            _effects.Clear();
            if (_overlayRoot != null)
            {
                for (int i = _overlayRoot.childCount - 1; i >= 0; i--)
                    Destroy(_overlayRoot.GetChild(i).gameObject);
            }
        }

        private List<Vector2Int> GetCellsBetween(Vector2Int from, Vector2Int to)
        {
            var cells = new List<Vector2Int>();
            int dx = Mathf.Abs(to.x - from.x), dy = Mathf.Abs(to.y - from.y);
            int sx = from.x < to.x ? 1 : -1, sy = from.y < to.y ? 1 : -1;
            int err = dx - dy;
            int x = from.x, y = from.y;
            while (true)
            {
                if (x != from.x || y != from.y) cells.Add(new Vector2Int(x, y));
                if (x == to.x && y == to.y) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }
            return cells;
        }
    }
}