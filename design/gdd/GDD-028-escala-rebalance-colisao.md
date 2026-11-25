# GDD-028: Escala ×3, rebalance 1:1 e colisão de unidades/terreno

| Campo | Valor |
|-------|-------|
| **Status** | Rascunho — proposta para aprovação do Marcus |
| **Depende de** | GDD-014 (ticks), GDD-016 (atributos), GDD-019/020 (magia/dano), GDD-024 (coop) |
| **Toca** | `AttributeStats.DefaultFootprint`, `GameBootstrap.DefaultMapSize`, `StatFormulas`, `CollisionGrid`, `GridManager`, tutorial (GDD-027) |

> **[CANON]** = decidido pelo Marcus. **[PROPOSTA]** = derivado das regras atuais, a confirmar. **[ABERTO]** = decisão pendente.
> Regra do Marcus: **entender o padrão e propor números que obedeçam às regras declaradas — não chutar.**

---

## 1. Diagnóstico (estado atual, medido) **[CANON: código]**

| Item | Valor atual | Onde |
|---|---|---|
| Footprint de unidade normal | `DefaultFootprint = 3` → 3×3 = **9 células** | `BattleTypes.cs:103` |
| Feras | Small=1, Medium=2, Large=4 | `BeastAI.FootprintOf` |
| Mapa padrão | **30×30** (900 células) | `GameBootstrap.DefaultMapSize` |
| Movimento | `2 + AGI×0.67` (~4 tiles com AGI 3) | `StatFormulas`/`GameTuning` |
| Dano corpo a corpo | por tile × `(1 + STR×0.06)` | `StatFormulas.dmgPctPerAttr` |
| Dano à distância | por tile × `(1 + DEX×0.02)` | `dexDmgPctPerAttr` |
| Status | pesos **desiguais**: Queimando=dano/tick, Encharcado=−1 tile, Vapor=−20% acerto, Areia=50% erro, Barreira=HP | `SpellResolver`/`Unit` |
| Colisão | `CollisionGrid` data-only (`walkable`/`zLevel`/`_occupants`), **não ligada** ao pathfinding; `FootprintsOverlapAny` ainda usa `FindObjectsOfType` (O(n)) | `CollisionGrid.cs`, `GridManager.cs` |

---

## 2. Mudança 1 — Escala ×3 (3×3 → 9×9) **[PROPOSTA: multiplicar por 3]**

**Intenção do Marcus:** "uma unidade normal, em vez de 3×3, agora é 9×9" — footprint **×3**. Para manter as **proporções relativas** (quantas unidades cabem no mapa, alcance vs. tamanho), **tudo que é medido em tiles também ×3**.

| Item | Antes | Depois (×3) | Regra obedecida |
|---|---|---|---|
| `DefaultFootprint` | 3 | **9** | pedido explícito (9×9 = 81 células) |
| Feras Small/Medium/Large | 1/2/4 | **3/6/12** | mesma proporção ×3 |
| Mapa padrão | 30×30 | **90×90** | mantém ~mesmo nº de "slots" de unidade |
| `moveTilesBase` | 2 | **6** | movimento escalate com o tamanho |
| `moveTilesPerAGI` | 0.67 | **2** | ~4 tiles → ~12 tiles (mesmo "sentimento" relativo) |
| `strDamageMaxRange` (fronteira melee/ranged) | 4 | **12** | range 1–12 melee, ≥13 ranged |
| Alcances de armas | 1..10 | **×3** | regra "quanto maior alcance, menor dano" preservada |
| `spellRangeMult`, `spellAreaMult` | 2, 2 | **6, 6** | magias alcançam/abrangem 3× |
| `spellRangePerINT` | 0.5 | **1.5** | proporcional |

**Por que ×3 e não só o footprint:** se só a unidade virar 9×9 num mapa 30×30, cabem ~3 unidades por lado e o movimento de 4 tiles fica menor que a própria unidade — o combate quebra. Multiplicar as medidas relativas mantém o **jogo jogável** e a hierarquia de armas.

⚠️ **[ABERTO]** Mapa 90×90 = 8100 células. Confirmar impacto em performance WebGL (renderização do grid + pathfinding A*). Alternativa: 60×60 (×2) com footprint 9 — menos espaço, unidades ocupam mais. **[PROPOSTA: 90×90]**

---

## 3. Mudança 2 — Rebalance 1:1 (peso comum) **[PROPOSTA]**

**Intenção do Marcus:** "deixando tudo 1:1, fazendo todos os status ter o mesmo peso no combate."

**Ideia central:** criar uma unidade comum de efeito — **1 Peso** — e expressar **todo efeito** em múltiplos inteiros de Peso. Assim um status não é "melhor" que outro por ter número diferente: todos custam e rendem **1 Peso por efeito**.

### 3.1 Tabela de Pesos (proposta)

