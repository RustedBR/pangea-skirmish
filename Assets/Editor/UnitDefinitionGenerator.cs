#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace PangeaSkirmish.Editor
{
    /// <summary>
    /// Editor script to generate example UnitDefinition ScriptableObjects.
    /// Run via menu: Pangea Skirmish > Generate Unit Definitions
    /// </summary>
    public static class UnitDefinitionGenerator
    {
        [MenuItem("Pangea Skirmish/Generate Unit Definitions")]
        public static void GenerateUnitDefinitions()
        {
            string folder = "Assets/Resources/Units";

            // Create Warrior
            CreateUnitDefinition(
                folder: folder,
                unitId: "warrior",
                displayName: "Guerreiro",
                description: "Unidade corpo a corpo equilibrada. Alta vitalidade e dano físico.",
                stats: new UnitStatBlock
                {
                    STR = 5f,
                    VIT = 4f,
                    DEX = 3f,
                    AGI = 2f,
                    INT = 1f,
                    WIS = 2f,
                    Footprint = 3,
                    AttackRange = 1
                },
                defaultWeaponId: "Hatchet",
                aiAggression: 0.8f,
                aiAttackPreference: 0.6f,
                aiIntelligence: 0.5f,
                aiSurvivalInstinct: 0.4f
            );

            // Create Archer
            CreateUnitDefinition(
                folder: folder,
                unitId: "archer",
                displayName: "Arqueiro",
                description: "Unidade de longo alcance. Alto AGI e DEX para esquiva e críticos.",
                stats: new UnitStatBlock
                {
                    STR = 2f,
                    VIT = 2f,
                    DEX = 5f,
                    AGI = 4f,
                    INT = 2f,
                    WIS = 2f,
                    Footprint = 2,
                    AttackRange = 4
                },
                defaultWeaponId: "ShortBow",
                aiAggression: 0.6f,
                aiAttackPreference: 0.7f,
                aiIntelligence: 0.7f,
                aiSurvivalInstinct: 0.7f
            );

            // Create Mage
            CreateUnitDefinition(
                folder: folder,
                unitId: "mage",
                displayName: "Mago",
                description: "Unidade mágica de suporte. Alto INT/WIS para magias poderosas.",
                stats: new UnitStatBlock
                {
                    STR = 1f,
                    VIT = 2f,
                    DEX = 2f,
                    AGI = 2f,
                    INT = 5f,
                    WIS = 4f,
                    Footprint = 2,
                    AttackRange = 3
                },
                defaultWeaponId: "WoodenStaff",
                aiAggression: 0.4f,
                aiAttackPreference: 0.3f,
                aiIntelligence: 0.9f,
                aiSurvivalInstinct: 0.8f,
                aiUseSpells: true
            );

            // Create Tank
            CreateUnitDefinition(
                folder: folder,
                unitId: "tank",
                displayName: "Tanque",
                description: "Unidade defensiva com alta vitalidade. Forte contra ataques físicos.",
                stats: new UnitStatBlock
                {
                    STR = 3f,
                    VIT = 6f,
                    DEX = 2f,
                    AGI = 1f,
                    INT = 1f,
                    WIS = 2f,
                    Footprint = 4,
                    AttackRange = 1
                },
                defaultWeaponId: "WoodenShield",
                aiAggression: 0.3f,
                aiAttackPreference: 0.4f,
                aiIntelligence: 0.4f,
                aiSurvivalInstinct: 0.2f
            );

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Unit definitions generated successfully!");
        }

        private static void CreateUnitDefinition(
            string folder,
            string unitId,
            string displayName,
            string description,
            UnitStatBlock stats,
            string defaultWeaponId,
            float aiAggression,
            float aiAttackPreference,
            float aiIntelligence,
            float aiSurvivalInstinct,
            bool aiUseSpells = false)
        {
            string assetPath = $"{folder}/{unitId}.asset";

            var def = ScriptableObject.CreateInstance<UnitDefinition>();
            def.unitId = unitId;
            def.displayName = displayName;
            def.description = description;
            def.baseStats = stats;
            def.defaultWeaponId = defaultWeaponId;
            def.aiAggression = aiAggression;
            def.aiAttackPreference = aiAttackPreference;
            def.aiIntelligence = aiIntelligence;
            def.aiSurvivalInstinct = aiSurvivalInstinct;
            def.aiUseSpells = aiUseSpells;

            AssetDatabase.CreateAsset(def, assetPath);
        }
    }
}
#endif
