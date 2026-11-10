# GDD-015: Modos de Jogo — PvP e Coop

| Campo | Valor |
|-------|-------|
| **Status** | Aprovado — passos 1 a 4 implementados (2026-11-10), aguardam partida de teste |
| **Substitui** | Seleção de modo atual (`gameMode` 0 = TDM, 1 = FFA) |
| **Depende de** | GDD-013 (lore), GDD-014 (semi-turno por ticks) |

> **[CANON]** = decidido pelo Marcus. **[PROPOSTA]** = sugestão a confirmar.

---

## 1. Visão geral **[CANON]**

O jogo passa a ter **dois modos**:

| Modo | Quem enfrenta quem | Jogadores |
|------|--------------------|-----------|
| **PvP** | Jogadores contra jogadores, em **times** ou **todos contra todos** | 2 a 8 |
| **Coop — Descida ao Inferno** | Jogadores juntos contra feras controladas por IA | **1 a 4** |

Os dois usam o mesmo combate (ticks, reação sim/não) e o mesmo mundo 3D.

## 2. PvP **[CANON + PROPOSTA]**

- **[CANON]** Mantém os dois formatos que já existem: **Times** e **Todos contra todos**.
- **[PROPOSTA]** Na sala, o anfitrião escolhe primeiro o **modo** (PvP ou Coop) e depois o **formato** (Times / Todos contra todos). Hoje só existe a escolha do formato.
- **[PROPOSTA]** Vitória: último time ou jogador com unidade viva (como hoje).
- **[PROPOSTA]** O medidor de Yorgoth (seção 5) também vale no PvP, mas só como efeito de ambiente, sem inimigos de IA.

## 3. Coop — Descida ao Inferno **[CANON + PROPOSTA]**

**[CANON]** Os jogadores descem **camadas cada vez mais fundas** de Pangea, enfrentando feras. Cada camada é mais difícil e dá conduítes melhores. Pode ser jogado **sozinho ou com até 4 jogadores**.

### 3.1 Estrutura de uma descida **[PROPOSTA]**

1. A descida começa na **superfície** (camada 1).
2. Cada camada é **uma batalha** num mapa próprio. Vence quem eliminar todas as feras.
3. Ao vencer, o grupo escolhe: **descer** para a próxima camada ou **recuar** e encerrar a descida com o que ganhou.
4. Se todas as unidades dos jogadores morrerem, a descida termina.

Isso segue a lore: **descer tem peso, recuar não tem nenhum** (GDD-013 §7).

### 3.2 O peso de descer **[PROPOSTA]**

- As unidades **não se curam por completo** entre camadas: recuperam uma parte da vida e da Mana.
- A cada camada, o **custo em Instinto** das ações físicas sobe um pouco (o corpo entra na densidade).
- Não existe número máximo de camadas. A dificuldade cresce sem fim, como no paradoxo de Aquiles e a tartaruga: sempre há mais uma.

### 3.3 Camadas **[PROPOSTA]**

| Faixa | Terreno | Inimigos |
|-------|---------|----------|
| 1–3 | Superfície: grama, pedra, água | Feras pequenas, poucas |
| 4–6 | Cavernas: pedra, desníveis fortes | Mais feras, feras maiores |
| 7+ | Profundezas: lava, espaço distorcido | Feras de elite; a distorção Física altera movimento e projéteis |

- O mapa de cada camada é **gerado por regras** a partir da profundidade (tamanho, desníveis, tipos de terreno), com semente compartilhada para todos verem o mesmo mapa.
- O número e a força das feras escalam com **profundidade × número de jogadores**.

### 3.4 Recompensa **[PROPOSTA]**

- Cada camada vencida dá um **conduíte** para o grupo escolher entre opções. Quanto mais fundo, mais elementos ele ressoa e mais forte é.
- A **camada mais funda alcançada** fica registrada por jogador: é a marca de profundidade (progressão).
- O efeito dos conduítes depende da magia nova, que ainda não existe. Nesta etapa eles dão só bônus de atributo.

## 4. IA das feras **[CANON + PROPOSTA]**

**[CANON]** IA **simples e previsível**: regras fixas por tipo de fera.

**[PROPOSTA]** Regras:

- A IA decide **nos mesmos pontos de decisão** dos jogadores (GDD-014) e monta uma fila de ticks como qualquer unidade.
- Regra base: **avançar até o inimigo mais próximo; atacar quando estiver no alcance**.
- Cada tipo de fera muda um detalhe: a que persegue o mais fraco, a que ataca à distância, a que protege outra.
- A IA é **determinística**: usa só o estado da batalha e o gerador de números da partida, com desempate por id da unidade. Assim cada máquina calcula a mesma jogada e a sincronização não quebra. Nenhum plano de IA trafega pela rede.
- Reação das feras: decidida pela mesma regra automática que já existe (AGI ≥ VIT esquiva, senão bloqueia).

