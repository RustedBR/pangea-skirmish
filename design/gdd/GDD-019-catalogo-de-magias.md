# GDD-019: Catálogo de Magias — Física e Elemental

| Campo | Valor |
|-------|-------|
| **Status** | Aprovado (2026-11-11) — em implementação. Interrupção: ticks restantes saem da fila e a unidade decide no tick seguinte [CANON] |
| **Branch** | feat/magia-e-ux-intencoes |
| **Depende de** | GDD-013 (lore), GDD-014 (ticks), GDD-016 (atributos), GDD-017 (reações), GDD-018 (decisões) |
| **Substitui** | GDD-003 (magia antiga, removida) |

> **[CANON]** = decidido pelo Marcus. **[PROPOSTA]** = número ou regra a confirmar. Todos os números aqui são **[PROPOSTA]** e vão para o `GameTuning`, simulados antes do balanceamento.

---

## 1. Regras gerais

| Regra | Valor | Origem |
|---|---|---|
| Magia **Física** gasta **Stamina**; magia **Elemental** gasta **Mana**. Nunca os dois. | — | [CANON] GDD-018 |
| Duração em ticks de qualquer magia: `max(1, round(base × 10 / (10 + DEX)))` | igual às outras ações | [CANON] |
| O efeito acontece **no fim** da duração. | — | [CANON] GDD-014 |
| Conjuração Elemental é **interrompida** se o conjurador tomar dano ≥ 25% do HP máximo durante a duração. A Mana é gasta mesmo assim. | 25% | [PROPOSTA] |
| Magia Física **não** é interrompida (é o próprio corpo). | — | [PROPOSTA] |
| Área **não acerta aliados**, exceto **cura**. | — | [CANON] |
| Mana regenera por tick; **Concentrar** aumenta a regeneração. | ver §4 | [CANON] |
| Mágico puro ignora **metade** da redução de armadura. | 50% | [PROPOSTA] |

**Potência mágica (Elemental):** `POT = INT`. Dano/cura das magias é `base + POT × fator`.

**Alcance:** em tiles, a partir do conjurador. Área em raio de tiles (0 = tile único).

---

## 2. Magia Física (Stamina) **[CANON: 4, uma por atributo]**

O corpo fazendo o "triplo". Complementa as reações do GDD-017 (que continuam como estão).

| Magia | Atributo | Stamina | Ticks base | Efeito |
|---|---|---|---|---|
| **Fúria** | STR | 3 | 1 (preparo) | O próximo ataque desta fila causa **dano × 2** (não acumula com Golpe poderoso: vale o maior). |
| **Salto** | AGI | 2 | 1 | Move até **3 × MoveStep** tiles em linha reta, **ignorando** obstáculos e ameaças (sem ataque de oportunidade). Não termina em tile ocupado. |
| **Pele de pedra** | VIT | 3 | 1 (preparo) | Até o fim do próximo ponto de decisão, redução de dano físico **×3** (armadura % e flat triplicadas, teto 90%). Ataques à distância são os mais afetados. |
| **Olho certeiro** | DEX | 2 | 1 (preparo) | O próximo ataque **não pode errar** e tem chance de crítico **+30%**. |

Notas:
- "Preparo" = a magia ocupa 1 tick e marca a unidade com o efeito, que se consome no próximo ataque/ponto. Assim ela entra na fila como qualquer ação.
- Stamina atual: base `1 + 0,25 × (STR + VIT)`, regen a cada 3 ticks. Com esses custos, uma unidade com STR+VIT = 12 (4 pontos) usa uma magia Física por ~2 pontos de decisão. **[PROPOSTA]** subir a base para `2 + 0,25 × (STR + VIT)` quando a magia Física entrar.

---

## 3. Magia Elemental (Mana) — catálogo fixo de 15 **[CANON: 15, nomeadas]**

Cada magia mostra **de que foi montada** (os elementos), para abrir combinação livre no futuro. Papel de cada elemento (GDD-018 §2): Fogo = dano/área, Água = controle/cura, Terra = barreira/defesa, Ar = posição/velocidade, Mágico = direto/percepção.

### 3.1 Puras (5)

| # | Nome | Montagem | Mana | Ticks | Alc. | Área | Efeito |
|---|---|---|---|---|---|---|---|
| 1 | **Brasa** | Fogo | 2 | 2 | 4 | 0 | Dano `4 + POT×1,0` de Fogo. Aplica **Queimando**: 2 de dano por tick por 3 ticks. |
| 2 | **Jato d'água** | Água | 2 | 2 | 4 | 0 | Dano `3 + POT×0,8`. Aplica **Encharcado**: −1 tile de MoveStep por 4 ticks. Apaga Queimando. |
| 3 | **Lasca de pedra** | Terra | 2 | 2 | 3 | 0 | Dano `3 + POT×0,8`. Se o alvo estava se movendo neste tick, **cancela** o movimento restante. |
| 4 | **Rajada** | Ar | 2 | 1 | 3 | 0 | Dano `2 + POT×0,5`. **Empurra** o alvo 1 tile para longe (2 se POT ≥ 8). |
| 5 | **Visão arcana** | Mágico | 1 | 1 | — | — | Por 6 ticks: vê as filas dos inimigos **até +2 ticks** à frente na barra (quebra a informação incompleta) e enxerga unidades em tiles ocultos. |

