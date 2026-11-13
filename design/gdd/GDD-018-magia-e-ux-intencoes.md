# GDD-018: Magia (Mana) e UX das Intenções

| Campo | Valor |
|-------|-------|
| **Status** | Rascunho de reflexão — nada implementado |
| **Branch** | feat/magia-e-ux-intencoes |
| **Depende de** | GDD-013 (lore), GDD-014 (ticks), GDD-016 (atributos), GDD-017 (reações) |

> **[PROPOSTA]** = sugestão a confirmar. **[ABERTO]** = decisão pendente.

## 1. Possíveis problemas de UX nas intenções

1. **Previsão x realidade.** A fila é planejada olhando o estado de agora, mas o inimigo muda a posição durante os ticks. Uma intenção "Investir" pode virar andar até um tile vazio. **[PROPOSTA]** Alvo por unidade (segue o alvo), não por tile; ícone de aviso quando o alvo sai de alcance.
2. **Compromisso sem cancelar.** Fila longa + sem cancelar = erro de clique caro. **[PROPOSTA]** Janela curta de desfazer só do último bloco, antes de confirmar.
3. **Informação demais na barra de ticks.** Blocos, régua, ameaças, pips de Stamina e prévia disputam atenção. **[PROPOSTA]** Camadas: padrão mostra só a sua fila e ameaças; detalhes ao passar o mouse.
4. **Poucos cliques vs. controle fino.** Intenções rápidas escondem o Modo Livre. **[PROPOSTA]** Mostrar na intenção o que ela vira (preview em blocos) e um atalho para "abrir no Modo Livre".
5. **Mobile.** Sem hover e sem Tab; menus de bloco e popups de incremento são pequenos para o dedo. **[ABERTO]** Fluxo toque dedicado.
6. **Janelas de decisão simultâneas.** Em multiplayer, quem decide mais rápido espera os outros; o tempo de 15s pode ser longo ou curto conforme o número de unidades. **[ABERTO]** Tempo proporcional à fila.
7. **Reações escondidas.** O usuário relatou que não viu as reações aparecerem. **[PROPOSTA]** Mostrar no bloco ameaçado quais reações estão disponíveis e o custo em Stamina antes do tick.
8. **Aprendizado.** Muitos conceitos (ticks, Stamina, intenções, reações). **[PROPOSTA]** Tutorial com combate contra uma fera, introduzindo um conceito por vez.

## 2. Sistema de magia (Mana) — direção

Princípios vindos da lore: magia da mente (Mágico, Água, Terra, Fogo, Ar); todos podem conjurar; mais conjuração = sentidos mais embotados; o Físico fica no corpo (Stamina).

- **Conduítes** amplificam/direcionam elementos (GDD-013 §6). **[PROPOSTA]** Equipamento que dá afinidade e reduz o custo do elemento.
- **Duração em ticks.** Conjurar tem tempo de conjuração (ex.: 3 ticks), o efeito acontece no fim e pode ser interrompido por dano. Combina com o semi-turno e com as reações.
- **Custo de Mana + embotamento.** Mana baixa reduz visão/alcance. **[PROPOSTA]** Cada 25% de Mana gasto reduz 1 tile de visão até regenerar.
- **Escalas por atributo.** INT = poder, WIS = reserva/regeneração de Mana, DEX = velocidade de conjuração (já governa ticks).
- **Elementos como papéis, não só dano:**
  - Fogo: dano em área/ao longo do tempo
  - Água: controle (lentidão, cura leve)
  - Terra: barreira e redução de dano
  - Ar: reposicionamento e velocidade
  - Mágico: dano direto que ignora parte da defesa
- **Reações mágicas.** Reação de mente gasta Mana; contrafeitiço e escudo arcano, espelhando as reações físicas do GDD-017.
- **Primeiro corte [PROPOSTA]:** 1 magia por elemento (5), alvo único ou área pequena, custo e ticks no `GameTuning`, com testes de simulador antes do balanceamento.

## 3. Perguntas abertas

1. Magias são aprendidas, equipadas em espaços ou vêm só dos conduítes?
2. Mana regenera por tick como a Stamina, ou só por "Concentrar"?
3. O embotamento dos sentidos afeta o jogador (névoa) ou só a unidade (mira)?
4. Magia de área acerta aliados?

## 4. Decisões do Marcus (rodada 1) **[CANON]**