## 5. Medidor de Yorgoth **[PROPOSTA]**

Da lore: cada morte manda uma alma ao Inferno e fortalece Yorgoth.

- Cada unidade morta (de qualquer lado) soma ao medidor da batalha.
- No Coop, o medidor cheio faz **surgir uma fera extra** e escurece o mapa.
- O medidor enche mais rápido quanto mais funda a camada.

## 6. Jogar sozinho **[CANON + PROPOSTA]**

- **[CANON]** O Coop aceita 1 jogador.
- **[PROPOSTA]** Sozinho, o jogo roda **sem servidor de rede**, usando o caminho local que já existe para a criação de conteúdo. Vira também o modo de treino e substitui o tutorial planejado no roadmap.
- **[PROPOSTA]** Jogando sozinho, o jogador controla **um grupo de até 3 unidades**, para a batalha não ficar 1 contra muitos.

## 7. Ordem de implementação **[PROPOSTA]**

1. **Seleção de modo** na sala (PvP / Coop) e condição de vitória do Coop.
2. **IA determinística** das feras, com um tipo de fera (usa o modelo `Monster`).
3. **Uma camada jogável** em Coop, sozinho e em grupo.
4. **Descida:** sequência de camadas, escolha entre descer e recuar, peso de descer.
5. **Geração de mapa** por profundidade e mais tipos de fera.
6. **Recompensas** e marca de profundidade.
7. **Medidor de Yorgoth.**

## 8. Fora do escopo desta etapa

- A magia nova e o efeito real dos conduítes.
- Objetivos de mapa no PvP.
- Chefes de camada.

## 9. Notas de implementação (2026-11-10)

Feito (passos 1 a 3 da seção 7):

- **Modo na sala:** `gameMode` 0 = PvP Times, 1 = PvP Todos contra todos, 2 = Coop (`GameModes`). A sala tem três botões.
- **Coop:** todos os jogadores ficam no time 0 e posicionam na metade esquerda do mapa. Quando todos posicionam, o host cria as feras (jogadores + 1) na metade direita, pelo mesmo RPC de spawn.
- **Vitória:** o time que sobrar. Mensagens próprias de vitória e derrota.
- **IA (`BeastAI`):** cada máquina calcula o plano da fera no início da simulação de ticks. Até 2 deslocamentos em direção ao hostil mais próximo e até 2 ataques, dentro do orçamento de ticks. Reação automática.
- **Sozinho:** funciona pela sala em loopback que já existia (Multiplayer → marcar loopback → criar sala → Coop).

Ainda não feito:

- Botão próprio de "Coop solo" no menu e grupo de até 3 unidades para quem joga sozinho.
- Limite de 4 jogadores na sala em Coop.
- Passos 5 a 7: geração de mapa por profundidade, mais tipos de fera, recompensas (conduítes) e medidor de Yorgoth.

Passo 4 — a descida (feito):

- Ao vencer uma camada, o **anfitrião** escolhe **Descer** ou **Recuar**; os outros veem o aviso de espera.
- **Descer:** as feras mortas saem, os jogadores voltam ao ponto de partida, e entram `jogadores + camada` feras, mais fortes a cada camada (+10% de atributos por camada).
- **Peso de descer:** entre camadas a unidade recupera só 30% da vida máxima e 50% da Mana. Quem caiu volta de pé com 25% da vida. Como o Instinto ainda não existe, o custo crescente de Instinto ficou para depois.
- **Recuar:** a descida termina em vitória, com o número de camadas vencidas.
- **Marca de profundidade:** a camada mais funda vencida fica salva no aparelho (`PlayerPrefs`, chave `pangea.coop.deepestLayer`). Ainda não aparece em nenhuma tela.
- O mapa é o mesmo em todas as camadas até o passo 5.

## 10. Ajustes após a primeira partida (2026-11-10)

A primeira partida Coop jogada (sozinho, 1 contra 2) terminou com o jogador morto no tick 6 sem causar dano. Correções:

- **Ataque de oportunidade:** só dispara quando a unidade **entrou andando** no alcance de um inimigo e depois **sai** sem tê-lo atacado. Aproximar-se para atacar no ponto de decisão seguinte não é mais punido. A regra antiga ("o inimigo veio até mim e eu saí" não dá gatilho) continua valendo.
- **Andar:** cada ação de andar cobre no máximo `maxTilesPerMove` (4) tiles. Antes cobria todo o atributo de movimento (8 a 11 tiles por tick).
- **Feras:** planejam 1 andada + 1 ataque por decisão (antes 2 + 2), então reagem ao jogador a cada ponto de decisão.
- **Quantidade de feras:** `jogadores + camada − 1` (1 fera na camada 1 jogando sozinho).
- **Força das feras:** 55% dos pontos de atributo do jogador na camada 1 (antes 65%), +10% por camada.
- **Reenvio de plano:** a fase sai de Planejamento assim que a resolução começa; o timer não reenvia mais o plano.
