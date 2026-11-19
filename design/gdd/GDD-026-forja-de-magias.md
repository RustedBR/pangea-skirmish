# GDD-026: Forja de Magias — criação livre de magias (1 e 2 elementos)

| Campo | Valor |
|-------|-------|
| **Status** | Rascunho — proposta para aprovação do Marcus |
| **Estende** | GDD-019 (catálogo), GDD-023 (incremento); promove o "combinação livre" que o GDD-019 §7 deixou fora do corte |
| **Depende de** | GDD-013 (lore, 6 elementos), GDD-014 (ticks), GDD-016 (atributos), GDD-018 (recursos Corpo/Mente), GDD-019 (SpellDef/SpellResolver) |
| **Referências** | Magicka (mistura de elementos → magia emergente), Oblivion/Morrowind Spellmaker (efeito + magnitude/área/duração/alcance → custo calculado) |

> **[CANON]** = decidido pelo Marcus. **[PROPOSTA]** = regra/número a confirmar. **[ABERTO]** = decisão pendente.
> Todos os números são **[PROPOSTA]** e vão para o `GameTuning`, simulados antes do balanceamento.

---

## 1. Visão **[CANON: o pedido]**

O jogador **cria a própria magia do zero**, em vez de só escolher de uma lista fixa:

1. Escolhe a **montagem**: 1 elemento (magia **simples/pura**) ou 2 elementos (magia **par**).
2. Escolhe o **efeito** (o "verbo") que aquela montagem permite.
3. Ajusta os **parâmetros**: potência, área, duração, alcance.
4. O jogo **calcula o custo** (Mana/Stamina) e os **ticks** de conjuração a partir das escolhas.
5. **Dá um nome** e guarda no **grimório** do personagem. A magia passa a existir como qualquer outra.

**Todos os 6 elementos têm magias simples e pares.** Com 6 elementos, são **6 simples + 15 pares = 21 montagens** (C(6,2)=15). Cada elemento aparece em 1 simples + 5 pares.

O catálogo fixo do GDD-019 (15 elementais + 4 físicas) **continua existindo** como **receitas prontas** — pontos de partida nomeados que o jogador pode usar como estão ou abrir na Forja e modificar.

### O que vem de cada referência **[PROPOSTA]**

| Referência | O que aproveitamos | O que NÃO copiamos |
|---|---|---|
| **Magicka** | A montagem de elementos define o caráter da magia; pares têm identidade própria (Fogo+Terra=Magma). | Combinação em tempo real com 5+ elementos e física de projétil caótica. Pangea é semi-turno: a montagem é escolhida no planejamento, não no calor do momento. |
| **Oblivion / Morrowind** | Escolher o efeito + regular magnitude/área/duração/alcance; o custo é **derivado** das escolhas; nomear e guardar no livro. | Comprar efeitos com ouro por NPC. Aqui a Forja é do próprio personagem (todo ser vivo conjura, GDD-013). |

---

## 2. Os 6 elementos e seus domínios **[CANON: GDD-013 §2, GDD-018 §2]**

| Elemento | Origem | Recurso | Domínio (papel) |
|---|---|---|---|
| **Físico** | Corpo | Stamina (+ Mana na metade, GDD-020 §6) | Preparo do corpo: buffs de ataque/defesa/mobilidade |
| **Mágico** | Mente | Mana | Direto / percepção / penetração |
| **Fogo** | Mente | Mana | Dano / área / queimar |
| **Água** | Mente | Mana | Cura / controle / fluxo |
| **Terra** | Mente | Mana | Barreira / defesa / terreno |
| **Ar** | Mente | Mana | Posição / velocidade / empurrão |

**Regra de montagem [CANON]:**
- **Simples** (1 elemento): o efeito sai do domínio daquele elemento.
- **Par** (2 elementos): o efeito combina os dois domínios. A ordem não importa (Fogo+Ar == Ar+Fogo).
- **Par com Físico** = magia **híbrida** Corpo/Mente: metade Stamina, metade Mana (como as físicas do GDD-019), e o efeito casa o preparo do corpo com o domínio do elemento mental.

