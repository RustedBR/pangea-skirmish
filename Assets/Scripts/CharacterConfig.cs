namespace PangeaSkirmish
{
    /// <summary>
    /// Configurações de personagem que não vivem no GameTuning.
    /// Os STATUS INICIAIS das entidades agora ficam no GameTuning
    /// (blocos guerreiro / ladino / goblin) — editáveis no Inspector e no menu principal.
    /// </summary>
    public static class CharacterConfig
    {
        // Limites do editor de atributos — agora vêm do GameTuning (attributeMin/attributeMax).
        public static int AttrMin => Tuning.Get().attributeMin;
        public static int AttrMax => Tuning.Get().attributeMax;

        // Ajuste de direção dos sprites (8 direções isométricas).
        public static int DirectionBias = 0;
    }
}
