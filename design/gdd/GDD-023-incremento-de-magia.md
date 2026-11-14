# GDD-023 — Incremento de magia

Frente 2 do backlog (2026-11-14).

## Decisões

| Tema | Decisão |
|---|---|
| Quando | Ao planejar: escolher a magia abre o menu INCREMENTAR MAGIA antes da mira. |
| Custo | Só Mana (a Stamina fica para ataques e reações), inclusive nas magias físicas. |
| Combinação | Os incrementos se somam; cada um uma vez por magia. |

## Incrementos

| Incremento | Mana | Efeito | Vale para |
|---|---|---|---|
| Potência | 2 | dano/cura e dano por tick ×1,5; +1 tick de conjuração | magias com valor (dano, cura, muralha) |
| Efeito | 1 | +2 ticks de duração (Queimando, Encharcado, zonas, muralha…) | magias com duração |
| Alcance | 1 | +2 células de alcance de conjuração | magias com alvo à distância (não o Salto) |
| Área | 2 | +1 de raio na área | magias de área |
| Rápida | 2 | −1 tick de conjuração (mínimo 1) | magias de 2+ ticks |

## Implementação

- `SpellBoost` (flags) e `SpellBoosts.Apply` em `Spells.cs`: cópia da magia com os números alterados e
  `boostMana` somado ao `ManaPart`. O catálogo nunca muda.
- `PlannedSpell.Boosts` guarda as flags; `PlannedSpell.Def` já devolve a cópia incrementada, então
  reserva de Mana, duração na fila, alcance, área e resolução usam os números novos sem outro código.
- `SpellWire.boosts` leva os incrementos no multiplayer.
- `PlanningController.ShowSpellBoostMenu`: cada toque liga/desliga; recusa o que não cabe na Mana livre.

## Fora deste passo

- Incrementar uma magia já na fila (hoje: desfazer e planejar de novo).
- Números calibrados pelo simulador novo.