---

## 3. A matriz completa das 21 montagens **[CANON: 6 simples + 15 pares]**

Cada montagem tem uma **receita canônica** (nome + efeito padrão). Na Forja o jogador pode partir dela e mudar o efeito dentro do domínio + os parâmetros.

### 3.1 Simples (6) — uma por elemento

| Elemento | Receita canônica | Efeito | Origem |
|---|---|---|---|
| Físico | **Fúria** (STR), **Salto** (AGI), **Pele de pedra** (VIT), **Curva** (AGI) | Preparo do corpo | GDD-019 §2 (já existem; Físico tem 4 simples) |
| Mágico | **Visão arcana** | Foresight (percepção) | GDD-019 #5 |
| Fogo | **Brasa** | Damage + Queimando | GDD-019 #1 |
| Água | **Jato d'água** | Damage + Encharcado | GDD-019 #2 |
| Terra | **Lasca de pedra** | Damage + CancelMove | GDD-019 #3 |
| Ar | **Rajada** | Damage + Push | GDD-019 #4 |

### 3.2 Pares mentais (10) — já existem (GDD-019 §3.2)

| Par | Receita | Efeito |
|---|---|---|
| Fogo + Ar | **Explosão** | Damage em área + Push |
| Água + Terra | **Bálsamo** | Heal em área (toca aliado) |
| Fogo + Água | **Vapor** | ZoneSteam (−acerto) |
| Fogo + Terra | **Magma** | ZoneLava (dano por tick) |
| Água + Ar | **Lama** | ZoneMud (MoveStep=1) |
| Terra + Ar | **Tempestade de areia** | ZoneSandstorm (projéteis erram) |
| Fogo + Mágico | **Chama arcana** | Damage + armorPierce |
| Água + Mágico | **Maré mental** | DrainMana |
| Terra + Mágico | **Muralha** | Barrier |
| Ar + Mágico | **Passo do vento** | Teleport |

### 3.3 Pares com Físico (5) — **NOVOS** **[PROPOSTA]**

Híbridos Corpo/Mente: metade Stamina, metade Mana; 1 tick de preparo quando o efeito é um buff de ataque. Escalam com o atributo do Corpo + INT.

| Par | Nome proposto | Efeito proposto | Temática |
|---|---|---|---|
| Físico + Mágico | **Golpe arcano** | PrepImbue: próximo ataque causa +dano mágico `(STR+INT)×fator`, ignora metade da armadura | A mente guia o golpe |
| Físico + Fogo | **Lâmina flamejante** | PrepImbueFire: próximo ataque aplica Queimando + dano de Fogo extra | Corpo em chamas |
| Físico + Água | **Fluxo marcial** | PrepSiphon: próximo ataque cura o conjurador em % do dano causado | O golpe flui de volta |
| Físico + Terra | **Guarda pétrea** | PrepCounter: até o fim do próximo ponto, reduz dano físico e contra-ataca quem bater | Corpo como rocha |
| Físico + Ar | **Investida eólica** | Leap + Push ao chegar (salto que empurra quem estiver no destino) | Vento que carrega o corpo |

> Estes 5 efeitos novos (`PrepImbue`, `PrepImbueFire`, `PrepSiphon`, `PrepCounter`, `LeapPush`) são **verbos novos** no `SpellEffect` + `SpellResolver`. Os 10 pares mentais e 6 simples reusam verbos que já existem.

---

## 4. A Forja (fluxo de criação) **[PROPOSTA]**

Tela acessível do menu de personagem / grimório (fora da batalha) **e** um atalho "criar variação" durante o planejamento (opcional, ver §10).

