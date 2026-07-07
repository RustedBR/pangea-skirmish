# Revisão de Polish — Pangea Skirmish (2026-07-07)

Auditoria multidimensional para orientar a fase de polish rumo ao jogo 100% multiplayer.
Método: 7 revisores especializados (network-programmer, unity-ui-specialist, game-designer,
gameplay-programmer, tools-programmer, technical-director) lendo o código real + verificação
adversarial dos bugs. **5 de 7 dimensões concluídas** — a caça-bugs geral e a revisão de
arquitetura/performance ficaram pendentes por limite de sessão (re-rodar após 15h).

Severidade: 🔴 CRÍTICO · 🟠 ALTO · 🟡 MÉDIO · ⚪ BAIXO · Confiança marcada por finding.

> **STATUS DE EXECUÇÃO (atualizado 2026-07-07, Hermes/rusted):**
> Verificado na fonte real — vários findings estavam **obsoletos** (já corrigidos pelo Claude):
> - ✅ **#1 Ataque 100%** — RESOLVIDO (`rusted 29b7312`): `AttackResolver` religado a `RollHit`/`RollDamage` (BattleRng lockstep-safe). AGI=esquiva, DEX=crítico, VIT=defesa.
> - ✅ **#2 Desync por ordenação** — JÁ ESTAVA CORRIGIDO antes (`RoundManager.cs:191` ordena por `unitId`). Finding obsoleto.
> - ✅ **Remover Single-Player** (Dimensão 2) — CONCLUÍDO (`943796d` + `234a77a`): SP da batalha + Sandbox 100% MP.
> - ⏳ **#3 Entrar no meio** / **#4 Queda do host** — NÃO verificados fundo; pendentes.
> - ⏳ **Dimensões 3 (UI MP), 5 (magias), 6 (janelas anim), 7/8** — NÃO feitas.

---

## ⭐ TOP CRÍTICOS (quebram o jogo — atacar primeiro)

1. **🔴 Todo ataque acerta 100% — esquiva, crítico, precisão e variância de dano são CÓDIGO MORTO.**
   `AttackResolver.cs:71,107` chama `TakeDamage` direto; `RollHit`/`RollDamage`/`DodgeChance`/`CritChance`
   (`BattleTypes.cs:118,129`) nunca são chamados na resolução — só aparecem como *labels* de menu.
   Consequência: AGI não dá esquiva, DEX não dá crítico, ataque físico ignora `PhysicalDefense` (VIT).
   Contradiz o GDD-001. *Confiança: CONFIRMADO.* Correção: religar `RollHit`/`RollDamage` no resolver
   (já são `BattleRng` fixed-point, lockstep-safe).

2. **🔴 Desync por ordenação instável em empate de iniciativa.** `RoundManager.cs:645,839` ordenam só
   pelo total da rolagem; `List.Sort` é não-estável → em empate a ordem diverge host/cliente → hash
   FNV-1a não bate → resync a cada empate. O comentário em `:760` diz que há desempate por `unitId`,
   mas **não existe no código**. *CONFIRMADO.* Correção: tiebreaker por `unitId` nas duas ordenações.

3. **🔴 Cliente entrando no meio da partida trava o round pra sempre.** `ConnectionApproval=false`
   (`NetBootstrap.cs:127`) + `AddSlot` incondicional (`RoomManager.cs:184`): quem conectar durante
   Placement/Battle vira slot que nunca submete plano → `AllSlotsHaveSubmitted` nunca fecha. *CONFIRMADO.*
   Correção: `ConnectionApprovalCallback` que rejeita fora do Lobby ou com sala cheia.

4. **🔴 Cliente não reage à queda do host → tela morta permanente.** `OnClientDisconnectCallback` só é
   assinado no servidor (`RoomManager.cs:158`); nenhuma tela escuta `OnClientDisconnected`. Host cai →
   cliente congela sem mensagem, overlays nunca fecham. *CONFIRMADO.* Correção: assinar no RoomHUD/BattleHUD,
   modal "Conexão perdida → Voltar ao menu".

