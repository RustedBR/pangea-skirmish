using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Save/Load system for persisting game state.
    /// Supports multiple save slots and auto-save.
    /// </summary>
    public static class SaveSystem
    {
        private const string SAVE_FOLDER = "Saves";
        private const string AUTO_SAVE_NAME = "autosave";
        private const int MAX_SAVE_SLOTS = 3;

        public static string SavePath => Path.Combine(Application.persistentDataPath, SAVE_FOLDER);

        /// <summary>
        /// Current game state that can be saved/loaded.
        /// </summary>
        [Serializable]
        public class GameState
        {
            public string saveName;
            public string timestamp;
            public int currentRound;
            public int currentTeamIndex;
            public bool isPlayerTurn;

            public List<UnitState> units = new List<UnitState>();
            public GridState grid = new GridState();
        }

        [Serializable]
        public class UnitState
        {
            public string unitId;
            public string unitName;
            public Team team;
            public Vector2Int anchor;
            public int currentHP;
            public int currentMana;
            public int remainingAP;
            public int remainingBAP;
            public List<StatusEffectState> statusEffects = new List<StatusEffectState>();
        }

        [Serializable]
        public class StatusEffectState
        {
            public string effectId;
            public int remainingRounds;
        }

        [Serializable]
        public class GridState
        {
            public int width;
            public int height;
            public List<TileState> tiles = new List<TileState>();
        }

        [Serializable]
        public class TileState
        {
            public Vector2Int position;
            public int tileId;
            public int height;
        }

        /// <summary>
        /// Save game state to a file.
        /// </summary>
        public static bool Save(GameState state, string saveName = null)
        {
            try
            {
                Directory.CreateDirectory(SavePath);

                if (string.IsNullOrEmpty(saveName))
                    saveName = AUTO_SAVE_NAME;

                state.saveName = saveName;
                state.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                string json = JsonUtility.ToJson(state, true);
                string filePath = Path.Combine(SavePath, $"{saveName}.json");

                File.WriteAllText(filePath, json);
                Debug.Log($"Game saved to: {filePath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save game: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load game state from a file.
        /// </summary>
        public static GameState Load(string saveName)
        {
            try
            {
                string filePath = Path.Combine(SavePath, $"{saveName}.json");

                if (!File.Exists(filePath))
                {
                    Debug.LogWarning($"Save file not found: {filePath}");
                    return null;
                }

                string json = File.ReadAllText(filePath);
                GameState state = JsonUtility.FromJson<GameState>(json);
                Debug.Log($"Game loaded from: {filePath}");
                return state;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load game: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Auto-save the current game state.
        /// </summary>
        public static bool AutoSave(GameState state)
        {
            return Save(state, AUTO_SAVE_NAME);
        }

        /// <summary>
        /// Delete a save file.
        /// </summary>
        public static bool Delete(string saveName)
        {
            try
            {
                string filePath = Path.Combine(SavePath, $"{saveName}.json");

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Debug.Log($"Save deleted: {filePath}");
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to delete save: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// List all available save files.
        /// </summary>
        public static string[] ListSaves()
        {
            try
            {
                if (!Directory.Exists(SavePath))
                    return new string[0];

                string[] files = Directory.GetFiles(SavePath, "*.json");
                var saves = new List<string>();

                foreach (string file in files)
                {
                    saves.Add(Path.GetFileNameWithoutExtension(file));
                }

                return saves.ToArray();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to list saves: {e.Message}");
                return new string[0];
            }
        }

        /// <summary>
        /// Check if a save file exists.
        /// </summary>
        public static bool SaveExists(string saveName)
        {
            string filePath = Path.Combine(SavePath, $"{saveName}.json");
            return File.Exists(filePath);
        }

        /// <summary>
        /// Create a GameState from the current scene.
        /// </summary>
        public static GameState CaptureGameState()
        {
            var state = new GameState();

            // Capture units
            var units = UnityEngine.Object.FindObjectsOfType<Unit>();
            foreach (var unit in units)
            {
                var unitState = new UnitState
                {
                    unitId = !string.IsNullOrEmpty(unit.definitionId) ? unit.definitionId : unit.unitName,
                    unitName = unit.unitName,
                    team = unit.team,
                    anchor = unit.anchor,
                    currentHP = unit.currentHP,
                    currentMana = unit.currentMana,
                    remainingAP = unit.remainingAP,
                    remainingBAP = unit.remainingBAP
                };

                // Capture status effects
                foreach (var effect in unit.statusEffects)
                {
                    unitState.statusEffects.Add(new StatusEffectState
                    {
                        effectId = effect.Kind.ToString(),
                        remainingRounds = effect.RoundsLeft
                    });
                }

                state.units.Add(unitState);
            }

            // Capture grid
            var grid = UnityEngine.Object.FindObjectOfType<GridManager>();
            if (grid != null)
            {
                state.grid.width = grid.width;
                state.grid.height = grid.height;
                // Captura todos os tiles (índice + altura) para reconstruir o terreno
                state.grid.tiles.Clear();
                for (int y = 0; y < grid.height; y++)
                {
                    for (int x = 0; x < grid.width; x++)
                    {
                        state.grid.tiles.Add(new TileState
                        {
                            position = new Vector2Int(x, y),
                            tileId   = grid.GetTileIndex(x, y),
                            height   = grid.GetHeight(x, y)
                        });
                    }
                }
            }

            return state;
        }

        /// <summary>
        /// Restore game state to the scene.
        /// </summary>
        public static void RestoreGameState(GameState state)
        {
            if (state == null)
            {
                Debug.LogError("Cannot restore null game state");
                return;
            }

            // Clear existing units
            var existingUnits = UnityEngine.Object.FindObjectsOfType<Unit>();
            foreach (var unit in existingUnits)
            {
                UnityEngine.Object.Destroy(unit.gameObject);
            }

            // Restore units
            var grid = UnityEngine.Object.FindObjectOfType<GridManager>();
            if (grid == null)
            {
                Debug.LogError("GridManager not found");
                return;
            }

            // Restore grid tiles
            if (grid != null && state.grid.tiles.Count > 0)
            {
                grid.width = state.grid.width;
                grid.height = state.grid.height;
                foreach (var tile in state.grid.tiles)
                {
                    grid.SetCell(tile.position.x, tile.position.y, tile.tileId, tile.height);
                }
            }

            // Restore units
            foreach (var unitState in state.units)
            {
                Unit unit;
                var def = UnitDefinitionRegistry.Get(unitState.unitId);
                if (def != null)
                {
                    // Spawn correto a partir da definição (stats, arma, IA, visual)
                    unit = def.SpawnUnit(grid, unitState.anchor, unitState.team);
                }
                else
                {
                    Debug.LogWarning($"[SaveSystem] UnitDefinition '{unitState.unitId}' não encontrada — criando unit básica.");
                    var go = new GameObject(unitState.unitName);
                    go.transform.position = grid.CellToWorld(unitState.anchor);
                    unit = go.AddComponent<Unit>();
                    unit.unitName = unitState.unitName;
                    unit.team = unitState.team;
                }

                unit.anchor = unitState.anchor;
                unit.currentHP = unitState.currentHP;
                unit.currentMana = unitState.currentMana;
                unit.remainingAP = unitState.remainingAP;
                unit.remainingBAP = unitState.remainingBAP;

                // Restaura efeitos de status
                foreach (var se in unitState.statusEffects)
                {
                    if (System.Enum.TryParse<StatusEffectKind>(se.effectId, out var kind))
                    {
                        unit.statusEffects.Add(new StatusEffect
                        {
                            Kind = kind,
                            RoundsLeft = se.remainingRounds
                        });
                    }
                }
            }

            Debug.Log($"Game state restored: {state.saveName}");
        }
    }
}