```
┌─ FORJA DE MAGIAS ─────────────────────────────┐
│ 1. MONTAGEM   [Fogo] [+] [Terra]   (1 ou 2)    │
│    → mostra os domínios combinados             │
│ 2. EFEITO     ( ) Dano  ( ) Zona  ( ) Barreira │
│    → só os verbos que a montagem permite       │
│ 3. PARÂMETROS                                  │
│    Potência  ▁▂▃▄▅  (nível 0–5)                │
│    Área      ▁▂▃    (0–3)                      │
│    Duração   ▁▂▃    (0–3 rounds)               │
│    Alcance   ▁▂▃▄   (1–6)                      │
│ 4. PREVIEW    "Magma personalizada"            │
│    Custo: 5 Mana · 3 ticks · área 1 · 2 rounds │
│    Dano ~8 + POT×1,2 por tick                  │
│ 5. NOME       [________________]  [SALVAR]     │
└────────────────────────────────────────────────┘
```

- **Montagem:** 6 botões de elemento; clicar seleciona 1, clicar no 2º forma o par; clicar de novo limpa. Pares com Físico marcados como híbridos (Stamina+Mana).
- **Efeito:** a lista de verbos é **filtrada pela montagem** (§5). Um par Fogo+Terra oferece Dano, ZoneLava, Barrier; não oferece Teleport (fora do domínio).
- **Parâmetros:** steppers de **nível inteiro** (0–5), não sliders contínuos — cada nível tem um efeito e um custo definidos (§6). Inteiros garantem determinismo no lockstep (§8).
- **Preview:** recalcula custo/ticks/valores a cada mudança, com a POT do personagem (INT). Mostra o texto do efeito como no hover do GDD-019.
- **Salvar:** exige nome não-vazio; grava no grimório (§7). Limite de magias guardadas **[ABERTO]** (sugestão: 12 por personagem).

---

## 5. Paleta de efeitos por domínio **[PROPOSTA]**

Cada elemento libera um conjunto de verbos. O par libera a **união** dos dois conjuntos (mais os verbos híbridos quando Físico está no par).

| Elemento | Verbos liberados |
|---|---|
| Físico | PrepFury, PrepStoneSkin, PrepCurve, Leap (+ híbridos PrepImbue/PrepSiphon/PrepCounter/LeapPush em par) |
| Mágico | Damage (pierce), DrainMana, Foresight, Teleport, Barrier |
| Fogo | Damage, ZoneLava, ZoneSteam (com Água), appliesStatus=Burning |
| Água | Heal, Damage, ZoneMud (com Ar), ZoneSteam (com Fogo), DrainMana (com Mágico), appliesStatus=Soaked |
| Terra | Damage+CancelMove, Barrier, ZoneLava (com Fogo), ZoneSandstorm (com Ar) |
| Ar | Damage+Push, Teleport (com Mágico), ZoneMud (com Água), ZoneSandstorm (com Terra), Leap (com Físico) |

**Regra de coerência [PROPOSTA]:** o efeito precisa fazer sentido com os elementos. A paleta acima é o filtro. Efeitos "coringa" (Damage puro) são liberados por Fogo/Mágico/Terra/Água/Ar para dar flexibilidade, mas o **status/tempero** (Queimando, Encharcado, pierce, push) só vem do elemento certo.

**Teto de poder [PROPOSTA]:** nem todo verbo aceita todo parâmetro. Ex.: Teleport não tem área/duração; Barrier tem comprimento em vez de raio; Foresight não tem potência. A Forja esconde os parâmetros que não se aplicam (como o `SpellBoosts.Allowed` já faz por incremento).

---

## 6. Custo e ticks (derivados das escolhas) **[PROPOSTA]**

Filosofia Oblivion: **magia mais forte custa mais caro e demora mais.** Números inteiros por nível.

### 6.1 Custo de Mana/Stamina

```
custo = baseVerbo + potNivel×2 + areaNivel×2 + durNivel×1 + max(0, alcNivel−2)×1
```

