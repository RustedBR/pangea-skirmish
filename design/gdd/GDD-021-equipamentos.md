# GDD-021 — Equipamentos

| Campo | Valor |
|---|---|
| **Status** | Rascunho para revisão (2026-11-13) |
| **Depende de** | GDD-016 (atributos), GDD-020 (formas de ataque e dano por tile), GDD-015 (modos de jogo) |
| **Substitui** | A arma única por personagem (`Unit.weaponId`) e a escolha de arma na criação |

Legenda: **[CANON]** decidido pelo Marcus. **[PROPOSTA]** número ou detalhe inicial, ajustável no próximo teste.

---

## 1. Os 5 slots **[CANON]**

| Slot | O que vai | Papel |
|---|---|---|
| Cabeça | elmo, capuz, chapéu | armadura |
| Torso | peitoral, cota, manto | armadura |
| Pernas | botas, grevas | armadura |
| Mão direita | arma | ataque |
| Mão esquerda | arma, escudo ou nada | ataque ou defesa |

- Mão direita e mão esquerda são onde ficam as armas.
- Cabeça, torso e pernas são armaduras.

## 2. Mãos **[CANON]**

Toda arma é de **uma mão** ou de **duas mãos**.

| Combinação | Como funciona |
|---|---|
| Arma de 2 mãos | Ocupa as duas mãos. Arcos, cajados e machados grandes. |
| 1 mão + escudo | A arma ataca; o escudo melhora o **Bloquear**. |
| 1 mão + 1 mão | A direita ataca; a esquerda dá um **golpe extra mais fraco** junto. |
| 1 mão + nada | Só a direita ataca. |

### 2.1 Arma principal **[CANON]**

- O ataque usa a arma da **mão direita** (ou a de 2 mãos).
- Forma, dano por tile, alcance e ticks vêm dela, como no GDD-020.
- **Armas de uma mão dão menos dano** que as de duas mãos **[CANON]**: o dano por tile delas é
  **80%** do valor da tabela do GDD-020 **[PROPOSTA: 80%]**. As de duas mãos ficam com 100%.
  Assim, 1 mão + escudo ou 1 mão + 1 mão não passam a de duas mãos só pelo slot extra.

### 2.2 Segunda arma **[CANON]**

- Quando a principal ataca, a da mão esquerda acerta junto, com **50% do dano** **[PROPOSTA: 50%]**.
- O golpe extra usa a **forma da segunda arma**, mirada no mesmo ponto.
- Ele só acontece se o alvo estiver no alcance da segunda arma.
- Não custa tick nem Stamina a mais.
- Rola acerto e crítico separado; a esquiva e o bloqueio do alvo valem para os dois golpes.

### 2.3 Escudo **[CANON]**

- O escudo não ataca.
- A dureza dele soma ao **Bloquear** (hoje vem da arma, `blockHardness`).

| Escudo | Dureza | Peso | Raridade |
|---|---|---|---|
| Broquel | +2 | 1 | comum |
| Escudo de Madeira | +3 | 2 | comum |
| Escudo de Ferro | +5 | 3 | raro |

**[PROPOSTA]** para os números.

## 3. Armaduras **[CANON]**

Cada peça de armadura pode dar três coisas:

1. **Defesa física e mágica.** Soma à defesa fixa por golpe, que hoje vem só de VIT e WIS.
2. **Bônus de atributo.** Exemplo: capuz +1 INT, peitoral +1 VIT.
3. **Peso.** Armadura pesada tem penalidade (ver §4).

### 3.1 Tabela inicial **[PROPOSTA]**

| Peça | Slot | Def. física | Def. mágica | Atributo | Peso | Raridade |
|---|---|---|---|---|---|---|
| Capuz de Pano | cabeça | 0 | 1 | — | 0 | comum |
| Elmo de Couro | cabeça | 1 | 0 | — | 1 | comum |
| Elmo de Ferro | cabeça | 2 | 0 | +1 VIT | 2 | raro |
| Túnica | torso | 0 | 1 | — | 0 | comum |
| Gibão de Couro | torso | 2 | 0 | — | 1 | comum |
| Cota de Malha | torso | 4 | 1 | +1 VIT | 3 | raro |
| Sandálias | pernas | 0 | 0 | — | 0 | comum |
| Botas de Couro | pernas | 1 | 0 | — | 1 | comum |
| Botas Ágeis | pernas | 0 | 0 | +1 AGI | 0 | raro |
| Manto Arcano | torso | 0 | 3 | +1 INT | 1 | raro |

## 4. Peso **[CANON — regra]** **[PROPOSTA — números]**

- Cada item tem peso: 0 leve, 1 médio, 2 pesado, 3 muito pesado.
- **Peso total = soma dos 5 slots.**
- O personagem aguenta sem penalidade até **2 + VIT/2** de peso.
- Cada ponto acima tira:
  - **−1 tile** de movimento por andada (mínimo 1);
  - **−4%** de esquiva passiva;
  - e a reação **Esquivar** anda 1 tile a menos (mínimo 1).
