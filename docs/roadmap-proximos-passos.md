# Roadmap — próximos pontos de desenvolvimento

> Definido em 2026-07-31, sobre `master` = `c9d292d` (merge do PR #4, rotação de vista completa).
> Base: auditoria de limpeza do repositório, com achados verificados no código.
> Três caminhos escolhidos, na ordem em que se sustentam.

---

## 1. Rede à prova de bala

**Fecha os dois críticos abertos desde 2026-07-07** (`docs/polish-review-2026-07-07.md`).
Ambos foram reconfirmados no código em 2026-07-31 — continuam exatamente como estavam.

### 1.1 `ConnectionApproval = false`

`Assets/Scripts/Net/NetBootstrap.cs:200` monta o `NetworkConfig` com aprovação de conexão
desligada. Qualquer cliente que conecte **durante Placement ou Battle** entra como slot
válido, mas nunca submete plano — e a barreira de sincronização espera por ele. O round
trava para todos os jogadores, sem recuperação.

**O que fazer:** registrar um `ConnectionApprovalCallback` que rejeite conexão quando
`RoomManager.Instance.CurrentPhase != RoomPhase.Lobby` ou quando a sala estiver cheia,
devolvendo motivo legível para o cliente exibir.

**Atenção:** `NetworkConfig` é criado na mão em `EnsureExists()` porque, em NGO 2.x, um
`NetworkManager` adicionado por `AddComponent` em runtime não instancia o config (é campo
serializado). O callback precisa ser registrado no mesmo ponto, antes de `StartHost`.

### 1.2 Queda do host não é tratada no cliente

`Assets/Scripts/Net/NetBootstrap.cs:177` declara `public Action<ulong> OnClientDisconnected`
e **nenhum script assina** — é dead code.

O que existe hoje é outra coisa: `RoomManager.cs:158` assina o
`NetworkManager.Singleton.OnClientDisconnectCallback` do próprio NGO, e faz isso dentro de
um `if (IsServer)` (linha 154). Ou seja, só o **host** reage a desconexões, para remover o
slot de quem saiu. O cliente não escuta nada. Host cai → tela morta permanente, sem modal,
sem retorno ao menu.

**O que fazer:** assinar o callback também no cliente, e ligar ao
`NetBootstrap.OnClientDisconnected` já declarado. Modal "Conexão perdida" com retorno ao
MainMenu, tratando os dois casos (queda do host vs. expulsão).

**Esforço:** 3-5 dias · **Risco:** mexer no handshake do NGO 2.x pode quebrar o caminho
Relay que hoje funciona. Testar com dois builds WebGL reais, não só no editor.

**Por que primeiro:** o jogo é 100% multiplayer desde `943796d` ("Remove Single-Player da
batalha") — não existe modo offline de escape. Enquanto a partida puder morrer sozinha,
todo playtest dos outros dois caminhos fica contaminado.

---

## 2. Mapa de tutorial

Substitui a reintrodução da IA de combate.

### Tensão de design a resolver antes de codar

O jogo não tem caminho single-player. Um tutorial jogável sozinho exige uma escolha:

| Opção | O que é | Custo | Reabre a IA? |
|---|---|---|---|
| **(a) Oponente roteirizado** | Script fixo de ações por round, sem scoring nem pathfinding adaptativo | Baixo | Não |
| (b) Single-player mínimo | Reintroduz um caminho de partida local | Médio | Parcial |
| (c) Sem oponente | Só movimentação, UI e magia em alvos estáticos | Muito baixo | Não |

**Recomendação: (a).** Entrega a sensação de partida real sem reabrir o `SimpleEnemyAI`
(removido em `943796d`, cujo design segue documentado em `design/gdd/GDD-005-ai-system.md`,
agora marcado como *Design-only*). Um roteiro fixo também é **determinístico por
construção** — não briga com o lockstep e o hash FNV-1a por round de
`LockstepBattleSync.cs:443`, que é onde uma IA adaptativa causaria desync.

### O que o tutorial precisa cobrir

Derivado do que o jogo já faz: movimentação em grid com waypoints, rotação de vista Q/E
(nova, e o compasso do `BattleHUD` existe justamente para orientar), planejamento por
round com timer, ataque e alcance, as seis escolas de magia de `SpellTypes.cs:6`, e efeitos
de tile.

**Peças que já existem e devem ser reaproveitadas:** `MapStorage` para o mapa do tutorial,
`TileDatabase`, `UnitDefinitionRegistry`, e o `SandboxController` — que já monta partida
fora do fluxo de rede e é o ponto de entrada natural.

**Esforço:** 5-8 dias · **Risco:** escopo. Tutorial tende a crescer; fixar antes quantos
passos ele tem.

---

## 3. Conteúdo: unidades e mapas

`Assets/Resources/Units/` tem exatamente **4** `UnitDefinition` (archer, mage, tank,
warrior) e `Assets/Resources/Maps/` **não existe**.

A máquina de conteúdo já está pronta e ociosa: `UnitDefinitionRegistry.cs` (carrega por
`Resources.LoadAll<UnitDefinition>("Units")`, então basta adicionar assets),
`TileDatabase.cs`, `MapStorage.cs`, `CollabMapSync.cs` (edição colaborativa de mapa em
rede) e as 6 escolas de `SpellTypes.cs`. O gargalo virou conteúdo, não código.

**Esforço:** 5-8 dias · **Risco:** balanceamento. Sem oponente para testar, N unidades novas
viram N unidades desequilibradas — por isso este caminho vem **depois** do tutorial, que
entrega o oponente roteirizado como banco de provas.

**Pré-requisito de higiene:** `design/gdd/GDD-002-units-classes.md` precisa ser reconciliado
com o que existe hoje antes de servir de base para unidades novas.

---

## Ordem recomendada

```
1. Rede à prova de bala   → nenhuma partida morre sozinha
2. Mapa de tutorial       → oponente roteirizado vira banco de provas
3. Conteúdo               → escala em cima de base estável e testável
```

---

## Dívida conhecida (registrada, fora do escopo acima)

- `sfx_view_rotate.wav` não existe. `AudioManager.cs:82` o carrega e `gen_sounds.py` nunca
  ganhou a função; `CameraController.cs:367` cai no fallback, então **o giro Q/E toca o som
  de dash**. Decisão de 31/07: não corrigir agora.
- `IsoConfig` também não existe como asset — `GameBootstrap.cs:36` e `SandboxController.cs:56`
  sempre caem em `ScriptableObject.CreateInstance` com defaults hardcoded.
- Migração UI Toolkit incompleta: `MainMenuManager.cs` e `Net/MpPhaseDirector.cs` ainda
  montam UGUI em C#; as outras 8 telas já são UXML/USS.
- Sem CI. `.github/` não existe, apesar de 53 testes `[Test]` em 4 arquivos EditMode e do
  comando `-runTests` já documentado em `Assets/Tests/README.md`.
- Sem `LICENSE`, com arte de terceiros redistribuída em repositório público.
- `productName` ainda é `My project` (`ProjectSettings.asset:16`), o que sustenta duas
  gambiarras: o comentário de `BuildWebGL.cs` sobre 404 no wasm e o `sed` de
  `deploy-ghpages-clean.sh:104`. Trocar muda `persistentDataPath` e apaga saves locais.
- Arquivos-monstro: `PlanningController.cs` (1697), `RoundManager.cs` (1528),
  `GridManager.cs` (1473), `SandboxController.cs` (1036), `Net/RoomHUD.cs` (1015).
- Warnings `CS0618` de `ServerRpcAttribute.RequireOwnership` obsoleto em
  `LockstepBattleSync.cs` (4x) e `RoomManager.cs` (3x).
- `SaveLoadUI.cs` e `MaterialFactory.cs` são órfãos confirmados por GUID. Mantidos por
  decisão de 31/07 — `GDD-010` projeta save/load.