| Efeito | Antes (desigual) | Depois (1:1) | Peso |
|---|---|---|---|
| Queimando | 2 dano/tick | **1 dano/tick** | 1 |
| Encharcado | −1 tile andada | **−1 tile** | 1 |
| Lama (zona) | MoveStep=1 | **MoveStep−1 tile** | 1 |
| Vapor | −20% acerto | **−10% acerto** | 1 |
| Tempestade areia | 50% erro projétil | **10% erro** | 1 |
| Barreira | 8+INT×2 HP | **1 HP por Peso, 3 Peso = 3 HP** | 1/Peso |
| Buff de atributo | +2 atributos | **+1 atributo** | 1 |
| Escudo mágico | absorve N | **absorve 1 por Peso** | 1/Peso |
| Resistência elementar | reduz N | **reduz 1 por Peso** | 1/Peso |

**Conversão 1:1 declarada:** `1 tile de movimento = 1 dano = 10% de acerto = 1 ponto de atributo = 1 Peso`. É a "moeda" comum.

### 3.2 Atributos 1:1

Hoje: HP=`10+VIT×2`, Mana=`6+WIS×1.5`, dano%=`STR×0.06`, acerto=`70%+DEX×0.03`… frações diferentes.

**[PROPOSTA 1:1]** cada atributo rende **exatamente 1 unidade** por ponto:
- HP = base + **VIT×1**, Mana = base + **WIS×1**
- Dano/tile = base + **STR×1** (melee) / **DEX×1** (ranged)
- Movimento = base + **AGI×1**, Iniciativa = base + **AGI×1** (ou DEX)
- Acerto = base + **DEX×1%**, Esquiva = base + **AGI×1%**

Isso elimina os multiplicadores ocultos (0.06/0.02/0.67/1.5…) e torna o balance **legível**: 1 ponto = 1 efeito.

⚠️ **[ABERTO]** Essa mudança altera **todos os números de combate** (dano, HP, mana, alcance). Precisa de re-simulação completa (`balance_sim.py` + testes) antes de aceitar. **Proposta: aplicar junto com ×3 numa branch isolada, validar com os 37 testes + simulador.**

---

## 4. Mudança 3 — Colisão de unidades e terreno **[PROPOSTA: ligar CollisionGrid ao jogo]**

Hoje a colisão é **data-only** — existe mas nada a usa de verdade. A nova mecânica torna ela **real e visível**:

### 4.1 Unidades bloqueiam movimento
- Substituir `FootprintsOverlapAny` (O(n), `FindObjectsOfType`) por `GridManager.Collision.IsFootprintFree(anchor, size, ignore)` (O(1)).
- Toda unidade **ocupa** seu footprint (9×9) no `_occupants`; morrer/mover libera.
- Consequência: com footprint 9×9, uma unidade **bloqueia 81 células** — posicionamento vira quebra-cabeça tático.

### 4.2 Terreno bloqueia movimento
- `walkable[x,y]` = false para void + tiles sólidos; pathfinding consulta **antes** de A*.
- `zLevel[x,y]` diferencia alturas — **[ABERTO]** rampa jogável (Δ≤1 sobe) ou parede (bloqueia)? (o skill pede clarificar isto antes de codar).

### 4.3 Terreno e unidades interagem com magia
- Barreira (GDD-019) vira **estrutura ocupante** no CollisionGrid — bloqueia passagem e projéteis de verdade.
- LAVA/lama/etc. continuam por tile, mas agora respeitam a ocupação (unidade só pisa onde cabe).

### 4.4 Tutorial (GDD-027) ganha 2 lições novas
- **Colisão de unidade**: gate = `IsFootprintFree` retorna false ao tentar mover para tile ocupado.
- **Colisão de terreno**: gate = pathfinding recusa tile `walkable=false`.

---

## 5. Plano de implementação (fases, cada uma compila + testa)

1. **Escala ×3** — trocar constantes (footprint 9, feras 3/6/12, mapa 90, move/range ×3). Validar compile + testes + simulador.
2. **Colisão real** — ligar `CollisionGrid` ao pathfinding (`IsFootprintFree`, `walkable`), substituir `FindObjectsOfType`, registrar unidades. Validar com teste EditMode de O(1) + bloqueio.
3. **Rebalance 1:1** — tabela de Pesos + atributos 1:1 no `StatFormulas`/`GameTuning`, re-simular, ajustar.
4. **Lições do tutorial** — colisão de unidade + terreno (gates reais, GDD-027).
5. **Validação final** — 37+ testes, build WebGL, deploy via CI.

---

## 6. Decisões em aberto **[ABERTO]**

1. **Multiplicador do mapa:** 90×90 (×3, proposto) ou 60×60 (×2)? Impacto em performance WebGL.
2. **"Tudo 1:1":** é (A) atributos rendem 1 unidade por ponto + Pesos iguais (proposto), ou (B) só os status ficam com peso igual e os atributos seguem com as frações atuais?
3. **z-level:** rampa jogável (sobe com Δ≤1) ou parede (bloqueia)?
4. **Barreira como estrutura:** vira ocupante real do CollisionGrid (proposto) ou só visual?
5. **Ordem:** escala → colisão → rebalance (proposto) ou outra?

## 7. Fora deste corte

- Novo sistema de armas/elementos (GDD-026 Forja, separado).
- PvP com 2+ jogadores reais (validação é solo/headless).
- Renderização 3D das camadas profundas (GDD-013 §7).