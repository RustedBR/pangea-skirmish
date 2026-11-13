# GDD-020 — Formas de ataque, dano por tile e projeção do turno

| Campo | Valor |
|---|---|
| **Status** | Aprovado para implementação (2026-11-12) |
| **Depende de** | GDD-016 (atributos), GDD-018/019 (magias), GDD-017 (reações) |
| **Substitui** | Ataque por tile do `AttackResolver` (linha reta / 1 tile) e dano fixo por acerto |

Legenda: **[CANON]** decidido pelo Marcus. **[PROPOSTA]** número inicial, ajustável no próximo teste.

---

## 1. Toda arma tem forma **[CANON]**

Cada arma define três coisas: **dano por tile**, **alcance** e **forma** (quais tiles o golpe pega).
A forma gira para a direção do golpe.

| Forma | Desenho (`@` = quem ataca, `X` = tiles pegos) |
|---|---|
| Arco 3×1 | `X X X` logo à frente |
| Varredura 3×2 | duas fileiras de 3 à frente |
| Linha N | N tiles em linha reta |
| Quadrado 2×2 | 2×2 no ponto mirado |
| 1 tile | só o tile mirado |

### Tabela inicial **[CANON — aprovada como ponto de partida]**

"Total" = contra um alvo 3×3 com a forma inteira dentro dele, antes de atributos.
Alvo de calibração: cerca de 9 de dano por tick.

| Arma | Forma | Tiles | Dano/tile | Ticks | Alcance | Total |
|---|---|---|---|---|---|---|
| Machado | arco 3×1 | 3 | 3 | 1 | 1 | 9 |
| Machado de Ferro | varredura 3×2 | 6 | 5 | 3 | 2 | 30 |
| Espada de Madeira | linha 2 | 2 | 8 | 2 | 3 | 16 |
| Espada de Ferro | arco 3×1 | 3 | 6 | 2 | 4 | 18 |
| Cajado | linha 3 | 3 | 6 | 2 | 7 | 18 |
| Cetro | quadrado 2×2 | 4 | 5 | 2 | 6 | 20 |
| Varinha de Aprendiz | quadrado 2×2 | 4 | 2 | 1 | 5 | 8 |
| Cajado Arcano | linha 3 | 3 | 6 | 2 | 7 | 18 |
| Arco Curto | 1 tile | 1 | 9 | 1 | 8 | 9 |
| Arco Longo | 1 tile | 1 | 28 | 3 | 10 | 28 |

Armas de 1 tile ficam viáveis pelo **dano por tile alto** **[CANON]**, não por regra especial.

## 2. Dano por tile **[CANON]**

- Uma unidade ocupa 3×3 = 9 tiles.
- **Dano = dano por tile × tiles da unidade dentro da forma.**
- Vale para **armas e magias**. Explosão 3×3 em cheio pega 9 tiles; de raspão, 1.
- STR (corpo a corpo) e DEX (distância) aumentam o dano por tile em **porcentagem** **[CANON]**,
  para não favorecer armas largas nem estreitas. Valor inicial: +6% por ponto **[PROPOSTA]**.
- Golpes de arma com área **não acertam aliados**, como as magias de área **[CANON]**.
- Armadura e bloqueio se aplicam ao total, depois de somar os tiles.

## 3. Mira: dois jeitos, dois resultados **[CANON]**

| Jeito | Como | Resultado |
|---|---|---|
| Clique na unidade | a forma centraliza no inimigo sozinha | dano comum |
| Livre, tile a tile | o jogador posiciona a forma | **+25% de dano e +15% de acerto** |

Intenção: métodos diferentes para problemas diferentes, e espaço para habilidade do jogador.
A mira livre mostra antes quais tiles cada unidade vai perder e o dano previsto.

## 4. Esquiva tira tiles **[CANON]**

- Acerto/esquiva continua rolando uma vez por unidade.
- Se a esquiva funciona, em vez de anular o golpe ela **remove tiles** da unidade dentro da forma,
  em quantidade que **escala com AGI**.
- Valor inicial: 1 tile a cada 3 de AGI, mínimo 1 **[PROPOSTA]**.
- Se sobrarem 0 tiles, é erro. Um golpe de 1 tile esquivado sempre vira erro.

## 5. Projeção do turno **[CANON]**

Tudo ligado enquanto o jogador planeja:

1. **Fantasma no destino:** cópia translúcida do modelo 3D onde a unidade vai parar, virada para o lado certo.
2. **Fantasma a cada parada** do caminho, não só no final.
3. **Alcance a partir do fantasma:** o alcance de ataque é desenhado da posição final planejada.
4. **Movimento e ataque distintos:** movimento preenchido em azul; alcance de ataque só com contorno
   vermelho; os tiles que o golpe vai pegar ficam destacados, com o dano previsto.

## 6. Custo misto das magias físicas **[CANON]** (substitui "sem mistura" do GDD-018)

