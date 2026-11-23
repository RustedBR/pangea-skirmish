# GDD-027 — Anexo: design por capítulo do tutorial

| Campo | Valor |
|-------|-------|
| **Status** | Proposta para aprovação do Marcus |
| **Anexo de** | GDD-027 (Tutorial completo) |
| **Depende de** | GDD-004, GDD-007, GDD-014, GDD-016, GDD-017, GDD-018, GDD-019, GDD-020, GDD-022, GDD-023, GDD-024 |
| **Artboards** | Cap0…Cap9.dc.html, Main.dc.html, Feedback.dc.html, Report.dc.html (vários não existem no caminho do scratchpad: ver §5) |

> **[CANON]** = decidido pelo Marcus. **[PROPOSTA]** = a confirmar. **[ABERTO]** = pendente. **[A CONFIRMAR]** = depende de checar o código ou jogar no Unity.

Todos os números citados nos textos (dano, vida, Mana, alcance) são **lidos em runtime** (`feedbackFn`). Os valores deste documento são os do `GameTuning.asset` atual e servem só de referência.

---

## 1. Resumo **[PROPOSTA]**

O que muda em relação ao tutorial atual (27 lições, a maioria demonstração com `Cast()` no setup e gate que fecha no 1º frame):

1. **Overlay novo.** O `TutorialOverlay` sai do pergaminho em px fixos e passa a usar 3 tokens em % da tela (card-esq, card-dir, feedback), barra de progresso por capítulo e "Lição x de y" dentro do capítulo.
2. **Spotlight.** Véu escuro + anel âmbar (1 por vez) no elemento de UI, na unidade ou no tile do mundo, com etiqueta "Clique aqui" / "Toque aqui" pelo último input, e "Veja aqui" quando não há o que clicar.
3. **Ações apagadas, nunca escondidas.** As 9 ações (Andar 1 … Reação 9) aparecem sempre. As fora do foco recebem a classe `bh-abtn--off` que já existe. `BuildActionBar` deixa de fazer `RemoveAll`.
4. **Aprender fazendo.** Todo gate é um evento real (o jogador executa). Nenhuma lição conjura ou age no setup.
5. **Feedback com número.** `TutorialLesson.feedbackFn: Func<string>` monta o texto com o resultado medido ("Brasa: 11 de dano + Queimando").
6. **Mapa de capítulos e checkpoint.** 10 capítulos, 60 lições, checkpoint salvo no fim de cada capítulo ("Continuar do Capítulo N").

### 1.1 Mapa de capítulos (nomes únicos) **[PROPOSTA]**

Tabela única: vale para o header do card, o relatório (Report.dc.html), o menu e as outros.

| Cap. | Nome curto (header/relatório) | Nome longo | Lições | Min | Opcionais (caminho curto) |
|---|---|---|---|---|---|
| 0 | ORIENTAÇÃO | Orientação | 5 | 3,5 | — |
| 1 | PLANEJAR | Planejamento semi-turno | 4 | 3 | — |
| 2 | MOVIMENTO | Movimento | 5 | 4 | 2.5 (oculta até o BFS) |
| 3 | ATAQUE | Ataque | 5 | 6 | — |
| 4 | REAÇÕES | Reações | 9 | 10 | 4.6, 4.8 |
| 5 | MAGIA | Magia — básico | 5 | 5 | — |
| 6 | ELEMENTOS | Magia — elementos e Mana (6A/6B) | 10 | 11 | 6.10 [PROPOSTA] |
| 7 | CORPO | Magia Física | 5 | 7 | — |
| 8 | PROGRESSÃO | Progressão | 5 | 5 | — |
| 9 | COOP | Coop — a descida | 7 | 6 | — |
| **Total** | | | **60** | **~60** | caminho curto ~45 min |

> O Report.dc.html mostra hoje de 1 a 6 lições por capítulo: atualizar para esta tabela. O Cap. 10 (Forja, GDD-026) entra quando existir.

### 1.2 Renumeração

Com as fusões, as lições ficam numeradas assim. Referências antigas em outros textos (0.4/0.5/0.6, 3.6, 6.1, 8.6) precisam ser atualizadas:

- Cap. 0: zoom e giro fundidos → inspecionar = **0.3**, barra = **0.4**, linhas = **0.5**.
- Cap. 3: a lição de chance de acerto vira passo da 3.1 → Mira firme = **3.5**.
- Cap. 4: Desequilibrar entra como **4.1b**.
- Cap. 6: a antiga 6.1 (Brasa/Queimando) sai → Encharcado = **6.1**.
- Cap. 8: a lição "xp" sai → salvar = **8.5**.

---

## 2. Padrões globais do overlay **[PROPOSTA]**

Regra do Marcus **[CANON]**: tamanhos e posições de UI sempre em % da tela (`Length.Percent`), nunca px fixo. Onde o texto abaixo cita px, é só o mínimo de alvo de toque (≥ 44 px) convertido em %.

### 2.1 Tokens de layout

| Token | Desktop | Celular | Uso |
|---|---|---|---|
| **card-esq** | 30% L · 2,5% da esquerda · 6% do topo · altura máx. 55% | 40% L · 1% · 12% (abaixo de Voltar/Tela cheia) [A CONFIRMAR] | Padrão |
| **card-dir** | 30% L · 2,5% da direita · 6% do topo | 40% L · 1% da direita · 12% | Quando uma janela ocupa a esquerda: REAGIR, INCREMENTAR, MAGIA, editor, baú, ficha fixa do celular |
| **feedback** | 36% L, centralizado, altura máx. 50% | variante móvel [A CONFIRMAR] | Card de resultado / falha / capítulo concluído |
| **folga do anel** | bounds do alvo + 0,6% da altura | igual | Único valor (0,8% sai) |
| **offset da etiqueta** | 1,5% acima (ou ao lado) do alvo | igual | Único valor |
| **chip de resolução** | posição do card-esq, ~14% L | igual | "Resolvendo · Tick k de 4" |
| **botão primário** | 100% da largura do card, altura mín. 5–6% | ≥ 44 px | "Começar capítulo N", "Editar personagem" |

Referências de HUD real (USS atual): grade de ações 1–7 com 14,4% L, 0,6% da esquerda, 2,8% da base (20% no celular); painel de intenções com a stance-row (8–9) com 14,4% L, 0,6% da direita, 1,1% da base; barra de ticks com 40,6% L, centralizada, 1,1% da base; intent-band de 29,7% a 70,3%; bússola com 4,4% L (6% no celular), 1% do topo, 0,6% da direita.

**[ABERTO]** O card-esq termina em 32,5% e invade a intent-band (29,7%). Proposta: no tutorial a band vai de 34% a 70,3%. Alternativa: escondê-la.
**[ABERTO]** O card-dir colide com a bússola. Esconder a bússola sempre que card-dir estiver em uso, ou descer o card-dir?

### 2.2 Card de lição

- Header: `CAPÍTULO N · NOME CURTO` + "Lição x de y".
- Título, instrução (rich text `<b>` do UI Toolkit), checklist de passos (`TutorialOverlay.SetStep(i, done)`).
- Rodapé: **Repetir** e **Pular**. Durante a resolução de um round, o card vira o chip de resolução sem Repetir/Pular.
- Barra de progresso: 1 segmento por capítulo, **calculado** do `TutorialScenario` (10 hoje, 11 com a Forja), com meio segmento para sub-blocos (6A/6B). Feitos em verde, atual em âmbar.

### 2.3 Spotlight

- Véu 100% × 100% sobre o mapa; a HUD fica acima do véu. Recorte na unidade quando o alvo é do mundo.
- **1 anel âmbar por vez**, só no alvo do passo atual. Tipos: elemento de UI (`worldBound`), unidade (projeção, ~8% da altura), tile (quadrilátero projetado, recalculado a cada frame), seta de borda (alvo fora do viewport, 5% × 9%).
- Etiqueta: "Clique aqui" ou "Toque aqui" pelo último input (`PointerInput.IsTouch` [A CONFIRMAR se expõe]); "Veja aqui" só quando não há o que clicar. Saem "Comece aqui", "Ande até aqui", "Leia aqui", "Alvo" etc.
- **Dica de ociosidade:** 20 s sem input → o anel/ícone pulsa (`TutorialLesson.idleHint`).

### 2.4 Barra de ações — 3 estados visuais

| Estado | Visual | Quando |
|---|---|---|
| **Em foco** | âmbar, fundo #2A2418, anel | o alvo do passo atual |
| **Liberado sem foco** (novo) | borda #4A5650, texto #EDE6D6, sem glow | liberado mas não é o passo atual (ex.: Desfazer/Limpar na 1.3) |
| **Apagado** (`bh-abtn--off`) | #5E6A64 sobre #1E2522 (hoje rgb(92,98,122): corrigir) | fora do `Allow` |

Rótulos únicos: **Andar 1 · Atacar 2 · Mirar livre 3 · Magia 4 · Concentrar 5 · Respirar 6 · Esperar 7 · Postura 8 · Reação 9**. Layout segue o código (grade 1–7 no canto inferior esquerdo, 8–9 na stance-row). O Main.dc.html (Mover/Atacar/Mirar/Magia/Esperar/Postura em 1–6, barra centralizada) deve ser corrigido para isso.

### 2.5 Card de feedback e de falha

- **Verde (resultado):** texto do `feedbackFn` com números. Botão "Continuar" (Espaço/Enter ou toque).
- **Laranja (erro do jogador):** "Tentar de novo", refaz o setup; **não** registra FALHOU (`retryIf`).
- **Vermelho (asserção falhou):** mensagem técnica, registra FALHOU no relatório; oferece Tentar de novo e Pular.
- **Dica inline (`onWrong`):** texto curto no próprio card, sem fechar nem falhar (ex.: "Esse já é o próximo round").
- Pular sempre registra PULADA e deixa o estado consistente para a lição seguinte (o setup da próxima é idempotente).

### 2.6 Teclas