### 3.2 Pares (10)

| # | Nome | Montagem | Mana | Ticks | Alc. | Área | Efeito |
|---|---|---|---|---|---|---|---|
| 6 | **Explosão** | Fogo + Ar | 5 | 3 | 5 | 1 | Dano `6 + POT×1,2` em área. Empurra 1 tile para fora do centro. |
| 7 | **Bálsamo** | Água + Terra | 4 | 2 | 3 | 1 | **Cura** `5 + POT×1,0` em aliados na área (único caso em que área toca aliado). Remove Queimando e Encharcado. |
| 8 | **Vapor** | Fogo + Água | 3 | 2 | 4 | 1 | Nuvem por 3 ticks: quem termina o tick dentro recebe −20% de acerto e não pode ser alvo de Olho certeiro. |
| 9 | **Magma** | Fogo + Terra | 4 | 3 | 3 | 1 | Tiles viram **lava** por 4 ticks: 3 de dano por tick a quem está em cima. Não afeta o conjurador. |
| 10 | **Lama** | Água + Ar | 3 | 2 | 4 | 1 | Tiles de **lama** por 4 ticks: MoveStep = 1 dentro da área. |
| 11 | **Tempestade de areia** | Terra + Ar | 3 | 2 | 4 | 2 | Por 3 ticks, ataques à distância **através** da área erram 50%. |
| 12 | **Chama arcana** | Fogo + Mágico | 4 | 2 | 5 | 0 | Dano `5 + POT×1,3` de Fogo que ignora metade da armadura. Sem Queimando. |
| 13 | **Maré mental** | Água + Mágico | 3 | 2 | 4 | 0 | **Drena** `2 + POT×0,5` de Mana do alvo e devolve metade ao conjurador. |
| 14 | **Muralha** | Terra + Mágico | 4 | 2 | 3 | linha de 3 | Cria **barreira** de 3 tiles por 4 ticks: bloqueia movimento e projéteis. HP da barreira `8 + POT×2`. |
| 15 | **Passo do vento** | Ar + Mágico | 3 | 1 | 5 | 0 | **Teleporta** um aliado (ou a si) até 5 tiles para um tile livre visível. Sem ataque de oportunidade. |

---

## 4. Mana e regeneração **[CANON + PROPOSTA]**

| Item | Valor atual no código | Proposta |
|---|---|---|
| Mana máxima | `5 + WIS × 1` | `6 + WIS × 1,5` |
| Regen por tick | `manaRegenBase + WIS × manaRegenPerWIS` (por round) | **1 a cada 2 ticks**, +1 tick mais cedo a cada 4 WIS — **implementado** (`manaRegenTicks`, `wisRegenStep`) |
| Concentrar | existe (2 ticks) | 2 ticks, regenera **+3 Mana** imediatos, além da regen normal — **implementado** (`concentrateMana`) |

**Embotamento dos sentidos (GDD-013):** com Mana abaixo de **25%** do máximo, a unidade recebe **−1 tile de alcance** em ataques à distância e magias e **−10% de acerto**, até voltar acima de 25%. Só afeta a unidade, não a tela do jogador. **Implementado:** `Unit.IsDulled`, `EffectiveAttackRange` (corpo a corpo nunca cai abaixo de 1), `EffectiveHitChance`; tuning `dullManaFraction`, `dullRangePenalty`, `dullHitPenalty`. A HUD marca "embotado" na linha de status.

---

## 5. Aprendizado e diferenciação **[PROPOSTA]**

Todas as unidades conhecem as 15 magias (inatas, [CANON]). A diferença vem de:
1. **INT/WIS** da unidade (potência e reserva).
2. **Conduítes** equipados: cada um ressoa com 1 ou 2 elementos e dá **−1 de Mana** e **+POT×0,25** nas magias que usam esses elementos. Sem conduíte, tudo custa o valor da tabela.
3. **Camada de descida:** conduítes mais fundos ressoam com mais força (GDD-013 §7).

---

## 6. UI (ponte com GDD-018)

- As magias entram no **menu de bloco** como uma aba "Magia" com dois grupos: Física (custo em Stamina) e Elemental (custo em Mana).
- Cada magia mostra: nome, ícones dos elementos que a montam, custo, ticks com a DEX atual, alcance/área.
- O bloco na barra de ticks usa a cor do elemento principal.
- Hover em qualquer magia mostra o texto do efeito.

## 7. Fora deste corte

- Combinação livre de elementos.
- Reações mágicas (contrafeitiço, escudo arcano).
- Resistência mágica por WIS (existe no GDD-016, mas sem fórmula).
- Status "Encharcado/Queimando" em feras (BeastAI ignora por enquanto).

## 8. Ordem de implementação sugerida

