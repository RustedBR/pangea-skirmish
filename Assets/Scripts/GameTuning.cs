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
        [Tooltip("Segundos extras de tolerância quando o tempo acaba com PA sobrando. 0 = corte seco no fim do timer.")]
        public float planningGraceSeconds = 2f;
        [Tooltip("Segundos do prompt de confirmação de ação bônus. Menor = decisão mais apressada.")]
        public float bonusConfirmSeconds = 3f;
        [Tooltip("Segundos para escolher o destino do passo rápido (ação bônus). Menor = escolha mais apressada.")]
        public float bonusStepSeconds = 5f;
        [Tooltip("Custo de movimento por tile andado. Maior = unidades andam menos com o mesmo orçamento.")]
        public float moveCostPerTile = 1f;
        [Tooltip("Custo extra ao mudar de direção no caminho. Maior = caminhos retos ficam mais vantajosos.")]
        public float turnCost = 0.5f;
        [Tooltip("Multiplicador de custo ao subir/descer elevação. Maior = terreno alto vira barreira tática.")]
        public float heightCostMultiplier = 1.5f;
        [Tooltip("Piso de Pontos de Ação por round — toda unidade age pelo menos isso, mesmo com stats baixos.")]
        public int minActionPoints = 1;
        [Tooltip("Dano mínimo após mitigação por defesa. 1 = todo golpe acertado tira pelo menos 1 de HP.")]
        public int minDamageFloor = 1;
        [Tooltip("Fração de STR somada ao dano do ataque em Tile. 1 = soma STR inteira; 0.5 = metade; 0 = sem bônus.")]
        public float tileAttackStrMultiplier = 1f;
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

        [Header("═══ PERSONAGENS PADRÃO & ARMAS ═══")]
        [Tooltip("Atributos iniciais do Guerreiro (STR/VIT/DEX/AGI/INT/WIS, footprint e alcance).")]
        public UnitStatBlock guerreiro = new UnitStatBlock { STR = 8, VIT = 10, DEX = 2, AGI = 3, INT = 1, WIS = 1, Footprint = 3 };
        [Tooltip("Atributos iniciais do Ladino.")]
        public UnitStatBlock ladino    = new UnitStatBlock { STR = 3, VIT = 5,  DEX = 8, AGI = 10, INT = 1, WIS = 1, Footprint = 3 };
        [Tooltip("Atributos iniciais do Goblin (inimigo básico).")]
        public UnitStatBlock goblin    = new UnitStatBlock { STR = 3, VIT = 3,  DEX = 3, AGI = 3,  INT = 1, WIS = 1, Footprint = 2 };
        [Tooltip("Atributos iniciais do Arqueiro.")]
        public UnitStatBlock arqueiro  = new UnitStatBlock { STR = 5, VIT = 5,  DEX = 10, AGI = 7, INT = 1, WIS = 2, Footprint = 3 };
        [Tooltip("Atributos iniciais do Mago.")]
        public UnitStatBlock mago      = new UnitStatBlock { STR = 1, VIT = 4,  DEX = 2, AGI = 4,  INT = 10, WIS = 8, Footprint = 3 };

        [Tooltip("Catálogo de armas: dano e alcance de cada uma. Adicionar aqui novas armas (o id precisa bater com o spritesheet).")]
        public WeaponDef[] weapons = {
            new WeaponDef{ id="Hatchet",     displayName="Machado",        damage=4, range=2 },
            new WeaponDef{ id="IronAxe",     displayName="Machado de Ferro",damage=7, range=2 },
            new WeaponDef{ id="WoodenSword", displayName="Espada de Madeira",damage=3, range=2 },
            new WeaponDef{ id="IronSword",   displayName="Espada de Ferro", damage=5, range=2 },
            new WeaponDef{ id="WoodenStaff", displayName="Cajado",          damage=2, range=3 },
            new WeaponDef{ id="Scepter",     displayName="Cetro",           damage=3, range=3 },
            new WeaponDef{ id="ShortBow",    displayName="Arco Curto",     damage=3, range=3 },
            new WeaponDef{ id="LongBow",     displayName="Arco Longo",     damage=5, range=5 },
            new WeaponDef{ id="ApprenticeWand", displayName="Varinha",     damage=2, range=3 },
            new WeaponDef{ id="ArcaneStaff", displayName="Cajado Arcano",  damage=6, range=5 },
        };

        [Tooltip("Valor mínimo por atributo no editor de personagem do menu.")]
        public int attributeMin = 1;
        [Tooltip("Valor máximo por atributo no editor de personagem do menu.")]
        public int attributeMax = 20;

        // ═════════════════════════ IA DO INIMIGO ═════════════════════════

        [Header("═══ IA DO INIMIGO ═══")]
        [Tooltip("0 = passiva (espera), 1 = avança sempre para cima do jogador.")]
        [Range(0f, 1f)] public float aiAggression = 0.7f;
        [Tooltip("0 = prefere reposicionar, 1 = ataca sempre que possível.")]
        [Range(0f, 1f)] public float aiAttackPreference = 0.6f;
        [Tooltip("0 = decisões aleatórias, 1 = escolhe alvos e posições ótimos.")]
        [Range(0f, 1f)] public float aiIntelligence = 0.5f;
        [Tooltip("0 = ignora o próprio HP, 1 = recua muito cedo ao se machucar.")]
        [Range(0f, 1f)] public float aiSurvivalInstinct = 0.3f;
        [Tooltip("Fração de HP abaixo da qual a IA considera modo sobrevivência (escalado pelo instinto). Maior = recua com mais vida.")]
        [Range(0f, 1f)] public float aiSurvivalHpThreshold = 0.6f;
        [Tooltip("Distância (tiles) que a IA usa para flanquear. Maior = manobras mais amplas.")]
        public int aiFlankRange = 4;
        [Tooltip("Gap máximo desejado quando agressiva — gruda a até N tiles do alvo. Menor = corpo a corpo.")]
        public int aiAggressiveMaxGap = 2;
        [Tooltip("Folga extra (tiles) além do alcance quando cautelosa. Maior = IA mais covarde.")]
        public int aiCautiousGapOffset = 1;
        [Tooltip("Bônus de pontuação para posições de flanco diagonal. Maior = IA flanqueia mais.")]
        public int aiFlankScoreBonus = 1;
        [Tooltip("Peso do bônus por alvo 'matável' neste round. Maior = IA foca quem pode morrer agora.")]
        public float aiKillabilityWeight = 0.4f;
        [Tooltip("Pausa (s) antes da IA 'decidir' — puramente cosmético, dá sensação de pensamento.")]
        public float aiReactionDelay = 0.3f;

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
        [Tooltip("Transparência do losango de footprint sob as unidades. 0 = invisível.")]
        [Range(0f, 1f)] public float footprintAlpha = 0.50f;
        [Tooltip("Cor do footprint das unidades do jogador.")]
        public Color playerFootprintColor = new Color(0.30f, 0.60f, 1.00f, 0.80f);
        [Tooltip("Cor do footprint das unidades inimigas.")]
        public Color enemyFootprintColor = new Color(1.00f, 0.38f, 0.20f, 0.80f);
        [Tooltip("Tint escurecido dos sprites inimigos (diferencia do jogador quando a classe é a mesma). Mais escuro = inimigos mais sombrios.")]
        public Color enemyTint = new Color(0.55f, 0.55f, 0.65f);
        [Tooltip("Cor do highlight de mira/hover sobre o alvo de ataque.")]
        public Color targetHoverColor = new Color(1f, 0.92f, 0.25f, 0.95f);
        [Tooltip("Cor persistente marcando 'esta unidade será atacada'.")]
        public Color attackMarkColor = new Color(0.20f, 0.03f, 0.03f, 0.95f);
        [Tooltip("Cor misturada ao selecionar uma unidade.")]
        public Color selectedTintColor = new Color(1f, 1f, 0.6f);
        [Tooltip("Intensidade da mistura de seleção. 0 = sem efeito; 1 = cor pura.")]
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
        [Tooltip("Liga/desliga o WeaponOverlay (arma desenhada por cima do personagem). Os sprites TinyTactics JÁ trazem a arma desenhada e animada nos frames de corpo, então o overlay é redundante e causa arma desalinhada/flutuando. Deixe DESLIGADO (false) para usar a arma do próprio corpo. Ligue (true) + alinhe na janela Pangea → Weapon Anim Editor se quiser separar a arma do corpo.")]
        public bool weaponOverlayEnabled = false;
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
        [Tooltip("Folga (unidades) além do grid que a câmera pode alcançar.")]
        public float panMargin = 2f;
        [Tooltip("Duração (s) das transições suaves da câmera automática.")]
        public float camTransitionDuration = 0.3f;
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
        [Tooltip("Delay (s) entre a UI aparecer e o resultado ser revelado.")]
        public float revealDelay = 1.0f;
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
        [Tooltip("Fração da duração em que o flash segura no pico antes de sumir. Maior = clarão mais longo no auge.")]
        [Range(0f, 1f)] public float flashHoldRatio = 0.5f;

        // ═════════════════════════ EFEITOS: LABELS ═════════════════════════

        [Header("═══ LABELS: COMPORTAMENTO ═══")]
        [Tooltip("Segundos que labels de batalha ficam na tela.")]
        public float battleLabelDuration = 1.8f;
        [Tooltip("Segundos do fade-out dos labels.")]
        public float battleLabelFadeTime = 0.4f;
        [Tooltip("Segundos que o label de anúncio de ataque fica visível.")]
        public float attackLabelDuration = 0.8f;
        [Tooltip("Altura (unidades) que o número de dano sobe ao flutuar.")]
        public float damageRiseHeight = 0.7f;
        [Tooltip("Duração (s) da subida do número de dano.")]
        public float damageRiseDuration = 0.35f;
        [Tooltip("Altura da subida do número de dano CRÍTICO (maior = mais espetaculoso).")]
        public float critRiseHeight = 1.0f;
        [Tooltip("Duração (s) da subida do dano crítico.")]
        public float critRiseDuration = 0.5f;
        [Tooltip("Altura da subida do label MISS.")]
        public float missRiseHeight = 0.6f;
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
        [Tooltip("Fração da duração usada no fade do label de ataque.")]
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
        [Tooltip("Segundos do pulso de highlight ao selecionar tiles.")]
        public float highlightDuration = 0.5f;
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
        [Tooltip("Cor/alpha das linhas de contorno do grid sobre os tiles. Alpha 0 = sem linhas.")]
        public Color gridLineColor = new Color(1f, 1f, 1f, 0.25f);
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
        [Tooltip("Sensibilidade do scroll do histórico de batalha. Maior = rola mais por 'dente'.")]
        public float logScrollSensitivity = 40f;
        [Tooltip("Cor do chip 'Mover' na fila de ações.")]
        public Color seqChipMoveColor = new Color(0.18f, 0.36f, 0.66f);
        [Tooltip("Cor do chip 'Atacar' na fila de ações.")]
        public Color seqChipAttackColor = new Color(0.66f, 0.32f, 0.12f);
        [Tooltip("Cor do chip 'Mover' bônus (BAP).")]
        public Color seqChipMoveBonusColor = new Color(0.60f, 0.20f, 0.92f);
        [Tooltip("Cor do chip 'Atacar' bônus (BAP).")]
        public Color seqChipAttackBonusColor = new Color(0.95f, 0.50f, 0.08f);
        [Tooltip("Quanto o chip selecionado clareia (mistura com branco). Maior = seleção mais óbvia.")]
        [Range(0f, 1f)] public float seqChipSelectedLighten = 0.4f;
        [Tooltip("Cor do topo do gradiente das janelas (tema FFT).")]
        public Color windowGradientTopColor = new Color(0.11f, 0.15f, 0.32f, 0.97f);
        [Tooltip("Cor da base do gradiente das janelas.")]
        public Color windowGradientBottomColor = new Color(0.02f, 0.04f, 0.11f, 0.97f);
        [Tooltip("Cor da moldura/borda das janelas.")]
        public Color windowBorderColor = new Color(0.50f, 0.64f, 0.92f);
        [Tooltip("Cor normal do timer de fase.")]
        public Color timerNormalColor = new Color(1f, 0.9f, 0.55f);
        [Tooltip("Cor do timer quando o tempo está acabando.")]
        public Color timerWarningColor = new Color(1f, 0.35f, 0.35f);
        [Tooltip("Cor do indicador de câmera em modo Auto.")]
        public Color cameraAutoColor = new Color(0.55f, 0.95f, 0.55f);
        [Tooltip("Cor do indicador de câmera em modo Manual.")]
        public Color cameraManualColor = new Color(1f, 0.75f, 0.35f);
        [Tooltip("Opacidade do fundo do indicador de câmera.")]
        [Range(0f, 1f)] public float cameraIndicatorBgAlpha = 0.85f;
        [Tooltip("Fração de HP abaixo da qual a barra fica AMARELA.")]
        [Range(0f, 1f)] public float hpBarYellowThreshold = 0.50f;
        [Tooltip("Fração de HP abaixo da qual a barra fica VERMELHA.")]
        [Range(0f, 1f)] public float hpBarRedThreshold = 0.25f;        [Tooltip("Cor da barra de HP cheia.")]
        public Color hpBarHighColor = new Color(0.20f, 0.78f, 0.28f);
        [Tooltip("Cor da barra de HP média.")]
        public Color hpBarMidColor = new Color(0.85f, 0.72f, 0.10f);
        [Tooltip("Cor da barra de HP crítica.")]
        public Color hpBarLowColor = new Color(0.82f, 0.20f, 0.20f);
        [Tooltip("Opacidade do escurecimento na tela de Vitória/Derrota.")]
        [Range(0f, 1f)] public float endScreenDimAlpha = 0.78f;

        // ═════════════════════════ MENU & SANDBOX ═════════════════════════

        [Header("═══ MENU & SANDBOX ═══")]
        [Tooltip("Segundos entre frames da caminhada no preview do editor de personagem.")]
        public float menuWalkAnimFrameDelay = 0.35f;
        [Tooltip("Cor de fundo do menu principal.")]
        public Color menuBackgroundColor = new Color(0.06f, 0.07f, 0.09f);
        [Tooltip("Cor de botão de lista em estado normal (menu e sandbox).")]
        public Color uiButtonNormalColor = new Color(0.25f, 0.25f, 0.30f);
        [Tooltip("Cor do item selecionado nas listas (presets, pincéis, mapas).")]
        public Color uiButtonActiveColor = new Color(0.28f, 0.50f, 0.82f);
        [Tooltip("Cor dourada dos títulos e labels de destaque.")]
        public Color uiTitleColor = new Color(0.92f, 0.80f, 0.35f);
        [Tooltip("Cor/alpha de fundo dos painéis laterais do Sandbox.")]
        public Color uiPanelBgColor = new Color(0.08f, 0.09f, 0.12f, 0.92f);
        [Tooltip("Cor do texto de toasts/avisos.")]
        public Color toastTextColor = new Color(1f, 0.92f, 0.6f);
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
        [Tooltip("Altura máxima ao empilhar tiles no Sandbox.")]
        public int maxTileHeight = 3;
        [Tooltip("Tamanho máximo do mapa (lado maior) permitido no Sandbox.")]
        public int maxMapSize = 50;

        // ═════════════════════════ ÁUDIO ═════════════════════════

        [Header("═══ ÁUDIO ═══")]
        [Tooltip("Volume dos efeitos sonoros (golpes, passos, etc).")]
        [Range(0f, 1f)] public float sfxVolume = 1f;
        [Tooltip("Volume da música de fundo.")]
        [Range(0f, 1f)] public float musicVolume = 1f;

        // ═════════════════════════ MAGIA ═════════════════════════

        [Header("═══ MAGIA: CUSTO & ALCANCE ═══")]
        [Tooltip("PA gasto por conjuração.")]
        public int spellApCost = 1;
        [Tooltip("Mana gasta por magia Self (buff).")]
        public int spellSelfManaCost = 2;
        [Tooltip("Mana gasta por magia de projétil (Unit).")]
        public int spellUnitManaCost = 3;
        [Tooltip("Mana gasta por magia de tile (terreno).")]
        public int spellTileManaCost = 4;
        [Tooltip("Mana recuperada ao usar Concentrar.")]
        public int concentrateManaGain = 3;
        [Tooltip("Tiles de alcance por mana gasta. Alcance = mana × spellRangePerMana + bônus do conduíte.")]
        public int spellRangePerMana = 1;
        [Tooltip("Alcance base extra de conduíte (soma ao alcance baseado em mana).")]
        public int spellRangeBase = 0;
        [Tooltip("Potência base do elemento Físico: multiplica par (DEX+STR).")]
        public float physicalBasePotency = 1f;
        [Tooltip("Potência base do elemento Mágico: multiplica par (INT+WIS).")]
        public float magicBasePotency = 1f;
        [Tooltip("Potência base do elemento Fogo: multiplica par (INT+VIT).")]
        public float fireBasePotency = 1f;
        [Tooltip("Potência base do elemento Água: multiplica par (VIT+INT).")]
        public float waterBasePotency = 1f;
        [Tooltip("Potência base do elemento Ar: multiplica par (AGI+INT).")]
        public float airBasePotency = 1f;
        [Tooltip("Potência base do elemento Terra: multiplica par (VIT+STR).")]
        public float earthBasePotency = 1f;
        [Tooltip("Bônus multiplicativo de afinidade quando o elemento da magia coincide com a afinidade do conduíte. 0.25 = +25% de dano.")]
        public float conduitAffinityBonus = 0.25f;
        [Tooltip("Dano mínimo de magia após todos os multiplicadores (não mitigável).")]
        public int spellMinDamage = 1;

        [Header("═══ MAGIA: SELF (BUFFS) ═══")]
        [Tooltip("Fator de potência para resistência elemental de buffs Self. P × fator = resistência concedida.")]
        public float spellResistFactor = 0.5f;
        [Tooltip("Duração (rounds) de buffs elementais (Fogo/Água/Ar/Terra Self).")]
        public int spellBuffDurationRounds = 3;
        [Tooltip("Duração (rounds) do buff Físico Self (PhysicalMight).")]
        public int physicalBuffDurationRounds = 3;
        [Tooltip("Fração da potência virada em bônus de STR no buff PhysicalMight.")]
        public float physicalBuffStrFactor = 0.5f;
        [Tooltip("Fração da potência virada em absorção VIT no buff PhysicalMight.")]
        public float physicalBuffVitFactor = 0.5f;
        [Tooltip("Fator de capacidade do escudo mágico: P × fator = absorção total do shield.")]
        public float shieldCapacityFactor = 1f;

        [Header("═══ MAGIA: TILE (TERRENO) ═══")]
        [Tooltip("Fator de dano de fogo em tile: potência do fogo × fator = dano por tique. Maior = chamas mais letais.")]
        public float fireTileDamageFactor = 0.4f;
        [Tooltip("Duração (rounds) do fogo em tile antes de apagar.")]
        public int fireTileDurationRounds = 3;
        [Tooltip("Duração (rounds) do vento em tile.")]
        public int windTileDurationRounds = 3;
        [Tooltip("Nº de tiles que o vento empurra uma unidade ao pisar.")]
        public int windPushTiles = 1;
        [Tooltip("Índice do tile de Água no atlas (TilePalette).")]
        public int waterTileIndex = 16;
        [Tooltip("Índice do tile de Pedra no atlas (elevável por Terra).")]
        public int earthStoneTileIndex = 8;
        [Tooltip("Quantidade de altura adicionada ao tile de Pedra por Terra-Tile.")]
        public int earthRaiseAmount = 1;
        [Tooltip("Fator de conversão de mana para potência de orbe: mana × fator = mana recuperável ao pegar.")]
        public float orbManaFactor = 1f;
        [Tooltip("Tamanho máximo do grid após dobra espacial (lado maior).")]
        public int spellFoldMaxGridSize = 40;

        [Header("═══ MAGIA: PROJÉTIL & RITMO ═══")]
        [Tooltip("Nº de tiles que um projétil de Ar empurra o alvo ao acertar.")]
        public int airProjectilePushTiles = 1;
        [Tooltip("Velocidade do projétil (unidades/s). Maior = bala mais rápida.")]
        public float spellProjectileSpeed = 14f;
        [Tooltip("Dano de impacto contra parede quando empurrado e não há tile livre.")]
        public int wallImpactDamage = 2;
        [Tooltip("Arco parabólico do projétil (0 = reto, 1 = arco alto).")]
        public float spellProjectileArc = 0.6f;
        [Tooltip("Duração (s) da animação de conjuração (charging frames).")]
        public float spellCastAnimDuration = 0.5f;
        [Tooltip("Pausa (s) após impacto do projétil antes de resolver.")]
        public float spellImpactPause = 0.35f;
        [Tooltip("FPS da animação de overlay de tile (ex: fogo tremendo).")]
        public float tileEffectAnimFps = 6f;
        [Tooltip("Alpha do overlay de efeito em tile (0=invisível, 1=sólido).")]
        [Range(0f, 1f)] public float tileEffectOverlayAlpha = 0.65f;

        [Header("═══ MAGIA: CORES ═══")]
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
        [Tooltip("Cor do highlight de tiles alvo de magia.")]
        public Color spellTargetHighlightColor = new Color(0.30f, 0.80f, 1.00f, 0.70f);
        [Tooltip("Cor do range de ataque destacado ao selecionar a unidade e ao mirar unidade.")]
        public Color attackRangeColor = new Color(1.00f, 0.82f, 0.30f, 0.35f);
        [Tooltip("Cor do range do inimigo ao passar o mouse sobre ele.")]
        public Color enemyRangeColor = new Color(1.00f, 0.28f, 0.22f, 0.45f);
        [Tooltip("Cor do chip de magia na sequência de ações.")]
        public Color seqChipSpellColor = new Color(0.60f, 0.20f, 0.80f);
        [Tooltip("Cor do chip de Concentração na barra de sequência de ações.")]
        public Color seqChipConcentrateColor = new Color(0.15f, 0.55f, 0.55f);
        [Tooltip("Cor do texto de mana na HUD.")]
        public Color manaTextColor = new Color(0.40f, 0.60f, 1.00f);

        [Header("═══ MAGIA: SPRITES ═══")]
        [Tooltip("Path do spritesheet de projétil Físico (Resources relative, sem extensão).")]
        public string projectileSheetPhysical = "Sprites/BDragon1727/Bullets/Bullet 24x24 Free Part 1A";
        [Tooltip("Path do spritesheet de projétil Mágico.")]
        public string projectileSheetMagic = "Sprites/BDragon1727/Bullets/Bullet 24x24 Free Part 2A";
        [Tooltip("Path do spritesheet de projétil Fogo.")]
        public string projectileSheetFire = "Sprites/BDragon1727/Bullets/Bullet 24x24 Free Part 3A";
        [Tooltip("Path do spritesheet de projétil Água.")]
        public string projectileSheetWater = "Sprites/BDragon1727/Bullets/Bullet 24x24 Free Part 4A";
        [Tooltip("Path do spritesheet de projétil Ar.")]
        public string projectileSheetAir = "Sprites/BDragon1727/Bullets/Bullet 24x24 Part 5A Free";
        [Tooltip("Path do spritesheet de projétil Terra.")]
        public string projectileSheetEarth = "Sprites/BDragon1727/Bullets/Bullet 24x24 Part 6A Free";
        [Tooltip("Path do spritesheet de impacto (explosão) — Effects.")]
        public string impactSheetPath = "Sprites/BDragon1727/Effects/113";
        [Tooltip("Índice do primeiro frame do sheet de IMPACTO a animar (atlas grande: janela)")]
        public int impactFrameStart = 0;
        [Tooltip("Quantos frames animar no IMPACTO a partir do início (0 = todos os frames do sheet)")]
        public int impactFrameCount = 0;
        [Tooltip("Path do spritesheet de aura de buff Self — RPGEffects.")]
        public string auraSheetPath = "Sprites/BDragon1727/RPGEffects/1010";
        [Tooltip("Índice do primeiro frame do sheet de AURA (Self) a animar")]
        public int auraFrameStart = 0;
        [Tooltip("Quantos frames animar na AURA a partir do início (0 = todos os frames do sheet)")]
        public int auraFrameCount = 0;
        [Tooltip("Escala do sprite de projétil no mundo.")]
        public float spellProjectileScale = 1.5f;
        [Tooltip("Escala do sprite de impacto/aura/tile-VFX no mundo.")]
        public float spellVfxScale = 2f;
        [Tooltip("FPS da animação de projétil/impacto/aura.")]
        public float spellVfxFps = 12f;
        [Tooltip("Ligar logs de debug de VFX de magia no Console (útil para testar).")]
        public bool spellVfxDebugLogs = true;
        [Tooltip("Índice do frame do sheet de projétil (os sheets têm variantes de COR; um frame fixo evita o projétil 'piscar' entre cores). -1 = animar por todos os frames.")]
        public int spellProjectileFrame = 0;

        [Header("═══ MAGIA: IA ═══")]
        [Tooltip("Ligado = IA usa magia quando tem mana e alvo.")]
        public bool aiUseSpells = true;
        [Tooltip("Par mínimo de atributos (soma) para IA considerar conjurar. Menor = IA conjura com atributos baixos.")]
        public int aiSpellMinPair = 8;
        [Tooltip("Fração da mana disponível que a IA gasta por conjuração. 0.34 = ~1/3 da mana.")]
        public float aiSpellManaFraction = 0.34f;
        [Tooltip("Threshold de mana para IA considerar Concentração. 0 = sempre tenta concentrar quando sem mana.")]
        public int aiConcentrationThreshold = 0;

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

        [Tooltip("Aplica uma moldura pixel-art gerada (contorno preto + realce claro) sobre os painéis FFT (StyleFFTWindow), pra combinar com a skin dos botões. O kit BDragon1727 não tem um frame de painel pronto, então essa moldura é gerada em runtime.")]
        public bool windowFrameEnabled = true;
        [Tooltip("Raio do canto arredondado da moldura de painel, em px (referência ~32ppu).")]
        public int windowFrameCorner = 10;
        [Tooltip("Espessura do contorno preto da moldura de painel, em px.")]
        public int windowFrameBorderPx = 2;
        [Tooltip("Cor do contorno externo da moldura de painel.")]
        public Color windowFrameBorderColor = new Color(0.04f, 0.05f, 0.09f);
        [Tooltip("Cor do realce interno (1px) da moldura de painel, logo depois do contorno.")]
        public Color windowFrameHighlightColor = new Color(0.55f, 0.68f, 0.92f);

        public float BasePotencyForElement(SpellElement e)
        {
            switch (e)
            {
                case SpellElement.Physical: return physicalBasePotency;
                case SpellElement.Magic:    return magicBasePotency;
                case SpellElement.Fire:     return fireBasePotency;
                case SpellElement.Water:    return waterBasePotency;
                case SpellElement.Air:      return airBasePotency;
                case SpellElement.Earth:    return earthBasePotency;
                default:                    return 1f;
            }
        }
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
