# GDD-022 — Celular (WebGL no navegador)

Prioridade 1 do backlog (decidida em 2026-11-13).

## Decisões

| Tema | Decisão |
|---|---|
| Plataforma | O mesmo build WebGL do gh-pages, jogado no navegador do celular. Sem app nativo. |
| Orientação | Só paisagem. Em retrato aparece um aviso "Gire o celular". |
| Hover no toque | 1º toque mostra a prévia (célula, alvo, alcance); 2º toque no mesmo lugar confirma. |

## Controles

| Ação | Mouse/teclado | Toque |
|---|---|---|
| Apontar / pré-visualizar | mover o mouse | 1º toque |
| Confirmar no mundo | clique esquerdo | 2º toque no mesmo ponto (até 3 s, raio ~40 px) |
| Mover a câmera | botão direito arrastando / bordas | arrastar com 1 dedo |
| Zoom | roda | pinça com 2 dedos |
| Girar a vista | Q / E | tocar na bússola (metade esquerda gira para um lado, direita para o outro) |
| Cancelar / voltar | Esc | botão "Voltar" na HUD, só no toque |
| Confirmar o round, modo livre, desfazer | Enter, Tab, Backspace | botões que já existem no painel de intenções |
| Ações 4–9 | teclas | botões da barra de ações (já existem) |

Botões de UI Toolkit funcionam com um toque só (não passam pela regra dos dois toques).

## Implementação

- `PointerInput` (estático): posição, clique, arrasto, pinça e "voltar" unificados para mouse e toque.
  Todo o gameplay lê dele em vez de `Mouse.current` / `Keyboard.current.escapeKey`.
- No toque recente, o mouse emulado pelo navegador é ignorado (evita clique duplo fantasma).
- `MobileHUD`: escala maior da UI no celular, aviso de retrato e botão "Voltar".
- Performance: no celular, `targetFrameRate` 30 e o nível de qualidade mais baixo.

## Fora deste passo

- Layout próprio para retrato.
- Tela cheia automática e template WebGL específico de celular.
- Modificadores Shift/Ctrl do modo livre no toque.