---

## 1. UI Multiplayer (item 1)

| Sev | Finding | Arquivo | Correção |
|-----|---------|---------|----------|
| 🔴 | Cliente não reage à queda do host (tela morta) | `RoomManager.cs:158`, `NetBootstrap.cs:197` | Assinar disconnect no cliente + modal |
| 🟠 | Sem ready-check; host arrasta todos p/ próxima fase | `RoomHUD.cs:327,517` | Campo `Ready` no `PlayerSlot` + botão + gate |
| 🟠 | Zero feedback de latência/ping | `RoomHUD`, `BattleHUD` | `GetCurrentRtt` + barras verde/amarelo/vermelho |
| 🟠 | Batalha não mostra "aguardando plano do oponente (X/Y)" | `LockstepBattleSync.cs:157`, `BattleHUD.cs:1353` | ClientRpc com contagem + overlay |
| 🟠 | Timer de planejamento não sincronizado entre clientes | `LockstepBattleSync.cs:217` | `NetworkVariable<double>` de ServerTime |
| 🟡 | Layout 100% em pixels fixos (quebra WebGL/mobile/ultrawide) | `RoomHUD.cs:186-346`, `CharCreationHUD.cs:72` | Âncoras de borda + `CanvasScaler` Scale-With-Screen |
| 🟡 | Sem indicador de "quem sou eu / meu time" na batalha | `BattleHUD.cs` | Badge "Você: Time A" + marcador de dono nas unidades |
| 🟡 | Erros de conexão crus (`ex.Message` em inglês) | `RoomHUD.cs:634,675` | Mapear p/ PT-BR acionável + botão "tentar de novo" |
| 🟡 | "Sair da sala" sem confirmação; host derruba todos sem aviso | `RoomHUD.cs:693` | Modal de confirmação + aviso especial p/ host |
| 🟡 | CharCreation sem progresso dos outros (X/Y) nem timeout | `CharCreationHUD.cs:252` | Contagem via ClientRpc + timeout do host |
| ⚪ | Chat sem timestamp/cor/limite de histórico (cresce infinito) | `RoomHUD.cs:490` | Cor p/ "Sistema", cap ~100 linhas |
| ⚪ | Sem foco inicial de teclado/gamepad; chat envia ao perder foco | `RoomHUD.cs:279` | `SetSelectedGameObject` + filtrar `onEndEdit` |
| ⚪ | Toggle "Modo local (loopback)" de dev exposto ao jogador | `RoomHUD.cs:137` | Ocultar atrás de `Debug.isDebugBuild` |

**Sugestões novas:** botão "Copiar código da sala"; toast ao entrar/sair; **preview do sprite** na criação
de personagem (hoje só mostra o nome); barra visual de budget; ícone de host na lista; **overlay unificado
de estado de rede** (hoje há 3 overlays de espera descoordenados que não fecham em disconnect).

---

## 2. Spawn Point / Placement de Jogadores (item 2)

| Sev | Finding | Arquivo | Correção |
|-----|---------|---------|----------|
| 🟠 | Zona de placement pode ficar VAZIA → partida trava permanente | `PlacementSync.cs:135`, `RoomManager.cs:493` | Validar ≥1 célula não-void por zona antes de avançar + auto-placement fallback |
| 🟠 | FFA usa 4 quadrantes fixos ignorando nº real de jogadores | `PlacementSync.cs:163,203` | Parametrizar zonas por `Slots.Count` (2=metades, 3=faixas, 4=quadrantes) |
| 🟡 | Placement é 1-clique irreversível, sem preview/confirmar | `PlacementSync.cs:319` | Fluxo 2 passos: hover+fantasma → confirmar |
| 🟡 | Validação só na âncora — unidades multi-célula se sobrepõem | `PlacementSync.cs:228` | Validar footprint inteiro contra `_occupiedCells` |

