# Migração UGUI → UI Toolkit (Pangea Skirmish)

Objetivo: trocar toda a UI (hoje UGUI montada em código C#) por **UI Toolkit** (UXML/USS),
com um fluxo em que o Marcus consiga **criar/editar as telas visualmente** no UI Builder nativo.

> Estado atual: UI é 100% UGUI construída em C# na unha (`new GameObject + AddComponent<Image/Text/Button>`).
> Telas: `MainMenuManager` · `Net/RoomHUD` · `Net/CharCreationHUD` · `Net/PlacementSync` (overlay) · `BattleHUD`.
> Pacote `com.unity.modules.uielements` já presente; UI Builder é nativo no Unity 6000.5.

---

## Princípio: migração ADITIVA, tela por tela, verificada no Unity

Não fazemos um "big-bang" às cegas. Cada tela nova em UI Toolkit é construída **ao lado** da versão
UGUI existente; só quando a nova compila e roda é que a antiga é removida. Isso garante que o jogo
nunca fica quebrado no meio da migração.

**Cada tela migrada precisa do Unity aberto** (com o bridge MCP), para: criar/validar os assets
(`.uxml`, `.uss`, `PanelSettings`), compilar o controller e testar em Play mode.

---

## Arquitetura escolhida (combina com o código atual, que é Resources-first)

```
Assets/
├─ Resources/UI/
│  ├─ PangeaPanelSettings.asset      ← PanelSettings (Scale With Screen Size, ref 1920×1080)
│  ├─ Theme/PangeaTheme.uss          ← tema base (variáveis de cor/tipografia + classes .pg-*)
│  └─ Screens/
│     ├─ MainMenu.uxml   MainMenu.uss
│     ├─ Lobby.uxml      Lobby.uss
│     ├─ CharCreation.uxml …
│     ├─ Placement.uxml  …
│     └─ Battle.uxml     …
└─ Scripts/UI/
   ├─ PangeaScreen.cs                ← classe-base runtime (carrega UXML+PanelSettings via Resources)
   └─ {Tela}Screen.cs               ← um controller por tela (Bind() liga elementos a lógica)
```

Por que `Resources/`: o jogo já carrega sprites/animações/prefabs via `Resources.Load` e monta a UI
por código (sem prefabs de UI na cena). Mantendo UXML/PanelSettings em `Resources/`, o controller
faz `Resources.Load<VisualTreeAsset>("UI/Screens/MainMenu")` — zero fiação manual na cena, mesmo
estilo do resto do projeto. (Se depois preferirmos prefabs+UIDocument na cena, dá pra trocar.)

### Runtime (padrão de cada tela)
`PangeaScreen` (MonoBehaviour, `[RequireComponent(UIDocument)]`) carrega o `PanelSettings` e o UXML
da tela por Resources, e chama `Bind()` — onde o controller faz `Root.Q<Button>("play")` etc. e liga
os callbacks. Substitui o `BuildXxxPanel()` gigante de hoje por um `Bind()` enxuto sobre o UXML.

### Autoria visual (o fluxo do Marcus)
1. `Pangea Skirmish/UI/New UI Screen…` → gera o trio `{Tela}.uxml` + `{Tela}.uss` + `{Tela}Screen.cs`.
2. Abrir o `.uxml` no **UI Builder** (`Window ▸ UI Toolkit ▸ UI Builder`, ou `Pangea Skirmish/UI/Open UI Builder`)
   e desenhar a tela arrastando componentes, aplicando as classes `.pg-*` do tema.
3. Nomear (`name`) os elementos que o código precisa referenciar.
4. No `{Tela}Screen.cs`, preencher `Bind()` ligando os elementos nomeados à lógica.

---

## Tema (`PangeaTheme.uss`)

Variáveis centrais (cor de painel FFT, texto, acento, cores de time e dos 6 elementos, portadas do
`GameTuning`) + classes reutilizáveis: `.pg-panel`, `.pg-title`, `.pg-button`, `.pg-button--primary`,
`.pg-input`, `.pg-list`, `.pg-chip`, `.pg-badge--teamA/teamB`. Assim toda tela sai com o mesmo visual
sem repetir estilo, e um ajuste de cor no tema propaga para tudo.

---

## Ordem de migração (simples → complexo)

| # | Tela | Origem UGUI | Complexidade | Observações |
|---|------|-------------|--------------|-------------|
| 1 | **MainMenu** | `MainMenuManager` (menu raiz) | baixa | tela de referência; valida o padrão ponta a ponta |
| 2 | **Lobby/Sala** | `Net/RoomHUD` (~800 ln) | média | lista de jogadores, chat, config do host; casar com os findings de UI MP |
| 3 | **CharCreation** | `Net/CharCreationHUD` (~368 ln) | média | steppers de atributo + budget; add preview de sprite |
| 4 | **Placement** | overlay em `Net/PlacementSync` | baixa/média | zona + confirmar; casar com findings de spawn point |
| 5 | **BattleHUD** | `BattleHUD` (~1892 ln) | ALTA | maior tela; fazer por último, em partes |

> Ao migrar 2–5, aplicar de brinde os findings de UI/UX do relatório de polish
> (ready-check, feedback de rede, timer sincronizado, layout responsivo, "quem sou eu").

## Checklist por tela
- [ ] `.uxml` + `.uss` criados (scaffolder) e desenhados no UI Builder
- [ ] Elementos que o código usa têm `name`
- [ ] `{Tela}Screen.cs` com `Bind()` completo, 0 erros de compilação (`read_console`)
- [ ] Testado em Play mode (host + cliente quando for tela de MP)
- [ ] Versão UGUI antiga removida e referências trocadas
- [ ] Smoke test do fluxo MP completo não regrediu

## O que NÃO migra (permanece)
- Lógica de rede/jogo (RoomManager, LockstepBattleSync, RoundManager…). Só a camada de **apresentação** muda.
- `UiSkin.cs` (slicing de sprite UGUI) fica obsoleto para UI; avaliar remover ao fim da migração.

---

## STATUS ATUAL — HANDOFF (2026-07-07)

> A partir daqui o trabalho continua com o **Hermes agent**. Tudo abaixo é o estado real.

**Branch:** `rusted` (NUNCA commitar em `master`). Commits de polish já feitos:
- `b99f9db` — item 4 (menus consolidados) + fundação UI Toolkit
- `59c05ec` — MainMenu migrado (tela-piloto)
- `8d74395` — Lobby/Sala (RoomHUD) migrado

**Como trabalhar (obrigatório para verificar):** o Unity precisa estar **aberto com o bridge MCP**.
Ciclo por tela: editar → `refresh_unity(compile=request)` → `read_console(types=[Error])` até 0 erros →
`manage_editor(play)` → `ScreenCapture` para ver o render → `manage_editor(stop)` → commit na `rusted`.

**Telas — feito e a fazer (nesta ordem):**
- ✅ MainMenu — `MainMenuManager` (troquei `BuildMenuPanel` por `Spawn<MainMenuScreen>`; menu 100% MP: só Multiplayer / Criar Personagem / Sair).
- ✅ Lobby/Sala — `RoomHUD` reescrito como `PangeaScreen`; `Room.uxml` tem `lobby-view` + `room-view`; toda lógica de rede intacta; flexbox responsivo; UX extra (copiar código, `(host)`, badges de time, chat de sistema).
- ✅ **CriaçãoPersonagem** — `CharCreationHUD.cs` reescrito como `PangeaScreen` (commit d72feb0). `CharCreation.uxml`/`.uss` + `MpPhaseDirector` usa `Spawn<CharCreationHUD>`. Valido: 0 erros + render Play mode OK.
- ⚠️ **Placement** — **NÃO É TELA UI**. `PlacementSync.cs` é `NetworkBehaviour` (lógica de rede + input de clique via `Mouse.current` raycast no `GridManager`). A única "UI" dele é `SetWaitingText` que aparece no **BattleHUD** (item abaixo). O handoff original descreveu "overlay" por engano — na prática são tiles isométricos do grid (confirmado por print do Marcus, 2026-07-07). **Não há UXML/USS a criar aqui.** A migração de UI só ocorre quando o BattleHUD virar UI Toolkit e o `SetWaitingText` passar a ser `Root.Q<Label>("waiting").text = ...`.
- ✅ **BattleHUD** — `Assets/Scripts/BattleHUD.cs` reescrito como `PangeaScreen` (commit 83b561e). `BattleHUD.uxml`/`.uss` (estilo FFT, estrutura declarativa) + `GameBootstrap` usa `Spawn<BattleHUD>()`. API pública 100% preservada (`LogAction`, `SetWaitingText`, `Bind*`, `ShowMainMenu`, `SetMoveState`...) para não quebrar ~40 callers. Compila 0 erros + render Play mode OK (topo fase/timer, HISTÓRICO, UNIDADE, COMANDOS cascata, waiting text). Diff: -1557/+663 lin.

**Gotchas aprendidos (não tropece de novo):**
- `NetworkManager.ServerClientId` é **estático** (não `.Singleton.ServerClientId`).
- `TextField` do UI Toolkit vem com fundo branco no elemento interno — já corrigido no tema via `.pg-theme .unity-base-text-field__input`.
- Vários `UIDocument` compartilham o `PanelSettings`. Para não interceptar cliques da tela de baixo, **esconda pela raiz** (`Root.style.display = None`), não deixe a raiz visível com filhos escondidos.
- Objetos de tela (RoomHUD, MainMenuScreen) são criados em runtime → só existem em **Play mode** (`FindAnyObjectByType` retorna null em edit mode).
- Screenshot no Editor: o Play mode às vezes sai entre chamadas MCP. Agende a captura **dentro** do play: `root.schedule.Execute(() => ScreenCapture.CaptureScreenshot(path)).StartingIn(200)` e leia o arquivo depois.
- Mudança de `.uxml`/`.uss` **não** recompila; mudança de `.cs` recompila (o domain reload derruba o bridge MCP por 1-2s — é normal, reconecta sozinho).

**Fluxo para criar/migrar a próxima tela:**
1. `Pangea Skirmish/UI/New UI Screen…` → gera `{Nome}.uxml` + `.uss` + `{Nome}Screen.cs`.
2. `Pangea Skirmish/UI/Open UI Builder` → desenhar; dar `name` aos elementos usados no código; aplicar classes `pg-*`.
3. Preencher `Bind()` no controller: `Root.Q<Tipo>("nome")` + ligar callbacks.
4. Trocar a UGUI antiga por `PangeaScreen.Spawn<T>()` (ou reescrever a classe existente como `PangeaScreen` preservando a API pública, como foi feito no `RoomHUD`).
5. Verificar no Unity e commitar na `rusted`.

**Fora da migração de UI (backlog de polish — ver `docs/polish-review-2026-07-07.md`):**
- 🔴 Bugs críticos: (1) todo ataque acerta 100% — `RollHit`/`RollDamage` são código morto (`AttackResolver.cs:71,107`); (2) desync por empate de iniciativa sem tiebreaker `unitId` (`RoundManager.cs:645,839`); (3) join no meio trava o round (`ConnectionApproval=false`); (4) queda de host = tela morta no cliente.
- Item 3 (remover singleplayer): mapa cirúrgico no relatório. Item 7 (balanceamento de magias). Item 5 (janela de animação: spec "Sprite Animation Studio").
- 2 dimensões da revisão não rodaram (caça-bugs geral + arquitetura/perf) — bateram no limite de sessão; rerodar.
