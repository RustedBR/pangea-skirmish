# GDD-027: Tutorial Completo — aprender jogando + banco de testes das mecânicas

| Campo | Valor |
|-------|-------|
| **Status** | Rascunho — proposta para aprovação do Marcus |
| **Realiza** | GDD-018 §8 ("Tutorial com combate contra uma fera, um conceito por vez") e §4 ("Tutorial: sim") |
| **Depende de** | GDD-014 (ticks), GDD-016 (atributos), GDD-017 (reações), GDD-018 (UX), GDD-019 (magia), GDD-023 (incremento), GDD-024 (coop) |
| **Prepara** | GDD-026 (Forja de Magias) — a Forja ganha uma lição própria quando existir |

> **[CANON]** = decidido pelo Marcus. **[PROPOSTA]** = a confirmar. **[ABERTO]** = pendente.

---

## 1. Objetivo duplo **[CANON: o pedido]**

1. **Ensinar o jogo inteiro** a um jogador novo, **um conceito por vez**, de forma clara (o texto de dica atual é confuso — substituir por lições guiadas).
2. **Servir de banco de testes**: como o tutorial roda o jogo real e obriga o jogador a executar cada mecânica, **completar o tutorial exercita (testa) todas as mecânicas**. Cada lição registra se a mecânica funcionou → um **relatório de teste** no fim.

Isso resolve dois problemas de uma vez: onboarding do jogador + verificação de que nada quebrou.

---

## 2. Princípios de UX **[PROPOSTA]**

- **Uma coisa por vez.** Cada lição ensina UM conceito, com frase curta e direta.
- **Aprender fazendo.** A lição só avança quando o jogador **executa** a ação (não só lê). Gate por evento real do jogo.
- **Mostrar onde.** Destaque visual (anel/realce) no elemento de UI ou tile do mundo com que interagir.
- **Resultado concreto.** Depois da ação, mostrar o número/efeito (como GDD-019 §9): "Brasa causou 9 de dano + Queimando".
- **Sem beco sem saída.** Sempre há "pular lição" e "repetir"; o tutorial nunca trava o jogo.
- **Mobile-first.** Alvos grandes, texto legível, sem depender de hover (GDD-022).
- **Ritmo.** O tutorial pausa o fluxo entre lições (prompt → ação → feedback → próxima), sem pressa.

---

## 3. Arquitetura **[PROPOSTA]**

Aditiva: arquivos novos + ganchos mínimos (eventos) no código existente. **Não refatorar a UI** (histórico de regressões).

### 3.1 Componentes novos

- **`TutorialDirector`** (MonoBehaviour): dirige a sequência de lições na batalha de tutorial. Escuta eventos do jogo, mostra prompts, verifica o gate de cada lição, avança, e registra o resultado.
- **`TutorialLesson`** (dado): `id`, `capítulo`, `título`, `texto` (instrução), `alvoDoRealce` (elemento UI ou tile), `setup` (opcional: posicionar unidades, dar Mana, spawnar fera), `gate` (condição de conclusão = evento/predicado), `asserção` (o que verificar p/ o teste), `feedback` (texto do resultado).
- **`TutorialOverlay`** (UI Toolkit): painel de instrução (reusa a linguagem visual do jogo), realce no alvo, indicador de progresso "Lição X/N", botões Pular/Repetir. Pode reusar `BattleHUD.ShowChoiceTitled`/`ShowPrompt` para os prompts, com um painel dedicado para o realce.
- **`TutorialTestLog`**: registra por lição `{mecânica, resultado: OK/FALHOU/PULADA, detalhe}`. No fim, dump num relatório (log + arquivo). É o payoff de "testar todas as mecânicas".
- **`TutorialScenario`**: monta a batalha controlada (loopback solo vs uma fera fraca, seed fixa), com o estado inicial de cada lição.

### 3.2 Ganchos no código existente (mínimos, aditivos)