**Sugestões:** mostrar as zonas de **todos** os jogadores (cor de time + rótulo "Você"/"Inimigo"), não só a
local; lógica de zona hoje **duplicada** entre `GetMyZone` e `IsInSpawnZone` (risco de divergir — unificar).

---

## 3. Remover Singleplayer (item 3)

Tecnicamente viável e bem isolado. **Não é deletar arquivos inteiros** — o acoplamento SP/MP está
intercalado dentro dos mesmos métodos via `if (IsMultiplayer) {...; return;} // resto é SP`. Mapa cirúrgico:

- **Apagar por completo:** `SimpleEnemyAI.cs` (380 linhas, exclusivo SP; só chamado em `RoundManager.cs:247,252`).
- **`GameBootstrap.cs:137-201`** — branch SP inteiro (spawn hardcoded Guerreiro/Ladino/3 Goblins). Sugestão:
  extrair antes `SetupSingleplayerBattle()`/`SetupMultiplayerBattle()` p/ remover com segurança.
- **`MainMenuManager.cs`** — remover botões "Começar o Jogo" e "Modo Sandbox" (solto) + toda `MapSelectPanel`
  (`:640-796`, ~160 linhas de batalha SP local via `SceneManager.LoadScene` sem NetworkManager).
- **`SandboxController`/`SandboxHUD`** — eliminar fases `Allies`/`Enemies` (~220 linhas já mortas em MP);
  o Sandbox vira só editor de terreno (como já é em MP). `SaveMap()`/botão Salvar são SP puro.
- **`RoundManager.cs`** — ~6 pontos de bifurcação (`EnterPlanning`, `ConfirmPlan`, win-condition, `DoBonusStep`,
  grace-period `:284-296`); remoção método-a-método, PR dedicada.
- **`PlanningController.cs:274-282`** — branch SP de `OnQuickStepClick` vira caminho único.

**⚠️ NÃO remover (compartilhado com MP):** tipos `MapData`/`UnitPlacement`/`CharacterPreset`; estáticos
`RuntimeMap`/`RuntimeSandbox`; `CharacterStorage`; `GameTuning.aiReactionDelay` e comparações `team==Team.Enemy`
em `RoundManager:682,846` (delay de reação **roda em MP também**, é intencional). `PangeaSceneBuilder.cs` recria
a cena Sandbox no build — atualizar junto. *Recomendo checklist linha-a-linha + smoke test TDM+FFA completo após cada corte* (não há testes automatizados de rede).

---

## 4. Consolidar Menus de Editor (item 4)

Hoje: **16 MenuItem em 2 roots** (`Pangea/*` e `Pangea Skirmish/*`) + **2 namespaces** (`PangeaSkirmish.EditorTools`
vs `PangeaSkirmish.Editor`). Proposta de root único `Pangea Skirmish/`:

| Antes | Depois |
|-------|--------|
| `Pangea/Teste/Golpe SE\|NE\|SW\|NW`, `Câmera Lenta` | `Pangea Skirmish/Debug/Attack Test/...` |
| `Pangea/Setup All Scenes` | `Pangea Skirmish/Project/Setup All Scenes` |
| `Pangea/Gerar Animações de Armas` | `Pangea Skirmish/Animation/Gerar Animações de Armas` |
| `Pangea/Weapon Anim Editor` | `Pangea Skirmish/Animation/Weapon Anim Editor` |
| `Pangea Skirmish/Build/*` | mantém |
| `Pangea Skirmish/Settings/Open Build Folder`, `Clean Builds` | `Pangea Skirmish/Build/...` |
| `Pangea Skirmish/Generate Unit Definitions` | `Pangea Skirmish/Content/Generate Unit Definitions` |

Root final: `Build/ · Debug/ · Animation/ · UI/ · Content/ · Project/`. Unificar namespace em `PangeaSkirmish.Editor`.
Baixo risco, muda só strings de atributo + `namespace`. (Editor bugs achados de brinde: `WeaponAnimEditorWindow.cs:286`
— diálogo "Descartar?" não impede o descarte, `LoadAll()` roda de qualquer jeito, **CONFIRMADO**; `UnitDefinitionGenerator.cs:148`
sobrescreve `.asset` sem confirmar, apagando ajustes manuais de balanceamento.)