- `baseVerbo`: custo base do efeito (Damage 2, Heal 3, ZoneLava 3, Barrier 3, Teleport 2, Foresight 1, DrainMana 2, Prep* 2…).
- `potNivel` 0–5: +2 Mana por nível (potência).
- `areaNivel` 0–3: +2 Mana por nível de raio.
- `durNivel` 0–3: +1 Mana por round de duração.
- `alcNivel` 1–6: +1 Mana por célula acima de 2.
- **Híbrido (par com Físico):** metade do custo em Stamina (arredondada para baixo), o resto em Mana — igual `SpellDef.StaminaPart`/`ManaPart` do GDD-020 §6.

Exemplo: Magma (Damage→ZoneLava base 3) pot 2 (+4) área 1 (+2) dur 2 (+2) alc 3 (+1) = **12 Mana**? → calibrar; os níveis默认 da receita canônica reproduzem os custos do GDD-019 (Magma 4 Mana). **[ABERTO]** a escala exata; o princípio é custo crescente com poder.

### 6.2 Ticks de conjuração

```
ticksBase = baseVerboTicks + potNivel≥3 ? 1 : 0 + areaNivel≥2 ? 1 : 0
ticksFinal = max(1, round(ticksBase × 10 / (10 + DEX)))   // DEX acelera, GDD-014
```

- Potência alta e área grande deixam a conjuração mais lenta (mais interrupível).
- O incremento **Rápida** do GDD-023 continua disponível como atalho (−1 tick).

### 6.3 Valores do efeito

Reusa a fórmula do GDD-019: `Amount = baseAmount + POT × potFactor`, com `POT = INT × spellPotPerINT`. Os níveis de potência mapeiam para `baseAmount`/`potFactor`/`tickDamage` via tabela por verbo (no `GameTuning`). Área → `area`; duração → `durationRounds`; alcance → `range`.

---

## 7. Grimório (persistência) **[PROPOSTA]**

- Cada personagem guarda suas magias craftadas como **`SpellDef` serializados** (a estrutura já existe e é `[Serializable]`), mais um flag `isCrafted` e a **receita** (montagem + verbo + níveis) para reconstruir.
- Persiste junto do perfil (`PlayerProfile`, mesmo canal dos presets de personagem), sobrevive a recarga/fechamento de aba (WebGL → `PlayerPrefs`/JSON).
- **Receitas prontas** (as 21 canônicas) vêm de fábrica; o jogador não precisa craftar para jogar. Craftar é **opcional e poderoso** (personalização).
- Compartilhar/receber magias entre jogadores **[ABERTO]** (futuro; não neste corte).

---

## 8. Determinismo / Multiplayer (lockstep) **[CANON: requisito técnico]**

O lockstep exige que todos os clientes resolvam a mesma magia identicamente.

- A magia craftada é **dado puro** (`SpellDef`): montagem (ints), verbo (enum), níveis (ints). **Sem floats livres** no wire — os valores derivados (`baseAmount`, `potFactor`) são recalculados localmente a partir da receita + `GameTuning` (igual em todos os clientes).
- O plano (`PlannedSpell`/`SpellWire`) carrega a **receita compacta** (elements + effect + potNivel/areaNivel/durNivel/alcNivel + nome/id do grimório), não o `SpellDef` inteiro. Cada cliente reconstrói o `SpellDef` via `SpellForge.Build(receita)` → mesmo resultado.
- O hash do round (`ReportRoundHash`) já cobre o estado; magias craftadas não introduzem não-determinismo porque a resolução é a mesma função `SpellResolver.Resolve` sobre dados idênticos.
- **Validação no host:** o host valida a receita (montagem legal, verbo permitido, níveis dentro do teto, custo pagável) antes de aceitar o plano — rejeita receita inválida (anti-cheat leve).

---

## 9. Balanceamento **[PROPOSTA]**

- A Forja **não pode criar magia estritamente melhor** que uma canônica pelo mesmo custo. O custo derivado (§6) é o equalizador: mais potência/área/duração/alcance → mais Mana + mais ticks.
- Teto por verbo (ex.: Damage pot máx 5, área máx 3) impede combos absurdos.
- Pares com Físico (híbridos) custam Stamina **e** Mana, então competem com ataques/reações (Stamina é escassa) — auto-limitantes.
- Simulador (`balance_sim.py` / testes EditMode) valida: mago INT 8 craftando vs guerreiro STR 8; nenhuma receita craftada domina as canônicas.
- Os números vão todos para o `GameTuning` (nada hardcoded), seguindo a filosofia do Marcus.

