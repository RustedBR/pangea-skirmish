using UnityEngine;
using PangeaSkirmish.UI;

namespace PangeaSkirmish
{
    /// <summary>
    /// Editor de personagem do tutorial (Cap. 8, lições 8.3–8.5): abre o CharCreationHUD REAL por cima da
    /// batalha, com um herói-sandbox de nível 5 (35 pontos, 30 gastos). Nada vai para o PlayerProfile:
    /// ao salvar, os atributos novos valem só para o herói da batalha de tutorial.
    /// </summary>
    public static class TutorialHeroEditor
    {
        public static CharCreationHUD Editor { get; private set; }
        /// <summary>Herói-sandbox em memória (nível 5, XP 80/100). Atualizado a cada Salvar.</summary>
        public static CharacterPreset Hero { get; private set; }
        /// <summary>Resultado do último Salvar (null até salvar).</summary>
        public static CharacterPreset Saved { get; private set; }
        public static bool Confirmed { get; private set; }

        public const int SandboxLevel = 5, SandboxXp = 80, BasePoints = 5;

        /// <summary>Lições que mantêm o editor aberto.</summary>
        public static bool IsEditorLesson(string id) => id == "budget" || id == "point" || id == "save";

        public static int Sum(CharacterPreset p) => p == null ? 0 : (int)(p.stats.STR + p.stats.VIT + p.stats.DEX + p.stats.AGI + p.stats.INT + p.stats.WIS);

        /// <summary>Monta o herói-sandbox a partir do herói da batalha (atributos e equipamento atuais).</summary>
        public static void ResetHero(Unit u)
        {
            Close();
            Saved = null; Confirmed = false;
            Hero = new CharacterPreset
            {
                presetName = u != null ? u.unitName : "Herói do Tutorial",
                spritePath = CharacterSpriteCatalog.Default,
                weaponId = u != null && !string.IsNullOrEmpty(u.weaponId) ? u.weaponId : "Hatchet",
                offhandId = u != null ? u.offhandId ?? "" : "", headId = u != null ? u.headId ?? "" : "",
                torsoId = u != null ? u.torsoId ?? "" : "", legsId = u != null ? u.legsId ?? "" : "",
                modelId = "",
                level = SandboxLevel, xp = SandboxXp,
                stats = new UnitStatBlock
                {
                    // Base fixa de 30 pontos (5 em cada): não herda o que um Salvar anterior deixou no herói da luta,
                    // então Repetir/escolher 8.4 ou 8.5 depois de salvar sempre tem 5 pontos para gastar.
                    STR = BasePoints, VIT = BasePoints, DEX = BasePoints, AGI = BasePoints, INT = BasePoints, WIS = BasePoints,
                    Footprint = 1, AttackRange = u != null ? u.stats.AttackRange : 1,
                },
            };
        }

        public static void ClearConfirmed() { Confirmed = false; Saved = null; }

        /// <summary>Abre o editor real (idempotente). Aberto em nível 5, ele só deixa somar pontos.</summary>
        public static void Open(TutorialDirector d)
        {
            if (Editor != null) return;
            ResetHero(d != null ? d.PlayerUnit : null);   // cada abertura nova parte do herói-base (30 de 35 pontos)
            var go = new GameObject("TutorialCharEditor");
            var ed = go.AddComponent<CharCreationHUD>();
            ed.OnConfirmed = p => { OnSaved(d, p); };
            ed.OnCancelled = () => Close();
            ed.Edit(Hero);
            if (ed.Document != null) ed.Document.sortingOrder = 8500;   // abaixo do overlay do tutorial (9000)
            Editor = ed;
            TutorialHud.EditorOpen = true;
        }

        public static void Close()
        {
            if (Editor != null) Object.Destroy(Editor.gameObject);
            Editor = null;
            TutorialHud.EditorOpen = false;
        }

        private static void OnSaved(TutorialDirector d, CharacterPreset p)
        {
            Saved = p; Confirmed = true;
            Hero = PlayerProfile.Clone(p);
            var u = d != null ? d.PlayerUnit : null;
            if (u != null)
            {
                u.stats.STR = p.stats.STR; u.stats.VIT = p.stats.VIT; u.stats.DEX = p.stats.DEX;
                u.stats.AGI = p.stats.AGI; u.stats.INT = p.stats.INT; u.stats.WIS = p.stats.WIS;
                u.currentHP = u.stats.MaxHP; u.currentMana = u.stats.MaxMana; u.currentStamina = u.stats.MaxStamina;
            }
            Close();
        }
    }
}
