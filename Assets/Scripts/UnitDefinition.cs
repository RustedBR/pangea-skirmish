using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// ScriptableObject que define um tipo de unidade.
    /// Contém stats iniciais, arma padrão, visual e comportamento de IA.
    /// </summary>
    [CreateAssetMenu(fileName = "NewUnitDefinition", menuName = "Pangea Skirmish/Unit Definition")]
    public class UnitDefinition : ScriptableObject
    {
        [Header("Identificação")]
        public string unitId;
        public string displayName;
        [TextArea(2, 4)]
        public string description;

        [Header("Stats Iniciais")]
        public UnitStatBlock baseStats;

        [Header("Arma Padrão")]
        public string defaultWeaponId;

        [Header("Visual")]
        public string spriteResourcePath;
        public Color teamTint = Color.white;

        /// <summary>
        /// Cria uma instância de Unit a partir desta definição.
        /// </summary>
        public Unit SpawnUnit(GridManager grid, Vector2Int anchor, Team team)
        {
            var go = new GameObject(unitId);
            go.transform.position = grid.CellToWorld(anchor);

            var unit = go.AddComponent<Unit>();
            unit.unitName = displayName;
            unit.definitionId = unitId;
            unit.team = team;
            unit.isPlayerCharacter = (team == Team.Player);
            unit.weaponId = defaultWeaponId;
            unit.stats = baseStats.ToAttributeStats();

            // Init visual (sprite, cor do time, weapon overlay)
            Color teamColor = team == Team.Player
                ? new Color(0.4f, 0.8f, 1f)
                : new Color(1f, 0.4f, 0.4f);
            string spritePath = !string.IsNullOrEmpty(spriteResourcePath)
                ? spriteResourcePath
                : "Sprites/TinyTactics/Characters/fighter";
            unit.Init(grid, anchor, teamColor, spritePath);

            return unit;
        }
    }
}