Eventos leves que o TutorialDirector escuta (sem mudar a lógica):
- `PlanningController`: ação enfileirada (tipo: mover/atacar/magia/reação), round confirmado, desfazer.
- `RoundManager`: round resolvido, reação disparada (`AskReaction`), magia resolvida, descanso, descida de camada.
- `SpellResolver`: magia conjurada (efeito, dano, status).
- `CameraController`: câmera movida/zoom/rotação.
- `BattleHUD`: unidade inspecionada.

Onde não houver evento, o TutorialDirector usa **predicado polled** (checa o estado a cada frame) — sem侵入 no código quente.

### 3.3 Determinismo / MP

O tutorial é **solo loopback** (`IsLocalContentSession`), então não há sync MP a preservar. Mas as lições rodam o **mesmo código** do jogo real (RoundManager/SpellResolver/etc.) — por isso testam de verdade. A seed é fixa para o cenário ser reproduzível.

---

## 4. As lições (capítulos) **[CANON: cobrir todas as mecânicas]**

Cada capítulo = uma mecânica central; cada lição dentro = um passo. O jogador pratica contra a fera controlada.

| Cap. | Mecânica | Lições (passos) | O que testa |
|---|---|---|---|
| **0** | Orientação | mover câmera (pan/zoom/rotate); inspecionar unidade; ler a barra de ticks | CameraController, BattleHUD.ShowUnitInfo, layout |
| **1** | Planejamento semi-turno | o que é um tick; enfileirar 1 ação; confirmar round; desfazer | PlanningController, ticks, intent band |
| **2** | Movimento | andar (MoveStep/AGI); alcance de movimento; terreno | pathfinding, GridManager, MoveStep |
| **3** | Ataque | ataque melee (STR); ataque à distância (DEX); chance de acerto; Golpe poderoso (+1 tick, Stamina); Mirar | AttackResolver, escala STR/DEX, Stamina |
| **4** | Reações | defender (Bloquear por VIT / Esquivar por AGI); contra-ataque; interpor; o grupo único de reação (1/round); menu de reação | RoundManager.AskReaction, ConsumeReaction, GDD-017 |
| **5** | Magia — básico | Mana vs Stamina; conjurar Brasa (Fogo); custo; ticks de conjuração; interrupção por dano | SpellResolver, Mana, interrupção GDD-019 |
| **6** | Magia — avançado | área (Explosão); status (Queimando/Encharcado); zona (Magma/Lama); barreira (Muralha); cura (Bálsamo, toca aliado); teleporte (Passo do vento); incrementos (Potência/Área/etc.); Concentrar; embotamento | todos os verbos SpellEffect, SpellBoosts, regen Mana, IsDulled |
| **7** | Magia Física | Fúria (STR); Salto (AGI); Pele de pedra (VIT); Curva; metade Stamina | SpellEffect.Prep*, custo híbrido |
| **8** | Atributos & progressão | os 6 atributos (Corpo/Mente); XP → nível → pontos; editar personagem; orçamento 15+5×(nível−1) | PlayerProfile, CharCreationHUD, BudgetFor |
| **9** | Coop | camadas de descida; descanso (recuperar); loot (baú); descer | RoundManager.BeginNextLayer/ShowRestMenu, Loot |
| **10** | Forja (futuro) | criar uma magia simples e uma par; custo derivado; grimório | SpellForge (quando GDD-026 existir) |

> A ordem é a do aprendizado natural (orientação → planejamento → movimento → ataque → reação → magia → progressão → coop). **[ABERTO]** se o Marcus quer outra ordem ou capítulos opcionais.

---

## 5. O cenário de tutorial **[PROPOSTA]**

- **Batalha solo loopback** contra **1 fera fraca** (BeastAI), seed fixa, mapa pequeno e plano.
- A fera é **controlada** para o tutorial: tem HP suficiente para sobreviver às lições de ataque/magia, ataca em momentos scriptados para provocar reações, e não mata o jogador.
- O `TutorialScenario` ajusta o estado por lição: enche Mana/Stamina antes das lições de magia, posiciona a fera para o ataque, spawna uma 2ª fera/aliado para as lições de área/cura/interpor.
- O personagem do tutorial é **pré-montado** (atributos que mostram bem cada mecânica: STR e INT razoáveis, Mana/Stamina cheias), para o jogador não precisar criar personagem antes de aprender. **[ABERTO]** usar um personagem fixo de tutorial ou deixar criar.

