using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Arenas do tutorial (GDD-027 §5): um mapa só, dividido em 9 salas separadas por vazio.
    /// Cada fase usa uma sala; trocar de fase reposiciona o elenco e foca a câmera, sem
    /// recarregar a cena. O mapa é gerado em código (MapData) e entregue ao GameBootstrap
    /// via RuntimeMap.Selected antes de carregar a cena Battle.
    /// </summary>
    public static class TutorialArena
    {
        public const string MapName = "Tutorial";
        public const int MapSize = 47;
        public const int RoomSize = 14;
        public const int Stride = 15;
        public const int PhaseCount = 9;

        /// <summary>Mapa 47×47: 9 salas 14×14 separadas por 1 célula de vazio.</summary>
        public static MapData BuildMap()
        {
            var m = MapData.CreateEmpty(MapSize, MapSize);
            m.mapName = MapName;

            // Tudo vazio; depois as salas são escavadas.
            for (int y = 0; y < MapSize; y++)
                for (int x = 0; x < MapSize; x++)
                    m.SetVoid(x, y, true);

            for (int p = 0; p < PhaseCount; p++)
            {
                var o = Origin(p);
                for (int y = o.y; y < o.y + RoomSize; y++)
                    for (int x = o.x; x < o.x + RoomSize; x++)
                    {
                        m.SetVoid(x, y, false);
                        int f = m.Flat(x, y);
                        m.terrainNames[f] = "grass_tile_full";
                        m.heights[f] = 0;
                        m.SetObject(x, y, "");
                    }
            }

            // Fase 1 (movimento): uma parede vertical no meio da sala, com passagem em cima/baixo.
            var o1 = Origin(1);
            for (int y = o1.y + 4; y <= o1.y + 9; y++)
                m.SetObject(o1.x + 6, y, "stone_1");

            // Fase 8 (coop): um baú decorativo no canto (sem colisão; só leitura visual).
            var o8 = Origin(8);
            m.SetObject(o8.x + 11, o8.y + 11, "bush");

            return m;
        }

        public static Vector2Int Origin(int phase)
        {
            int p = Mathf.Clamp(phase, 0, PhaseCount - 1);
            return new Vector2Int(1 + (p % 3) * Stride, 1 + (p / 3) * Stride);
        }

        public static Vector2Int Center(int phase)
        {
            var o = Origin(phase);
            return new Vector2Int(o.x + RoomSize / 2, o.y + RoomSize / 2);
        }

        /// <summary>
        /// Posiciona o elenco na sala da fase. Unidades fora de cena vão para o "estacionamento"
        /// (sala 8) e ficam inativas — continuam vivas para o RoundManager, mas não entram no combate.
        /// </summary>
        public static void Place(int phase, Unit player, List<Unit> dummies, Unit ally,
                                 GridManager grid, CameraController cam)
        {
            var c = Center(phase);
            var park = Center(8);
            int p = Mathf.Clamp(phase, 0, PhaseCount - 1);

            // Por padrão, todo mundo estacionado/inativo.
            PlaceUnit(player, c, true);
            if (ally != null) PlaceUnit(ally, park, false);
            if (dummies != null)
                foreach (var d in dummies) if (d != null) PlaceUnit(d, park, false);

            switch (p)
            {
                case 0: // orientação: um manequim de cada tamanho
                    PlaceUnit(player, c + new Vector2Int(-4, 0), true);
                    Show(dummies, 1, c + new Vector2Int(3, -4));
                    Show(dummies, 2, c + new Vector2Int(3, -1));
                    Show(dummies, 3, c + new Vector2Int(3, 3));
                    Show(dummies, 4, c + new Vector2Int(-4, -4));
                    break;

                case 1: // movimento: parede no meio; dois alvos pequenos do outro lado
                    PlaceUnit(player, c + new Vector2Int(-4, 0), true);
                    Show(dummies, 1, c + new Vector2Int(4, -4));
                    Show(dummies, 2, c + new Vector2Int(4, 0));
                    break;

                case 2: // ataque: alvos de tamanhos diferentes
                    PlaceUnit(player, c + new Vector2Int(-4, 0), true);
                    Show(dummies, 1, c + new Vector2Int(-1, 0));
                    Show(dummies, 2, c + new Vector2Int(4, 2));
                    Show(dummies, 3, c + new Vector2Int(4, -2));
                    break;

                case 3: // reações: atacante colado + aliado para interpor
                    PlaceUnit(player, c + new Vector2Int(-3, 0), true);
                    Show(dummies, 1, c + new Vector2Int(1, 0));
                    if (ally != null) PlaceUnit(ally, c + new Vector2Int(-3, 3), true);
                    break;

                case 4: // magia básica
                    PlaceUnit(player, c + new Vector2Int(-4, 0), true);
                    Show(dummies, 1, c + new Vector2Int(2, 0));
                    Show(dummies, 2, c + new Vector2Int(2, 3));
                    break;

                case 5: // magia avançada: elenco completo + aliado
                    PlaceUnit(player, c + new Vector2Int(-4, 0), true);
                    Show(dummies, 1, c + new Vector2Int(2, -3));
                    Show(dummies, 2, c + new Vector2Int(2, 0));
                    Show(dummies, 3, c + new Vector2Int(2, 3));
                    Show(dummies, 4, c + new Vector2Int(-4, 4));
                    if (ally != null) PlaceUnit(ally, c + new Vector2Int(-4, -4), true);
                    break;

                case 6: // magia física
                    PlaceUnit(player, c + new Vector2Int(-4, 0), true);
                    Show(dummies, 1, c + new Vector2Int(2, 0));
                    break;

                case 7: // progressão: só o herói, sem combate
                    PlaceUnit(player, c, true);
                    break;

                case 8: // coop: herói + aliado + uma fera
                    PlaceUnit(player, c + new Vector2Int(-4, 0), true);
                    if (ally != null) PlaceUnit(ally, c + new Vector2Int(-1, 0), true);
                    Show(dummies, 1, c + new Vector2Int(4, 0));
                    break;
            }

            if (cam != null && grid != null)
            {
                cam.SetAuto();
                cam.SnapTo(grid.CellToWorld(c), 9f);
            }
        }

        private static void Show(List<Unit> dummies, int footprint, Vector2Int anchor)
        {
            var u = Find(dummies, footprint);
            if (u != null) PlaceUnit(u, anchor, true);
        }

        public static Unit Find(List<Unit> dummies, int footprint)
        {
            if (dummies == null) return null;
            foreach (var u in dummies)
                if (u != null && u.stats != null && u.stats.Footprint == footprint) return u;
            return null;
        }

        /// <summary>Reposiciona/limpa uma unidade. Inativa = estacionada e fora do render.</summary>
        public static void PlaceUnit(Unit u, Vector2Int anchor, bool active)
        {
            if (u == null) return;
            u.queue.Clear();
            u.statusEffects.Clear();
            u.ResetPlan();
            u.anchor = anchor;
            u.spawnAnchor = anchor;
            u.plannedAnchor = anchor;
            u.plannedBonusAnchor = anchor;
            u.currentHP = u.stats.MaxHP;
            u.currentMana = u.stats.MaxMana;
            u.currentStamina = u.stats.MaxStamina;
            u.remainingAP = Tuning.Get().maxQueueTicks;
            u.remainingBAP = u.currentStamina;
            u.remainingReactions = 1;
            u.furyReady = 0f;
            u.stoneSkinTicks = 0;
            u.stoneSkinMult = 1f;
            u.curveReady = false;
            u.trueAimCrit = 0f;
            u.burningTicks = 0;
            u.soakedTicks = 0;
            u.foresightTicks = 0;
            u.dodgeReactTiles = 0;
            u.blockFlat = 0;
            u.gameObject.SetActive(active);
            if (active) u.SnapToAnchor();
        }
    }
}
