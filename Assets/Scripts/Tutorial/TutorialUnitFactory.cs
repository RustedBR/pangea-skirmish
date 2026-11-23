using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Elenco do tutorial (GDD-027): todos com 1 em todos os atributos, para a matemática
    /// ficar legível. O jogador é sempre 3×3; os manequins vêm nos tamanhos 1×1, 2×2, 3×3
    /// e 4×4. Manequins são desarmados e "tutorialSafe" (nunca morrem) — o objetivo é
    /// ensinar, não vencer.
    /// </summary>
    public static class TutorialUnitFactory
    {
        /// <summary>Dono fictício dos manequins: NÃO é BeastAI, então eles nunca planejam sozinhos.</summary>
        public const ulong DummyOwnerId = ulong.MaxValue - 1;

        public static Unit CreatePlayer(GridManager grid, Vector2Int anchor)
        {
            var tuning = Tuning.Get();
            return Create("Herói do Tutorial", Team.Player, anchor, tuning.playerTeamColor, grid,
                new UnitStatBlock
                {
                    STR = 1, VIT = 1, DEX = 1, AGI = 1, INT = 1, WIS = 1,
                    Footprint = AttributeStats.DefaultFootprint, AttackRange = 1,
                },
                isPlayerChar: true, weaponId: "Hatchet", ownerId: 0, definitionId: "");
        }

        public static Unit CreateDummy(GridManager grid, Vector2Int anchor, int footprint)
        {
            var tuning = Tuning.Get();
            return Create($"Manequim {footprint}×{footprint}", Team.Enemy, anchor, tuning.enemyTeamColor, grid,
                new UnitStatBlock
                {
                    STR = 1, VIT = 1, DEX = 1, AGI = 1, INT = 1, WIS = 1,
                    Footprint = Mathf.Clamp(footprint, 1, 4), AttackRange = 1,
                },
                isPlayerChar: false, weaponId: "", ownerId: DummyOwnerId, definitionId: "monster");
        }

        public static Unit CreateAlly(GridManager grid, Vector2Int anchor)
        {
            var tuning = Tuning.Get();
            return Create("Aliado de Treino", Team.Player, anchor, tuning.playerTeamColor, grid,
                new UnitStatBlock
                {
                    STR = 1, VIT = 1, DEX = 1, AGI = 1, INT = 1, WIS = 1,
                    Footprint = 1, AttackRange = 1,
                },
                isPlayerChar: false, weaponId: "", ownerId: 0, definitionId: "");
        }

        private static Unit Create(string name, Team team, Vector2Int anchor, Color color,
            GridManager grid, UnitStatBlock block, bool isPlayerChar, string weaponId,
            ulong ownerId, string definitionId)
        {
            var go = new GameObject(name);
            var u = go.AddComponent<Unit>();
            u.unitName = name;
            u.team = team;
            u.ownerId = ownerId;
            u.isPlayerCharacter = isPlayerChar;
            u.weaponId = weaponId;
            u.definitionId = definitionId;
            u.tutorialSafe = true;   // elenco de treino: HP nunca chega a 0
            u.stats = block.ToAttributeStats();
            u.Init(grid, anchor, color, CharacterSpriteCatalog.Default);
            return u;
        }
    }
}
