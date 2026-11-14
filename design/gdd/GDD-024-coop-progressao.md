# GDD-024 — Coop: progressão, descanso, dia/noite e loot

Frente 3 do backlog (2026-11-14). Expande o GDD-015 (Coop) e o GDD-021 (equipamentos).

## Decisões do Marcus

| Tema | Decisão |
|---|---|
| Modo principal | Só o padrão: a sala abre com o Coop selecionado. |
| Personagem novo | **15 pontos** de atributo (eram 30). Todos ficam mais fracos, feras inclusive. |
| Nível | Cada nível dá **+5 pontos** para distribuir. XP fica salvo no personagem. |
| Curva | Linear, não exponencial: o custo do nível cresce, e as recompensas crescem com a camada no mesmo passo. |
| Feras | Acompanham os pontos médios do grupo (não mais o orçamento fixo de 30). |
| Descanso | 3ª opção entre camadas: cura e reanima tudo, mas o tempo avança (dia → entardecer → noite → dia). À noite as feras são mais fortes e o mapa escurece. |
| Dinheiro | Vem de feras abatidas e de vender itens. |
| Loja | Troca dinheiro por XP. |
| Loot | Feras têm pequena chance de deixar item; baú no fim da camada; no fim da camada o grupo vê todos os itens do grupo e pode trocar, vender ou pegar. |

## Números (proposta inicial)

| Item | Valor |
|---|---|
| Pontos no nível N | 15 + 5 × (N − 1) — nível 4 = os 30 de antes |
| XP para passar do nível N | 20 × N |
| XP por fera abatida (para cada personagem do grupo) | 5 × camada |
| XP por camada vencida | 15 × camada |
| Dinheiro por fera abatida (para cada personagem) | 3 × camada |
| Loja | 2 moedas → 1 XP |
| Feras: orçamento | pontos médios do grupo × (fração do tuning + 10% por camada) |
| Feras: entardecer / noite | orçamento × 1,15 / × 1,30 |
| Descer (sem descanso) | cura 30% da vida, 50% da Mana, reanima com 25% (como hoje) |
| Descansar | cura 100%, Mana 100%, reanima com 100%; o tempo avança um passo |
| Drop de fera | 8% de chance de item da raridade da camada |
| Baú | 1 item por personagem + 1, raridade da camada |
| Venda | comum 5, raro 12, épico 25 moedas |

Recompensas por camada crescem linearmente (5 × camada), e o custo do nível também (20 × N):
o ritmo de níveis por camada fica estável.

Personagens antigos (30 pontos) migram para o nível em que os pontos cabem (30 → nível 4).
Os pontos de um nível novo se distribuem na tela do personagem; o personagem só entra
numa sala com todos os pontos gastos.

## Etapas

1. **Etapa A (este PR):** pontos 15 + 5/nível, XP e dinheiro salvos no personagem/perfil,
   recompensas no Coop, feras pelo grupo, descanso com dia/noite, loja de XP no perfil.
2. **Etapa B (feita):** drops das feras, baú, tela ITENS DO GRUPO entre as cartas e a decisão
   de descer. Pegar equipa e devolve o item antigo ao baú (é assim que o grupo troca); Deixar
   tira um item seu para outro pegar; Vender paga a quem vendeu. O host guarda o baú oficial
   e valida cada pedido (`PlacementSync.Loot*ServerRpc`); itens valem durante a descida, como as cartas.

## Fora do escopo

- Subir de nível no meio da descida (os pontos novos valem na próxima partida).
