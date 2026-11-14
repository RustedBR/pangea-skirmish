# GDD-025 — Roteiro de polimento (decisões de 2026-11-14)

Cinco frentes pedidas pelo Marcus, com as respostas dele. Ordem: a identidade da UI vem primeiro.

| # | Frente | Decisão |
|---|---|---|
| 1 | Identidade da UI | Mistura do **azul atual refinado** com **pergaminho e tinta**. Uma só identidade em todas as telas; reaproveitar os sprites que existem (`UI/Icons`, `UI/TextIcons`) e gerar os que faltarem por `scripts/gen_icons.py`. |
| 2 | Ferramenta de animação | **Folha de quadros** (teste que grava os quadros de cada animação lado a lado em PNG, para o Claude ler) **+ visualizador** no jogo (tocar em câmera lenta e anotar). |
| 3 | Animações | **Por estilo, bem polido:** os 5 estilos de golpe (cortar, rachar, estocar, atirar, morder) com antecipação, impacto e recuperação; idle por postura; polir andar, bloquear, esquivar, conjurar e morrer. |
| 4 | Roupas e inventário | **Peça por item equipado:** cada item do catálogo tem modelo próprio (gerado por `scripts/gen_models.py`) e aparece no corpo. **Inventário estilo Diablo:** boneco com os 5 slots + mochila em grade; itens passam a ficar salvos no personagem. |
| 5 | Ataque livre | Revisar como os golpes aparecem na grade (em especial **machado** e **ataques à distância**), **forma girável** e **golpes diferentes** por arma. |

## Em aberto (perguntar ao começar cada frente)

- **UI:** onde entra o pergaminho (painéis de leitura, fichas, tooltips?) e onde fica o azul (HUD de batalha, botões?).
- **Ataque livre:** o que está errado hoje na representação do machado e dos ataques à distância; quantos golpes por arma; se o giro é em 4 ou 8 direções.
- **Inventário:** tamanho da mochila; se o baú do grupo (GDD-024) vira uma aba da mesma tela; o que acontece com os itens na derrota.

## Contexto técnico

- Animações são feitas por código em `UnitModel3D` (temporizadores `_attackT`, `_hitT`, `_blockT`…), sem clipes.
- Humanoides têm as partes Torso, Head, ArmL, ArmR, LegL, LegR; as roupas precisam seguir essas partes.
- Formas de golpe: `AttackShape.Cells` e `WeaponDef.shape` (`AttackShapeKind`).
- Tema atual: `Assets/Resources/UI/Theme/PangeaTheme.uss` (classes `pg-*`); a HUD de batalha usa classes próprias `bh-*`.
