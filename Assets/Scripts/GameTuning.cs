using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Fonte única de tuning do jogo — TODOS os valores ajustáveis por design vivem aqui,
    /// organizados por sistema. Cada campo tem tooltip explicando o efeito prático de
    /// aumentar/diminuir. Editar o asset Assets/Resources/GameTuning.asset no Inspector.
    /// Acesso no código: Tuning.Get() (nunca retorna null).
    /// </summary>
    [CreateAssetMenu(fileName = "GameTuning", menuName = "Pangea/Game Tuning", order = 0)]
    public class GameTuning : ScriptableObject
    {
        // ═════════════════════════ COMBATE & REGRAS ═════════════════════════

        [Header("═══ COMBATE & REGRAS ═══")]
        [Tooltip("Segundos da fase de Planejamento por round. Menor = jogo mais frenético; maior = mais tempo para pensar.")]
        public float planningTime = 15f;

        [Header("═══ SEMI-TURNO POR TICKS (GDD-014) ═══")]
        [Tooltip("Segundos para decidir num ponto de decisão (SP). Em MP vale o planningTime da sala.")]
        public float decisionTime = 15f;
        [Tooltip("O relógio de decisão PARA enquanto há intenção armada ou prévia na tela, por no máximo este tempo total por ponto de decisão. Em rede, o host espera esse teto a mais.")]
        public float planningPauseBudget = 12f;
        [Tooltip("Janela entre ticks (segundos) para incrementar a próxima ação (golpe poderoso, mirar).")]
        public float incrementWindowSeconds = 1.8f;
        [Tooltip("Zoom mínimo (tamanho ortográfico) ao enquadrar o grupo durante os ticks. Maior = mais longe.")]
        public float tickFrameMinSize = 8f;
        [Tooltip("Quantos ticks à frente a fila de uma unidade pode ir.")]
        public int maxQueueTicks = 6;
        [Tooltip("Tiles por andada = moveTilesBase + AGI × moveTilesPerAGI, até maxTilesPerMove.")]
        public float moveTilesBase = 2f;
        public float moveTilesPerAGI = 0.67f;
        [Tooltip("Teto de tiles que uma ação de andar cobre.")]
        public int maxTilesPerMove = 6;

        [Header("═══ ESTÂMINA (GDD-016) ═══")]
        [Tooltip("Estâmina máxima = staminaBase + (STR + VIT) × staminaPerBodyPoint.")]
        public float staminaBase = 1f;
        public float staminaPerBodyPoint = 0.25f;
        [Tooltip("Recupera 1 de Estâmina a cada N ticks.")]
        public int staminaRegenTicks = 3;
        [Tooltip("AGI acima deste valor acelera a regeneração de Estâmina (o excedente que a andada já não aproveita).")]
        public int agiRegenFrom = 6;
        [Tooltip("A cada N pontos de AGI acima de agiRegenFrom, a Estâmina volta 1 tick mais cedo.")]
        public int agiRegenStep = 3;
        [Tooltip("Golpe poderoso multiplica o dano base por este valor (e custa +1 tick e Estâmina).")]
        public float powerStrikeMult = 1.5f;
        public int staminaPowerStrike = 2;
        public int staminaAim = 1;
        public int staminaQuickStep = 1;
        public int staminaReaction = 2;

        [Header("═══ MANA (GDD-019 §4) ═══")]
        [Tooltip("Recupera 1 de Mana a cada N ticks.")]
        public int manaRegenTicks = 2;
        [Tooltip("A cada N pontos de WIS, a Mana volta 1 tick mais cedo.")]
        public int wisRegenStep = 4;
        [Tooltip("Mana imediata que Concentrar dá (além da regeneração normal).")]
        public int concentrateMana = 3;
        [Tooltip("Embotamento dos sentidos: abaixo desta fração da Mana máxima, a unidade perde alcance e acerto.")]
        [Range(0f, 1f)] public float dullManaFraction = 0.25f;
        [Tooltip("Tiles de alcance perdidos no embotamento (só ataques/magias à distância; corpo a corpo não cai abaixo de 1).")]
        public int dullRangePenalty = 1;
        [Tooltip("Acerto perdido no embotamento (0.10 = −10%).")]
        [Range(0f, 1f)] public float dullHitPenalty = 0.10f;

        [Header("═══ MAGIAS (GDD-019) ═══")]
        [Tooltip("Catálogo de magias. Vazio = usa SpellCatalog (4 Físicas + 15 Elementais) do código.")]
        public SpellDef[] spells = System.Array.Empty<SpellDef>();
        [Tooltip("Conjuração Elemental é interrompida se o conjurador tomar ≥ esta fração do HP máximo durante a duração.")]
        [Range(0f, 1f)] public float spellInterruptHpFraction = 0.25f;
        [Tooltip("Conduíte: desconto de Mana nas magias que usam o elemento do conduíte.")]
        public int conduitManaDiscount = 1;
        [Tooltip("Conduíte: bônus de potência (fração de POT) nas magias do elemento do conduíte.")]
        public float conduitPotBonus = 0.25f;
        [Tooltip("Potência mágica por ponto de INT. Dano/cura = base × spellBaseMult + INT × spellPotPerINT × fator da magia. 3 deixa a Brasa (INT 6) perto de uma espada (STR 6).")]
        public float spellPotPerINT = 3f;
        [Tooltip("Multiplicador do valor base das magias (dano, cura, HP da Muralha).")]
        public float spellBaseMult = 2f;
        [Tooltip("Multiplicador do dano por tick (Queimando, Magma).")]
        public float spellTickDamageMult = 2f;
        [Tooltip("Alcance em células = alcance da magia × este valor + INT × spellRangePerINT. Uma unidade ocupa 3 células: sem isto o alcance 3 do Magma era corpo a corpo.")]
        public float spellRangeMult = 2f;
        [Tooltip("Células de alcance ganhas por ponto de INT.")]
        public float spellRangePerINT = 0.5f;
        [Tooltip("Raio da área em células = área da magia × este valor. 2 = uma Explosão pega o alvo e quem está colado nele.")]
        public float spellAreaMult = 2f;
        [Tooltip("Pausa (s) depois que as magias de um tick resolvem, para o projétil e o impacto serem vistos.")]
        public float spellFxPause = 0.45f;
        [Tooltip("Ticks base: andar (1 waypoint).")]
        public int ticksMove = 1;
        [Tooltip("Ticks base do ataque desarmado e das feras (armas usam WeaponDef.attackTicks). 2 = igual a uma arma média.")]
        public int ticksUnarmed = 2;
        [Tooltip("Ticks somados à base da arma num golpe poderoso.")]
        public int ticksPowerStrikeExtra = 1;
        [Tooltip("Ticks base: concentrar.")]
        public int ticksConcentrate = 2;
        [Tooltip("Ticks de Esperar (timeout sem ação). Não escala com AGI.")]
        public int ticksWait = 1;
        [Tooltip("k da fórmula de rapidez: ticks = base × k / (k + DEX). Maior = DEX pesa menos.")]
        public float tickDexFactor = 10f;
        [Tooltip("A cada N ticks globais: status, efeitos de tile e reações renovam (equivale ao antigo round).")]
        public int ticksPerRound = 4;
        [Tooltip("Ticks adicionados à fila de quem reage.")]
        public int reactionTickCost = 1;
        [Tooltip("Segundos de resolução visual por tick (orçamento de animação).")]
        public float tickResolveTime = 0.4f;
        [Tooltip("Segundos extras de tolerância quando o tempo acaba com PA sobrando. 0 = corte seco no fim do timer.")]
        public float planningGraceSeconds = 2f;
        [Tooltip("Segundos do prompt de confirmação de ação bônus. Menor = decisão mais apressada.")]
        public float bonusConfirmSeconds = 3f;
        [Tooltip("Segundos para escolher o destino do passo rápido (ação bônus). Menor = escolha mais apressada.")]
        public float bonusStepSeconds = 5f;
        [Tooltip("Piso de Pontos de Ação por round — toda unidade age pelo menos isso, mesmo com stats baixos.")]
        public int minActionPoints = 1;
        [Tooltip("Dano mínimo após mitigação por defesa. 1 = todo golpe acertado tira pelo menos 1 de HP.")]
        public int minDamageFloor = 1;
        [Tooltip("Dano de ataque sem arma equipada.")]
        public int unarmedDamage = 1;
        [Tooltip("Alcance (tiles) do ataque sem arma equipada. 0 = só alcança com a base.")]
        public int unarmedRange = 0;
        [Tooltip("Alcance natural da unidade além do footprint, em tiles (gap). Efetivo = base + arma.")]
        public int baseAttackRange = 1;
        [Tooltip("Armas com range ACIMA disso não escalam dano com STR (usam Mirar/DEX).")]
        public int strDamageMaxRange = 4;
        [Tooltip("Mirar: dano extra no próximo ataque = DEX * este fator.")]
        public float aimDexMultiplier = 1f;

        [Header("── Fórmulas de stats ──")]
        [Tooltip("Fórmulas derivadas (HP, mana, dano, movimento...) a partir dos atributos. Editar com cuidado — afeta todo o balanceamento.")]
        public StatFormulas statFormulas = new StatFormulas();

        // ═════════════════════════ PERSONAGENS PADRÃO & ARMAS ═════════════════════════


        [Tooltip("Catálogo de armas: dano e alcance de cada uma. Adicionar aqui novas armas (o id precisa bater com o spritesheet).")]
        // Tabela de armas (rebalanceada em 2026-11-12). Princípio: o atributo conta × (ticks ÷ 2),
        // então todas ficam perto do mesmo dano por tick e o que muda é o estilo: rápida e curta
        // (Machado), alcance (Espada de Ferro), golpe pesado (Machado de Ferro), à distância por
        // DEX (arcos), conduítes que reforçam magia (cajados). Antes o Machado batia 10 em 1 tick.
        public WeaponDef[] weapons = {
            new WeaponDef{ id="Hatchet", displayName="Machado", damage=3, range=1, attackTicks=1, blockHardness=2, spellPowerMult=1.0f, spellRangeBonus=0 },
            new WeaponDef{ id="IronAxe", displayName="Machado de Ferro", damage=12, range=2, attackTicks=3, blockHardness=2, spellPowerMult=1.0f, spellRangeBonus=0 },
            new WeaponDef{ id="WoodenSword", displayName="Espada de Madeira", damage=4, range=3, attackTicks=2, blockHardness=3, spellPowerMult=1.0f, spellRangeBonus=0 },
            new WeaponDef{ id="IronSword", displayName="Espada de Ferro", damage=6, range=4, attackTicks=2, blockHardness=3, spellPowerMult=1.0f, spellRangeBonus=0 },
            new WeaponDef{ id="WoodenStaff", displayName="Cajado", damage=3, range=7, attackTicks=2, blockHardness=2, spellPowerMult=1.1f, spellRangeBonus=0 },
            new WeaponDef{ id="Scepter", displayName="Cetro", damage=3, range=6, attackTicks=2, blockHardness=2, spellPowerMult=1.15f, spellRangeBonus=1 },
            new WeaponDef{ id="ShortBow", displayName="Arco Curto", damage=2, range=8, attackTicks=1, blockHardness=0, spellPowerMult=1.0f, spellRangeBonus=0 },
            new WeaponDef{ id="LongBow", displayName="Arco Longo", damage=5, range=10, attackTicks=3, blockHardness=0, spellPowerMult=1.0f, spellRangeBonus=0 },
            new WeaponDef{ id="ApprenticeWand", displayName="Varinha", damage=2, range=5, attackTicks=1, blockHardness=1, spellPowerMult=1.0f, spellRangeBonus=0 },
            new WeaponDef{ id="ArcaneStaff", displayName="Cajado Arcano", damage=3, range=7, attackTicks=2, blockHardness=2, spellPowerMult=1.3f, spellRangeBonus=2 },
        };

        [Tooltip("Valor mínimo por atributo no editor de personagem do menu.")]
        public int attributeMin = 1;
        [Tooltip("Valor máximo por atributo no editor de personagem do menu.")]
        public int attributeMax = 20;
        [Tooltip("Teto da AGI na criação. A andada tem teto próprio (maxTilesPerMove); acima dele a AGI vira esquiva e fôlego.")]
        public int agiMax = 20;


        // ═════════════════════════ UNIDADES ═════════════════════════

        [Header("═══ UNIDADES: VISUAL ═══")]
        [Tooltip("Escala do sprite relativa ao footprint. Maior = personagens maiores sobre os mesmos tiles.")]
        public float spriteScalePerFootprint = 1.12f;
        [Tooltip("Escala do losango de footprint relativa ao footprint no grid. 1 = cobre a célula exata.")]
        public float footprintScalePerFootprint = 0.95f;
        [Tooltip("Offset vertical do sprite (fração da escala) para assentar os pés no tile. Mais negativo = personagem desce.")]
        public float spriteFootOffsetRatio = -0.2017f;
        [Tooltip("Fração da altura do sprite onde nascem labels de dano (posição da 'cabeça'). Maior = labels mais altos.")]
        public float headHeightRatio = 0.9f;
        [Range(0f, 1f)] public float footprintAlpha = 0.50f;
        [Tooltip("Cor do footprint das unidades do jogador.")]
        public Color playerFootprintColor = new Color(0.30f, 0.60f, 1.00f, 0.80f);
        [Tooltip("Cor do footprint das unidades inimigas (jogadores adversários no MP e inimigos no SP).")]
        public Color enemyFootprintColor = new Color(1.00f, 0.38f, 0.20f, 0.80f);
        [Tooltip("Tint escurecido dos sprites inimigos (diferencia do jogador quando a classe é a mesma). Mais escuro = inimigos mais sombrios.")]
        public Color enemyTint = new Color(0.55f, 0.55f, 0.65f);
        [Tooltip("Cor do highlight de mira/hover sobre o alvo de ataque.")]
        public Color targetHoverColor = new Color(1f, 0.92f, 0.25f, 0.95f);
        [Tooltip("Cor persistente marcando 'esta unidade será atacada'.")]
        public Color attackMarkColor = new Color(0.20f, 0.03f, 0.03f, 0.95f);
        [Tooltip("Cor misturada ao selecionar uma unidade.")]
        public Color selectedTintColor = new Color(1f, 1f, 0.6f);
        [Range(0f, 1f)] public float selectedTintStrength = 0.5f;
        [Tooltip("Cor do flash no sprite ao tomar dano.")]
        public Color hitFlashColor = new Color(1f, 0.35f, 0.35f);
        [Tooltip("Área clicável do corpo (largura x altura, em unidades locais do sprite). Maior = mais fácil de clicar.")]
        public Vector2 bodyColliderSize = new Vector2(0.8f, 0.9f);
        [Tooltip("Deslocamento da área clicável do corpo (ajuste para o torso).")]
        public Vector2 bodyColliderOffset = new Vector2(0f, 0.2f);
        [Tooltip("Área clicável achatada na base (losango do footprint).")]
        public Vector2 footprintColliderSize = new Vector2(1.2f, 0.6f);
        [Tooltip("Deslocamento vertical da área clicável da base.")]
        public Vector2 footprintColliderOffset = new Vector2(0f, -0.1f);

        [Header("═══ UNIDADES: ANIMAÇÃO & MOVIMENTO ═══")]
        [Tooltip("Duração do golpe (charging → attack x4 → release), em segundos. Menor = ataque mais seco; maior = mais legível.")]
        public float attackAnimDuration = 0.4f;
        [Tooltip("Frames por segundo da caminhada durante o movimento. Maior = pernas mais rápidas (não muda a velocidade real).")]
        public float walkAnimFps = 8f;
        [Tooltip("Segundos entre os frames do balanço parado (alterna 2 poses eretas). Maior = idle mais calmo.")]
        public float idleFrameInterval = 0.55f;
        [Tooltip("Base da fórmula de velocidade: duração por tile = max(mín, base / AGI). Maior = todo mundo anda mais devagar.")]
        public float moveDurationBase = 0.9f;
        [Tooltip("Duração mínima por tile (s) — trava de velocidade para AGI altíssima.")]
        public float moveDurationMin = 0.18f;
        [Tooltip("Duração (s) do deslocamento do passo rápido (ação bônus).")]
        public float bonusMoveDuration = 0.3f;
        [Tooltip("Duração (s) do flash vermelho ao tomar dano.")]
        public float hitFlashDuration = 0.16f;
        [Tooltip("Duração (s) do fade-out ao morrer. Maior = morte mais dramática.")]
        public float deathFadeDuration = 0.6f;

        // ═════════════════════════ ARMAS (OVERLAY) ═════════════════════════

        [Header("═══ ARMAS (OVERLAY) ═══")]
        [Tooltip("Liga/desliga o WeaponOverlay (arma desenhada por cima do personagem). Os sprites TinyTactics NAO trazem a arma na mao (maos vazias), entao o overlay E a forma de mostrar a arma. Deixe LIGADO (true). O overlay nasce com a arma desalinhada (offset padrao) — alinhe na janela Pangea → Weapon Anim Editor (frame a frame) para cada arma/classe/direcao. Desligar (false) deixa o personagem DESARMADO (sem arma nenhuma).")]
        public bool weaponOverlayEnabled = true;
        [Tooltip("Offset base da arma na pose de ataque. (0, 0.25) = alinhamento puro de canvas 32x48 sobre 32x32 — o arco do golpe já é arte. Ajustes por frame: janela Pangea → Weapon Anim Editor.")]
        public Vector2 weaponAttackOffset = new Vector2(0f, 0.25f);
        [Tooltip("Offset base da arma na pose de descanso (idle/andar), quando visível.")]
        public Vector2 weaponCarryOffset = new Vector2(0f, 0.25f);
        [Tooltip("Ligado = arma visível parada em idle/andar; desligado = arma só aparece durante o golpe (padrão do kit).")]
        public bool weaponShowWhileIdle = false;

        // ═════════════════════════ CÂMERA ═════════════════════════

        [Header("═══ CÂMERA ═══")]
        [Tooltip("Velocidade com que a câmera persegue o alvo. Maior = câmera mais 'dura'; menor = mais flutuante.")]
        public float followSpeed = 9f;
        [Tooltip("Zoom (ortho) inicial ao começar a batalha. Maior = vê mais mapa.")]
        public float camInitialSize = 12f;
        [Tooltip("Zoom (ortho) usado na fase de Planejamento. Menor = mais perto da ação.")]
        public float zoomSize = 5f;
        [Tooltip("Velocidade do zoom por scroll.")]
        public float zoomSpeed = 2f;
        [Tooltip("Fator do scroll no zoom (multiplica zoomSpeed). Maior = cada 'dente' do scroll muda mais.")]
        public float zoomScrollFactor = 0.1f;
        [Tooltip("Duração do efeito de giro de vista (Q/E), em segundos. Objetos esparsos deslizam em arco por esse tempo. 0 = sem efeito (troca seca).")]
        public float viewRotateFxDuration = 0.32f;
        [Tooltip("Tempo mínimo entre dois giros de vista, em segundos. Impede encavalar giros (o que embaralhava as posições do efeito). Convém ser >= duração + stagger.")]
        public float viewRotateCooldown = 0.45f;
        [Tooltip("Quantos graus a câmera 'balança' ao girar a vista. Dá o tranco físico do giro. 0 = desliga.")]
        public float viewRotateRollDegrees = 5f;
        [Tooltip("Giro VISUAL do tabuleiro na transição, em graus. 90 = o mapa percorre a volta inteira até a posição nova (ilusão de rotação real). 0 = desliga. Só afeta a aparência: a posição lógica já é a final desde o primeiro frame.")]
        public float viewRotateWorldSpin = 90f;
        [Tooltip("Quanto a câmera afasta no tranco do giro, como fração do zoom (0.08 = 8% mais longe no pico).")]
        public float viewRotateZoomPunch = 0.08f;
        [Tooltip("Quanto a tela escurece no auge do giro. 0 = desliga o escurecimento.")]
        public float viewRotateSweepBrightness = 0.35f;
        [Tooltip("Que fração do escurecimento é UNIFORME na tela (atinge o centro, onde o jogador olha) em vez de concentrada nas bordas. 0 = só vinheta (quase invisível), 1 = escurece tudo por igual.")]
        [Range(0f, 1f)]
        public float viewRotateDarkenUniform = 0.55f;
        [Tooltip("Quanto os tiles incham na passagem da onda (0.06 = 6%). Só para CIMA: encolher abriria fresta entre os losangos.")]
        public float viewRotateSweepScale = 0.06f;
        [Tooltip("Atraso máximo entre o primeiro e o último objeto na onda de giro, em segundos. 0 = todos se movem juntos.")]
        public float viewRotateStagger = 0.14f;
        [Tooltip("Zoom mínimo (mais perto) permitido ao jogador.")]
        public float zoomMin = 3f;
        [Tooltip("Zoom máximo (mais longe) permitido ao jogador.")]
        public float zoomMax = 20f;
        [Tooltip("Pixels da borda da tela que ativam o edge-pan. Maior = pan dispara mais longe da borda.")]
        public float edgeMargin = 18f;
        [Tooltip("Velocidade do pan pela borda da tela.")]
        public float edgePanSpeed = 12f;
        [Tooltip("Zoom de referência para normalizar o edge-pan (na prática: em zooms menores o pan fica proporcionalmente mais lento).")]
        public float edgePanReferenceZoom = 13f;
        [Tooltip("Velocidade do arrasto com botão direito.")]
        public float dragSpeed = 1f;
        [Tooltip("Segundos que o modo Manual dura após o jogador arrastar/zoom, antes de voltar ao Auto.")]
        public float camManualTimeout = 2f;
        [Tooltip("Distância mínima ao destino para considerar a câmera 'assentada'. Maior = corta transições mais cedo.")]
        public float camSettleThreshold = 0.05f;
        [Tooltip("Timeout (s) esperando a câmera assentar antes de prosseguir a ação mesmo assim.")]
        public float camSettleTimeout = 1.2f;
        [Tooltip("Espera mínima (s) pela câmera no foco automático de ações.")]
        public float camSettleMinDuration = 0.2f;
        [Tooltip("Margem extra (s) de espera após a câmera assentar no foco automático.")]
        public float camSettleExtra = 0.6f;
        [Tooltip("Margem extra (s) ao focar a área de um ataque.")]
        public float attackFocusSettleExtra = 0.4f;
        [Tooltip("Folga (unidades) ao enquadrar várias posições no zoom automático. Maior = enquadramento mais aberto.")]
        public float autoFramePadding = 1.5f;
        [Tooltip("Zoom mínimo do enquadramento automático (não aproxima mais que isso).")]
        public float autoFrameZoomMin = 3f;
        [Tooltip("Zoom máximo do enquadramento automático (não afasta mais que isso).")]
        public float autoFrameZoomMax = 20f;
        [Tooltip("Raio mínimo (unidades) do enquadramento da disputa de iniciativa.")]
        public float initiativeContestMinRadius = 3f;
        [Tooltip("Folga somada ao raio da disputa de iniciativa para definir o zoom.")]
        public float initiativeContestZoomPadding = 3.5f;

        // ═════════════════════════ RITMO DO ROUND ═════════════════════════

        [Header("═══ RITMO DO ROUND ═══")]
        [Tooltip("Duração (s) do movimento da câmera entre focos de ação.")]
        public float camMoveDuration = 0.45f;
        [Tooltip("Pausa (s) antes de cada ação executar. Maior = ritmo mais teatral.")]
        public float preActionPause = 0.35f;
        [Tooltip("Pausa (s) depois de cada ação. Maior = mais tempo para ler o resultado.")]
        public float postActionPause = 0.55f;
        [Tooltip("Segundos segurando a exibição da iniciativa no início da fase de ação.")]
        public float initiativeHold = 1.1f;
        [Tooltip("Pausa (s) entre slots de ação consecutivos.")]
        public float slotPause = 0.3f;
        [Tooltip("Pausa (s) após o banner 'Round N' antes do planejamento abrir.")]
        public float roundBannerHold = 0.4f;
        [Tooltip("Pausa (s) entre o fim da fase de ação e o próximo round.")]
        public float roundEndPause = 0.5f;
        [Tooltip("Pausa extra (s) após recolher as tags de iniciativa.")]
        public float initiativeCollapseExtraPause = 0.2f;
        [Tooltip("Pausa (s) entre o label de ataque aparecer e a câmera/animação seguirem.")]
        public float attackLabelLeadPause = 0.15f;
        [Tooltip("Duração (s) da transição de câmera atacante → alvo durante o golpe.")]
        public float attackAreaFocusDuration = 0.3f;

        // ═════════════════════════ EFEITOS: SHAKE & FLASH ═════════════════════════

        [Header("═══ EFEITOS: SHAKE & FLASH ═══")]
        [Tooltip("Duração (s) do tremor de tela em acerto CRÍTICO.")]
        public float shakeDurationCrit = 0.25f;
        [Tooltip("Amplitude do tremor em crítico. Maior = impacto mais violento.")]
        public float shakeMagnitudeCrit = 0.2f;
        [Tooltip("Duração (s) do tremor em acerto normal.")]
        public float shakeDurationNormal = 0.15f;
        [Tooltip("Amplitude do tremor em acerto normal.")]
        public float shakeMagnitudeNormal = 0.1f;
        [Tooltip("Duração default do tremor quando o chamador não especifica.")]
        public float shakeDurationDefault = 0.2f;
        [Tooltip("Amplitude default do tremor quando o chamador não especifica.")]
        public float shakeMagnitudeDefault = 0.15f;
        [Tooltip("Duração (s) do flash de tela em crítico.")]
        public float flashDurationCrit = 0.25f;
        [Tooltip("Opacidade máxima do flash de crítico. Maior = clarão mais forte.")]
        public float flashIntensityCrit = 0.3f;
        [Tooltip("Duração (s) do flash vermelho quando o JOGADOR toma dano crítico.")]
        public float flashDurationRed = 0.2f;
        [Tooltip("Opacidade máxima do flash vermelho.")]
        public float flashIntensityRed = 0.35f;
        [Tooltip("Cor do flash vermelho de dano.")]
        public Color flashRedColor = new Color(1f, 0.1f, 0.05f);
        [Tooltip("Duração (s) do flash branco genérico.")]
        public float flashDurationWhite = 0.15f;
        [Tooltip("Opacidade máxima do flash branco.")]
        public float flashIntensityWhite = 0.5f;
        [Range(0f, 1f)] public float flashHoldRatio = 0.5f;

        // ═════════════════════════ EFEITOS: LABELS ═════════════════════════

        [Header("═══ LABELS: COMPORTAMENTO ═══")]
        [Tooltip("Segundos que o label de anúncio de ataque fica visível.")]
        public float attackLabelDuration = 0.8f;
        [Tooltip("Duração (s) da subida do número de dano.")]
        public float damageRiseDuration = 0.35f;
        [Tooltip("Duração (s) da subida do dano crítico.")]
        public float critRiseDuration = 0.5f;
        [Tooltip("Duração (s) da subida do MISS.")]
        public float missRiseDuration = 0.3f;
        [Tooltip("Espaço vertical entre labels de dano empilhados no mesmo lugar.")]
        public float battleLabelStackSpacing = 0.9f;
        [Tooltip("Raio (unidades) para considerar labels 'no mesmo lugar' e empilhar.")]
        public float battleLabelStackRadius = 2.0f;
        [Tooltip("Espaço vertical entre ghosts numerados da sequência de ações.")]
        public float seqStackSpacing = 1.2f;

        [Header("═══ LABELS: ESTILO ═══")]
        [Tooltip("Largura reservada por caractere no fundo do label. Maior = fundo mais largo por letra.")]
        public float battleLabelCharWidth = 0.45f;
        [Tooltip("Padding horizontal do fundo do label.")]
        public float battleLabelBgPadding = 0.80f;
        [Tooltip("Altura do fundo do label.")]
        public float battleLabelBgHeight = 1.40f;
        [Tooltip("Tamanho da fonte dos labels (nitidez; combine com characterSize).")]
        public int battleLabelFontSize = 100;
        [Tooltip("Tamanho físico do texto no mundo. Maior = letras maiores na tela.")]
        public float battleLabelCharacterSize = 0.06f;
        [Tooltip("Altura extra do label acima da cabeça da unidade.")]
        public float battleLabelHeightOffset = 0.3f;
        [Tooltip("Máximo de caracteres do nome no label de ataque antes de truncar.")]
        public int attackLabelMaxNameChars = 12;
        [Tooltip("Cor do texto do anúncio de ataque.")]
        public Color attackLabelTextColor = new Color(1f, 0.75f, 0.25f);
        [Range(0f, 1f)] public float attackLabelFadeRatio = 0.5f;
        [Tooltip("Escala do label de dano crítico relativa ao normal. Maior = crítico mais gritante.")]
        public float critLabelScale = 1.35f;
        [Tooltip("Cor do número de dano crítico.")]
        public Color critLabelTextColor = new Color(1f, 0.9f, 0.2f);
        [Tooltip("Cor do número de dano normal.")]
        public Color damageLabelTextColor = new Color(1f, 0.28f, 0.28f);
        [Tooltip("Altura extra do label MISS acima da unidade.")]
        public float missLabelHeightOffset = 0.4f;
        [Tooltip("Cor do texto MISS.")]
        public Color missLabelTextColor = new Color(0.6f, 0.6f, 0.6f);
        [Tooltip("Escala dos rótulos numerados de sequência relativa ao label padrão.")]
        public float seqLabelScale = 0.70f;
        [Tooltip("Tamanho da fonte do label de rolagem de iniciativa.")]
        public int floatingLabelFontSize = 80;
        [Tooltip("Tamanho físico do texto da rolagem de iniciativa no mundo.")]
        public float floatingLabelCharacterSize = 0.06f;

        // ═════════════════════════ PLANEJAMENTO: CORES & GHOSTS ═════════════════════════

        [Header("═══ PLANEJAMENTO: CORES & GHOSTS ═══")]
        [Tooltip("Cor/alpha do ghost branco em cada waypoint de movimento confirmado.")]
        public Color moveGhostColor = new Color(1f, 1f, 1f, 0.30f);
        [Tooltip("Cor do destaque vermelho na unidade alvo durante a mira.")]
        public Color unitTargetColor = new Color(0.80f, 0.10f, 0.10f);
        [Tooltip("Cor dos tiles atacáveis no modo Atacar Tile.")]
        public Color tileTargetColor = new Color(0.35f, 0.85f, 0.95f);
        [Tooltip("Cor/alpha do ghost marcando tile de ataque confirmado.")]
        public Color tileTargetGhostColor = new Color(0.35f, 0.85f, 0.95f, 0.45f);
        [Tooltip("Cor/alpha do cursor-ghost que segue o mouse ao escolher destino.")]
        public Color cursorGhostColor = new Color(1f, 1f, 0.6f, 0.45f);
        [Tooltip("Fundo dos rótulos numerados de MOVIMENTO na sequência planejada.")]
        public Color seqLabelMoveColor = new Color(0.10f, 0.23f, 0.36f);
        [Tooltip("Fundo dos rótulos numerados de ATAQUE na sequência planejada.")]
        public Color seqLabelAttackColor = new Color(0.36f, 0.10f, 0.10f);
        [Tooltip("Altura base (unidades) do rótulo de sequência acima do ghost.")]
        public float seqLabelBaseHeight = 0.7f;
        [Tooltip("Cor/alpha do losango do cursor de passo rápido.")]
        public Color stepGhostColor = new Color(1f, 0.90f, 0.35f, 0.55f);
        [Tooltip("Escala do ghost do passo rápido relativa ao footprint.")]
        public float stepGhostScale = 0.95f;

        // ═════════════════════════ GRID & CENÁRIO ═════════════════════════

        [Header("═══ GRID & CENÁRIO ═══")]
        [Tooltip("Cor dos tiles alcançáveis no movimento (verde).")]
        public Color reachHighlightColor = new Color(0.35f, 0.85f, 0.45f);
        [Tooltip("Cor dos tiles alcançáveis pela ação bônus (dourado).")]
        public Color bonusHighlightColor = new Color(0.90f, 0.75f, 0.30f);
        [Tooltip("Cor/alpha das linhas de contorno do grid sobre os tiles. Alpha 0 = sem linhas. Azul claro combina com a borda dos menus (pg-panel-border).")]
        public Color gridLineColor = new Color(0.40f, 0.66f, 1.0f, 0.25f);
        [Tooltip("Cor da face lateral ESQUERDA dos tiles elevados (terra escura, lado na sombra).")]
        public Color sideFaceLeftColor = new Color(0.38f, 0.25f, 0.12f);
        [Tooltip("Cor da face lateral DIREITA dos tiles elevados (terra média, lado na luz).")]
        public Color sideFaceRightColor = new Color(0.50f, 0.34f, 0.20f);
        [Tooltip("Cor de time aplicada às unidades do JOGADOR vindas de mapas customizados.")]
        public Color playerTeamColor = new Color(0.30f, 0.50f, 0.92f);
        [Tooltip("Cor de time aplicada às unidades INIMIGAS vindas de mapas customizados.")]
        public Color enemyTeamColor = new Color(0.80f, 0.30f, 0.25f);
        [Tooltip("Cor de fundo da câmera na batalha (o 'vazio' fora do mapa).")]
        public Color battleBackgroundColor = new Color(0.08f, 0.09f, 0.11f);

        // ═════════════════════════ TAG DE INICIATIVA ═════════════════════════

        [Header("═══ TAG DE INICIATIVA ═══")]
        [Tooltip("Altura da tag acima da cabeça da unidade.")]
        public float initiativeTagHeightOffset = 0.5f;
        [Tooltip("Escala (largura x altura) do fundo da tag.")]
        public Vector2 initiativeTagBgScale = new Vector2(2.4f, 0.8f);
        [Tooltip("Tamanho da fonte da tag na fase de rolagem.")]
        public int initiativeTagFontSize = 80;
        [Tooltip("Tamanho da fonte na fase 2 (resultado final, maior).")]
        public int initiativePhase2FontSize = 130;
        [Tooltip("Cor/alpha do fundo da tag.")]
        public Color initiativeTagBgColor = new Color(0.08f, 0.09f, 0.14f, 0.88f);
        [Tooltip("Cor da borda da tag.")]
        public Color initiativeTagBorderColor = new Color(0.50f, 0.65f, 0.92f);
        [Tooltip("Cor do componente d20 na rolagem.")]
        public Color initiativeD20Color = new Color(1f, 0.867f, 0.2f);
        [Tooltip("Cor do componente AGI na rolagem.")]
        public Color initiativeAgiColor = new Color(0.4f, 1f, 0.4f);
        [Tooltip("Cor do componente DEX na rolagem.")]
        public Color initiativeDexColor = new Color(0.4f, 0.667f, 1f);
        [Tooltip("Cor do vencedor da disputa de iniciativa.")]
        public Color initiativeWinnerColor = new Color(0.4f, 1f, 0.439f);
        [Tooltip("Cor dos perdedores da disputa.")]
        public Color initiativeLoserColor = new Color(0.533f, 0.533f, 0.533f);

        // ═════════════════════════ HUD: TEMA ═════════════════════════

        [Header("═══ HUD: TEMA ═══")]
        [Tooltip("Cor dos botões de comando em estado normal.")]
        public Color hudButtonNormalColor = new Color(0.16f, 0.26f, 0.48f);
        [Tooltip("Cor do botão ativo (escolhendo alvo/destino).")]
        public Color hudButtonActiveColor = new Color(0.30f, 0.50f, 0.85f);
        [Tooltip("Cor do botão com ação já confirmada.")]
        public Color hudButtonConfirmedColor = new Color(0.18f, 0.45f, 0.28f);
        [Tooltip("Cor do botão desabilitado.")]
        public Color hudButtonDisabledColor = new Color(0.18f, 0.20f, 0.26f);
        [Tooltip("Cor do texto de log das ações do JOGADOR.")]
        public Color logPlayerColor = new Color(0.55f, 0.80f, 1.00f);
        [Tooltip("Cor do texto de log das ações INIMIGAS.")]
        public Color logEnemyColor = new Color(1.00f, 0.52f, 0.52f);
        [Tooltip("Cor do texto de log de mensagens de sistema.")]
        public Color logSystemColor = new Color(0.82f, 0.85f, 0.92f);
        [Tooltip("Cor/alpha do highlight ao passar o mouse nas linhas do log.")]
        public Color logHoverColor = new Color(0.25f, 0.38f, 0.65f, 0.35f);
        [Tooltip("Cor do chip 'Mover' na fila de ações.")]
        public Color seqChipMoveColor = new Color(0.18f, 0.36f, 0.66f);
        [Tooltip("Cor do chip 'Atacar' na fila de ações.")]
        public Color seqChipAttackColor = new Color(0.66f, 0.32f, 0.12f);
        [Tooltip("Cor do chip 'Mover' bônus (BAP).")]
        public Color seqChipMoveBonusColor = new Color(0.60f, 0.20f, 0.92f);
        [Tooltip("Cor do chip 'Atacar' bônus (BAP).")]
        public Color seqChipAttackBonusColor = new Color(0.95f, 0.50f, 0.08f);
        [Range(0f, 1f)] public float seqChipSelectedLighten = 0.4f;
        [Tooltip("Cor normal do timer de fase.")]
        public Color timerNormalColor = new Color(1f, 0.9f, 0.55f);
        [Tooltip("Cor do timer quando o tempo está acabando.")]
        public Color timerWarningColor = new Color(1f, 0.35f, 0.35f);
        [Tooltip("Cor do indicador de câmera em modo Auto.")]
        public Color cameraAutoColor = new Color(0.55f, 0.95f, 0.55f);
        [Tooltip("Cor do indicador de câmera em modo Manual.")]
        public Color cameraManualColor = new Color(1f, 0.75f, 0.35f);
        [Range(0f, 1f)] public float cameraIndicatorBgAlpha = 0.85f;
        [Range(0f, 1f)] public float hpBarYellowThreshold = 0.50f;
        [Range(0f, 1f)] public float hpBarRedThreshold = 0.25f;        [Tooltip("Cor da barra de HP cheia.")]
        public Color hpBarHighColor = new Color(0.20f, 0.78f, 0.28f);
        [Tooltip("Cor da barra de HP média.")]
        public Color hpBarMidColor = new Color(0.85f, 0.72f, 0.10f);
        [Tooltip("Cor da barra de HP crítica.")]
        public Color hpBarLowColor = new Color(0.82f, 0.20f, 0.20f);
        [Range(0f, 1f)] public float endScreenDimAlpha = 0.78f;

        // ═════════════════════════ MENU & SANDBOX ═════════════════════════

        [Header("═══ MENU & SANDBOX ═══")]
        [Tooltip("Cor de fundo do menu principal.")]
        public Color menuBackgroundColor = new Color(0.06f, 0.07f, 0.09f);
        [Tooltip("Cor de botão de lista em estado normal (menu e sandbox).")]
        public Color uiButtonNormalColor = new Color(0.25f, 0.25f, 0.30f);
        [Tooltip("Cor do item selecionado nas listas (presets, pincéis, mapas).")]
        public Color uiButtonActiveColor = new Color(0.28f, 0.50f, 0.82f);
        [Tooltip("Cor dourada dos títulos e labels de destaque.")]
        public Color uiTitleColor = new Color(0.92f, 0.80f, 0.35f);
        [Tooltip("Zoom (ortho) inicial da câmera no Sandbox.")]
        public float sandboxCameraSize = 12f;
        [Tooltip("Cor de fundo da câmera no Sandbox.")]
        public Color sandboxBackgroundColor = new Color(0.08f, 0.09f, 0.11f);
        [Tooltip("Lado (tiles) do mapa novo criado no Sandbox.")]
        public int sandboxDefaultMapSize = 20;
        [Tooltip("Cor/alpha do preview verde da pintura de tiles.")]
        public Color paintPreviewColor = new Color(0.3f, 1f, 0.3f, 0.5f);
        [Tooltip("Cor/alpha do losango de hover no grid do Sandbox.")]
        public Color hoverCursorColor = new Color(1f, 0.95f, 0.4f, 0.40f);
        [Tooltip("Altura máxima ao empilhar tiles no Sandbox (empilhar 4 full tiles = 8 de altura).")]
        public int maxTileHeight = 8;
        [Tooltip("Diferença máxima de altura que uma unidade pode subir/descer sem rampa (parede de altura, Regra b).")]
        public int maxStepClimb = 1;
        [Tooltip("Tamanho máximo do mapa (lado maior) permitido no Sandbox.")]
        public int maxMapSize = 50;

        // ═════════════════════════ ÁUDIO ═════════════════════════

        [Header("═══ ÁUDIO ═══")]
        [Range(0f, 1f)] public float sfxVolume = 1f;
        [Range(0f, 1f)] public float musicVolume = 1f;

        // ═════════════════════════ ELEMENTOS & TILES ═════════════════════════
        // Sistema de conjuração removido (refazer a partir do GDD-013). Mantidos: efeitos de tile e cores.

        [Header("═══ EFEITOS DE TILE ═══")]
        [Tooltip("Fator de dano de fogo em tile: potência do fogo × fator = dano por tique. 1.0 = dano igual à magia direta (Decisão Marcus 2026-07-11, após potência base pela metade).")]
        public float fireTileDamageFactor = 1.0f;
        [Tooltip("Duração (rounds) do fogo em tile antes de apagar.")]
        public int fireTileDurationRounds = 3;
        [Tooltip("Duração (rounds) do vento em tile.")]
        public int windTileDurationRounds = 3;
        [Tooltip("Nº de tiles que o vento empurra uma unidade ao pisar.")]
        public int windPushTiles = 1;
        [Tooltip("Nome do sprite de Água no atlas (TilePalette).")]
        public string waterTileName = "water_full";
        [Tooltip("Nome do sprite de Pedra no atlas (elevável por Terra).")]
        public string earthStoneTileName = "stone_2";
        [Tooltip("Quantidade de altura adicionada ao tile de Pedra por Terra-Tile.")]
        public int earthRaiseAmount = 1;

        [Range(0f, 1f)] public float tileEffectOverlayAlpha = 0.65f;

        [Header("═══ CORES (ELEMENTOS & GRID) ═══")]
        [Tooltip("Cor do elemento Físico.")]
        public Color elemPhysicalColor = new Color(0.80f, 0.60f, 0.30f);
        [Tooltip("Cor do elemento Mágico.")]
        public Color elemMagicColor = new Color(0.50f, 0.20f, 1.00f);
        [Tooltip("Cor do elemento Fogo.")]
        public Color elemFireColor = new Color(1.00f, 0.30f, 0.00f);
        [Tooltip("Cor do elemento Água.")]
        public Color elemWaterColor = new Color(0.10f, 0.40f, 1.00f);
        [Tooltip("Cor do elemento Ar.")]
        public Color elemAirColor = new Color(0.70f, 0.90f, 1.00f);
        [Tooltip("Cor do elemento Terra.")]
        public Color elemEarthColor = new Color(0.50f, 0.35f, 0.15f);
        [Tooltip("Cor do range de ataque destacado ao selecionar a unidade e ao mirar unidade.")]
        public Color attackRangeColor = new Color(1.00f, 0.82f, 0.30f, 0.35f);
        [Tooltip("Cor do range do inimigo ao passar o mouse sobre ele.")]
        public Color enemyRangeColor = new Color(1.00f, 0.28f, 0.22f, 0.45f);
        [Tooltip("Cor do chip de Concentração na barra de sequência de ações.")]
        public Color seqChipConcentrateColor = new Color(0.15f, 0.55f, 0.55f);
        [Tooltip("Cor do texto de mana na HUD.")]
        public Color manaTextColor = new Color(0.40f, 0.60f, 1.00f);

        [Header("═══ BARRAS NO MUNDO (HP/MP) ═══")]
        [Tooltip("Mostrar barras de HP/MP flutuando sobre as unidades no campo.")]
        public bool worldBarsEnabled = true;
        [Tooltip("Largura e altura (unidades de mundo) das barras sobre as unidades.")]
        public Vector2 worldBarSize = new Vector2(1.4f, 0.14f);
        [Tooltip("Espaço vertical entre a barra de HP e a de MP.")]
        public float worldBarGap = 0.05f;
        [Tooltip("Deslocamento vertical das barras acima da cabeça da unidade.")]
        public float worldBarYOffset = 0.25f;
        [Tooltip("Cor de fundo das barras (atrás do preenchimento).")]
        public Color worldBarBgColor = new Color(0.05f, 0.05f, 0.07f, 0.85f);
        [Tooltip("Cor da barra de HP cheia.")]
        public Color worldHpHighColor = new Color(0.30f, 0.85f, 0.30f);
        [Tooltip("Cor da barra de HP quase vazia (interpola com a cheia pelo %HP).")]
        public Color worldHpLowColor = new Color(0.90f, 0.25f, 0.20f);
        [Tooltip("Cor da barra de MP.")]
        public Color worldMpColor = new Color(0.30f, 0.55f, 1.00f);
        [Tooltip("Sorting order base das barras (acima do sprite da unidade).")]
        public int worldBarSortingBase = 15000;

        [Header("═══ SKIN DE UI (BDragon1727) ═══")]
        [Tooltip("Liga a skin de UI com sprites do kit BDragon1727 (fatiados em runtime). Desligado = fallback gerado.")]
        public bool uiSkinEnabled = true;
        [Tooltip("Mostrar indicadores no mundo (marcador de unidade ativa e de alvo de ataque).")]
        public bool worldIndicatorsEnabled = true;
        [Tooltip("Path (Resources) do sheet de UI do kit (montagem single-sprite fatiada em runtime).")]
        public string uiSheetPath = "Sprites/BDragon1727/UI/00";
        [Tooltip("Rect (x,y,w,h em coords TOP-LEFT) da seta usada como marcador no mundo.")]
        public Vector4 markerArrowRect = new Vector4(241, 50, 14, 12);
        [Tooltip("Cor do marcador da unidade ativa/selecionada.")]
        public Color activeMarkerColor = new Color(0.40f, 1.00f, 0.50f);
        [Tooltip("Cor do marcador de unidade alvo de ataque.")]
        public Color targetMarkerColor = new Color(1.00f, 0.30f, 0.25f);
        [Tooltip("Deslocamento vertical do marcador acima da cabeça da unidade.")]
        public float markerYOffset = 0.55f;
        [Tooltip("Amplitude do bob (sobe/desce) do marcador.")]
        public float markerBobAmplitude = 0.12f;
        [Tooltip("Velocidade do bob do marcador.")]
        public float markerBobSpeed = 3f;
        [Tooltip("Escala do marcador no mundo.")]
        public float markerScale = 1.3f;
        [Tooltip("Sorting order do marcador (acima das barras).")]
        public int markerSortingOrder = 15100;

        [Tooltip("Aplica a moldura do BDragon1727 nos botões e chips da UI (9-slice). Desligado = retângulo de cor sólida (visual antigo).")]
        public bool uiButtonSkinEnabled = true;
        [Tooltip("Rect (TOP-LEFT) da moldura cápsula cinza usada como fundo dos botões de menu.")]
        public Vector4 uiButtonFrameRect = new Vector4(64, 85, 48, 22);
        [Tooltip("Borda 9-slice da moldura de botão (left,bottom,right,top em px).")]
        public Vector4 uiButtonFrameBorder = new Vector4(14, 4, 14, 4);
        [Range(0f, 1f)]
        [Tooltip("Quanto clarear (lerp p/ branco) a cor do botão antes de multiplicar pelo sprite sombreado. 0 = cor original (fica escuro demais); 1 = branco puro.")]
        public float uiButtonFrameTintLerp = 0.55f;
        [Tooltip("Inverte a direção do sombreado do sprite de botão (espelha verticalmente). Desligue uiButtonSkinEnabled p/ remover o sombreado por completo (volta ao retângulo de cor sólida).")]
        public bool uiButtonFrameFlipShading = false;
        [Tooltip("Rect (TOP-LEFT) da pílula cinza usada nas barras de HP/MP no mundo.")]
        public Vector4 worldBarPillRect = new Vector4(68, 68, 38, 8);
        [Tooltip("Borda 9-slice da pílula da barra (left,bottom,right,top em px).")]
        public Vector4 worldBarPillBorder = new Vector4(8, 3, 8, 3);


        // ═════════════════════════ REAÇÕES (Ações Bônus rework) ═════════════════════════
        [Header("═══ REAÇÕES (Ações Bônus) ═══")]
        [Tooltip("Tempo (s) que o jogador tem para escolher a reação. Expira → não reage. Válido em SP e MP. Marcus 2026-07-10: 10x (50s) — era 5s e 'piscava'.")]
        public float reactionChoiceTime   = 3f;
        [Tooltip("Esquiva: +chance de dodge por ponto de AGI (fração). Ex: 0.03 → AGI 10 = +30% dodge no ataque.")]
        public float dodgeReactionPerAGI  = 0.09f;
        [Tooltip("Bloqueio: redução de dano recebido por ponto de VIT (fração). Ex: 0.03 → VIT 10 = -30% dano.")]
        public float blockReductionPerVIT = 0.04f;   // legado (percentual): o Bloquear agora é FIXO
        [Tooltip("Bloquear: redução fixa = VIT × este valor + dureza da arma (GDD-017).")]
        public float blockFlatPerVIT = 0.25f;
        [Tooltip("Teto da redução de dano do Bloqueio (fração, 0.60 = máx 60% a menos).")]
        public float maxBlockReduction    = 0.60f;
        [Tooltip("Multiplicador de dano do Contra-ataque (AoO). 1 = dano normal; 0.5 = metade.")]
        public float aooDamageMult        = 1.0f;
    }

    /// <summary>
    /// Guarda a instância de GameTuning ativa entre cenas (ex: ajustes feitos no menu
    /// principal chegam à cena de batalha). Como é static, sobrevive ao LoadScene e mantém
    /// a referência viva. O menu trabalha sobre uma CÓPIA do asset (Instantiate) para não
    /// modificar o asset original no disco.
    /// </summary>
    public static class RuntimeTuning
    {
        public static GameTuning Active;
    }

    /// <summary>
    /// Acesso central ao tuning: cópia de runtime → asset em Resources → instância default
    /// em memória (nunca retorna null; a instância default usa os valores dos inicializadores
    /// de campo, então o jogo degrada graciosamente se o asset sumir).
    /// </summary>
    public static class Tuning
    {
        private static GameTuning _fallback;

        public static GameTuning Get()
        {
            var t = RuntimeTuning.Active;
            if (t != null) return t;
            t = Resources.Load<GameTuning>("GameTuning");
            if (t != null) return t;
            if (_fallback == null) _fallback = ScriptableObject.CreateInstance<GameTuning>();
            return _fallback;
        }
    }
}