| Tecla | Papel no tutorial |
|---|---|
| **Espaço** | só avança o card de feedback (`BlockPlanInput = true` em Feedback e em passo de tick). Não é ensinado como confirmar |
| **Enter** | confirmar o round (o único ensinado) |
| **Esc** | Desfazer o último bloco |
| **1–9** | as 9 ações |
| **Q / E** | girar a vista |
| **Repetir / Pular** | só botões (sem atalho, para não colidir com ações) [PROPOSTA] |
| **1..n em janelas de escolha** | **[ABERTO]** atalho global (REAGIR, INCREMENTAR, RECOMPENSA, DESCANSO). Até aprovar, nenhum texto cita tecla em janela |

### 2.7 Mobile

- Inspecionar = duplo toque (regra do `PointerInput`, GDD-022; o ✓ do MobileHUD só aparece com ação armada).
- No tutorial o ✓ do MobileHUD se chama "✓ Pôr na fila" (não colide com o Confirmar do round) [ABERTO: só no tutorial ou no jogo todo].
- btn-confirm, btn-undo e opções de menu sobem para ≥ 10% da altura / ≥ 44 px (hoje 32–36 px).
- Nada depende de hover: dicas de chips e do editor abrem por toque.

### 2.8 Formato do texto

- Rich text `<b>` do UI Toolkit (sem markdown nem `<b>` HTML misturado).
- Padrão de ação: `<b>Ação</b> (tecla N ou toque)`.
- Intro e outro: só texto ao jogador, sem spec de UI nem header.
- **Checklist = ações do jogador.** O resultado observado vai para o feedback.
- Termo único: **Contra-atacar** (nunca "golpe de oportunidade"). Incremento de mira: **Mira firme**. Arma: nome de exibição do `WeaponCatalog` (Machado).

### 2.9 Paleta — tokens novos

