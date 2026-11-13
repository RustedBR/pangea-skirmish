using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Retorno imediato do combate acima da cabeça das unidades: números e palavras flutuantes
    /// e o som de cada evento (GDD-019 §9.3). Os resolvedores chamam estes métodos; aqui nada
    /// altera o estado do jogo. Sem câmera (testes de regra, servidor), só conta os eventos.
    /// </summary>
    public static class BattleFeedback
    {
        public static Camera Cam;
        /// <summary>Eventos emitidos desde o último reset, por tipo ("heal", "miss", "block"...). Para testes.</summary>
        public static readonly List<string> Log = new List<string>();
        public static void Reset() => Log.Clear();

        private static readonly Color32 Dark = new Color32(0x05, 0x04, 0x06, 255);

        private static void Pop(Unit u, string kind, string text, Color color, Color32 border, float scale = 1f, float dur = 1.0f, string sfx = null)
        {
            Log.Add(kind);
            if (u == null) return;
            if (sfx != null) AudioManager.I?.PlayNamed(sfx);
            if (Cam == null || !Application.isPlaying) return;
            BattleLabel.CreatePop(Cam, u.HeadWorld, text, color, Dark, border, scale, dur);
        }

        public static void Heal(Unit u, int amount)
        {
            if (amount <= 0) return;
            Log.Add("heal");
            if (u == null) return;
            AudioManager.I?.PlayNamed("sfx_heal");
            if (Cam == null || !Application.isPlaying) return;
            BattleLabel.CreatePixelNumber(Cam, u.HeadWorld, $"{amount}", new Color(0.35f, 1f, 0.40f), 0.55f, 1.1f, 1.45f, 0.55f);
        }

        public static void Miss(Unit target) =>
            Pop(target, "miss", "Errou", new Color(0.80f, 0.80f, 0.84f), new Color32(0x66, 0x66, 0x6E, 255), 0.6f, 0.8f, "sfx_miss");

        public static void Dodge(Unit u) =>
            Pop(u, "dodge", "Esquiva!", new Color(0.55f, 0.85f, 1f), new Color32(0x2A, 0x7A, 0xB8, 255), 0.6f, 0.9f, "sfx_dodge");

        public static void Block(Unit u, int absorbed) =>
            Pop(u, "block", absorbed > 0 ? $"Bloqueou {absorbed}" : "Bloqueio!", new Color(1f, 0.86f, 0.40f),
                new Color32(0xB0, 0x86, 0x18, 255), 0.6f, 1.0f, "sfx_block");

        public static void Mana(Unit u, int delta)
        {
            if (delta == 0) return;
            Pop(u, "mana", (delta > 0 ? "+" : "−") + Mathf.Abs(delta) + " Mana", new Color(0.55f, 0.75f, 1f),
                new Color32(0x2A, 0x50, 0xB8, 255), 0.6f, 1.0f);
        }

        /// <summary>Estado aplicado ou consumido: "Queimando", "Fúria", "Empurrado"...</summary>
        public static void Status(Unit u, string text, Color color) =>
            Pop(u, "status", text, color, new Color32(0x4A, 0x40, 0x66, 255), 0.5f, 1.1f);

        public static void Interrupted(Unit u, int taken, int limit) =>
            Pop(u, "interrupt", $"Interrompido! ({taken} ≥ {limit})", new Color(1f, 0.50f, 0.42f),
                new Color32(0xB8, 0x2E, 0x22, 255), 0.65f, 1.6f, "sfx_interrupt");

        public static void KeptConcentration(Unit u, int taken, int limit) =>
            Pop(u, "kept", $"Manteve a concentração ({taken} < {limit})", new Color(0.80f, 0.74f, 1f),
                new Color32(0x5A, 0x48, 0x99, 255), 0.42f, 1.3f);

        /// <summary>Dano absorvido por algo que não é a unidade (Muralha).</summary>
        public static void Absorbed(Unit near, string what) =>
            Pop(near, "absorbed", what, new Color(0.85f, 0.78f, 0.70f), new Color32(0x6A, 0x5A, 0x4A, 255), 0.5f, 1.0f, "sfx_block");
    }
}
