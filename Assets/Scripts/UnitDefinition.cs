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

        [Header("Comportamento de IA")]
        [Range(0f, 1f)]
        public float aiAggression = 0.7f;
        [Range(0f, 1f)]
        public float aiAttackPreference = 0.5f;
        [Range(0f, 1f)]
        public float aiIntelligence = 0.8f;
        [Range(0f, 1f)]
        public float aiSurvivalInstinct = 0.6f;
        public bool aiUseSpells = false;

        /// <summary>
        /// Cria uma instância de Unit a partir desta definição.
        /// </summary>
        public Unit SpawnUnit(GridManager grid, Vector2Int anchor, Team team)
        {
            var go = new GameObject(unitId);
            go.transform.position = grid.CellToWorld(anchor);

            var unit = go.AddComponent<Unit>();
            unit.unitName = displayName;
            unit.team = team;
            unit.isPlayerCharacter = (team == Team.Player);
            unit.weaponId = defaultWeaponId;
            unit.stats = baseStats.ToAttributeStats();

            // Configurar IA para inimigos
            if (team == Team.Enemy)
            {
                unit.aiAggression = aiAggression;
                unit.aiAttackPreference = aiAttackPreference;
                unit.aiIntelligence = aiIntelligence;
                unit.aiSurvivalInstinct = aiSurvivalInstinct;
                unit.aiUseSpells = aiUseSpells;
            }

            return unit;
        }
    }
}
