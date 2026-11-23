# Mapa de dependências e lista de tarefas dos GDDs

> Gerado em 2026-11-19 a partir dos 25 GDDs em `design/gdd/`.
> Legenda: **[CANON]** = decidido; **[PROPOSTA]** = a calibrar.
> Este documento é um instrumento de trabalho; não substitui os GDDs.

---

## 1. Mapa de dependências

```mermaid
flowchart TD
  classDef lore fill="#3b2f5b,stroke:#c9b8ff,color:#fff
  classDef core fill="#1f3a5f,stroke:#80a4eb,color:#fff
  classDef magic fill:#4a2a5e,stroke:#d0a0ff,color:#fff
  classDef content fill:#3a4a2a,stroke:#b8d080,color:#fff
  classDef ux fill:#5a3a2a,stroke:#e0b080,color:#fff
  classDef legacy fill:#333333,stroke:#888,color:#ddd

  LORE["GDD-013 · Lore"]:::lore
  TICK["GDD-014 · Ticks"]:::core
  ATTR["GDD-016 · Atributos"]:::core
  REACT["GDD-017 · Reações"]:::core
  UXINT["GDD-018 · UX das intenções"]:::core
  MAGIA["GDD-019 · Catálogo de magias"]:::magic
  FORMA["GDD-020 · Formas de ataque"]:::magic
  INC["GDD-023 · Incremento de magia"]:::magic
  MODOS["GDD-015 · Modos PvP/Coop"]:::content
  EQUIP["GDD-021 · Equipamentos"]:::content
  PROG["GDD-024 · Progressão Coop"]:::content
  CEL["GDD-022 · Celular"]:::ux
  POL["GDD-025 · Polimento"]:::ux

  LEGACY["GDD-001/002/003/005/008 · legado"]:::legacy
  BASE["GDD-004/006/007/010/011/012 · base"]:::legacy

  LORE --> TICK
  LORE --> ATTR
  LORE --> MODOS
  LORE --> MAGIA
  LORE --> EQUIP
  LORE --> PROG

  TICK --> ATTR
  TICK --> REACT
  TICK --> MODOS
  TICK --> MAGIA
  TICK --> FORMA

  ATTR --> REACT
  ATTR --> MAGIA
  ATTR --> FORMA
  ATTR --> EQUIP
  ATTR --> PROG

  REACT --> FORMA
  UXINT --> MAGIA
  UXINT --> CEL
  UXINT --> POL

  MAGIA --> FORMA
  MAGIA --> INC
  MAGIA --> POL

  FORMA --> EQUIP
  FORMA --> CEL
  FORMA --> POL

  MODOS --> EQUIP
  MODOS --> PROG
  EQUIP --> PROG
  PROG --> POL
  INC --> POL

  BASE --> TICK
  BASE --> MAGIA
  BASE --> FORMA

  LEGACY -.->|substitui| TICK
  LEGACY -.->|substitui| ATTR
  LEGACY -.->|substitui| MAGIA
  LEGACY -.->|substitui| UXINT
```

### Como ler

- **Seta cheia** = “depende de / precisa estar estável”.
- **Seta tracejada** = “substitui / revisa”.
- **Cinza** = legado ou base; **colorido** = design atual.
- O nó `LEGACY` agrupa 001, 002, 003, 005 e 008; o nó `BASE` agrupa 004, 006, 007, 010, 011 e 012.

### Tabela de dependências

| GDD | Depende de | Substitui / revisa | Status |
|---|---|---|---|
| 013 | — | fundação do 003 | Rascunho (fundação) |
| 014 | 013 | 001 | Implementado |
| 015 | 013, 014, 016 | — | Em implementação (passos 5–7 pendentes) |
| 016 | 013, 014 | 002 (fórmulas) | Em implementação |
| 017 | 014, 016 | — | Implementado em parte |
| 018 | 013, 014, 016, 017 | 008 (UX) | Rascunho de reflexão |
| 019 | 013, 014, 016, 017, 018 | 003 | Em implementação |
| 020 | 016, 017, 018, 019 | 017, ataque por tile | Feito |
| 021 | 016, 020, 015 | 002 (arma única) | Rascunho para revisão |
| 022 | 018, 020 | — | Backlog 1 |
| 023 | 019, 020 | — | Backlog 2 |
| 024 | 015, 021, 013, 020 | 015 (expande), 021 (pontos) | Backlog 3 |
| 025 | 018, 019, 020, 021, 024 | 021 (visual), 008 (UI) | Polimento |

