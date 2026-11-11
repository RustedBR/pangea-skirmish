# GDD-017: Reações inspiradas no Pathfinder 2e

| Campo | Valor |
|-------|-------|
| **Status** | Implementado em parte (2026-11-11) — aguarda partida de teste |
| **Depende de** | GDD-014 (ticks), GDD-016 (Instinto) |
| **Referência** | Archives of Nethys: Shield Block, Reactive Strike, Nimble Dodge, Intercept Attack |

> **[CANON]** = decidido pelo Marcus. **[PROPOSTA]** = sugestão a confirmar.

## 1. Regras gerais **[CANON]**

- Reação **automática**, paga em **Instinto** (2) e em **1 tick** na fila de quem reage. Como hoje.
- No PF2e há **uma reação por rodada**; aqui, **uma a cada `ticksPerRound` (4) ticks**.
- O Instinto continua pagando golpe poderoso, mirar, passo rápido e as reações.

## 2. As reações

| Reação | Ideia do PF2e | Gatilho | Efeito |
|---|---|---|---|
| **Bloquear** **[CANON]** | Shield Block: reduz o dano pela **dureza** do escudo | Vai ser atingida por um golpe | Reduz o dano por um valor **FIXO**: `VIT × 0,25 + dureza da arma` |
| **Esquiva ágil** **[CANON]** | Nimble Dodge: bônus de defesa contra **um** ataque, declarado antes da rolagem | Vai ser atacada | +9% de esquiva por ponto de AGI naquele ataque |
| **Golpe reativo** **[CANON]** | Reactive Strike | Um inimigo em que você entrou andando sai do seu alcance sem atacar | Golpe de oportunidade (a regra atual) |
| **Interceptar** **[CANON]** | Intercept Attack: recebe o dano no lugar de um aliado | Um aliado **adjacente** está em risco: golpe fatal ou vida abaixo de 40% | O guardião, com mais vida que ele, recebe o golpe com as **suas** defesas |
| **Passo reativo** **[CANON]** | Step (sem provocar reação) | Vai ser atacada e há tile livre a até 2 passos que a tira do alcance | Anda até lá e o golpe não a alcança |

### Escolha da defesa **[PROPOSTA]**
A unidade escolhe sozinha: **Passo reativo** se a AGI for pelo menos 4 acima da VIT e houver tile; senão **Esquiva** se AGI ≥ VIT; senão **Bloquear**.

## 3. Dureza por arma **[CANON + PROPOSTA]**
Cada arma tem uma **dureza** (campo `blockHardness`), somada ao Bloquear: espadas 3, machados e cajados 2, varinha 1, arcos 0. **[PROPOSTA]** os valores. No PF2e o escudo também sofre dano; aqui a arma não se desgasta.

## 4. Balanceamento (`scripts/balance_sim.py`)
Bloqueio fixo faz quem bloqueia (VIT > AGI) dominar e o Ágil perder. Para compensar, a esquiva de reação sobe de 3% para **9%** por ponto de AGI. Na simulação: Bruto 50%, Tanque 52%, Ágil 49%, Equilibrado 55%, Ágil AGI 12 37%. Extremos entre 23% e 42%. O Ágil com AGI 12 ficou um pouco abaixo; vale reavaliar jogando.

## 5. Ainda não feito
- **Interceptar** e **Passo reativo** não têm botão de confirmação: disparam sozinhos pelas regras acima.
- Ideias do PF2e que ficaram de fora: **Erguer escudo** como ação, **penalidade de múltiplos ataques**, **Auxiliar** (Aid) e o desgaste do escudo.
- A reformulação dos **golpe poderoso / mirar / passo rápido** (os outros usos do Instinto) não foi feita: pela sua escolha, o Instinto continua como está.