1. Renomear Stamina → **Stamina** (código, UI, GDDs, tuning).
2. Mover DEX para Corpo na ficha e no `Attr`.
3. Mana: nova fórmula, regen por tick, Concentrar, embotamento.
4. `SpellDef` + catálogo em `ScriptableObject` (as 15 + 4), com montagem (elementos) explícita.
5. Resolução no `RoundManager` (duração, interrupção, área, status novos, tiles de lava/lama, barreira).
6. Menu de bloco com aba Magia e cores por elemento.
7. Simulador: mago INT 8 vs guerreiro STR 8, ajustar números.
8. Testes PlayMode: cada magia uma vez, interrupção, área sem aliado, cura com aliado.

---

## 9. Clareza para o jogador: Defender, Interrupção e Reações **[PROPOSTA]**

Princípio: **o jogador nunca deve ser surpreendido por uma regra**. Tudo que pode interromper, reagir ou defender aparece **antes** (na fila), **durante** (no tick) e **depois** (no log), sempre com a mesma linguagem visual.

### 9.1 Vocabulário visual único

| Conceito | Ícone | Cor | Onde aparece |
|---|---|---|---|
| Ameaça (ataque vindo contra a unidade) | ⚠ | laranja | Bloco da fila no tick do impacto |
| Reação disponível | ↩ | azul | Dentro do bloco ameaçado, com custo em Stamina |
| Defender (Bloquear/Esquivar ativo) | 🛡 | azul | Bloco da fila e sobre a unidade no mundo |
| Conjurando (interrompível) | ✦ com barra | roxo | Bloco da fila com borda tracejada |
| Interrompido | ✦ riscado | vermelho | Bloco da fila + flash sobre a unidade + log |
| Magia Física (não interrompível) | ✦ com borda sólida | laranja | Bloco da fila |

A borda **tracejada = pode ser interrompido**, **sólida = não pode**. Essa regra vale para qualquer bloco, de qualquer unidade.

### 9.2 Antes: na fila (planejamento)

- Ao enfileirar uma magia Elemental, o bloco já nasce com borda tracejada e o texto de hover: *"Interrompida se tomar ≥ 25% do HP (X de dano) antes do tick N. A Mana é gasta mesmo assim."* O **X** é calculado com o HP atual.
- Se há uma ameaça conhecida (⚠) em algum tick **dentro** da duração da conjuração, o bloco mostra um **aviso amarelo**: *"Um ataque pode chegar antes de terminar."* Ameaças só são conhecidas até o tick atual (informação incompleta), então o aviso é parcial e diz isso.
- Ao enfileirar **Defender**, o bloco mostra o valor concreto: *"Bloquear: −N de dano"* ou *"Esquivar: +P% de desvio"*, com N e P calculados pelos atributos e arma.
- Reações disponíveis aparecem **dentro** do bloco ameaçado como botões pequenos (↩ Golpe reativo 2, ↩ Esquiva ágil 2…), já com o custo. Sem Stamina suficiente, aparecem apagadas com o motivo no hover.

### 9.3 Durante: no tick

- Quando um ataque acerta um conjurador no meio da conjuração, o jogo mostra **sobre a unidade** um rótulo curto: *"Interrompido! (12 ≥ 9)"*, com o dano recebido e o limite. O bloco na barra fica riscado em vermelho e os ticks restantes dele são **removidos** da fila (a unidade fica livre e decide no tick seguinte).
- Quando o dano **não** chega ao limite, um rótulo discreto: *"Manteve a concentração (5 < 9)"*. Isso ensina a regra sem precisar de tutorial.
- O prompt de reação (sim/não) mostra **o que vai acontecer nos dois casos**: *"Reagir com Esquiva ágil (−2 Stamina): 65% de desviar. Não reagir: toma 8 de dano."*
- Defender ativo mostra o escudo 🛡 sobre a unidade durante o tick, e ao resolver, o número reduzido aparece ao lado do dano: *"8 → 3"*.

### 9.4 Depois: no log de tick

Cada fileira da barra já tem o texto do que aconteceu (GDD-018). Entradas padronizadas:
- `Orc ataca Ana (8). Ana bloqueia (−5) → 3.`
- `Orc ataca Ana (12). Ana estava conjurando Explosão → interrompida (12 ≥ 9). Mana gasta.`
- `Ana reage: Golpe reativo (−2 Stamina) → 6 de dano no Orc.`
- `Ana pula (Salto) por cima do Orc. Sem ataque de oportunidade.`

### 9.5 Tela de ajuda rápida

Um botão **"?"** na barra de ticks abre um painel de uma página com as três regras, cada uma com o ícone e uma frase:
1. **Defender** reduz dano (Bloquear: flat por VIT + arma) ou desvia (Esquivar: % por AGI).
2. **Reações** custam Stamina e respondem a uma ameaça ⚠ no tick em que ela chega.
3. **Interrupção**: conjuração Elemental (borda tracejada) é cancelada se o conjurador tomar ≥ 25% do HP antes de terminar. Magia Física (borda sólida) nunca é interrompida.

O mesmo painel é a base do tutorial (GDD-018 §4).