---

## 10. UI / UX **[PROPOSTA]**

- **Tela Forja** (fora da batalha): o fluxo do §4, com a linguagem visual do jogo (cores por elemento `ElementInfo.Color`, ícones de texto `TextIcons`).
- **No planejamento:** a aba Magia (GDD-018) ganha um botão **"Forjar"** que abre a Forja rápida; ao salvar, a magia entra no grimório e pode ser enfileirada na hora. **[ABERTO]** se a Forja abre durante a batalha ou só fora (sugestão: fora, para não quebrar o ritmo do semi-turno; durante o planejamento só escolher do grimório).
- **Preview honesto** (GDD-019 §9): custo, ticks com a DEX atual, alcance/área, interrupção (borda tracejada se Elemental), tudo antes de confirmar.
- Mobile: steppers grandes (toque), layout responsivo (GDD-022).

---

## 11. Plano de implementação (fases) **[PROPOSTA]**

1. **Dados + Forja (lógica pura):** `SpellForge` (valida montagem, paleta de verbos, calcula custo/ticks/valores a partir de níveis inteiros), `SpellRecipe` (struct serializable), extensão de `SpellDef` (`isCrafted`, receita). Testes EditMode da Forja (custo crescente, paleta por montagem, tetos, determinismo da reconstrução).
2. **Verbos novos (5 híbridos):** `PrepImbue`, `PrepImbueFire`, `PrepSiphon`, `PrepCounter`, `LeapPush` no `SpellEffect` + `SpellResolver`. Testes de resolução.
3. **Grimório:** persistência no `PlayerProfile`, receitas de fábrica (21), CRUD de magias craftadas.
4. **Wire lockstep:** `SpellWire` carrega a receita; host valida; reconstrução determinística. Teste PlayMode de sync.
5. **UI Forja:** tela de criação (montagem → efeito → parâmetros → preview → nome → salvar), cores/ícones por elemento, mobile.
6. **Integração no planejamento:** aba Magia mostra grimório + canônicas; enfileirar magia craftada.
7. **Balanceamento:** simulador + tuning, calibrar custos/níveis para nenhuma receita dominar.
8. **Testes PlayMode:** craftar uma magia de cada elemento (simples + par), conjurar, interrupção, área sem aliado, cura com aliado, sync MP.

---

## 12. Decisões em aberto **[ABERTO]**

1. **Grau de liberdade:** (A) só escolher a montagem → receita canônica fixa (estilo Magicka, mais simples); (B) montagem + escolher verbo + regular parâmetros (estilo Oblivion, mais livre — **proposta deste doc**); (C) montagem + parâmetros mas verbo fixo por montagem (meio-termo). **Proposta: B.**
2. **Forja durante a batalha?** Sugestão: só fora; no planejamento escolhe do grimório. Confirmar.
3. **Limite de magias no grimório** (sugestão: 12 por personagem).
4. **Escala exata de custo/ticks** (§6) — calibrar no simulador; o princípio (custo cresce com poder) é o que importa.
5. **Os 5 pares com Físico** (§3.3) — nomes/efeitos propostos; confirmar a temática e se entram já neste corte ou depois.
6. **Compartilhar magias entre jogadores** — futuro, fora deste corte.

## 13. Fora deste corte

- Mais de 2 elementos por magia (Magicka usa 5; aqui o teto é 2, como o `SpellDef` atual).
- Reações mágicas (contrafeitiço, escudo arcano) — GDD-019 §7.
- Resistência mágica por WIS com fórmula — GDD-016.
- Compra de efeitos com ouro/loot de receitas — a Forja é inata (todo ser vivo conjura).