---

## 6. O banco de testes (payoff) **[PROPOSTA]**

- Cada lição tem uma **asserção**: a mecânica executou corretamente? (ex.: "Brasa causou dano > 0 e aplicou Queimando"; "Esquivar reduziu o dano"; "Muralha bloqueou movimento").
- O `TutorialTestLog` acumula `{capítulo, mecânica, OK/FALHOU/PULADA, detalhe}`.
- No fim do tutorial: **relatório** na tela + arquivo (`Logs/tutorial-test-report.json`), ex.:
  ```
  Movimento ......... OK
  Ataque melee ...... OK
  Reação Esquivar ... OK
  Magia Brasa ....... OK
  Magia área ........ FALHOU (aliado atingido — não deveria)
  ...
  ```
- **Modo headless (MCP):** dá para dirigir o tutorial via `execute_code` (executar as ações de cada lição) e coletar o relatório sem jogador — vira um **teste de regressão automatizado** de todas as mecânicas. Isso complementa a suíte EditMode/PlayMode existente.

---

## 7. UI do tutorial **[PROPOSTA]**

- **Painel de lição** (canto inferior ou lateral, mobile-friendly): título + instrução curta + "Pular" / "Repetir".
- **Realce**: anel pulsante no elemento de UI alvo (botão Andar, aba Magia) ou no tile do mundo (célula para mover, inimigo para atacar).
- **Progresso**: "Capítulo 3 · Lição 2/4" + barra.
- **Feedback**: após a ação, nota com o resultado concreto ("9 de dano + Queimando por 2 rounds").
- Reusa `BattleHUD.ShowChoiceTitled`/`ShowPrompt`/`ShowPlanNote` onde possível; painel dedicado só para o realce + progresso.
- **Acesso**: botão "Tutorial" no MainMenu (abre o cenário de tutorial) + opção "pular tutorial" para quem já sabe. **[ABERTO]** tutorial obrigatório na 1ª vez ou opcional.

---

## 8. Plano de implementação (fases) **[PROPOSTA]**

1. **Framework:** `TutorialDirector` + `TutorialLesson` + `TutorialOverlay` + `TutorialTestLog` + ganchos de evento. Teste EditMode do director (avança lição no gate, registra resultado).
2. **Cenário:** `TutorialScenario` (loopback vs fera controlada, seed fixa, setup por lição).
3. **Capítulos 0–2** (orientação/planejamento/movimento) — primeira fatia jogável.
4. **Capítulos 3–4** (ataque/reações).
5. **Capítulos 5–7** (magia básica/avançada/física).
6. **Capítulos 8–9** (progressão/coop).
7. **Relatório de teste** + modo headless MCP (teste de regressão automatizado).
8. **(Futuro) Capítulo 10** — Forja (quando GDD-026 implementar).

Cada fase compila + testa antes da próxima. A fatia jogável (fase 3) já permite ao Marcus sentir o formato e ajustar o tom das lições cedo.

---

## 9. Decisões em aberto **[ABERTO]**

1. **Ordem dos capítulos** — proposta é a do aprendizado natural; confirmar.
2. **Personagem do tutorial** — fixo pré-montado (proposta) ou deixar criar.
3. **Tutorial obrigatório na 1ª vez** ou opcional (proposta: opcional, botão no menu).
4. **Tom do texto** — direto e curto (proposta), com um toque da lore pré-histórica (GDD-013) ou neutro.
5. **Escopo do 1º corte** — todos os capítulos de uma vez ou entregar a fatia 0–2 primeiro e iterar (proposta: fatia primeiro).

## 10. Fora deste corte

- Tutorial de PvP multiplayer real (o tutorial é solo loopback; MP é testado à parte).
- Forja (capítulo 10) até o GDD-026 implementar.
- Localização/i18n (textos em PT-BR por enquanto).