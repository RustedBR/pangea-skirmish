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