**UX**
- Concordou com os itens 1 a 4 (alvo por unidade, desfazer o último bloco, camadas na barra de ticks, reações visíveis no bloco). Acrescentou: **hover do mouse com informações da UI** em todos os elementos.
- **Mobile:** preparar o jogo novamente para mobile (fluxo de toque, alvos maiores).
- **Tempo de decisão:** **sem limite de tempo**. Medir quanto os jogadores levam para montar a estratégia e usar isso para decidir depois.
- **Tutorial:** sim.

**Magia**
- As magias são **inatas**: todos os seres usam todos os elementos. **Isso é um problema de design** (ver seção 5).
- **Mana regenera por tick**; **Concentrar aumenta a quantidade** regenerada.
- **Magia de área não acerta aliados**, exceto magias de **cura**.

## 5. Problema de design: todos conjuram tudo **[ABERTO]**

Sem restrição, toda unidade tem as mesmas opções, e a diferença entre personagens vem só dos números. Opções para diferenciar sem violar a lore:
- **A. Afinidade por atributo:** o custo e a potência de cada elemento dependem de INT/WIS/DEX (ex.: Fogo escala com INT, Água com WIS, Ar com DEX).
- **B. Conduítes:** quem tem o conduíte certo conjura o elemento barato e forte; sem conduíte, caro e fraco.
- **C. Aptidão por nascimento:** cada ser tem um elemento favorito (bônus), mas pode usar os outros.

## 6. Decisões do Marcus (rodada 2) **[CANON]**

**Fundamento.** Todos os seres vivos têm pedaços de Xaliah e Yorgoth em si. É isso que permite usar o **esforço mental** para fazer magia.

**Atributos (muda o GDD-016):**
- **Físicos:** STR, VIT, AGI, **DEX**
- **Mágicos:** WIS, INT

**Magia Física** = controlar o próprio corpo e fazer o que não seria comum sem meios mágicos. Exemplos:
- Pular o triplo do que conseguiria.
- Forçar um cadeado usando o triplo da força.
- Defender um ataque à distância só com a pele, com resistência tripla.

**Magia Elemental** = controlar a mana ao redor, fundi-la com a mana da própria mente e conjurar algo "do nada" no mundo material. Exemplos:
- **Água + Terra** → cura feridas.
- **Fogo + Ar** → explosão à distância.
- **Só Mágico** → visão mágica (como a *mage sight* do Mundo das Trevas).

### Leitura de design **[PROPOSTA]**
- A magia Física é o "triplo": amplificadores do corpo, pagos com Stamina. As reações do GDD-017 e o golpe poderoso já são magia Física sem o nome.
- A magia Elemental é **combinatória**: os pares de elementos (10 pares + 5 puros) formam o catálogo. Isso resolve em parte o "todos conjuram tudo": a diferença entre seres vem de **quais pares cada um domina** e de quão forte, não de quem pode ou não conjurar.
- Mágico puro = **percepção**. Faz par com a lore (mente = sentidos) e com o embotamento: gastar Mana cega, conjurar Mágico enxerga.

### Perguntas da rodada 2 **[ABERTO]**
1. Com DEX físico, ela continua governando a **velocidade em ticks** de todas as ações (inclusive conjurar)? Ou a conjuração passa para INT/WIS?
2. Que atributo escala a magia Física: STR para força, AGI para salto, VIT para pele? Ou tudo pela Stamina?
3. Os pares são fixos (Água+Terra = cura, sempre) ou o jogador combina na hora?
4. A magia Física continua custando Stamina e a Elemental Mana, sem mistura?

## 7. Decisões do Marcus (rodada 3) **[CANON]**

| Tema | Decisão |
|------|---------|
| Orçamento de pontos | **Manter como está.** DEX só muda de lado (Corpo: STR, VIT, AGI, DEX; Mente: WIS, INT). |
| Magia Física | **Sistema novo**, que complementa as reações e o golpe poderoso. Não é só renomear. |
| Recurso físico | **Stamina** substitui o nome "Stamina" em todo o jogo. |
| Elemental | Catálogo de **15 magias nomeadas e fixas** (5 puras + 10 pares). **Sem combinação na hora** por enquanto. Cada magia mostra **o que foi montado e como** (quais elementos), para abrir a combinação livre no futuro. |
| Ticks | **Tudo por DEX**, inclusive conjurar. |
| Custos | **Sem mistura:** Física gasta Stamina, Elemental gasta Mana. |

**Escala da magia Física por atributo:**
- **STR** → força de um ataque (Ataque poderoso)
- **AGI** → movimento e esquiva (Contra-ataque)
- **VIT** → vida e defesa (Defender)
- **DEX** → velocidade da ação e precisão (Ataque rápido ou Mirar)