---

## 5. Nova Janela de Animação (item 5)

Já existe `WeaponAnimEditorWindow` (madura: onion-skin, timeline ativo/skip, mini-preview de escala, atalhos) —
mas só **ajusta posição/rotação de arma sobre clips já existentes**. A criação do clip em si é um script em lote
sem UI (`WeaponAnimationBuilder`, processa TODOS os spritesheets de uma vez, sem seleção/preview).

**Spec proposta — `Pangea Skirmish/Animation/Sprite Animation Studio`:** (1) seletor de qualquer pasta de sprites
(não preso ao padrão hardcoded `{id}attackSE`); (2) grid de miniaturas com drag-to-reorder; (3) player com scrub
bar real, play/pause, FPS ajustável, loop; (4) botão "Criar/Atualizar AnimationClip" gravando `ObjectReferenceCurve`
p/ qualquer saída — generaliza o builder de armas p/ VFX de magia, ícones de UI, animação de morte. Aditiva, não
substitui a janela atual. Considerar avaliar `com.unity.timeline` nativo antes de reinventar scrubbing.

---

## 6. Nova Janela de UI (item 6) — maior ganho de produtividade

**UI é 100% UGUI construída em C# na unha** (`com.unity.ugui 2.5.0`, zero UXML/USS/prefab). Cada HUD repete
150–800 linhas de `new GameObject(..., typeof(Image)) + AddComponent + SetParent` (`RoomHUD` 800, `BattleHUD` 1892,
`SandboxHUD` 591, `CharCreationHUD` 368). Qualquer ajuste exige recompilar + entrar em Play mode.

**Spec proposta — `Pangea Skirmish/UI/UGUI Layout Studio`:** montar a UI num GameObject temporário na cena
(fora de Play mode) com os componentes UGUI reais, iterar visualmente (arrastar/redimensionar RectTransform com as
ferramentas nativas) e um botão **"Gerar código C#"** que serializa a hierarquia em código equivalente ao que os
HUDs fazem à mão. Aplicar `UiSkin` automaticamente; preview de estados com/sem skin.
**Pré-requisito de alto valor:** extrair `UiFactory.cs` (runtime) com os helpers de fábrica hoje duplicados em
`RoomHUD.cs:735-758` e nos outros HUDs — a janela gera código chamando esses helpers em vez de reinventar.

> Decisão de fundo a tomar: manter UGUI-por-código (+ ferramenta) **ou** migrar UI nova para UI Toolkit (UXML/USS).

---

## 7. Balanceamento / Magias (item 7) — vários problemas estruturais

