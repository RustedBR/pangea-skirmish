# GDD-016: Atributos e Intenções de Planejamento

| Campo | Valor |
|-------|-------|
| **Status** | Aprovado (2026-11-10) — em implementação |
| **Substitui** | Fórmulas de atributo do GDD-002; menus de planejamento (Ações > Mover/Atacar) do GDD-008 |
| **Depende de** | GDD-013 (Corpo/Mente, Instinto/Mana), GDD-014 (ticks) |

> **[CANON]** = decidido pelo Marcus. **[PROPOSTA]** = regra ou número sugerido, a confirmar.

---

# Parte 1 — Atributos

## 1. Estrutura **[CANON]**

Três atributos do **Corpo**, que alimentam o **Instinto**, e três da **Mente**, que alimentam a **Mana**.

| Corpo (Instinto) | Mente (Mana) |
|---|---|
| **STR** — força do golpe | **DEX** — precisão e rapidez das ações |
| **VIT** — vida e resistência | **INT** — potência da magia |
| **AGI** — esquiva, alcance da andada, iniciativa | **WIS** — reserva e defesa mental |

## 2. O que cada atributo faz

| Atributo | Efeito | Origem |
|---|---|---|
| **STR** | Dano físico: +2 por ponto | como hoje |
| **STR** | Instinto máximo: +0,5 por ponto | **[PROPOSTA]** |
| **VIT** | Vida: +3 por ponto. Armadura: −1 de dano por ponto | como hoje |
| **VIT** | Força do Bloqueio: 3% por ponto, até 60% | como hoje |
| **VIT** | Instinto máximo: +0,5 por ponto | **[PROPOSTA]** |
| **AGI** | Esquiva: +2% por ponto. Força da reação de Esquivar: +3% por ponto | **[CANON]** fica com a AGI |
| **AGI** | Tiles por andada: `2 + AGI/2`, até 6 | **[CANON]** fica com a AGI; fórmula é **[PROPOSTA]** |
| **AGI** | Iniciativa: d20 + AGI + DEX | **[CANON]** como hoje |
| **DEX** | Acerto: +3% por ponto. Crítico: +2% por ponto. Dano extra do Mirar | como hoje |
| **DEX** | **Rapidez das ações:** quanto mais DEX, menos ticks cada ação leva (ataque, conjuração, concentrar) | **[CANON]** |
| **DEX** | Iniciativa (junto com a AGI) | **[CANON]** como hoje |
| **INT** | Potência da magia | **[CANON]** espera a magia nova |
| **WIS** | Mana máxima, regeneração de Mana, resistência mágica | **[CANON]** espera a magia nova |

**O que muda em relação a hoje:**

- A rapidez das ações **passa da AGI para a DEX**. Um mago com DEX alta conjura mais rápido que um sem. **[CANON]**
- A DEX **deixa de dar pontos de ação bônus** (o Instinto assume esse papel).
- O teto fixo de 4 tiles por andada vira a fórmula da AGI.

### 2.1 Duração das ações **[CANON + PROPOSTA]**

**[CANON]** A DEX encurta as ações: `ticks = max(1, arredonda(base × 10 / (10 + DEX)))`. É a fórmula que já existe, trocando a AGI pela DEX. Andar e Esperar não mudam com a DEX (o alcance da andada é da AGI).

**[PROPOSTA]** O **peso da arma** define a base do ataque:

| Ação | Ticks base | Com DEX 5 | Com DEX 10 |
|---|---|---|---|
| Andar | 1 | 1 | 1 |
| Atacar com arma leve (adaga, arco curto) | 1 | 1 | 1 |
| Atacar com arma média (espada, cajado) | 2 | 1 | 1 |
| Atacar com arma pesada (machado, arco longo) | 3 | 2 | 2 |
| Golpe poderoso | +1 sobre a base da arma | | |
| Concentrar | 2 | 1 | 1 |
| Conjurar magia (quando existir) | pela magia | | |

Arma leve bate mais vezes, pesada bate mais forte, e a DEX aproxima as pesadas das leves.

## 3. Instinto **[CANON + PROPOSTA]**

**[CANON]** O Instinto é a reserva do Corpo, como a Mana é a da Mente. Ele **substitui os pontos de ação bônus (PAB)** e paga ações bônus e reações.

**[PROPOSTA]** Números:

- **Máximo:** `3 + (STR + VIT) / 2`.
- **Regeneração:** 1 a cada 2 ticks.
- **Custos:**

| Uso | Instinto |
|---|---|
| Golpe poderoso | 2 |
| Mirar | 1 |
| Passo rápido | 1 |
| Reação (Esquivar, Bloquear, Afastar-se, Contra-atacar, Interpor) | 2 |

- A unidade começa a batalha com o Instinto cheio.
- No Coop, descer uma camada **não recupera Instinto** (o peso de descer, GDD-015 §3.2).

---

# Parte 2 — Intenções de planejamento

## 4. Ideia **[CANON]**

Em vez de montar a fila ação por ação em menus, o jogador dá uma **intenção** e o jogo monta a fila. Poucos cliques para o caso comum, mais cliques só quando o jogador quer controle fino.

## 5. Intenções prontas **[CANON]**

