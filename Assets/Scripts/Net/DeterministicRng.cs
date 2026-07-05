// Net/DeterministicRng.cs
// RNG determinístico baseado em System.Random (seed por round).
// Em SP, semeia automaticamente no primeiro uso (comportamento aleatório preservado).
// Em MP, semeado via Seed(int) pelo LockstepBattleSync antes de cada fase de ação.

using System;

namespace PangeaSkirmish
{
    /// <summary>
    /// RNG determinístico para resolução de batalha (dano, hit, iniciativa).
    /// Proibido UnityEngine.Random no código de resolução; VFX pode usar à vontade.
    /// </summary>
    public static class BattleRng
    {
        private static Random _rng;
        private static bool   _seeded;

        /// <summary>Semeia o RNG com um valor fixo (MP: chamado antes da fase de ação).</summary>
        public static void Seed(int seed)
        {
            _rng    = new Random(seed);
            _seeded = true;
        }

        /// <summary>
        /// Retorna um inteiro em [min, maxExclusive).
        /// Em SP sem seed prévia, semeia automaticamente com Environment.TickCount (aleatório).
        /// </summary>
        public static int Next(int min, int maxExclusive)
        {
            EnsureSeeded();
            return _rng.Next(min, maxExclusive);
        }

        /// <summary>
        /// Retorna um inteiro em [0, 9999] (fixed-point 0.0001 de resolução).
        /// Usar para comparações de chance: BattleRng.Roll10000() &lt; chance * 10000.
        /// </summary>
        public static int Roll10000()
        {
            EnsureSeeded();
            return _rng.Next(0, 10000);
        }

        private static void EnsureSeeded()
        {
            if (!_seeded)
            {
                _rng    = new Random(Environment.TickCount);
                _seeded = true;
            }
        }

        /// <summary>Reseta o estado (chamado pelo LockstepBattleSync ao início de batalha).</summary>
        public static void Reset()
        {
            _rng    = null;
            _seeded = false;
        }
    }
}