| Sev | Finding | Arquivo | Correção (nº atual → sugerido) |
|-----|---------|---------|-------------------------------|
| 🔴 | Precisão/esquiva/crít/variância = código morto (ver Top Críticos #1) | `AttackResolver.cs:71,107` | Religar `RollHit`/`RollDamage` |
| 🔴 | Potência de magia NÃO escala com mana — mana só compra alcance | `SpellTypes.cs:39`, `SpellResolver.cs:50` | Custo vira piso real + `spellPotencyPerExtraMana≈0.25` |
| 🟠 | Escudo Mágico não expira de fato + Concentrar barato = tank quase infinito | `SpellResolver.cs:88`, `StatusEffects.cs:79` | Não fazer `Max(RoundsLeft)` no recast; `shieldCapacityFactor 1→0.5` |
| 🟠 | 6 elementos com potência idêntica (`basePotency=1`); Fire=Water (mesmo par) → Magic domina | `GameTuning.cs:540-550` | Diferenciar basePotency + dar par/efeito único a cada elemento |
| 🟠 | `concentrateManaGain=3` é knob MORTO — código usa `ManaRegen` (=6 p/ mago, 2× o GDD) | `RoundManager.cs:517` | Ler o knob OU documentar `ganho=ManaRegen` |
| 🟡 | `spellApCost=1` sem leitor — magias podem não gastar PA (spam) | `GameTuning.cs:526` | Confirmar gate de PA em `CommitSpell`; adicionar se ausente |
| 🟡 | `PhysicalMight` dá +STR e absorção VIT juntos por 1 mana = abertura dominante melee | `SpellResolver.cs:71` | Separar buff ofensivo/defensivo ou fatores 0.5→0.34; STR decair por round |
| 🟡 | Empurrão de Ar + dano de parede = kite/execução sem contra-jogo | `SpellResolver.cs:120` | Gate por mana/PA + resistência a push por footprint ou diminishing |
| ⚪ | Crít `1.5` no código vs "×2" no GDD-001; descrição d20 divergente | `BattleTypes.cs:47` | Alinhar GDD ao código |
| ⚪ | Budget de atributos custo 1:1 até 20 → dump extremo é sempre ótimo | `CharCreationHUD.cs:175` | Custo crescente por faixa ou `attributeMax 20→15` |

**Sugestões:** knobs reais `spellPotencyPerExtraMana` e `spellManaCostFloor*`; **tabela de interações elementais**
(Fire+Water=vapor etc., hoje só parcial); reação de oportunidade contra kite; **unificar mitigação** num único
`ApplyDamage(amount, DamageType)` (hoje ataque ignora `PhysicalDefense`, magia subtrai `MagicDefense` — inconsistente).

---

## 8. Bugs (item 8)

Bugs confirmados já capturados acima (Top Críticos #1–3; placement #2; editor tooling; balance). Bugs adicionais do netcode:

| Sev | Finding | Arquivo |
|-----|---------|---------|
| 🟡 | Reconexão impossível — disconnect mata o jogador com `TakeDamage(hp+9999)` (queda de rede = eliminação) | `RoomManager.cs:191` |
| 🟡 | Snapshot de resync não restaura status effects/mana reservada → desync silencioso persiste | `LockstepBattleSync.cs:441` |
| 🟡 | Host pode iniciar partida com 1 jogador (vitória degenerada imediata) | `RoomManager.cs:316` |
| 🟡 | Race: plano do cliente "adiantado" descartado se chega antes de `BeginCollection` | `LockstepBattleSync.cs:152` |
| ⚪ | `EnsureWired` reconstrói `_units` de `Dictionary.Values` (ordem não-determinística) | `LockstepBattleSync.cs:96` |
| ⚪ | Buffer de chunk pode descomprimir incompleto/corrompido (sem validar contiguidade de `seq`) | `LockstepBattleSync.cs:490` |
| ⚪ | `HeartbeatLoop` `async void` sem cancelamento pode vazar entre sessões | `LobbyService.cs:139` |

**⏳ PENDENTE (re-rodar após 15h):** caça-bugs geral em `RoundManager`/`PlanningController`/`Unit`/`SpellResolver`/
`StatusEffects`/`GridManager` (NRE, off-by-one, coroutines, eventos não desinscritos) + revisão de arquitetura/perf
(God classes de 1300–1900 linhas, GC em Update loops — relevante p/ WebGL single-thread).

---

## Sugestões extras (fora da sua lista)

- **Testes de determinismo headless** — rodar a mesma sequência de planos+seed 2× e comparar `ComputeStateHash`
  pegaria regressões de desync (como o #2) antes do jogador. Alto valor p/ um jogo lockstep sem testes de rede.
- **Fechar o loop de fim de partida** — a fase `PostGame` existe no enum mas nada a aciona; após vitória os
  clientes ficam presos na cena Battle. Levar todos de volta ao Lobby com placar.
- **Rate-limit dos logs de rede** — RPCs de caminho quente logam a cada evento (caro em WebGL).
- **Determinismo de ponto flutuante** — auditar se toda a matemática de resolução é inteira/fixed-point;
  um único `float` arredondado diferente entre wasm/mono vira desync.
