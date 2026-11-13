# GDD-014: Semi-turno por Ticks (Tempo por Ação)

| Campo | Valor |
|-------|-------|
| **Status** | Implementado (2026-11-10) — aguarda playtest |
| **Substitui** | Planejamento por round (Planning 200s → Movimento → Ataque) do GDD-001 |
| **Lore** | Reencenação do Primeiro Jogo (GDD-013): decisões simultâneas, informação incompleta |

> **[CANON]** = decidido pelo Marcus. **[PROPOSTA]** = valor/regra sugerida, a confirmar.

---

## 1. Resumo **[CANON]**

- O tempo da batalha é contado em **ticks**.
- Cada ação tem uma **duração em ticks**. Você enfileira **quantas ações quiser**.
- O jogo só **pausa para decidir** quando a fila de alguma unidade acaba.
- **Todas as unidades que ficam livres no mesmo tick decidem juntas**, ao mesmo tempo e sem ver a escolha dos outros.
- As ações de um mesmo tick **resolvem simultaneamente**. A câmera enquadra o grupo.
- **Reação rápida sim/não:** quando um ataque vem, o defensor tem poucos segundos para confirmar uma reação.
- **Iniciativa:** o resultado aparece direto, sem animação de disputa.

```
tick:  0     1     2     3     4     5
Ana : [andar][  atacar  ][livre → decide]
Orc : [   golpe pesado   ][livre → decide]
Bob : [andar][andar][andar][ atacar ][...]
                         ↑ tick 3: Ana e Orc decidem juntos; Bob segue a fila
```

## 2. Duração das ações **[PROPOSTA]**

| Ação | Ticks base |
|------|-----------|
| Andar 1 tile | 1 |
| Ataque normal | 2 |
| Golpe poderoso | 3 |
| Passo rápido (bônus) | 1 |
| Concentrar (Mana) | 2 |
| Esperar | 1 |

**Velocidade.** A AGI reduz a duração: `ticks = max(1, round(base × 10 / (10 + AGI)))`. Com AGI 10, o ataque cai de 2 para 1 tick. Os valores ficam no `GameTuning`.

**Quando o efeito acontece.** O efeito de uma ação acontece **no fim** da duração. Um golpe lento é mais fácil de ver chegando e mais fácil de reagir.

## 3. Ponto de decisão **[PROPOSTA]**

- **Timer:** 8s. O ponto termina antes se **todos os envolvidos confirmarem**.
- **Fila máxima:** 6 ticks à frente. Isso evita planejar a partida inteira de uma vez e força pontos de decisão frequentes.
- **Timeout sem ação:** a unidade recebe **Esperar (1 tick)** e volta a decidir no tick seguinte.
- **Cancelar a fila:** não é permitido. O compromisso é a regra do Primeiro Jogo, e as reações (seção 5) são a válvula de escape.

## 4. Recursos (ponte com GDD-013) **[PROPOSTA]**

- **PA (pontos de ação)** deixam de existir. O limite passa a ser **tempo**.
- **Stamina** (corpo) é gasto em ações físicas pesadas (golpe poderoso, reações) e regenera devagar por tick.
- **Mana** (mente) fica reservada para a magia nova. A Concentrar continua regenerando Mana.
- Nesta etapa, a Stamina entra só como custo de **reação** e de **golpe poderoso**. O balanceamento fino vem depois.

## 5. Reação rápida sim/não **[CANON + PROPOSTA]**

- Quando um ataque vai acertar uma unidade, o **dono do defensor** recebe um prompt **"Reagir? [Sim] [Não]"**.
- **[PROPOSTA]** O prompt dura **3s**. Se o tempo acabar, a resposta é Não.
- **[PROPOSTA]** O tipo de reação (esquiva ou bloqueio) é escolhido **antes**, na ficha da unidade ou no ponto de decisão. Na hora o jogador só confirma, sem menu.
- **[PROPOSTA]** Reagir custa Stamina e **adiciona 1 tick** à fila do defensor.
- Se houver várias reações no mesmo tick, elas são pedidas **em paralelo**, cada jogador responde as suas.

## 6. Multiplayer (lockstep) **[PROPOSTA]**

- A **barreira de sincronização** muda: em vez de uma por round, passa a ser **uma por ponto de decisão**. Só os donos das unidades livres enviam plano. Os outros mandam um "ok" vazio.
- A **simulação de ticks é determinística**: ordem por `unitId`, sem tempo real dentro da resolução.
- O **hash de estado** passa a ser verificado **por ponto de decisão**, no lugar de por round.
- As **reações** continuam pelo `SubmitReactionServerRpc`. O host espera as respostas, ou o timeout, antes de fechar o tick.

## 7. Ritmo visual **[PROPOSTA]**

- **Resolução de um tick:** cerca de 0,4s de animação, todas as ações do tick juntas.
- **Câmera:** um enquadramento por tick, cobrindo todas as unidades que agem. Sem foco individual.
- **Pausas:** os valores atuais do `GameTuning.asset` saem (`camMoveDuration` 2s, `attackLabelLeadPause` 3s, `initiativeCollapseExtraPause` 3s). Fica um orçamento de cerca de 0,5s por tick.
- **Barra de linha do tempo** no HUD: mostra as filas de todas as unidades, com a sua detalhada e as dos inimigos **só até o tick atual**. Assim a informação continua incompleta.

## 8. Fora do escopo desta etapa

- A magia nova (aguarda a lore estar completa).
- O remapeamento 3D.
- O balanceamento fino dos números.

## 9. Notas de implementação (2026-11-10)

- **Andar = 1 waypoint**, que pode ter até `MoveBudget` tiles. Não é 1 tile por tick. Assim o movimento por AGI continua como estava.
- **PA → ticks.** `remainingAP` passou a significar ticks livres e é recalculado a partir do plano (`Unit.RecomputeTickBudget`).
- **Fila comprometida:** `Unit.queue` (`QueuedAction`) guarda destino e ataque por valor, então a fila carrega de um ponto de decisão para o outro. Unidades ocupadas não recebem `ResetPlan`, e o `PlanWire.ApplyToUnit` as ignora.
- **"Round" do código = ponto de decisão.** Status, efeitos de tile e reações renovam a cada `ticksPerRound` (4) ticks globais.
- **Reação:** a defesa é a postura por atributo (AGI ≥ VIT → Esquiva, senão Bloqueio). A escolha manual da postura fica para depois. O custo continua em **PAB** (Stamina ainda não existe) e soma `reactionTickCost` à fila.
- **Linha do tempo:** por enquanto é uma linha no log a cada ponto de decisão. A barra visual no HUD fica pendente.
- **Ritmo:** pausas reduzidas no `GameTuning.asset`. As animações de ataque do mesmo tick tocam juntas, e o dano resolve em ordem de iniciativa, com desempate por `unitId`.