---

## 2. Lista de tarefas por frente

### F0 — Base em andamento (pré-requisito das frentes)

- [ ] **019** — Menu de bloco com aba “Magia” (Física/Elemental), ícones de elementos, custo, ticks, hover.
- [ ] **019** — Simulador mago INT 8 vs. guerreiro STR 8; ajustar números.
- [ ] **019** — Testes PlayMode: cada magia, interrupção, área sem aliado, cura com aliado.
- [ ] **019** — Reações mágicas (contrafeitiço, escudo arcano) — fora do corte, mas registrar.
- [ ] **015** — Passo 5: geração de mapa por profundidade (tamanho, desníveis, terreno, seed compartilhada).
- [ ] **015** — Passo 5: mais tipos de fera.
- [ ] **015** — Passo 6: recompensas (conduítes) e marca de profundidade visível.
- [ ] **015** — Passo 7: medidor de Yorgoth (fera extra + escurecer mapa).
- [ ] **021** — Implementar `ItemDef`, 5 slots, `Equipment.ApplyTo`, ficha e rede.
- [ ] **010** — Completar save/load (tile effects, status, cooldowns, iniciativa).
- [ ] **018** — Fechar decisões de UX: tempo sem limite, tutorial, fluxo mobile.

### F1 — Celular (GDD-022) · backlog 1

- [ ] F1.1 `PointerInput` estático: posição, clique, arrasto, pinça e “voltar” unificados para mouse e toque.
- [ ] F1.2 Ignorar o mouse emulado pelo navegador em toque recente (evita clique duplo fantasma).
- [ ] F1.3 `MobileHUD`: escala maior da UI, aviso de retrato e botão “Voltar”.
- [ ] F1.4 Performance no celular: `targetFrameRate` 30 e nível de qualidade mais baixo.
- [ ] F1.5 Testar em build WebGL real no navegador do celular (dois toques, pinça, arrasto).
- [ ] F1.6 Ajustar barras/botões para toque (hover → prévia no 1º toque).

### F2 — Incremento de magia (GDD-023) · backlog 2

- [ ] F2.1 `SpellBoost` (flags) + `SpellBoosts.Apply` em `Spells.cs`.
- [ ] F2.2 `PlannedSpell.Boosts`; `PlannedSpell.Def` devolve a cópia incrementada.
- [ ] F2.3 `SpellWire.boosts` no multiplayer.
- [ ] F2.4 `PlanningController.ShowSpellBoostMenu`: ligar/desligar e validar Mana livre.
- [ ] F2.5 Testes: reserva de Mana, duração na fila, alcance/área e resolução com os números novos.
- [ ] F2.6 (Fora do passo) Incrementar magia já na fila — hoje exige desfazer e replanejar.

### F3 — Coop: progressão (GDD-024) · backlog 3

- [ ] F3.1 Etapa A: pontos 15 + 5/nível, XP e dinheiro salvos no personagem/perfil.
- [ ] F3.2 Recompensas por camada (XP 5×camada, dinheiro 3×camada) e feras escalando com o grupo.
- [ ] F3.3 Descanso separado da descida: cura 100%, tempo avança, ficha DESCANSO.
- [ ] F3.4 Loja de XP no perfil (2 moedas → 1 XP).
- [ ] F3.5 Migração de personagens antigos (30 pontos → nível 4).
- [ ] F3.6 Validação: personagem só entra em sala com todos os pontos gastos.
- [ ] F3.7 Regra: personagem acima do nível 2 não é mais editado.
- [ ] F3.8 Etapa B (feita): drops, baú, ITENS DO GRUPO, vender/deixar/pegar — verificar e polir.