Acrescentar ao sistema e trocar nos artboards: `--plan-block` (proposto #33403A, borda #4A5650, texto #FFF8EA), `--mana`, `--stamina`, `--hp`, `--el-fogo`, `--el-agua`, `--el-terra`, `--el-ar`, `--el-magico`, `--el-fisica`, `--danger-text`. Destino de andada = tracejado 2px no accent #F2B544. Saem #4A90E2, #2B4F86, #6FA8FF, os salmões #F6C4B0/#F3D3C4/#F3B9A2 e o muted #8A9690.

### 2.10 Invariantes globais (ligadas no Cap. 0, valem no tutorial inteiro)

| API | Efeito |
|---|---|
| `TutorialHud.FreezeClock` | congela o relógio de decisão (60 s) e o de reação (18 s). Desligado só na 4.6 |
| **hold-tick** do RoundManager (controlado pelo TutorialHud) | segura o round com o card aberto e faz o "Avançar tick" da 5.3. Nunca `Time.timeScale` |
| `TutorialBeast.Mode {Free, Idle, Script(lista), ScriptedHit(dano)}` | substitui FeraPassive/FeraHold/FeraScript |
| `TutorialHud.HeroHpFloor = 1` | `Unit.TakeDamage` não leva o herói abaixo de 1 com `TutorialHud.Active` |
| `TutorialHud.BlockPlanInput` | em Feedback ou passo de tick, `HandleIntentInput` ignora Enter/Espaço/Backspace/Esc |
| `TutorialHud.Allow(categoria, ids)` | categorias: actions, intents/round (confirm/undo/clear), reactions, spells, boosts (`SpellBoost?`: null = todos, None = pula o menu), choice |
| `TutorialPlan` | Clear, EnqueueWait, EnqueueMove, FillWait, AppendSpell |
| `TutorialEvents` (estática) | todos os eventos (§4.2) |
| `TutorialGates.RoundResolvedSince(tick0)` | `Phase == Planning && GlobalTick ≥ tick0 + ticksPerRound`. **Nunca** usar `IsEngaged` (falso positivo com Desfazer/Limpar) |
| `TutorialProgress.Save(chapterDone)` | checkpoint por capítulo [ABERTO: PlayerProfile ou PlayerPrefs] |

**Estado da fera:** desarmada nos Cap. 0–3; **Garras permanentes + STR 10 a partir do Cap. 4**; quem decide quando ataca é o `TutorialBeast`. Vida sempre `stats.MaxHP` (54), nunca 300.

---

## 3. Capítulos

Legenda da tabela: **status** = existe / ajustar / nova. Gate e teste resumidos; o detalhe vem nas fichas.

---

### Cap. 0 — Orientação (~3,5 min)

**Objetivo.** Deixar o jogador à vontade antes da 1ª ação: câmera (pan, zoom, giro), ficha compacta (vida/Mana/Stamina) e barra de ticks. Relógio congelado; nenhum tick resolve. O Cap. 0 é dono de "o round tem 4 ticks" e "todos decidem juntos".

**Intro.** "Antes de lutar, aprenda a olhar. Você vai mover a câmera, examinar a Fera de Treino e ler a barra de ticks, que é o relógio da luta."
**Outro.** "Você já sabe olhar o campo e ler o tempo da luta. Progresso salvo. No Capítulo 1 você coloca a sua primeira ação nessa barra."

**Setup.** Batalha do `TutorialLauncher.Attach` (solo loopback). Herói em (16,15), atributos 5, Machado. Fera de Treino em (20,15), desarmada, STR 3 VIT 8 DEX 3 AGI 3 INT 1 WIS 1, vida 54. Liga as invariantes da §2.10. `Allow("actions", [])` e `Allow("intents", [])`: as 9 ações e Confirmar/Desfazer/Limpar apagados. Setups por lição rodam de novo em Repetir.

| # | id | Título | Status | Gate | Teste |
|---|---|---|---|---|---|
| 0.1 | cam | Ache a fera | ajustar | `CameraPanned` + centro andou ≥ 2 células + fera no viewport | modo Manual, fera visível |
| 0.2 | zoom | Zoom e giro | nova (funde zoom+rotate) | 3 passos: aproximar ≥ 2, afastar ≥ 2, girar | zoom em [5,20], bússola = −Orientation, ninguém se moveu |
| 0.3 | inspect | Inspecione a fera | nova | `UnitInspected(Fera)` | painel visível, hp-text 54/54, 2 barras |
| 0.4 | bar | A barra de ticks | ajustar | `TickColumnClicked(col == 3)` | 4 + 2 colunas, opacidade ≥ 0,9 |
| 0.5 | rows | Vocês decidem juntos | nova | `TickRowClicked(Fera)` | 2 linhas "decide agora", fila da fera vazia |

**0.1 Ache a fera**
- Instrução: "Arraste o mapa até achar a <b>Fera de Treino</b>. No mouse, segure o <b>botão direito</b> e arraste (ou leve o cursor à borda da tela). No toque, arraste com <b>1 dedo</b>."
- Realce: sem véu; seta âmbar na borda apontando a fera, some quando ela entra na tela.
- HUD: `TutorialHud.None()`. Só mapa e card.
- Feedback: "Você achou a fera: ela está a <b>4 células</b> de você. Solte a câmera e, <b>2 s</b> depois, ela volta ao modo Auto."
- Falha: 20 s parado → seta pulsa e mão-fantasma; arrastar com o esquerdo → "Use o botão direito". Limiares [PROPOSTA].
- Impl.: setup `HideUnitInfo` + `SnapTo` (fera fora da tela [A CONFIRMAR o deslocamento]). `CameraController.HandleManualPan/HandleZoom` disparam `TutorialEvents.CameraPanned/CameraZoomed`. `TutorialOverlay.PointEdge(Unit)`.

**0.2 Zoom e giro**
- Instrução: "Aproxime e depois afaste a câmera com a <b>roda do mouse</b> (no toque, <b>pinça</b> com 2 dedos). Depois gire a vista: <b>Girar</b> (tecla Q ou E, ou clique ou toque na bússola)."
- Realce: ícone de gesto no centro; depois anel na bússola com <kbd>Q</kbd> <kbd>E</kbd>.
- HUD: novo `TutorialHud.Camera()` = None + flag nova `Compass` (hoje a bússola some com a Topbar e, no celular, é o único jeito de girar).
- Feedback: "Você aproximou <b>40%</b>, voltou e girou a vista <b>90°</b>. Perto para mirar, longe para ver o campo. O <b>N</b> da bússola mostra o norte, e ninguém saiu do lugar." (% e graus medidos.)
- Falha: no limite 5 → "Esse é o mais perto; agora afaste". Q/E no cooldown de 0,45 s não conta como erro.

**0.3 Inspecione a fera**
- Instrução: "Abra a ficha da <b>Fera de Treino</b>: <b>clique</b> nela, ou <b>toque duas vezes</b> nela no celular."
- Realce: anel na fera, véu com recorte.
- HUD: ficha liberada, 9 ações apagadas, topbar visível, bússola oculta.
- Feedback: "Fera de Treino: <b>54/54</b> de vida." (Os 6 atributos ficam para a 8.1.)
- Falha: clicou no herói → "Esse é você. Procure a Fera de Treino."; 1º toque no celular → "Toque de novo no mesmo ponto".
- Impl.: `BattleHUD.InspectedUnit` (getter); `ShowUnitInfo` dispara `UnitInspected`. Setup chama `HideUnitInfo` porque o RoundManager abre a ficha do herói sozinho. `SetTutorialVisibility` só mostra o painel com unidade. Posição automática da ficha evita `TutorialOverlay.CardRect`. No celular a ficha é fixa à esquerda → card-dir.

**0.4 A barra de ticks**
- Instrução: "Esta é a <b>barra de ticks</b>, o relógio da luta. A linha vertical fecha o <b>round</b>. Clique ou toque no <b>último tick</b> deste round."
- Realce: anel na ticks-bar; a coluna "4" não é realçada.
- Feedback: "Certo: o round tem <b>4 ticks</b> e fecha na linha."
- Falha (`onWrong`): "próx"/"2" → "Esse já é o próximo round."; 1–3 → "Ainda tem tick depois desse."; 2 erros → a linha pisca.
- Impl.: texto atual diz "Em cima" (a barra fica embaixo). Gate passivo (`display != None`) sai. Labels da régua ganham `name "tick-col-k"` e `pickingMode Position` com tutorial ativo. Forçar opacity 1 nos painéis em foco (o fade de câmera do BattleHUD zera a barra). Não citar carry-over aqui.

**0.5 Vocês decidem juntos**
- Instrução: "Cada linha é uma unidade. Clique ou toque na linha da <b>Fera de Treino</b>."
- Feedback: "<b>2 unidades</b> decidem agora: você e a fera. Nenhuma vê o plano da outra até o round começar."
- Falha: tocou a própria linha → "Essa é a sua linha. Procure o nome Fera de Treino."
- Impl.: nome da linha com `pickingMode Position`; evento `TickRowClicked`. Fim do capítulo: checkpoint.

---

### Cap. 1 — Planejar (~3 min)

**Objetivo.** Cada ação ocupa ticks; pôr na fila, desfazer o último bloco e confirmar um round de 4 ticks. Desfazer vem antes de Confirmar porque a fila confirmada não cancela (GDD-014 §3; GDD-018 §1.2 [CANON]). A 1.1 só retoma a 0.4 ("você viu que o round tem 4 ticks").

**Intro.** "Hora de montar seu primeiro plano: pôr uma ação na fila, desfazer e confirmar o round."
**Outro.** "Você viu cada ação ocupar ticks, desfez um bloco e confirmou um round inteiro. Progresso salvo. Próximo: Movimento, até onde uma andada chega e o que o terreno muda."

**Setup.** Mesma batalha. `TutorialPlan.Clear()`, `carriedTicks = 0`, `TutorialBeast.Idle` (fera vira Esperar via `ResetPlan`), `FreezeClock`. Tile sugerido (16,13) [A CONFIRMAR se andável]. ticksPerRound 4, maxQueueTicks 4, ticksMove 1, ticksWait 1, andada 7 com AGI 5.

| # | id | Título | Status | Gate | Teste |
|---|---|---|---|---|---|
| 1.1 | tick | Cada ação ocupa ticks | ajustar (era "sched") | 1º bloco = Wait | Duration = 1, remainingAP = 3 |
| 1.2 | undo | Desfaça o último bloco | nova | `PlanUndone` | count −1, AP devolvido |
| 1.3 | plan | Enfileire uma andada | ajustar | bloco Move existe | Duration 1, destino alcançável |
| 1.4 | confirm | Confirme o round | ajustar | `PlanConfirmed` → `RoundResolvedSince` | +4 ticks, herói no destino, fera intacta |

**1.1 Cada ação ocupa ticks**
- Instrução: "Cada ação ocupa ticks na barra. Use <b>Esperar</b> (tecla 7 ou toque) e veja o bloco ocupar 1 tick."
- Realce: `abtn-wait`; depois "Veja aqui" na linha do herói, coluna 1.
- HUD: `Allow("actions", {wait})`, `Allow("round")` sem ids (os 3 botões apagados e visíveis).
- Feedback: "Esperar ocupou <b>1 tick</b>. Seu plano: <b>1 de 4</b> ticks, sobram <b>3</b> neste round." (Shift+7 ×4 → "round cheio".)
- Falha: clique no chão/fera é ignorado (`FreeClick` passa a respeitar o filtro) com nota "Nesta lição, use <b>Esperar</b> (tecla 7 ou toque)".

**1.2 Desfaça o último bloco**
- Instrução: "Mudou de ideia? Use <b>Desfazer</b> (Esc ou toque) para tirar o último bloco. Só funciona antes de confirmar."
- Realce: `btn-undo`; no feedback, "Veja aqui" no `btn-clear` (sem anel, travado).
- Feedback: "Bloco removido: o plano voltou a <b>0 de 4</b> ticks. <b>Desfazer</b> tira só o último bloco; <b>Limpar</b> tira o plano inteiro. Depois de confirmar, nenhum dos dois funciona."
- Falha: Esc com plano vazio não fecha (setup garante 1 bloco com `EnqueueWait`).
- Impl.: `PlanUndone` disparado em `OnUndoClick`. Backspace funciona mas não é ensinado.

**1.3 Enfileire uma andada**
- Instrução: "Use <b>Andar</b> (tecla 1 ou toque) e depois o tile marcado. No celular, confirme no <b>✓ Pôr na fila</b>. A andada vira 1 bloco na barra."
- Realce: `abtn-move`, depois o tile (16,13) (anel de tile do mundo).
- HUD: Andar em foco; Desfazer e Limpar "liberados sem foco"; Confirmar apagado.
- Feedback: "Andar ocupou <b>1 tick</b> e leva você <b>2 tiles</b> (até <b>7</b> com AGI 5). Plano: <b>1 de 4</b> ticks. Nada acontece até você confirmar."
- Falha: fora do alcance → nota existente "Longe demais…"; outro tile válido é aceito.

**1.4 Confirme o round**
- Instrução: "Seu plano usa <b>1 de 4</b> ticks. Use <b>Confirmar</b> (Enter ou toque) para o tempo andar. Depois disso o plano não muda."
- Realce: `btn-confirm`; durante a resolução, "Veja aqui" na coluna do tick atual; card vira chip "Resolvendo · Tick k de 4".
- Feedback: "Round 1 resolvido em <b>4 ticks</b>. Sua andada aconteceu no fim do <b>tick 1</b>: você andou <b>2 tiles</b>."
- Falha: fera agiu → FALHOU "fera agiu no Cap. 1". Pular confirma via `ConfirmPlan()` e espera o round.
- Impl.: gate atual (`IsEngaged` true→false) sai. `RoundManager.GlobalTick` público. Fim do capítulo: card "Capítulo 1 concluído", botão "Começar capítulo 2", 4 linhas no relatório, checkpoint.

---

### Cap. 2 — Movimento (~4 min)

**Objetivo.** Até onde vai uma andada (EffectiveMoveStep = 7 com AGI 5), encadear andadas no round e terreno real: degrau (maxStepClimb 1) e pedras. **Sem magia.** A Lama vai para o Cap. 6.

**Intro.** "Você já colocou uma andada na fila. Agora descubra até onde ela leva e o que o chão faz com o seu passo."
**Outro.** "Você sabe até onde anda, como ir mais longe num round e como o terreno muda o caminho. Próximo: bater na fera e ler cada número do golpe."

**Setup.** Mapa 30×30 plano. Fera longe (26,24), `TutorialBeast.Idle`. Reset por lição: `TutorialPlan.Clear`, `TeleportTo(start)` (ex.: (12,15)), restaurar células alteradas, capturar `tick0`. 2.4: mirante M = start+(1,0) altura 2, degrau D = start+(1,1) altura 1 (`SetCell`). 2.5: 9 "Pedra 1" em x = start.x+2, marco em start+(5,0).

| # | id | Título | Status | Gate | Teste |
|---|---|---|---|---|---|
| 2.1 | reach | Seu alcance | nova | `Mode == MovePicking` | EffectiveMoveStep = fórmula; área ≤ step |
| 2.2 | moved | Ande até o marco | ajustar | `RoundResolvedSince` && anchor == marco (7) | Chebyshev 7, Duration 1 |
| 2.3 | chain | Vá mais longe | nova | snapshot do confirm com ≥ 2 andadas && no marco (12) | trechos ≤ 7, ticks ≤ 4 |
| 2.4 | climb | Suba pelo degrau | nova | no mirante, passou por D | CanStepBetween(start, M) == false |
| 2.5 | rocks | Pedras bloqueiam | nova, **opcional** | ≥ 2 andadas, nenhum passo em parede | alcance não contém pedra (**falha hoje**) |

**2.1 Seu alcance**
- Instrução: "Aperte <b>Andar</b> (tecla 1 ou toque). A área verde mostra até onde vai uma andada."
- Realce: `abtn-move`, depois "Veja aqui" no chip "move" da ficha.
- Feedback: "Uma andada cobre até <b>7 tiles</b>. Vem da sua AGI 5: 4 + 5 × 0,67 = 7."
- Falha: clique direto no chão → desfaz e "Primeiro aperte <b>Andar</b> (tecla 1 ou toque)".
- Impl.: getter `PlanningController.Mode`.

**2.2 Ande até o marco**
- Instrução: "Aperte <b>Andar</b> (tecla 1 ou toque) e escolha o marco na borda verde. No toque: toque no marco e confirme. Depois, <b>Confirmar</b> (Enter ou toque) fecha o round."
- Realce: `TutorialMarker` tracejado no marco (start+(0,−7)); depois `btn-confirm`.
- Feedback: "Andou <b>7 tiles</b> em <b>1 tick</b>: o máximo de uma andada."
- Falha: parou fora → laranja "Você parou a N tiles do marco". Gate antigo (`anchor != spawn`) já nascia verdadeiro.

**2.3 Vá mais longe**
- Instrução: "O marco está a 12 tiles e uma andada cobre 7. Faça duas: <b>Andar</b> (tecla 1 ou toque) até o meio do caminho, de novo até o marco, e <b>Confirmar</b> (Enter ou toque)."
- Feedback: "Você percorreu <b>12 tiles</b> com <b>2 andadas</b> e gastou <b>2 de 4 ticks</b> do round. Mesma casa no mesmo tick: as duas unidades rolam iniciativa (d20 + AGI + DEX), e quem tira mais chega primeiro."
- Falha: clique direto no marco → "Longe demais"; Desfazer/Limpar não fecham.

**2.4 Suba pelo degrau**
- Instrução: "O mirante tem 2 de altura, e cada passo sobe no máximo 1. Suba pelo degrau: <b>Andar</b> (tecla 1 ou toque) até o degrau, de novo até o mirante, e <b>Confirmar</b> (Enter ou toque)."
- Realce: etiqueta em D, depois em M, depois `btn-confirm`; selos de altura 0/1/2.
- Feedback: "O mirante tem <b>2</b> de altura e cada passo sobe no máximo <b>1</b>. Pelo degrau foram <b>2 passos</b>. Descer segue a mesma regra."
- Falha: ficou no chão → "Você ficou no chão: suba pelo degrau". Risco até o BFS: de 2+ tiles o alcance pinta M e a andada sobe 2 → gate detecta, card "Subiu 2 de uma vez: use o degrau" e registra bug.
- Impl.: só vizinhos, roda com o código atual. Restaurar as 2 células.

**2.5 Pedras bloqueiam (opcional)**
- Instrução: "Pedras bloqueiam o passo, e o marco está atrás delas. Contorne com duas andadas: <b>Andar</b> (tecla 1 ou toque), e depois <b>Confirmar</b> (Enter ou toque)."
- Feedback: "Em linha reta eram <b>5 tiles</b>; contornando as pedras, <b>10 passos</b>. Passou de 7, por isso foram 2 andadas."
- Impl.: flags `requiresPathfinding` e `optional`. Oculta para o jogador até o BFS (sem PULADA); no headless roda com "EM CONSTRUÇÃO". **Pré-requisito da fase 3:** `GetReachableAnchors` por BFS e `MoveToDestination` célula a célula.

---

### Cap. 3 — Ataque (~6 min)

**Objetivo.** Perto (STR), longe (DEX), Mirar livre, Golpe poderoso (com carry-over) e Mira firme, sempre com acerto e dano reais. A 3.3 apresenta a Stamina.

**Intro.** "A Fera de Treino está parada a dois passos de você. Hora de bater e de entender cada número que sai do golpe."
**Outro.** "Você bate de perto e de longe, lê a chance de acerto e gasta Stamina para bater mais forte. É a mesma Stamina que paga as reações: no próximo capítulo a fera revida."

**Setup.** Herói 45/45, Stamina 5/5, Machado, a 2 tiles da fera. Fera 54/54, Stamina 0 (não reage), desarmada, `TutorialBeast.Idle`. Clique direto no mapa desligado. Seed fixa. 3.4–3.5: Arco Curto (`Unit.SetWeapon("ShortBow")`), fera a 6 tiles e AGI 0. 3.3: Esperar ×3 pré-montado, golpe no tick 4.

| # | id | Título | Status | Gate | Teste |
|---|---|---|---|---|---|
| 3.1 | attack | Golpe corpo a corpo | ajustar (funde hitchance) | confirm com ataque na fera → round resolvido com hit/miss | strScales, DamagePerTile ≈ 5,2, PreviewHitPct = fórmula |
| 3.2 | freeaim | Mirar livre | nova | ataque Mode Tile → dano | Preview livre/normal ≈ 1,25 |
| 3.3 | power | Golpe poderoso | nova | ataque bônus no tick 4 → 2 rounds | Duration 2, custo 2, carriedTicks == 1 |
| 3.4 | ranged | Ataque à distância | nova | ataque à distância com dano | dexScales, esquiva 0 |
| 3.5 | aim | Mira firme | nova | Attack IsAimed → dano | custo 1, +5, sem tick extra |

**3.1 Golpe corpo a corpo**
- Instrução: "<b>Atacar</b> (tecla 2 ou toque) e clique ou toque na fera. Leia o % de acerto na prévia do plano. Depois <b>Confirmar</b> (Enter ou toque)."
- Checklist: Atacar · tocar a fera · ler o % na prévia (fecha por `PreviewHitPct`, não por leitura passiva de texto) · Confirmar.
- Realce: `abtn-attack` → fera → "Veja aqui" em `plan-preview` → `btn-confirm`.
- Feedback: "Machado acertou <b>{tiles} tiles</b>: <b>{dano}</b> de dano (54 → {hp}). STR 5 soma +30% por tile. Seu acerto era {hit}% (90% + 3% × DEX). Crítico: {crit}%, dano ×1,5. A fera esquiva {dodge}% (5% × AGI), e cada esquiva tira 1 tile do golpe."
- Falha: confirmou sem ataque → repete com dica.

**3.2 Mirar livre**
- Instrução: "<b>Mirar livre</b> (tecla 3 ou toque). Ponha a forma laranja sobre a fera e clique ou toque para fixar. Depois <b>Confirmar</b> (Enter ou toque)."
- Feedback: "Mira livre: <b>{dano}</b> de dano, contra ~{base} do clique na unidade (+25%)."
- Falha: forma não pegou a fera → laranja (`retryIf`). Bônus de acerto não é citado (herói está em 100%).

**3.3 Golpe poderoso**
- Instrução: "O plano já tem <b>Esperar</b> nos ticks 1 a 3. <b>Atacar</b> (tecla 2 ou toque) na fera, abra o bloco Atacar na barra de ticks e escolha <b>Golpe poderoso</b>. Depois <b>Confirmar</b> (Enter ou toque)."
- Realce: `abtn-attack` → fera → `plan-seg-1` → `blk-power` → `btn-confirm`; anel nos pips de Stamina na linha de Stamina.
- Feedback: "Golpe poderoso: <b>{dano}</b> de dano (×1,5). Custou 2 Stamina e durou 2 ticks: começou no tick 4, cruzou a linha (↷) e terminou no tick 1 do round seguinte." + "<b>Stamina</b> = fôlego do corpo: paga golpes fortes e reações; volta com <b>Respirar</b> (tecla 6)."
- Impl.: nomes `blk-*`, `plan-seg-{i}`; opções do menu do bloco 44 px+. Resolve a lacuna do carry-over.

**3.4 Ataque à distância**
- Instrução: "Você está com o Arco Curto. <b>Atacar</b> (tecla 2 ou toque) na fera, lá longe. Depois <b>Confirmar</b> (Enter ou toque)."
- Feedback: "Flecha a 6 tiles: <b>{dano}</b> de dano. De longe, DEX 5 soma +10% por tile; o tiro levou 2 ticks."

**3.5 Mira firme**
- Instrução: "<b>Atacar</b> (tecla 2 ou toque) na fera. No bloco Atacar da barra de ticks, escolha <b>Mira firme</b>. Depois <b>Confirmar</b> (Enter ou toque)."
- Feedback: "Mira firme somou <b>+5</b> de dano (DEX 5): {dano} em vez de ~{base}. Custou 1 Stamina e nenhum tick."
- Impl.: renomear o incremento "Mirar" → "Mira firme" na UI (código segue `IsAimed`). Saída: Machado, AGI original, fera recebe Garras (Cap. 4).

---

### Cap. 4 — Reações (~10 min)

**Objetivo.** Responder no tempo da fera: Bloquear, Desequilibrar, Esquivar, 1 reação por round, Contra-atacar (inclusive o risco), Interpor, Reação padrão (9), Postura (8) e, opcional, Olho certeiro. Exercita `AskReaction / ApplyReaction / Provoke` reais.

**Intro.** "A fera cansou de apanhar. Até agora você agiu no seu tempo. Agora vai responder no tempo dela: quando um golpe vier, o round pausa e abre a janela <b>REAGIR</b>."
**Outro.** "Você bloqueou, esquivou, contra-atacou, protegeu um aliado, marcou uma defesa no plano e trocou de postura. Reagir gasta Stamina e 1 tick, então guarde fôlego para o golpe que importa. Próximo capítulo: Magia, que gasta Mana em vez de Stamina."

**Setup.** Herói 45, Stamina 5, Machado (blockHardness 2). Fera com **Garras permanentes + STR 10** (golpe ~9), Stamina 0. Em cada lição: recursos cheios, `ResetReactionsForRound`, `defaultReaction = None`, `TutorialPlan.FillWait`, início em tick múltiplo de 4. 4.1–4.5: `HeroDodgeOverride = 0`. Aliado de Treino só na 4.5 (herói (16,15), aliado (18,17), fera (19,17)). Fim: atributos da fera voltam (Garras ficam), Postura Neutra, checkpoint.

| # | id | Título | Status | Gate | Teste |
|---|---|---|---|---|---|
| 4.1 | react-block | Bloquear | ajustar | MenuShown + Applied(Block) + Blocked | depois = antes − 6; Stamina −1; 1 abertura |
| 4.1b | react-unbalance | Desequilibrar | nova | Blocked(→0) && Fera.staggered | fila da fera vazia, 0 de dano |
| 4.2 | react-dodge | Esquivar | nova | Applied(Dodge) + round resolvido | HP igual, ≤ 2 tiles, Stamina −2 |
| 4.3 | react-once | Uma reação por round | nova | 1 janela, 1 reação, 2 golpes | d2 > d1 |
| 4.4 | react-counter | Contra-atacar | nova | Applied(CounterAttack) && fera ferida | Atacar da fera não resolveu; Stamina −3 |
| 4.5 | react-intercept | Interpor | nova | Applied(Intercept) && herói ferido && colado | aliado intacto; Stamina −2 |
| 4.6 | react-default | Reação padrão | nova, **opcional** | confirm com padrão → Applied sem janela | 0 janelas |
| 4.7 | react-stance | Postura | nova | StanceChanged → golpe | dano = fórmula da postura [A CONFIRMAR]; reação intacta |
| 4.8 | react-trueaim | Olho certeiro | nova, **opcional** | Applied(TrueAim) && fera ferida | Stamina −1 |

**4.1 Bloquear**
- Instrução: "A fera vai te atacar. <b>Confirmar</b> (Enter ou toque). Quando abrir a janela REAGIR, toque em <b>Bloquear</b>."
- Realce: `btn-confirm` → bloco "Garras ataca você" → `react-Block`. Card vai para card-dir com REAGIR aberta.
- Feedback: "<b>Bloquear</b> segurou 6 de dano (4 da VIT 5 + 2 do Machado): a garra de {antes} virou {depois}. Custou 1 Stamina e 1 tick."
- Falha: não reagiu → laranja "A garra entrou inteira: {dano}".
- Impl.: sai o `ConsumeReaction` direto. `ShowReactionMenu` dispara `ReactionMenuShown`, dá `name "react-"+kind` e aplica `--off` fora do Allow.

**4.1b Desequilibrar**
- Instrução: "A fera está fraca agora. <b>Confirmar</b> (Enter ou toque) e toque em <b>Bloquear</b>."
- Feedback: "Golpe de {antes} contra Bloquear 6: absorveu tudo. A fera ficou <b>Desequilibrada</b> e perdeu o 2º ataque, então você tomou 0 de dano."
- Impl.: só aqui a fera fica com STR 3 e sem Garras; Script [Atacar, Atacar].

**4.2 Esquivar**
- Instrução: "Outra garra vindo. <b>Confirmar</b> (Enter ou toque) e, na janela REAGIR, toque em <b>Esquivar</b>."
- Feedback: "<b>Esquivar</b> te tirou do arco: você andou {tiles} tile(s) e tomou 0, em vez de {danoEstimado}. Custou 2 Stamina."

**4.3 Uma reação por round**
- Instrução: "A fera ataca duas vezes no mesmo round. <b>Confirmar</b> (Enter ou toque) e toque em <b>Bloquear</b> no 1º golpe."
- Feedback: "1º golpe: Bloquear segurou 6 e você tomou {d1}. 2º golpe: sem janela, você tomou {d2}. Só cabe <b>1 reação por round</b>, e ela volta a cada 4 ticks."
- Falha: janela abriu 2× → FALHOU (bug do limite).

**4.4 Contra-atacar**
- Instrução: "A fera está no seu alcance e vai se mexer. <b>Confirmar</b> (Enter ou toque). Quando ela andar, toque em <b>Contra-atacar</b>."
- Feedback: "O ataque de 2 ticks da fera não aconteceu: 0 de dano em você. <b>Contra-atacar</b> acertou {dano} e cortou a fila dela. Custou 3 Stamina. O mesmo vale contra você: andar, conjurar ou reagir colado num inimigo provoca o <b>Contra-atacar</b> dele."
- Impl.: Script [Andar herói+(1,1), Atacar]; fera AGI 0, VIT 2 só aqui [ABERTO]. Dona do conceito de golpe de oportunidade, que usa o termo "Contra-atacar" em todo o tutorial.

**4.5 Interpor**
- Instrução: "A fera vai atacar seu aliado. <b>Confirmar</b> (Enter ou toque) e toque em <b>Interpor</b>."
- Feedback: "<b>Interpor</b>: você andou {n} tiles e entrou na frente. O Aliado tomou 0 e você tomou {dano}. Custou 2 Stamina."
- Impl.: criar o aliado (Team.Player, Stamina 0, Idle) e registrar no RoundManager/PlanningController; tile de guarda calculado em runtime.

**4.6 Reação padrão (opcional)**
- Instrução: "Marque a defesa antes do golpe: <b>Reação</b> (tecla 9 ou toque) até mostrar Bloquear. Depois <b>Confirmar</b> (Enter ou toque)."
- HUD: `FreezeClock = false` (o relógio de 18 s faz parte do conceito).
- Feedback: "<b>Reação padrão</b>: {reacao} disparou na hora, sem janela, e você tomou {depois}. Ela fica marcada até você trocar com 9."
- Falha: não cabe → janela abre com relógio e "Sua Reação padrão não cabe aqui".

**4.7 Postura**
- Instrução: "Troque a postura antes do golpe: <b>Postura</b> (tecla 8 ou toque) até a defensiva. Depois <b>Confirmar</b> (Enter ou toque)."
- HUD: `Allow(Reactions, {})` para isolar o efeito.
- Feedback: "<b>Postura</b> {postura}: a garra causou {dano}, contra {danoNeutro} em postura Neutra. Ela vale o round todo e não gasta sua reação."
- Impl.: evento `StanceChanged`; `danoNeutro` pela mesma fórmula, sem rolar de novo. **[A CONFIRMAR o efeito numérico de cada postura no código.]**

**4.8 Olho certeiro (opcional)**
- Instrução: "Atire na fera com o Arco: <b>Atacar</b> (tecla 2 ou toque) e <b>Confirmar</b>. Quando o tiro for errar, toque em <b>Olho certeiro</b>."
- Feedback: "O tiro ia errar; <b>Olho certeiro</b> refez a mira e acertou {dano}. Custou 1 Stamina."
- Impl.: decisão: fica aqui (4.8), não como 3.5b. Arco Curto só aqui; erro forçado por `TutorialHud.ForceMissNext` (o acerto do herói é 100%).

---

### Cap. 5 — Magia (básico) (~5 min)

**Objetivo.** Mana em contraste com a Stamina já conhecida, conjurar Brasa, custo pago no início, conjuração em ticks com Queimando (dono único do status) e interrupção por dano (limite 25% do HP máx. = 12).

**Intro.** "A Fera de Treino está a 4 tiles e o seu machado não chega lá. A sua mente chega: hora da primeira magia."
**Outro.** "Você pagou Mana, esperou os ticks e viu uma magia ser cortada no meio. A seguir: área, status, zonas, cura e Concentrar."

**Setup.** Herói (16,15), fera (20,15) com Garras. Recursos no máximo (45 / 14 / 5, lidos em runtime), Postura Neutra, `TutorialPlan.Clear`, `SpellField` limpo, fera 54, Stamina 0. 5.1–5.3: Idle. 5.4–5.5: fera em (17,15), `ScriptedHit(dano)` no tick 1, herói sem reação. `Allow("spells", {Ember})`, `Boosts = None`.

| # | id | Título | Status | Gate | Teste |
|---|---|---|---|---|---|
| 5.1 | cast_pick | Escolha e mire a Brasa | ajustar (funde mana_stamina) | Ember na fera na fila | Elemental só Mana; MaxMana 14; Duration 2; Reach 10; Mana intacta |
| 5.2 | cast_pay | Custo: a Mana sai no início | ajustar | `SpellPaid(Ember)` | 14 → 12, Stamina igual |
| 5.3 | cast_ticks | Conjurar leva ticks | nova | dano na fera + Queimando | 2 passos; burning 4 ticks × 3; 11 de dano |
| 5.4 | interrupt_kept | Limite de concentração | nova | `KeptConcentration` | 5 < 12; Brasa saiu |
| 5.5 | interrupt_cut | Dano interrompe | nova | `Interrupted` | ≥ 12; Mana perdida; fera intacta |

**5.1** Instrução: "Abra <b>Magia</b> (tecla 4 ou toque) e veja <b>Mana 14</b>. Escolha <b>Brasa</b> e depois a <b>Fera</b>. A Brasa alcança 10 células." Realce: `abtn-spell` → `spell-res` → `spell-Ember` → fera → bloco. Feedback: "Elemental custa só Mana; a Stamina que você usou nos Cap. 3–4 fica intacta. Brasa na fila: 2 ticks, alcance 10, fera a 4. A Mana segue em 14/14." (Não falar da magia Física: é da 7.5.) Checklist sem "ler Mana" passivo: Abrir Magia · Brasa · Fera.

**5.2** Instrução: "Confirme o round: <b>Confirmar</b> (Enter ou toque). Olhe a barra de <b>Mana</b>." Feedback: "Brasa custou 2 Mana: 14 → 12. A Stamina continua 5." Impl.: setup hoje põe Mana 20 > MaxMana (falso positivo): usar `MaxMana`.

**5.3** Instrução: "A Brasa leva <b>2 ticks</b>. Toque em <b>Avançar tick</b> no chip de resolução e veja a barra sobre o herói encher." Feedback: "No fim do 2º tick a Brasa saiu: 11 de dano + Queimando (3 por tick, 4 ticks)." Impl.: modo passo = hold-tick do RoundManager; botão no chip, sem tecla (o Espaço não faz nada aqui).

**5.4** Instrução: "Conjure <b>Magia</b> (tecla 4 ou toque), escolha Brasa, toque na Fera e confirme com <b>Confirmar</b> (Enter ou toque). A garra tira menos que 12." Feedback: "Tomou 5 (menos que 12) e manteve a concentração: a Brasa causou 11." Falha: interrompeu com 5 → FALHOU (bug).

**5.5** Instrução: "Mais uma Brasa na Fera e <b>Confirmar</b> (Enter ou toque). Agora a garra tira <b>12</b>." Feedback: "Interrompida! Tomou 12 (limite = 25% de 45). A Brasa não saiu e os 2 de Mana foram gastos." Fim: Idle, fera volta a (20,15), checkpoint.

---

### Cap. 6 — Elementos e Mana (~11 min)

**Objetivo.** 6A (6.1–6.6): efeitos elementais (Encharcado, área, zonas, barreira, teleporte, cura). 6B (6.7–6.10): economia de Mana (incrementos, embotamento, Concentrar). Todo efeito conjurado pelo jogador.

**Intro.** "A Fera de Treino aguenta mais um pouco. Agora cada magia faz algo diferente: água que apaga fogo, chão que vira lava ou lama, parede, salto no espaço e cura."
**Outro.** "Você usou cada efeito elemental e sabe gastar e recuperar Mana. No próximo capítulo o próprio corpo conjura."

**Setup.** Herói (atributos 5, Mana 14, andada 7), fera (54, andada 6, def. mágica 1, Garras, Idle). Aliado de Treino (o da 4.5) na 6.2 e 6.6; 2ª fera na 6.2 e 6.8. Em cada lição: plano limpo, Mana = MaxMana, fera cheia, status zerados, `SpellField` limpo (exceto 6.4 → 6.5). `Boosts = None` de 6.1 a 6.6. Checkpoint "Metade do capítulo 6" (meio segmento) no fim da 6A. 6.8–6.10: `FreezeRegen = true`; 6.8 começa com Mana 12.

| # | id | Título | Status | Gate | Teste |
|---|---|---|---|---|---|
| 6.1 | soaked | Status: Encharcado | ajustar | `SpellResolved(WaterJet)` && soaked | burning 0; andada −1; 9 de dano |
| 6.2 | area | Área: Explosão | ajustar | 2 feras feridas | aliado intacto; empurrão 1 |
| 6.3 | zones | Zonas: Magma e Lama | ajustar (funde zone+mud) | 2 passos: lava fere / fera na lama | lava 25 células × 4, herói ileso; andada 1 |
| 6.4 | barrier | Barreira: Muralha | ajustar | Muralha na linha herói–fera | 9 células, HP = Amount |
| 6.5 | windstep | Teleporte: Passo do vento | nova | herói do outro lado da Muralha | anchor = alvo; HP igual; Mana −3 |
| 6.6 | heal | Cura: Bálsamo | ajustar | herói e aliado curados | cura = min(14, falta); fera não cura |
| 6.7 | increment | Incremento: Potência | ajustar | Ember com Power na fera | 17 vs 11; Mana −4; Duration 3; Efeito/Alcance via `SpellBoosts.Apply` |
| 6.8 | boostcombo | Somar incrementos | nova | Explosion Area+Quick, 2 feras | raio 4; Duration 2; 12 → 3; Embotado |
| 6.9 | dulled | Embotamento | nova | `PlanRejected("Longe demais")` | Reach 9; acerto −10% |
| 6.10 | concentrate | Concentrar | nova, opcional [PROPOSTA] | `ActionResolved(Concentrate)` | não Embotado; Reach 10 |

Fichas (instrução → feedback; falha padrão = laranja "Tentar de novo — …"):

- **6.1** "A fera ainda queima. Use <b>Magia</b> (tecla 4 ou toque), escolha Jato d'água e mire na fera. Confirme com <b>Confirmar</b> (Enter ou toque)." → "Jato d'água: 9 de dano + Encharcado. O fogo apagou e a andada da fera caiu de 6 para 5 por 4 ticks." Fogo encenado pelo setup (Queimando já ensinado na 5.3; a antiga 6.1 sai).
- **6.2** "Use <b>Magia</b> (tecla 4 ou toque) → Explosão no tile marcado. O Aliado de Treino também está na área. Confirme." → "Explosão acertou as 2 feras ({dano} em cada). O Aliado de Treino estava na área e não perdeu vida."
- **6.3** Passo 1 Magma embaixo da fera, passo 2 Lama na fera → "Magma: a fera tomou {n}×4 na lava. Lama: a andada dela caiu de 6 para 1 tile dentro da zona." Herda do Cap. 2 as lacunas do plural "1 tiles" na nota "Longe demais" e do EffectiveMoveStep que usa o anchor real no planejamento.
- **6.4** "Use <b>Magia</b> (tecla 4 ou toque) → Muralha no tile marcado entre você e a fera. Confirme." → "Muralha: {células} células e {hp} HP entre você e a fera, por 4 ticks."
- **6.5** "Use <b>Magia</b> (tecla 4 ou toque) → Passo do vento e toque no tile marcado do outro lado da Muralha. Confirme." → "Passo do vento: você reapareceu {d} células adiante, do outro lado da Muralha, em 1 tick. Custou 3 Mana." (Não provoca Contra-atacar.)
- **6.6** "Você e o Aliado de Treino estão feridos. Use <b>Magia</b> (tecla 4 ou toque) → Bálsamo em você. Confirme." → "Bálsamo: você {hp0} → {hp1} e o aliado {a0} → {a1}. A fera também estava na área e não curou." Setup: herói 20 HP + Encharcado.
- **6.7** "Use <b>Magia</b> (tecla 4 ou toque) → Brasa. No INCREMENTAR MAGIA ligue <b>Potência</b> e toque em Conjurar. Mire na fera e confirme." → "Potência: a Brasa foi de 11 para {dano} na fera e o Queimando de 3 para 4 por tick. Custou 4 Mana e 3 ticks." Card-dir com INCREMENTAR aberto. Primeira vez que o menu abre no tutorial.
- **6.8** "Use <b>Magia</b> (tecla 4 ou toque) → Explosão, ligue Área e Rápida e toque em Conjurar no tile marcado. Confirme." → "Área + Rápida: Explosão 9×9 em 2 ticks, acertou as 2 feras. Custou 9 Mana: sobrou 3 de 14."
- **6.9** "Você gastou quase toda a Mana. Use <b>Magia</b> (tecla 4 ou toque) → Brasa e mire na fera a 10 células." → "Embotado com 3 de 14 de Mana: a Brasa alcança 9 em vez de 10 e o acerto caiu 10%." Falha: sem nota → FALHOU "embotamento não aplicado".
- **6.10** "Use <b>Concentrar</b> (tecla 5 ou toque) e confirme o round." → "Concentrar: Mana {m0} → {m1}. Você saiu do Embotado e a Brasa voltou a alcançar 10."

---

### Cap. 7 — Magia Física (~7 min)

**Objetivo.** As 4 físicas (Fúria, Pele de pedra, Salto, Curva) como preparos de 1 tick, pelo caminho real (plano → pagamento → resolução → consumo). A 7.5 é dona do custo misto.

**Intro.** "A Fera de Treino continua de pé, mas agora quem conjura é o seu corpo. Magias físicas levam 1 tick e preparam o próximo golpe, a defesa ou o movimento."
**Outro.** "Você usou as quatro magias do corpo: Fúria, Pele de pedra, Salto e Curva. Todas levam 1 tick, nunca são interrompidas e pagam metade em Stamina. No Capítulo 8 você vê de onde vêm esses números."

**Setup.** Herói 45 / Stamina 5 / Mana 14 / Machado. Fera 54 com Garras, Idle, Stamina 0. Ao entrar: `SpellField` limpo (zonas do Cap. 6 vazavam), `furyReady/curveReady/stoneSkinTicks` zerados. `Boosts = None` no capítulo. Exceções: 7.2 fera Free, herói sem reação; 7.3 herói colado, fera com Stamina e 1 reação (ameaça real), tile de saída a 6; 7.4 herói a 6 da fera, Muralha no meio (Caster = Fera); 7.5 Stamina 0.

| # | id | Título | Status | Gate | Teste |
|---|---|---|---|---|---|
| 7.1 | fury | Fúria: golpe ×2,5 | ajustar | furyReady visto e consumido + fera ferida | paga 1+1; mult 2,5 |
| 7.2 | stoneskin | Pele de pedra | ajustar | stoneSkinTicks > 0 + round resolvido | 4 ticks, ×5; sonda da fórmula |
| 7.3 | leap | Salto: saia sem provocar | ajustar | saiu de perto da fera | ≤ 14; 0 Contra-atacar; paga 1+2 |
| 7.4 | curve | Curva: contorne a Muralha | ajustar | curveReady consumido + fera ferida | Muralha intacta; Queimando |
| 7.5 | halfStamina | Metade Stamina | nova | `SpellPaid(Fury)` após Respirar | StaminaPart = cost/2; canFitBefore false |

- **7.1** "Abra <b>Magia</b> (tecla 4 ou toque) e escolha <b>Fúria</b>. Depois use <b>Atacar</b> (tecla 2 ou toque) na fera e confirme o round com <b>Confirmar</b> (Enter ou toque)." → "A Fúria pagou 1 Stamina + 1 Mana. O <b>Atacar</b> saiu ×2,5 e tirou {dano} de vida da fera." Não soma com o Golpe poderoso (vale o maior). Falha: Atacar antes → nota "A Fúria vale para o PRÓXIMO ataque".
- **7.2** "A fera está colada em você e vai atacar neste round. Abra <b>Magia</b> (tecla 4 ou toque), escolha <b>Pele de pedra</b> e confirme com <b>Confirmar</b> (Enter ou toque)." → "A Pele de pedra (1 Stamina + 1 Mana) levou sua defesa física de {def} para {def×5} durante 4 ticks. A garra da fera tirou só {dano}." Variante de erro da fera: "A fera errou; a pele segue ativa por {n} ticks."
- **7.3** "Você está colado na fera, e ela ainda tem fôlego para um Contra-atacar. Andar provocaria esse golpe. Abra <b>Magia</b> (tecla 4 ou toque), escolha <b>Salto</b>, toque no tile marcado e confirme com <b>Confirmar</b> (Enter ou toque)." → "O Salto te levou {n} células para longe da fera, sem provocar o Contra-atacar dela. Andar colado teria provocado. Custou 1 Stamina + 2 Mana." Andar --off. Falha: fera contra-atacou → FALHOU (bug do Provoke).
- **7.4** "A fera está atrás da Muralha. Em <b>Magia</b> (tecla 4 ou toque), escolha <b>Curva</b>. Abra <b>Magia</b> de novo, escolha <b>Brasa</b> e toque NA fera para travar nela. Confirme com <b>Confirmar</b> (Enter ou toque)." → "A Curva contornou a Muralha: a Brasa acertou a fera, com {dano} de dano + Queimando. A Muralha ficou intacta ({hp} HP)." [ABERTO] Brasa como ataque à distância.
- **7.5** "Seu fôlego acabou: Stamina 0, Mana 14. Use <b>Respirar</b> (tecla 6 ou toque), depois enfileire <b>Fúria</b> em <b>Magia</b> (tecla 4 ou toque) e confirme com <b>Confirmar</b> (Enter ou toque)." → "Magia física = metade em Stamina + o resto em Mana. Com 0 Stamina nenhuma física cabia, nem com 14 de Mana. O Respirar deu +3 Stamina, e a Fúria pagou 1 Stamina + 1 Mana." Falha: Fúria antes do Respirar → "Respire ANTES: a magia paga no tick em que começa".

---

### Cap. 8 — Progressão (~5 min)

**Objetivo.** Ler os 6 atributos, ver de onde vem cada máximo, entender que cada nível dá 5 pontos e gastá-los no editor real. O XP real fica para o Cap. 9. **Preset-sandbox único `TutorialDirector.Hero`, o mesmo do Cap. 9** (`TutorialCoop.Preset` sai).

**Intro.** "Seu herói não é fixo. Cada nível dá 5 pontos, e cada ponto muda um número da ficha. Neste capítulo você abre a ficha e gasta os pontos do nível novo."
**Outro.** "Os pontos que você gastou ficam com o herói e valem no próximo capítulo. No Coop, o XP vem de vencer camadas, e o Descanso abre esta mesma soma de pontos. Próximo: Capítulo 9, Coop."

**Setup.** `TutorialDirector.Hero` clonado da unidade (atributos 5, Machado, sem armadura), só em memória (nada de `PlayerProfile.Save`). Ao entrar na 8.3 (idempotente): nível 5, XP 80/100, 30 de 35 pontos. Fera Idle. 8.3–8.5: `CharCreationHUD` real por cima da batalha (`Edit(Hero)`; nível 5 trava arma/itens/aparência/nome). Ao salvar, diferenças vão para a unidade e para `d.Hero`.

| # | id | Título | Status | Gate | Teste |
|---|---|---|---|---|---|
| 8.1 | sheet | A ficha do herói | nova | herói inspecionado && ficha expandida | 6 chips = stats (soma 30) |
| 8.2 | bodymind | Corpo e Mente | nova | tocou 1 chip do Corpo e 1 da Mente | MaxStamina e MaxMana = fórmulas |
| 8.3 | budget | Nível dá pontos | nova (sai a "xp") | editor aberto no nível 5 | BudgetFor 35; Points 30; XP 80/100 |
| 8.4 | point | Um ponto, um número | nova | Points == 31 | diff de stats = fórmula do atributo |
| 8.5 | save | Gaste tudo e salve | nova | `OnConfirmed` com 35/35 | IsValid; unidade com MaxHP novo; PlayerProfile intocado |

- **8.1** "Selecione o seu <b>herói</b> (clique, ou toque duas vezes). No painel dele, abra a <b>ficha</b> (clique ou toque): ali estão os <b>6 atributos</b>." → "Ficha aberta: <b>STR, VIT, DEX, AGI, INT e WIS</b>, 5 em cada, 30 pontos no total." Falha: abriu a fera → "Essa é a Fera. Selecione o herói (azul)."
- **8.2** "Toque ou passe o mouse num atributo do <b>Corpo</b> (STR, VIT, DEX, AGI) e depois num da <b>Mente</b> (INT, WIS)." → "<b>Stamina máxima</b> vem do Corpo (STR + VIT + DEX = 15 → 5). <b>Mana máxima</b> vem da WIS (5 → 14)." Não repete custos (são da 5.1 e da 7.5). Chips abrem dica por toque (`TutorialEvents.InspectChip`).
- **8.3** "XP vem de vencer camadas no Coop (Capítulo 9). Seu herói já está no <b>nível 5</b>. Abra <b>Editar personagem</b> (clique ou toque) e veja os <b>Pontos</b> no topo." → "Nível 5 = 15 + 5 × 4 = <b>35 pontos</b>. Você usa 30: sobram <b>5</b>." Card-dir com botão primário (variante nova, a desenhar no Main/Feedback). Editor 64% × 92% a 2%/4%.
- **8.4** "Toque em <b>+</b> ao lado de <b>VIT</b> (clique ou toque). Veja a <b>Vida</b> mudar na Ficha." → "<b>VIT 5 → 6</b>: Vida 45 → 48 e Defesa −2 → −3. Pontos 31 de 35." (montado do diff real; qualquer atributo fecha [ABERTO]). "−" no piso fica --off com "pontos já gastos não voltam".
- **8.5** "Some os <b>4 pontos</b> restantes onde quiser e toque em <b>Salvar personagem</b> (clique ou toque). O <b>−</b> não tira pontos já gastos." → "35 de 35: herói salvo no nível 5. Na luta: <b>Vida {MaxHP} · Stamina {MaxStamina} · Mana {MaxMana}</b>. Vale também no Capítulo 9."

---

### Cap. 9 — Coop (~6 min)

**Objetivo.** Ciclo entre camadas pelas próprias mãos: vencer, recompensa, baú (equipar e vender), descansar, somar pontos e descer. Fluxo real do RoundManager, sem rede, sobre o preset do Cap. 8.

**Intro.** "No Coop, até 4 jogadores descem juntos as camadas de Pangea rumo ao Inferno. Cada camada é uma batalha, e entre uma e outra o grupo decide: pegar o que ganhou, descansar ou descer."
**Outro.** "A camada 2 começou, com mais feras e mais fortes. Descer tem peso; recuar não tem nenhum: no jogo de verdade, Recuar encerra a descida em vitória com tudo o que o grupo ganhou. Com isso o tutorial termina."

**Setup.** `TutorialCoop.Active = true` (estática nova) espelha sem rede `HostRest`, `HostDescend`, `Loot*`, `ApplyCard`, `RestAttr` com as mesmas funções puras. Preset = `TutorialDirector.Hero` do Cap. 8; aberto direto → nível 5, XP 80. +20 XP da camada 1 → nível 6, 5 pontos para a 9.6. `TutorialCoop.Gold` começa em 0. Nada grava em `PlayerProfile` nem em `pangea.coop.deepestLayer`. Números de vida/Mana sempre de `stats.MaxHP/MaxMana`. 9.1 fera colada e fraca; 9.2 3 cartas de atributo forçadas; 9.3–9.4 baú = {LeatherHelm, SwiftBoots}; 9.5 herói 20 HP / 5 Mana, `DayNight.Reset()`; 9.7 2 feras ×1,15.

| # | id | Título | Status | Gate | Teste |
|---|---|---|---|---|---|
| 9.1 | coop-clear | Vença a camada 1 | nova | fera morta && `AwaitingDescent` | +20 XP; Gold 3; nível 5 → 6 |
| 9.2 | coop-card | Escolha uma recompensa | nova | `CardsPicked > 0` | atributo +2 |
| 9.3 | coop-equip | Equipe do baú | nova | Head == LeatherHelm | ArmorPhys +1; Stash −1 |
| 9.4 | coop-sell | Venda o que não serve | nova | Gold > gold0 | +12 (raro) |
| 9.5 | coop-rest | Descanse antes de descer | nova | `RestedThisLayer` && Dusk | HP/Mana = máx; BeastMult 1,15 |
| 9.6 | coop-points | Some um ponto no descanso | nova | Points > pts0 | atributo +1 no preset e na unidade; restam 4 |
| 9.7 | layer | Desça para a camada 2 | ajustar | Layer == 2 após o toque | 2 feras; Dusk; HP cheio |

- **9.1** "No Coop, cada camada é uma batalha. Derrube a Fera de Treino: <b>Atacar</b> (tecla 2 ou toque), clique ou toque na fera e confirme o round (<b>Confirmar</b>, Enter ou toque)." → "Fera abatida, camada 1 vencida: +20 XP (5 da fera + 15 da camada) e +3 moedas. Você subiu para o nível 6: +5 pontos para somar." Errou → checklist "Planeje outro ataque", sem FALHOU. Impl.: ramo SP da vitória chama `LayerCleared()` com `TutorialCoop.Active`.
- **9.2** "Camada vencida dá recompensa. Clique ou toque numa das 3 cartas. Cada uma soma <b>+2 pontos</b> num atributo. No jogo, uma carta também pode trazer um item." → "Você pegou a carta: +2 {ATTR}: {ATTR} {antes} → {depois}. Vale até o fim da descida."
- **9.3** "O baú do grupo abriu. Clique ou toque em <b>Elmo de Couro</b> e depois em <b>Equipar</b>. Se você já usasse algo no slot, isso voltaria para o baú." → "Elmo de Couro equipado: +1 defesa. Sua cabeça estava vazia, então nada voltou ao baú." `ShowChoiceTitled` passa a respeitar `Allow`.
- **9.4** "Botas Ágeis é um item raro. Clique ou toque nele e depois em <b>Vender</b>. As moedas vão para você." → "Botas Ágeis vendidas: +12 moedas (você tem {Gold}). Na loja do perfil, 2 moedas viram 1 XP."
- **9.5** "Descer direto cura só 30% da vida e 50% da Mana. <b>Descansar</b> cura tudo, mas o tempo passa e as feras ficam mais fortes. Feche o baú (<b>Pronto</b>) e clique ou toque em <b>Descansar</b>." → "Descansar curou tudo: vida 20 → {MaxHP} e Mana 5 → {MaxMana}. Agora é Entardecer: as feras da próxima camada vêm +15% mais fortes (à Noite, +30%)."
- **9.6** "A ficha DESCANSO abriu com 5 pontos. Clique ou toque em <b>+1</b> num atributo. Cada ponto já vale na próxima camada." → "{ATTR} {antes} → {depois} na unidade e no personagem. Restam 4 pontos para somar."
- **9.7** "Clique ou toque em <b>Pronto</b> e depois em <b>Descer</b>. A camada 2 tem mais feras, e mais fortes. Recuar encerraria a descida com o que você ganhou." → "Camada 2 começou, no Entardecer: 2 feras, mais fortes (+10% por camada e +15% pela hora). Você desceu descansado, com vida {MaxHP}/{MaxHP}." Remover o `BeginNextLayer(2)` do setup.

Sem teclas nas janelas do Cap. 9 até decidir o atalho global 1..n.

---

## 4. Lacunas consolidadas e plano de implementação

### 4.1 Lacunas vs GDD-027 (e como este anexo as fecha)

| Lacuna | Resolução proposta |
|---|---|
| Postura (8) nunca ensinada | 4.7 Postura, efeito numérico [A CONFIRMAR] |
| Golpe de oportunidade nunca ensinado | feedback da 4.4; termo único "Contra-atacar" (6.5, 7.3) |
| Olho certeiro sem dono | 4.8 opcional, erro forçado por seed |
| Carry-over (↷) citado e nunca praticado | 3.3 começa no tick 4, assere `carriedTicks == 1`; sai da 0.4 |
| Terreno/pathfinding do Cap. 2 | 2.4 com degrau real; 2.5 oculta até o BFS; **BFS é pré-requisito da fase 3** |
| Limpar, Desequilibrar, Efeito/Alcance | Limpar no feedback da 1.2; 4.1b; asserções via `SpellBoosts.Apply` na 6.7 |
| "A fera não mata o jogador" (§5) | `HeroHpFloor = 1` global |
| Pausa entre lições (§2) | hold-tick global do RoundManager, nunca `Time.timeScale` |
| Ritmo (~60 min, sem retomada) | checkpoint por capítulo; caminho curto ~45 min com opcionais |
| Colisão de movimento (iniciativa) | só citada no feedback da 2.3 [ABERTO: quem pratica] |
| Duplicações (Queimando, Lama, Mana × Stamina, round de 4 ticks, ficha, subir de nível) | donos únicos: 5.3, 6.3, 7.5 (custo misto), 0.4/0.5, 8.1; preset único Cap. 8–9 |
| Ordem: Stamina usada antes de ser explicada | linha de Stamina na 3.3 |
| Ordem: INCREMENTAR abre antes de ser ensinado | `Boosts = None` até a 6.6; tipo único `SpellBoost?` |
| Estado da fera inconsistente | Garras permanentes do Cap. 4 em diante |
| Gate frágil `IsEngaged` | `RoundResolvedSince` + `PlanConfirmed` em todo o tutorial |
| Ações sumindo nos artboards (Cap. 2, 7.1, 8.3) | as 9 ações sempre visíveis |

### 4.2 API a consolidar antes da fase 3

- **TutorialBeast** {Free, Idle, Script(lista), ScriptedHit(dano)} — substitui FeraPassive (TutorialHud/Scenario/RoundManager), FeraHold, FeraScript e TutorialBeast.Mode avulso.
- **TutorialHud.FreezeClock** (decisão + reação) — substitui FreezeReactionTimer e as checagens de `TutorialLauncher.Active`.
- **TutorialHud.Allow(categoria, ids)** — substitui RoundButtons, Reactions, Spells, Boosts, ChoiceAllowed.
- **Classe `--off` única** (`bh-abtn--off`) — saem bh-abtn--locked, bh-spellblock--off, is-locked.
- **TutorialPlan** — substitui ClearPlan, TutorialEnqueueWait/Move, TutorialAppendSpell, ResetPlan.
- **TutorialEvents** (estática): CameraPanned, CameraZoomed, UnitInspected, TickColumnClicked, TickRowClicked, PlanUndone, PlanConfirmed, RoundResolved, PlanRejected, SpellPaid, SpellResolved, ActionResolved, DamageTaken, ReactionMenuShown, ReactionApplied, Blocked, StanceChanged, KeptConcentration, Interrupted, InspectChip — substitui OnResolved, OnSpellPaid, OnActionResolved, OnReactionApplied, d.FeraHits.
- **Getters aditivos:** `RoundManager.GlobalTick/AwaitingDescent/RestedThisLayer`, `PlanningController.Mode`, `BattleHUD.InspectedUnit/InspectExpanded/PreviewHitPct`.
- **TutorialLesson:** `feedbackFn`, `onWrong`, `idleHint`, `retryIf`, `steps`, `optional`, `requiresPathfinding`.
- **Outros:** `Unit.SetWeapon`, `HeroDodgeOverride`, `ForceMissNext`, `FreezeRegen`, `TutorialCoop`, `TutorialMarker`, `TutorialProgress`.

### 4.3 Plano em fases

1. **Fase A — Overlay novo e infraestrutura** (bloqueia todo o resto)
   - `TutorialOverlay` em %: tokens, véu, anel (UI/unidade/tile), seta de borda, etiqueta Clique/Toque/Veja, checklist, cards verde/laranja/vermelho, chip de resolução, barra de progresso calculada, "Lição x de y".
   - Invariantes globais (§2.10) e API consolidada (§4.2).
   - `BuildActionBar` sem `RemoveAll`; `FreeClick` respeita o filtro; estado "liberado sem foco"; `.bh-abtn--off` na paleta.
   - Checkpoint por capítulo.
2. **Fase B — Ajustar as lições existentes** (12 com status "ajustar"): cam, bar, tick (ex-sched), plan, confirm, moved, attack, react-block, cast_pick, cast_pay, Cap. 6 (soaked, area, zones, barrier, heal, increment), Cap. 7 (fury…curve), layer. Tirar todo `Cast()`/ação do setup, trocar gates passivos, `feedbackFn` com números, corrigir Mana 20 e HP 300.
3. **Fase C — Lições novas por capítulo**, na ordem do tutorial: Cap. 0 (zoom, inspect, rows) → Cap. 1 (undo) → Cap. 2 (reach, chain, climb) → Cap. 3 → Cap. 4 (+ aliado, Garras) → Cap. 5 → Cap. 6 (+ 2ª fera) → Cap. 7 (halfStamina) → Cap. 8 (editor na batalha) → Cap. 9 (`TutorialCoop`).
   - **Pré-requisito da fase 3 do GDD-027 §8:** BFS/A* em `GetReachableAnchors` e `MoveToDestination`. Libera a 2.5.
4. **Fase D — Artboards e GDDs**: corrigir Main.dc.html (barra real de 9 ações com "Andar"), Report.dc.html (nomes e contagens da §1.1), recriar Cap3/4/5/7 (não encontrados) e a tarefa única "alinhar GDDs ao código" (§5).

---

## 5. Decisões em aberto consolidadas **[ABERTO]**

**Decididas pelo Marcus (2026-11-22) [CANON]**
- Atalhos 1..n nas janelas de escolha (REAGIR, INCREMENTAR, RECOMPENSA, DESCANSO): **sim**. Valem só com a janela aberta, no jogo todo, e os textos citam tecla + toque. (Fecha o item global 1.)
- Tom do texto: **direto e curto**, sem lore. (Fecha o item global 8.)
- GDD × código: **o código vale**. Os textos leem os valores em runtime e os GDDs listados abaixo são atualizados para bater com o código.
- Final do Cap. 9: o jogador **joga a camada 2** livremente, numa camada curta, antes do relatório. (Fecha a 1ª pergunta do Cap. 9; a 9.7 vira a transição e o relatório entra depois da camada 2.)

**Globais**
1. Atalho 1..n nas janelas de escolha (REAGIR, INCREMENTAR, RECOMPENSA, DESCANSO): aprovar ou tirar toda tecla dos textos de janela.
2. Variante móvel dos tokens card-esq / feedback (proposta 40% / 1% / 12%).
3. Intent-band × card-esq: mover a band para 34–70,3% ou escondê-la.
4. Bússola × card-dir: esconder a bússola ou descer o card-dir.
5. Onde gravar o checkpoint: PlayerProfile ou PlayerPrefs.
6. "✓ Pôr na fila" só no tutorial ou no jogo todo.
7. Limite do relógio de decisão fora do tutorial (registrar no GDD-018 §4).
8. Tom do texto: direto ou com lore do GDD-013.
9. Valor final do `--plan-block` e demais tokens de cor.
10. Limiares dos gates (pan 2 células, zoom 2, ociosidade 20 s): playtest.
11. ~~Artboards ausentes~~ — recriados a partir deste anexo e publicados no canvas.

**Alinhar GDDs ao código** — FEITO 2026-11-22 em 007/016/017/019/020 (Brasa e GDD-023 já batiam; o dano da Brasa depende dos atributos e do spellValueMult 0,75). Itens originais: (tarefa única, antes do texto final; os textos já leem runtime)
- GDD-007: zoom 3–20 (GDD) × 5–20; edgeMargin 18 × 15; faltam Q/E, bússola e pinça.
- GDD-016 §13: andada 5 × 7; Golpe poderoso +4 × ×1,5; §1 "três atributos do Corpo" lista quatro.
- GDD-017: custos, Esquivar, Interpor, VIT × 0,75.
- GDD-019: Brasa 9 × 11; custos físicos; Passo do vento (aliado × só quem conjura).
- GDD-020: Arco Curto 9 / 1 tick × 25 / 2 ticks.
- GDD-023: Efeito em ticks × rounds.
- Feedback.dc.html: "A andada gastou 3 ticks" → 1 tick.

**Por capítulo**
- Cap. 0: clicar numa linha da barra de ticks abre a ficha também no jogo normal? Deslocamento do SnapTo da 0.1.
- Cap. 1: ordem Desfazer antes de Confirmar (confirmar com o Marcus); tile (16,13) andável; Limpar liberado até o fim?
- Cap. 2: BFS simples ou A* do GDD-004; a 2.4 ensina clique direto depois do BFS?; rampas/escadas no tutorial?; quem pratica a colisão de movimento?
- Cap. 3: corrigir a prévia de dano (mostra ~16 sem a defesa; o real é 12)?
- Cap. 4: efeito numérico de cada postura; Reação padrão volta a Nenhuma no fim?; dono (ownerId) do Aliado de Treino no solo; VIT 2 da fera na 4.4.
- Cap. 5: garra roteirizada (5 e 12) aceitável?; explicar a regen de Mana aqui ou no Cap. 6?
- Cap. 6: 2ª fera e aliado nascem no Attach ou spawn em runtime (RoundManager sem AddUnit público)?; Muralha: deixar a fera tentar passar?; 6.10 opcional?
- Cap. 7: Brasa como ataque da Curva; o herói tem ArmorPhys? (define o número da Pele); o Provoke usa a reação padrão e a Stamina da fera?; `CanFitSpell` projeta a Stamina do fim do plano: corrigir ou só ensinar a ordem?
- Cap. 8: duplo toque para inspecionar (confirmado na 0.3?); agrupar o editor em CORPO/MENTE?; gate da 8.4 aceita qualquer atributo?; loja e moedas no Cap. 9?
- Cap. 9: o tutorial termina na 9.7 ou o jogador joga a camada 2?; ensinar "Deixar um item para o grupo" e a loja de XP?; Recuar apagado o capítulo todo basta?