- Magia Física gasta metade Mana e metade Stamina; o custo total não muda.
- **Stamina = metade arredondada para baixo; a Mana fica com o resto.**

| Magia | Custo | Mana | Stamina |
|---|---|---|---|
| Fúria | 3 | 2 | 1 |
| Pele de pedra | 3 | 2 | 1 |
| Salto | 2 | 1 | 1 |
| Olho certeiro | 2 | 1 | 1 |

Magia Elemental continua só com Mana.

## 7. Defesa da VIT fixa **[CANON]**

- Sai a redução em porcentagem (8% por ponto, até 60%).
- **Cada ponto de VIT tira 1 de dano físico**, descontado **uma vez no total** do golpe,
  depois de somar os tiles. O golpe nunca cai abaixo do dano mínimo (`minDamageFloor`).

## 8. Revisão das reações **[CANON]**

### Golpe de oportunidade
- Dispara contra o inimigo que, **dentro do alcance da arma**, **anda, reage ou começa a conjurar**
  (hoje só contra quem entra no alcance e sai sem atacar).
- **No máximo um golpe por inimigo provocador a cada tick.**
- Se várias unidades podem punir o mesmo gatilho, **só a mais rápida (maior DEX) golpeia**.
  Empate: decide a semente da rodada (determinístico no lockstep) **[PROPOSTA]**.

### Confirmação
- O jogador tem **18 segundos** para confirmar uma reação (era 3). Sem resposta, vale a reação padrão marcada no plano (§8.1).

### Custo e frequência
- Cada reação tem custo próprio e **1 tick** de atraso na fila de quem reage.
- **Sai o limite de uma reação a cada 4 ticks:** a Stamina é o limite.

### As quatro reações (decididas uma a uma, 2026-11-12)

| Reação | Gatilho | Efeito | Stamina | Ticks |
|---|---|---|---|---|
| **Esquivar** | vai ser atacada (arma ou magia) | esquiva **garantida**, sem rolagem: tira tiles do golpe pela AGI (1 a cada 3, mínimo 1). Golpe de 1 tile vira erro | 1 | 1 |
| **Bloquear** | vai ser atacada **por arma** | soma dureza da arma/escudo **+ metade da VIT** à defesa fixa daquele golpe. Se absorver tudo, o atacante **perde o resto da fila daquele golpe**. **Não bloqueia magia** | 1 | 1 |
| **Contra-atacar** | golpe de oportunidade (anda, reage ou conjura no alcance; um por provocador por tick; só o de maior DEX) | golpe comum da arma, centralizado no provocador; se acertar, **interrompe a ação que provocou** (o passo ou a magia não acontece) | 3 | 1 |
| **Interpor** | **qualquer aliado adjacente** vai ser atacado | entra na frente e os **tiles do golpe se dividem** entre os dois pela posição real; cada um aplica a própria defesa | 2 | 1 |

### Removidos
- **Afastar-se** sai do jogo: a Esquivar já faz esse papel (tirar tiles do golpe).
- A ideia de postura = reação automática (Manter distância, Proteger etc.) sai; ver §8.1.

### Interpor: quando dispara
Sempre que um **aliado colado** é atacado e o protetor tem Stamina (antes: só aliado "em risco" e protetor com mais vida).

## 8.1 Posturas **[CANON]**

Como era (2026-11-12): não havia postura escolhível. A cada gatilho abria um menu de **3 s**; sem clique,
a unidade **não reagia**. Só as feras escolhiam sozinhas (AGI ≥ VIT esquiva, senão bloqueia;
contra-ataque sempre).

Agora a postura é um **bônus de atributo com troca** e **não limita as reações** (qualquer reação em qualquer postura).

| Postura | Bônus | Custo |
|---|---|---|
| Neutra | — | — |
| Agressiva | +2 STR | −1 VIT |
| Defensiva | +2 VIT | −1 AGI |
| Ágil | +2 AGI | −1 STR |
| Focada | +2 DEX | −1 AGI |
| Concentrada | +2 INT | −1 VIT |
| Serena | +2 WIS | −1 STR |

Números iniciais **[PROPOSTA]**.

- Escolhida em cada **ponto de decisão**, de graça, e vale desde o primeiro tick do plano; lembra a última escolha.
- Os atributos alterados mudam tudo que deriva deles enquanto a postura vale (dano, defesa, esquiva, Mana etc.).
- **Recursos máximos mudam com a postura [CANON]:** ao entrar, a postura soma o ganho ao **máximo e ao atual**
  (Serena: Mana; Defensiva: Vida; Stamina pelos atributos do Corpo). Ao trocar, **só o máximo volta**; o atual
  é cortado no novo máximo se passar dele. Ex.: Mana 16/16 na Serena → troca → 13/13; se já tinha gasto até 10/16 → 10/13.
  Assim nunca se perde recurso que estava abaixo do máximo normal.

### Escolher a reação
- A cada gatilho aparece o **menu de 18 s** com as reações possíveis.
- No plano, o jogador marca uma **reação padrão** (ou nenhuma), usada se o tempo esgotar.
- As feras seguem escolhendo pela regra de atributo.