### F4 — Polimento (GDD-025)

#### F4.1 Identidade da UI

- [ ] Definir onde entra o pergaminho (painéis de leitura, fichas, tooltips) e onde fica o azul (HUD, botões).
- [ ] Reaproveitar `UI/Icons` e `UI/TextIcons`; gerar os que faltarem com `scripts/gen_icons.py`.
- [ ] Aplicar identidade única em todas as telas (`pg-*` e `bh-*`).

#### F4.2 Animação

- [ ] Editor de rig no Unity: mover membros/arma e salvar offsets em asset.
- [ ] Visualizador de animações no editor (câmera lenta, anotações).
- [ ] Folha de quadros (PNG lado a lado) para revisão.
- [ ] 5 estilos de golpe com antecipação, impacto e recuperação.
- [ ] Idle por postura; polir andar, bloquear, esquivar, conjurar e morrer.

#### F4.3 Roupas e inventário

- [ ] `scripts/gen_models.py`: modelo por item do catálogo.
- [ ] Item equipado aparece no corpo (Torso, Head, ArmL, ArmR, LegL, LegR).
- [ ] Inventário Diablo: boneco com 5 slots + mochila em grade.
- [ ] Itens salvos no personagem.
- [ ] Decidir tamanho da mochila, integração com o baú do grupo e destino dos itens na derrota.

#### F4.4 Ataque livre

- [ ] Revisar representação do machado e dos ataques à distância.
- [ ] Forma girável e golpes diferentes por arma.
- [ ] Definir giro em 4 ou 8 direções.
- [ ] Corrigir prévia de dano (crítico, variação, esquiva, bloqueio).
- [ ] Fantasma 3D com arma na mão.

### F5 — Dívida e harmonização (não-GDD)

- [ ] Reconciliar `GDD-002` com o código antes de gerar conteúdo novo.
- [ ] Marcar `GDD-003` e `GDD-005` como históricos no próprio arquivo.
- [ ] Completar save/load (`GDD-010`).
- [ ] Atualizar `docs/roadmap-proximos-passos.md`, que ainda descreve o combate por rounds.
- [ ] CI (`.github/`) e suíte de testes EditMode/PlayMode.
- [ ] Migrar `MainMenuManager` e `MpPhaseDirector` para UXML/USS.
- [ ] Corrigir `sfx_view_rotate.wav` e o asset `IsoConfig` (dívida conhecida).

---

## 3. Caminho crítico

O backlog oficial é **F1 → F2 → F3**, com **F4** em paralelo por frente. Mas o mapa mostra que as frentes dependem da base ainda em implementação:

```text
F0 (019 menu/015 passos 5-7/021 equipamentos/010 save)
        │
        ├── F1 Celular (precisa da barra de ações e UX de 018/020)
        ├── F2 Incremento (precisa do catálogo 019)
        └── F3 Progressão (precisa de 021 equipamentos e 015 descida)
        │
        └── F4 Polimento (precisa de 021/024 para roupas e inventário)
```

**Recomendação:** fechar F0 antes de abrir as frentes de backlog. Caso contrário, F1 e F3 vão retrabalhar sobre sistemas que ainda vão mudar.

---

## 4. Decisões a fechar antes de começar

| Frente | Decisão em aberto | GDD |
|---|---|---|
| UI | Onde entra o pergaminho e onde fica o azul | 025 |
| Ataque livre | O que está errado no machado e nos ataques à distância; giro em 4 ou 8 direções | 025 |
| Inventário | Tamanho da mochila; se o baú do grupo vira aba; o que acontece com itens na derrota | 025 |
| Magia | Como diferenciar personagens se todos conjuram tudo (afinidade, conduítes, aptidão) | 018 §5 |
| UX | Tempo de decisão sem limite; fluxo de toque dedicado; tutorial | 018 |
| Reações | Confirmar Interceptar/Passo reativo; ideias de PF2e fora do corte | 017 |
| Save | Se o save deve cobrir tile effects/status ou ficar apenas no perfil | 010 |