- A ficha mostra o peso atual e o limite.

## 5. Raridade **[CANON]**

| Raridade | Cor da borda | O que tem |
|---|---|---|
| Comum | cinza | Só os números básicos. É o que existe na criação. |
| Raro | azul | Números básicos + um bônus de atributo. |
| Épico | roxo | Raro + um efeito especial. |

### 5.1 Efeitos especiais de exemplo **[PROPOSTA]**

| Efeito | O que faz |
|---|---|
| Resistente a fogo | Queimando dura metade. |
| Seco | Imune a Encharcado. |
| Contra-golpe afiado | O contra-ataque ganha +30% de dano. |
| Pés leves | Esquivar anda +1 tile. |
| Conduíte | +1 de alcance nas magias. |

## 6. Criação de personagem **[CANON]**

- Na criação, **todo item é básico (comum) e de graça**.
- Os 30 pontos continuam só para atributos.
- Os itens melhores só vêm no Coop, nas descidas (§7).

### 6.1 Tela **[PROPOSTA]**

- A troca de arma de hoje vira um **boneco com os 5 slots**.
- Clicar num slot abre a lista dos itens comuns daquele slot.
- Escolher arma de 2 mãos limpa a mão esquerda; escolher algo na esquerda tira a arma de 2 mãos.
- A ficha ao lado mostra, ao vivo:
  - defesa;
  - peso e limite;
  - movimento;
  - dano e forma da arma.
- O modelo 3D mostra só armas e escudo (§8).

### 6.2 Kit inicial padrão **[PROPOSTA]**

Para quem não quiser montar, cada arma de partida já sugere o resto:

| Arma | Esquerda | Cabeça | Torso | Pernas |
|---|---|---|---|---|
| Espada de Ferro | Escudo de Madeira | Elmo de Couro | Gibão de Couro | Botas de Couro |
| Arco Longo | — (2 mãos) | Capuz de Pano | Gibão de Couro | Botas de Couro |
| Cajado Arcano | — (2 mãos) | Capuz de Pano | Túnica | Sandálias |
| Machado | Machado | Elmo de Couro | Gibão de Couro | Botas de Couro |

## 7. Coop: escolha a cada descida **[CANON]**

- Ao vencer uma camada, antes de descer, **cada jogador vê 3 cartas sorteadas e pega 1**.
- Uma carta é **um item** de qualquer slot **ou** **pontos de atributo**.
- Item novo vai direto para o slot dele; **o antigo some** **[CANON]**. Não há inventário por enquanto.
- Camadas mais fundas sorteiam itens melhores.

| Camada | Chance de raro | Chance de épico | Carta de atributo |
|---|---|---|---|
| 1–2 | 30% | 0% | +2 pontos |
| 3–4 | 55% | 10% | +3 pontos |
| 5+ | 60% | 30% | +4 pontos |

**[PROPOSTA]** para os números.

### 7.1 Rede

- O sorteio usa o `BattleRng` com a seed da camada, então é igual em todas as máquinas.
- Cada jogador só vê as próprias cartas.
- A escolha vai ao anfitrião, que repassa para todos, como a janela de reação.
- Quem não escolher no tempo de planejamento fica com a **primeira carta** **[PROPOSTA]**.

## 8. Visual **[CANON]**

- Agora, só **armas e escudo** aparecem no modelo 3D.
- As armaduras aparecem só na ficha e na inspeção, com ícone por slot e borda de raridade.
- Modelos de armadura ficam para depois.

## 9. Dados e rede (técnico)

- `ItemDef`: id, nome, slot, mãos (1/2), peso, raridade, defesa física/mágica, bônus de atributo, efeito.
- As armas atuais (`WeaponDef`) viram `ItemDef` de mão, com os campos de forma do GDD-020.
- `Unit.equipment`: 5 ids, um por slot, no lugar de `weaponId`.
- O personagem salvo grava os 5 ids.
- O plano em rede (`PlanWire`) não muda; o equipamento vai no envio do personagem à sala e na escolha da descida.
- Atributos finais = base + postura + **itens**, calculados num lugar só (`WeaponDef.ApplyTo` vira `Equipment.ApplyTo`).

## 10. Respostas (2026-11-13) **[CANON]**

1. **Segunda arma forte demais?** Armas de uma mão dão menos dano (§2.1). Conferir no `balance_sim.py`
   ao implementar.
2. **Mochila?** Não. O item trocado na descida some; sem sistema de inventário agora.
3. **Feras usam equipamento?** Não. Continuam com garras.
4. **PvP só com itens comuns?** Sim. Sem descidas, todo mundo joga com os itens da criação.