### Clareza na tela
- Antes: aviso de que a reação vai disparar, qual e quanto custa.
- Depois: o que ela evitou (tiles tirados, dano absorvido, golpe de oportunidade causado).

## 9. Barra de ações e intenções **[CANON]**

Problema: o menu das intenções deixa fácil andar e bater, mas esconde Concentrar, Esperar e magia.

- **Barra fixa de ícones** embaixo, acima da barra de ticks, sempre visível quando é a vez de planejar.
  Cada ícone tem tecla; o **hover** mostra nome, custo (ticks e recurso) e explicação.
- Ações: Mover, Atacar, Magia (abre o catálogo), Concentrar, **Respirar**, Esperar, mais as
  intenções prontas (Investir, Golpear e recuar, Recuar).
- **Clique direto no mapa continua como atalho:** inimigo = Investir, chão = andar.
- Ações sem alvo (Concentrar, Respirar, Esperar): **clique = 1 vez na fila; Shift+clique = até encher**
  (até o próximo ponto de decisão ou até o recurso encher).

### Respirar (ação nova) **[CANON]**
Espelho do Concentrar para o Corpo: parado por alguns ticks, recupera Stamina.
Concentrar continua só para Mana. Valores iniciais iguais aos do Concentrar **[PROPOSTA]**.

## 10. Nome do recurso **[CANON]**
"Stamina" passa a se chamar **Stamina** em todo o jogo e nos documentos (substitui a decisão do GDD-018).

## 11. O que muda no código

- `WeaponDef`: forma (tipo + dimensões) e dano por tile no lugar de `damage`.
- `AttackResolver`: um caminho só por forma (substitui "linha reta" e "1 tile"); contagem de tiles
  por unidade; esquiva por AGI; bônus da mira livre.
- `SpellResolver`: dano/cura por tile de unidade dentro da área.
- `PlannedAttack`: guarda se a mira foi livre e a rotação da forma (vai no `PlanWire` do lockstep).
- `PlanningController`: fantasmas 3D, alcance a partir do destino, contorno de ataque e prévia de tiles/dano.
- Ficha da criação de personagem e ajuda das armas: mostrar forma, tiles e dano por tile.

## 12. Notas de implementação (2026-11-12)

Feito:
- **Formas e dano por tile:** `AttackShape` (BattleTypes.cs) com arco, linha, quadrado e 1 tile, girando em 8 direções.
  `AttributeStats.DamagePerTile` = dano da arma × (1 + atributo × 6%); `PhysicalDamage` passou a ser o golpe
  centralizado num alvo 3×3. A tabela aprovada está no `GameTuning.asset`.
- **AttackResolver** reescrito: um caminho só por forma; acerto por alvo; esquiva tira tiles (reação garante, AGI rola);
  mira livre +25% de dano e +15% de acerto; aliados fora; defesa da VIT uma vez no total; bloqueio que absorve tudo
  desequilibra o atacante (perde o resto da fila no fim do tick).
- **Magias por tile:** área conta os tiles da unidade no quadrado; alvo único pega os 9. Esquivar vale contra magia.
- **Custo misto** (`SpellDef.StaminaPart/ManaPart/CostText`) e **VIT fixa** (`armorPerVIT 1`, `armorPctPerVIT 0`).
  Pele de pedra passou a multiplicar a defesa fixa.
- **Reações:** custos por reação, sem limite de frequência, menu de 18 s com reação padrão do plano, Afastar-se
  removido, golpe de oportunidade ao andar/reagir/começar a conjurar no alcance (um por provocador por tick, só o de
  maior DEX, interrompe se acertar), Interpor de qualquer aliado colado dividindo os tiles.
- **Posturas:** `Posture`/`PostureRules`, escolhidas no plano (`plannedPosture`, vão no `PlanWire` com a reação
  padrão) e aplicadas ao comprometer a fila; máximos com a regra de §8.1.
- **Respirar** (`ActionType.Breathe`, fim do enum para não renumerar a rede).
- **Barra de ações** com ícones e teclas 4–0 e `-`, hover com nome, custo e explicação; Shift repete.
- **Projeção:** fantasmas 3D translúcidos (shader `Pangea/LowPolyGhost`) em cada parada, alcance como contorno vermelho
  a partir do destino planejado, andada em azul, formas dos ataques planejados no chão, prévia da forma sob o
  cursor com o dano esperado em cada inimigo.
- **Textos:** descrições das magias com marcadores trocados pelos números reais (`SpellResolver.Describe`),
  rótulos das reações com efeito e custo, estados com explicação curta.

Limites conhecidos:
- A prévia de dano não conta crítico, variação, esquiva nem bloqueio (mostra o dano esperado sem sorte).
- O fantasma é só o corpo, sem a arma na mão.
- A forma do golpe de oportunidade e da IA das feras é a centralizada (as feras não usam mira livre).
