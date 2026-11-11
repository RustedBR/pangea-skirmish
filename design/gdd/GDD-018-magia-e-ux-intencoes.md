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
3. **Informação demais na barra de ticks.** Blocos, régua, ameaças, pips de Instinto e prévia disputam atenção. **[PROPOSTA]** Camadas: padrão mostra só a sua fila e ameaças; detalhes ao passar o mouse.
4. **Poucos cliques vs. controle fino.** Intenções rápidas escondem o Modo Livre. **[PROPOSTA]** Mostrar na intenção o que ela vira (preview em blocos) e um atalho para "abrir no Modo Livre".
5. **Mobile.** Sem hover e sem Tab; menus de bloco e popups de incremento são pequenos para o dedo. **[ABERTO]** Fluxo toque dedicado.
6. **Janelas de decisão simultâneas.** Em multiplayer, quem decide mais rápido espera os outros; o tempo de 15s pode ser longo ou curto conforme o número de unidades. **[ABERTO]** Tempo proporcional à fila.
7. **Reações escondidas.** O usuário relatou que não viu as reações aparecerem. **[PROPOSTA]** Mostrar no bloco ameaçado quais reações estão disponíveis e o custo em Instinto antes do tick.
8. **Aprendizado.** Muitos conceitos (ticks, Instinto, intenções, reações). **[PROPOSTA]** Tutorial com combate contra uma fera, introduzindo um conceito por vez.

## 2. Sistema de magia (Mana) — direção

Princípios vindos da lore: magia da mente (Mágico, Água, Terra, Fogo, Ar); todos podem conjurar; mais conjuração = sentidos mais embotados; o Físico fica no corpo (Instinto).

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
2. Mana regenera por tick como o Instinto, ou só por "Concentrar"?
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
