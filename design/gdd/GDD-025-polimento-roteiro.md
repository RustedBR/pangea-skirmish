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

## Rodada 3 de feedback (2026-11-14, depois do build do gh-pages)

| Tema | Decisão |
|---|---|
| DEX à distância | Bônus por ponto de DEX cai de 4% para **2%**; STR continua 6%. |
| Duração das magias | Contada em **rounds** (1 round = `ticksPerRound` ticks); o dano contínuo (Queimando, zonas) segue **por tick**. |
| Tela de magias | **Grade de blocos pequenos com sprites, agrupada por elemento** (Fogo, Água, Terra, Ar, Mágico, Física; pares no grupo do 1º elemento) e **busca** por nome. A descrição fica numa área de altura fixa, para a tela não pular. |
| Celular | **Ação primeiro, um toque no alvo:** toca a ação na grade, vê o alcance, um toque no tile/inimigo põe na fila. Sem segundo toque para ações armadas. |
| Rig e animação | **Editor de rig no Unity:** selecionar e mover membros/arma com as ferramentas do Unity e salvar offsets num asset que o jogo lê. A seção de animações (visualizador) passa a existir **só no editor**, fora do menu do jogo. |
| Sala | Edições avançadas com ícones e **busca simples**; **presets nomeados** (Salvar como… / Carregar) com modo, regras e tuning, guardados no perfil do anfitrião. |
| Pergaminho | Textura **mais velha, com rasgos, bem mais escura**. |