| # | Intenção | O que monta |
|---|---|---|
| 1 | **Investir** | Vai até o alvo e gasta todo o tempo restante atacando |
| 2 | **Golpear e recuar** | Vai até o alvo, ataca uma vez e volta gastando o movimento que sobrar |
| 3 | **Recuar** | Gasta todo o movimento se afastando dos inimigos, ou até um ponto clicado |
| 4 | **Manter distância** | Fica onde está; se um inimigo tentar atacá-la, afasta-se antes do golpe |
| 5 | **Proteger** | Com um aliado clicado: fica entre ele e o inimigo mais próximo dele. Sem alvo: fica entre o grupo aliado e o grupo inimigo |

## 6. Gestos **[CANON + PROPOSTA]**

**[CANON]** O jogo usa as quatro variações juntas, mais modificadores de teclado como atalhos extras:

- **A. Contexto decide** — o que foi clicado define a intenção.
- **B. Intenção primeiro** — teclas 1 a 5 (ou botões) armam a intenção; o próximo clique aplica.
- **C. Clicar e arrastar** — arrastar a partir do inimigo escolhe o tile onde a unidade termina.
- **E. Fila por cliques** — cada clique acrescenta um passo; duplo clique preenche o resto.

**[PROPOSTA]** Como A e E dão significados diferentes ao mesmo clique, o planejamento tem **dois modos**, trocados por um botão ou pela tecla `Tab`:

### Modo Intenção (padrão)

Um clique monta a fila inteira e **substitui** a prévia anterior.

| Clique em | Sem intenção armada | Com intenção armada (teclas 1–5) |
|---|---|---|
| Inimigo | Investir | A intenção armada contra esse inimigo |
| Chão | Andar até lá | Recuar até lá, se a armada for Recuar |
| Aliado | Proteger esse aliado | Proteger esse aliado |
| A própria unidade | Manter distância | A intenção armada sem alvo (ex.: Proteger = segurar a linha) |

### Modo Livre

Cada clique **acrescenta** um passo à fila.

| Gesto | Efeito |
|---|---|
| Clique no chão | Acrescenta uma andada |
| Clique no inimigo | Anda o necessário e acrescenta **um** ataque |
| Duplo clique no inimigo | Ataca até acabar o tempo |
| Botão Desfazer ou `Backspace` | Remove o último passo |

### Nos dois modos

- **Arrastar a partir do inimigo até um tile:** ataca e termina naquele tile (golpear e recuar com destino escolhido).
- **Atalhos de teclado:** `Shift`+clique = atacar uma vez. `Ctrl`+clique = golpear e recuar. Botão direito no chão = recuar até ali.
- No celular valem os botões, o toque e o arrasto; os modificadores não existem.

## 7. Prévia e confirmação **[CANON + PROPOSTA]**

- **[CANON]** O gesto mostra uma **prévia** e precisa de confirmação.
- **[PROPOSTA]** A prévia mostra o caminho com os fantasmas de cada andada, os alvos marcados e uma **barra de ticks** com a fila (ex.: `[Andar][Andar][Atacar 2][Atacar 2]`).
- **[PROPOSTA]** Confirmam: `Enter`, `Espaço`, o botão Confirmar, ou um **segundo clique no mesmo alvo**.
- **[PROPOSTA]** Se o tempo de decisão acabar com uma prévia montada, ela é confirmada como está.

## 8. Posturas (intenções condicionais) **[CANON + PROPOSTA]**

**[CANON]** Reações disparam **sozinhas por padrão**, com uma opção por unidade para **pedir confirmação** (o sim/não de 3 segundos).

**[PROPOSTA]** Cada unidade tem uma **postura**, escolhida no planejamento e mantida até ser trocada:

| Postura | Gatilho | Efeito |
|---|---|---|
| **Esquivar** | Vai ser atacada | Bônus de esquiva naquele ataque |
| **Bloquear** | Vai ser atacada | Reduz o dano daquele ataque |
| **Afastar-se** | Um inimigo termina uma andada no alcance dela, ou vai atacá-la | Anda uma vez para longe antes do golpe; se sair do alcance, o ataque erra |
| **Contra-atacar** | Um inimigo que entrou andando no alcance sai sem atacá-la | Golpe de oportunidade (regra atual) |
| **Interpor** | Um aliado protegido vai ser atacado e ela está adjacente | Recebe o golpe no lugar do aliado |

- Toda reação custa **2 de Instinto** e **1 tick** na fila de quem reage. Sem Instinto, não reage.
- Uma reação por unidade a cada 4 ticks, como hoje.
- **Manter distância** = ficar parada com a postura Afastar-se. **Proteger** = posicionar-se e usar a postura Interpor.
- As feras do Coop usam a postura pelo atributo, como hoje (AGI ≥ VIT esquiva, senão bloqueia).

## 9. Ordem de implementação **[PROPOSTA]**

1. **Atributos:** fórmulas novas, rapidez pela DEX, duração por peso da arma.
2. **Instinto:** reserva, regeneração e custos; remover os PAB.
3. **Montador de intenções:** funções que geram a fila para Investir, Golpear e recuar e Recuar (as feras passam a usar o mesmo montador).
4. **Modo Intenção:** clique por contexto, intenção armada, prévia com barra de ticks e confirmação.
5. **Posturas:** reação automática com opção de confirmar; Afastar-se e Interpor.
6. **Proteger e Manter distância.**
7. **Modo Livre, arrastar e atalhos de teclado.**

## 10. Fora do escopo

- Os papéis de INT e WIS (aguardam a magia nova).
- Rebalancear os números de dano e vida das armas.
