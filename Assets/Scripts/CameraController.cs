using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using PangeaSkirmish.UI;

namespace PangeaSkirmish
{
    public enum CameraMode { Auto, Manual }

    /// <summary>
    /// Câmera ortográfica 2D pura. Modo Auto segue ações de batalha automaticamente.
    /// Quando o jogador interage (arrastar/zoom/edge-pan), entra em Manual por 2s,
    /// depois volta para Auto. FocusOn só funciona em modo Auto.
    ///
    /// CAMINHO 1 (2026-07-14): Q/E viram a VISÃO do mapa em passos de 90° (4 estados
    /// fixos: 0/90/180/270). Ao virar, o facing de TODAS as unidades é re-derivado
    /// (Unit.SetViewOrientation) para "encarar" o novo norte da tela — sem arte nova,
    /// usando as 4 direções que o motor já suporta (NE/SE + flipX).
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        private Camera _cam;
        private Vector2 _targetXY;
        private float   _targetSize;
        private Vector2 _curXY;
        private float   _curSize;
        private const float CamZ = -10f;
        // Fix (2026-07-20): câmera acima do chão, olhando reto para baixo
        // (ver Configure — reto de cima, não mais inclinada). Em ortográfica a
        // altura exata não importa para a projeção, só precisa ficar acima do
        // chão com clip planes normais (não mais negativo).
        private const float CAM_HEIGHT = 60f; // distância ao longo da visão (ortográfica: só evita clipping)

        private Vector2 _panMinXY = new Vector2(-50f, -50f);
        private Vector2 _panMaxXY = new Vector2(50f, 50f);

        [Header("Suavizacao")]
        public float followSpeed = 9f;

        [Header("Pan manual")]
        public bool  enableDragPan = true;
        public bool  enableEdgePan = true;
        public float edgeMargin    = 18f;
        public float edgePanSpeed  = 12f;
        public float dragSpeed     = 1f;

        [Header("Zoom")]
        public float zoomSpeed      = 2f;
        public float zoomMin        = 3f;
        public float zoomMax        = 20f;

        [Header("Snap de vista (Q/E)")]
        public bool  enableViewRotate = true;

        // Estado lógico da vista (0/90/180/270). Usado p/ re-derivar o facing
        // das unidades (Unit.SetViewOrientation). A rotação VISUAL está no grid.
        private int _orientation;
        [Header("Screen Shake")]
        public bool  shakeEnabled = true;

        // ── Auto/Manual state ──
        private CameraMode _mode = CameraMode.Auto;
        private float _manualTimer;
        private float _manualTimeout = 2f;

        /// <summary>Modo atual da câmera.</summary>
        public CameraMode Mode => _mode;

        /// <summary>Evento disparado quando o modo muda (Auto↔Manual).</summary>
        public event Action<CameraMode> OnModeChanged;

        /// <summary>Orientação atual da vista em graus (0/90/180/270).</summary>
        public int Orientation => _orientation;

        /// <summary>
        /// True enquanto o tabuleiro está visualmente girando.
        ///
        /// Durante o giro o que aparece na tela NÃO corresponde à posição lógica: os
        /// tiles estão rotacionados no rig, mas o picking é analítico e já responde
        /// pela orientação final. Clicar nessa janela acertaria uma célula diferente
        /// da que está sob o cursor — em multiplayer isso vira PlaceUnitServerRpc na
        /// célula errada, que é irreversível. GridManager.ScreenToGround consulta
        /// esta flag e recusa o picking enquanto durar.
        /// </summary>
        public bool IsViewSpinning => _spinRig != null;

        // Tranco do giro de vista: roll (graus, em torno do eixo de visão) + afastamento
        // momentâneo do zoom. Roll em torno do FORWARD não distorce a projeção — a
        // câmera continua perpendicular ao chão, só a imagem inteira inclina por igual.
        private float _rollDeg, _rollTarget;
        private float _zoomPunch, _punchTarget;
        private float _fxTimer, _fxDuration;
        private float _viewRotateCooldown;   // trava entre giros consecutivos

        // Giro VISUAL do tabuleiro. O grid inteiro nasce girado no ângulo de onde
        // veio e "desgira" até a posição nova, percorrendo de fato os 90° — é o que
        // cria a ilusão de rotação. Como é o rig inteiro que gira (rotação rígida),
        // os losangos não se desencaixam entre si; e como termina exatamente em
        // zero, nenhum estado final fica torto. Foi girar o grid e PARAR torto que
        // quebrou o jogo em 20/07; aqui a rotação é transitória.
        private Transform _spinRig;
        private float _spinFrom;

        // Shake state
        private float   _shakeDuration;
        private float   _shakeMagnitude;
        private float   _shakeTimer;
        private Vector2 _shakeOffset;

        public Camera Cam => _cam;

        // ── Acesso global ──
        private static CameraController _instance;
        public static CameraController Instance => _instance;

        // ── Setup ──

        private void Awake() => _instance = this;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public void Configure(Camera cam, Vector3 initialCenter, float initialSize)
        {
            _cam = cam;
            _cam.orthographic = true;
            // Fix (2026-07-20): câmera RETO DE CIMA (perpendicular ao chão XZ),
            // não mais inclinada. A arte do TinyTactics (sprites com SpriteMeshType
            // .Tight — o mesh já é o contorno do losango) foi desenhada para ser
            // vista assim, com a perspectiva isométrica 2:1 embutida na MALHA
            // (halfW/halfH), igual SimCity/Civilization e o padrão oficial de
            // Isometric Tilemap da Unity (cell size 1:0.5). Um tilt real de
            // câmera duplica a distorção isométrica (já bakeada no mesh) — foi
            // a causa do bug de rotação Q/E esticando/"escadeando" o mapa.
            // Câmera normal acima do chão: near/far clip padrão (nada de negativo).
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane  = 1000f;
            // Mundo 3D (2026-11): câmera inclinada de verdade (ver World3D). O chão agora é
            // um grid quadrado girado 45°, então a inclinação não duplica mais distorção.
            _cam.transform.rotation = World3D.CamRotation;
            _cam.transform.position = new Vector3(initialCenter.x, 0f, initialCenter.z)
                                      - (World3D.CamRotation * Vector3.forward) * CAM_HEIGHT;

            _targetXY = _curXY = new Vector2(initialCenter.x, initialCenter.z);
            _targetSize = _curSize = initialSize;
            _orientation = 0;

            // Nenhum resíduo de giro visual entre batalhas/cenas.
            if (_spinRig != null) { _spinRig.rotation = Quaternion.identity; _spinRig = null; }
            _rollDeg = 0f; _zoomPunch = 0f; _fxTimer = 0f;

            var tuning = RuntimeTuning.Active;
            if (tuning != null)
            {
                followSpeed = tuning.followSpeed;
                edgeMargin  = tuning.edgeMargin;
                edgePanSpeed = tuning.edgePanSpeed;
                dragSpeed   = tuning.dragSpeed;
                zoomSpeed   = tuning.zoomSpeed;
                zoomMin     = tuning.zoomMin;
                zoomMax     = tuning.zoomMax;
                _manualTimeout = tuning.camManualTimeout;
            }

            ApplyToCamera();
        }

        public void SetPanBounds(Vector2 minXY, Vector2 maxXY)
        {
            _panMinXY = minXY;
            _panMaxXY = maxXY;
        }

        // ── Public API ──

        /// <summary>Foca um ponto do mundo. Só funciona em modo Auto.</summary>
        public void FocusOn(Vector3 center, float size = -1f)
        {
            if (_mode == CameraMode.Manual) return;
            _targetXY = ClampCenter(new Vector2(center.x, center.z));
            if (size > 0f) _targetSize = size;
        }

        /// <summary>Foca em uma região do mundo, ajustando zoom para caber tudo. Só funciona em modo Auto.</summary>
        public void FocusOnArea(Vector3 center, float requiredSize)
        {
            if (_mode == CameraMode.Manual) return;
            _targetXY = ClampCenter(new Vector2(center.x, center.z));
            _targetSize = Mathf.Clamp(requiredSize, zoomMin, zoomMax);
        }

        /// <summary>Força o modo Manual (ex: jogador clicou no mapa).</summary>
        public void SetManual()
        {
            if (_mode == CameraMode.Manual) return;
            _mode = CameraMode.Manual;
            _manualTimer = _manualTimeout;
            OnModeChanged?.Invoke(_mode);
        }

        /// <summary>Força o modo Auto imediatamente.</summary>
        public void SetAuto()
        {
            if (_mode == CameraMode.Auto) return;
            _mode = CameraMode.Auto;
            _manualTimer = 0f;
            OnModeChanged?.Invoke(_mode);
        }

        public void SnapTo(Vector3 center, float size)
        {
            _targetXY = _curXY = ClampCenter(new Vector2(center.x, center.z));
            _targetSize = _curSize = size;
            ApplyToCamera();
        }

        /// <summary>Vira a vista em um passo de 90° (clockwise=true → +90/Leste,
        /// false → -90/Oeste). Reindexação lógica instantânea no GridManager —
        /// não gira mais nenhum transform físico (nem grid, nem câmera). Re-deriva
        /// o facing de todas as unidades para encarar o novo norte.</summary>
        public void CycleView(bool clockwise)
        {
            if (!enableViewRotate) return;
            ApplyOrientationAnchored(_orientation + (clockwise ? 90 : -90));
            // Virar a vista conta como input manual (pausa auto-focus).
            if (_mode == CameraMode.Auto)
            {
                _mode = CameraMode.Manual;
                _manualTimer = _manualTimeout;
                OnModeChanged?.Invoke(_mode);
            }
        }

        /// <summary>Define a orientação absoluta da vista (múltiplo de 90).</summary>
        public void SetViewOrientation(int degrees)
        {
            if (!enableViewRotate) return;
            ApplyOrientationAnchored(degrees);
        }

        /// <summary>
        /// Aplica uma orientação de vista mantendo FIXO na tela o pedaço de
        /// tabuleiro que está no centro.
        ///
        /// Fix (2026-07-30): sem isto a câmera continuava mirando o mesmo ponto do
        /// MUNDO, mas a reindexação lógica troca qual célula ocupa aquele ponto —
        /// então o que o jogador estava olhando saltava para longe. Medido num
        /// grid 20×20: uma unidade fora do centro do mapa saltava até 24 unidades,
        /// ou ~1,5 altura de tela, ou seja, sumia do enquadramento. Era a causa da
        /// sensação de "perder o rastro" ao girar, mesmo com tudo tecnicamente
        /// correto. Agora o tabuleiro gira EM VOLTA do ponto observado.
        /// </summary>
        private void ApplyOrientationAnchored(int degrees)
        {
            var grid = GridManager.Instance;

            // Que ponto do tabuleiro está no centro da tela agora? Em coordenada de
            // célula CONTÍNUA — arredondar daria um solavanco de até meia célula.
            Vector2 ancora = Vector2.zero;
            bool temAncora = grid != null;
            if (temAncora)
                ancora = grid.WorldToCellContinuous(new Vector3(_curXY.x, 0f, _curXY.y));

            Vector3 camAntes = new Vector3(_curXY.x, 0f, _curXY.y);
            int orientacaoAntes = _orientation;

            // Zera qualquer deslize ainda em curso ANTES de medir. As posições dos
            // objetos carregam o offset visual do arco anterior; medir sem limpar
            // fazia cada giro partir de um ponto corrompido, e o erro se somava
            // quando o jogador martelava Q/E.
            ViewSlide.ClearAll();

            // Onde estava cada objeto esparso ANTES da troca (para o efeito de giro).
            float fxDur = Tuning.Get().viewRotateFxDuration;
            var alvos = fxDur > 0f ? ColetarAlvosDoEfeito() : null;

            _orientation = ((degrees % 360) + 360) % 360;
            if (grid != null) grid.SetViewOrientation(_orientation);
            UnitRegistry.ApplyViewOrientation(_orientation);

            // Recoloca a câmera sobre o MESMO ponto do tabuleiro, no novo slot.
            if (temAncora)
            {
                Vector3 destino = grid.CellContinuousToWorld(ancora);
                _targetXY = _curXY = ClampCenter(new Vector2(destino.x, destino.z));
                ApplyToCamera();
            }

            if (alvos != null && _orientation != orientacaoAntes)
                DispararEfeitoDeGiro(alvos, camAntes, fxDur, HorarioEntre(orientacaoAntes, _orientation));
        }

        // ── EFEITO DE GIRO ──
        //
        // A rotação é reindexação: com a câmera ancorada e o chão uniforme, girar 90°
        // devolve uma imagem quase idêntica e o jogador não percebe que o mundo virou.
        // O efeito devolve essa leitura por dois canais somados: os objetos esparsos
        // percorrem o arco entre o slot antigo e o novo (ViewSlide), e a câmera dá um
        // tranco curto (roll + afastamento). O CHÃO troca seco — animá-lo é o que
        // quebrou o jogo em 20/07, porque os sprites têm SpriteMeshType.Tight e não
        // encaixam em ângulos intermediários.

        private List<(Transform tr, Vector3 pos)> ColetarAlvosDoEfeito()
        {
            var lista = new List<(Transform, Vector3)>();
            foreach (var u in UnitRegistry.Tracked)
                if (u != null && !u.IsDead) lista.Add((u.transform, u.transform.position));
            foreach (var a in GridAnchored.All)
                if (a != null) lista.Add((a.transform, a.transform.position));
            return lista;
        }

        private void DispararEfeitoDeGiro(List<(Transform tr, Vector3 pos)> alvos,
                                          Vector3 camAntes, float dur, bool clockwise)
        {
            // A câmera saltou de camAntes para camDepois no mesmo frame. Sem compensar,
            // o arco começaria no lugar errado da TELA e o objeto daria um salto antes
            // de deslizar. Somando o deslocamento da câmera, o arco parte exatamente de
            // onde o objeto estava sendo desenhado.
            Vector3 camDepois = new Vector3(_curXY.x, 0f, _curXY.y);
            Vector3 shift = camDepois - camAntes;
            var T = Tuning.Get();

            // Giro visual do tabuleiro: o rig começa girado no ângulo de origem e
            // volta a zero ao longo do efeito.
            var gridFx = GridManager.Instance;
            var rig = gridFx != null ? gridFx.GridRig : null;
            float spin = T.viewRotateWorldSpin;
            bool girandoMundo = rig != null && spin > 0.01f;
            // A rotação só é APLICADA no fim deste método: o TileSweep captura as
            // posições dos tiles no mesmo frame, e com o rig já girado ele mediria a
            // vinheta e a fase sobre posições que ainda vão voltar.

            // Chão e objetos compartilham a MESMA onda: a fase angular que acende o
            // tile é a que decide quando aquele objeto começa a se mover. Sem isso
            // seriam dois efeitos concorrentes em vez de um só passando pelo mapa.
            float stagger = Mathf.Max(0f, T.viewRotateStagger);
            foreach (var (tr, posAntiga) in alvos)
            {
                if (tr == null) continue;
                // Quem já é filho do rig percorre o arco junto com ele; aplicar o
                // deslize por cima moveria o objeto duas vezes.
                if (girandoMundo && tr.IsChildOf(rig)) continue;

                Vector3 partida = posAntiga + shift;
                float atraso = stagger * TileSweep.PhaseAt(partida, camDepois, clockwise);
                ViewSlide.Play(tr.gameObject, partida, tr.position, camDepois, dur, atraso);
            }

            // Varredura do terreno (cor + inchaço leve; nunca posição — ver TileSweep).
            if (gridFx != null && T.viewRotateSweepBrightness > 0f)
            {
                // Interrompe a varredura em curso; TileSweep.Run restaura o estado
                // pendente antes de capturar o novo, então girar rápido não acumula
                // brilho nem deixa tiles inchados.
                if (_sweep != null) StopCoroutine(_sweep);
                _sweep = StartCoroutine(TileSweep.Run(
                    gridFx, _cam, camDepois, clockwise, dur + stagger,
                    T.viewRotateSweepBrightness, T.viewRotateSweepScale,
                    Mathf.Clamp01(T.viewRotateDarkenUniform)));
            }

            var audio = AudioManager.I;
            if (audio != null)
                audio.Play(audio.sfxViewRotate != null ? audio.sfxViewRotate : audio.sfxDash);

            _rollTarget = (clockwise ? 1f : -1f) * T.viewRotateRollDegrees;
            _punchTarget = T.viewRotateZoomPunch;
            _fxTimer = dur;
            _fxDuration = dur;

            // Agora sim: joga o tabuleiro de volta ao ângulo de origem. A partir do
            // próximo frame o LateUpdate o traz até zero, percorrendo os 90°.
            if (girandoMundo)
            {
                _spinRig = rig;
                // Sentido contrário ao da reindexação: o mapa parte de onde ESTAVA e
                // alcança onde já está logicamente.
                _spinFrom = clockwise ? -spin : spin;
                rig.rotation = Quaternion.Euler(0f, _spinFrom, 0f);
            }
        }

        private Coroutine _sweep;

        /// <summary>Sentido do giro considerando a volta curta (270°→0° é horário).</summary>
        private static bool HorarioEntre(int de, int para)
        {
            int delta = ((para - de) % 360 + 360) % 360;
            return delta <= 180;
        }

        /// <summary>Screen shake da câmera por duration segundos. Sem argumentos usa os
        /// defaults do GameTuning (chamadores de combate passam os valores crit/normal).</summary>
        public void Shake(float duration = -1f, float magnitude = -1f)
        {
            if (!shakeEnabled) return;
            var tuning = Tuning.Get();
            if (duration < 0f) duration = tuning.shakeDurationDefault;
            if (magnitude < 0f) magnitude = tuning.shakeMagnitudeDefault;
            _shakeDuration = duration;
            _shakeMagnitude = magnitude;
            _shakeTimer = 0f;
        }

        public bool IsSettled =>
            Vector2.Distance(_curXY, _targetXY) < Tuning.Get().camSettleThreshold &&
            Mathf.Abs(_curSize - _targetSize) < Tuning.Get().camSettleThreshold;

        public IEnumerator WaitUntilSettled(float timeout = -1f)
        {
            if (timeout < 0f) timeout = Tuning.Get().camSettleTimeout;
            float t = 0f;
            while (!IsSettled && t < timeout)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        // ── Loop ──

        /// <summary>Último instante (unscaled) em que o jogador arrastou ou deu zoom (HUD esconde a inspeção).</summary>
        public static float LastManualMoveTime = -99f;

        private void LateUpdate()
        {
            if (_cam == null) return;

            if (_viewRotateCooldown > 0f) _viewRotateCooldown -= Time.deltaTime;

            bool moved = HandleManualPan() | HandleZoom();
            if (moved) LastManualMoveTime = Time.unscaledTime;
            bool hadManualInput = moved || HandleViewRotate();

            // Se houve input manual, ativa modo Manual e reinicia timer
            if (hadManualInput && _mode == CameraMode.Auto)
            {
                _mode = CameraMode.Manual;
                _manualTimer = _manualTimeout;
                OnModeChanged?.Invoke(_mode);
            }
            else if (_mode == CameraMode.Manual)
            {
                _manualTimer -= Time.deltaTime;
                if (_manualTimer <= 0f)
                {
                    _mode = CameraMode.Auto;
                    OnModeChanged?.Invoke(_mode);
                }
            }

            float k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            _curXY   = Vector2.Lerp(_curXY, _targetXY, k);
            _curSize = Mathf.Lerp(_curSize, _targetSize, k);

            // Rotação da vista: NÃO é mais da câmera (Caminho 3). O grid
            // gira no GridManager (_gridRig, eixo Z do mundo). Câmera só pan/zoom/shake.

            // Tranco do giro de vista: o roll desaba de volta a zero (ease-out) e o
            // zoom "respira" — afasta no meio do giro e volta, como se a câmera
            // tivesse sido arrastada pelo mundo virando.
            if (_fxTimer > 0f)
            {
                _fxTimer -= Time.deltaTime;
                float fxK = Mathf.Clamp01(1f - _fxTimer / Mathf.Max(0.0001f, _fxDuration));
                float restante = 1f - fxK;
                _rollDeg   = _rollTarget * restante * restante;
                _zoomPunch = _punchTarget * Mathf.Sin(fxK * Mathf.PI);

                // Desgira o tabuleiro até zero. Ease-out: sai rápido e assenta
                // devagar, que é como uma massa girando de fato desacelera.
                if (_spinRig != null)
                {
                    float suave = 1f - restante * restante;
                    float ang = Mathf.LerpAngle(_spinFrom, 0f, suave);
                    _spinRig.rotation = Quaternion.Euler(0f, ang, 0f);
                }

                if (_fxTimer <= 0f)
                {
                    _rollDeg = 0f; _zoomPunch = 0f;
                    // Termina exatamente em zero: nenhum resíduo de rotação no grid.
                    if (_spinRig != null) { _spinRig.rotation = Quaternion.identity; _spinRig = null; }
                }
            }

            // Screen shake
            _shakeOffset = Vector2.zero;
            if (_shakeTimer < _shakeDuration)
            {
                _shakeTimer += Time.deltaTime;
                float damp = 1f - (_shakeTimer / _shakeDuration);
                _shakeOffset = new Vector2(
                    UnityEngine.Random.Range(-1f, 1f) * _shakeMagnitude * damp,
                    UnityEngine.Random.Range(-1f, 1f) * _shakeMagnitude * damp);
            }

            ApplyToCamera();
        }

        // ── Input handlers (retornam true se houve input manual) ──

        private bool HandleViewRotate()
        {
            if (!enableViewRotate || Keyboard.current == null) return false;
            // Fix (2026-07-29): não girar a vista enquanto o jogador digita num
            // campo da UI (ex.: nome do mapa no Sandbox) — cada "q"/"e" digitado
            // virava um giro de 90°.
            if (UIHelper.IsTextInputFocused()) return false;
            // Fix (2026-07-30): um giro só é aceito depois que o anterior assentou.
            // Encavalar giros embaralhava as posições de partida do efeito e a
            // âncora da câmera, e o jogo só voltava ao normal quando o jogador
            // parava de apertar.
            if (_viewRotateCooldown > 0f) return false;

            bool q = Keyboard.current.qKey.wasPressedThisFrame;
            bool e = Keyboard.current.eKey.wasPressedThisFrame;
            if (!q && !e) return false;

            CycleView(e);   // E = +90 (horário), Q = -90 (anti-horário)
            _viewRotateCooldown = Mathf.Max(0f, Tuning.Get().viewRotateCooldown);
            return true;
        }

        private bool HandleZoom()
        {
            // Pinça (GDD-022): abrir os dedos aproxima.
            float pinch = PointerInput.PinchDelta;
            if (Mathf.Abs(pinch) > 0.5f)
            {
                _targetSize = Mathf.Clamp(_targetSize * (1f - pinch / Mathf.Max(1, Screen.height) * 2f), zoomMin, zoomMax);
                return true;
            }
            if (Mouse.current == null || PointerInput.IsTouch) return false;
            // Em UI Toolkit o EventSystem.IsPointerOverGameObject() dispara para qualquer
            // Canvas UGUI (mesmo vazio do HUD), bloqueando o zoom no combate. Checamos o
            // UIDocument real: só bloqueia se o ponteiro estiver sobre um botão clicável.
            if (UIHelper.IsPointerOverClickableUI()) return false;

            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _targetSize = Mathf.Clamp(_targetSize - scroll * zoomSpeed * Tuning.Get().zoomScrollFactor, zoomMin, zoomMax);
                return true;
            }
            return false;
        }

        private bool HandleManualPan()
        {
            if (!PointerInput.Available) return false;

            float worldPerPixel = (_curSize * 2f) / Mathf.Max(1, Screen.height);

            // Arraste com botão direito (migração XY→XZ: _curXY = (X mundo, Z mundo))
            // Botão direito arrastando, ou 1–2 dedos arrastando (GDD-022).
            if (enableDragPan && PointerInput.Dragging)
            {
                Vector2 d = PointerInput.DragDelta;
                // d.x → X mundo, d.y → Z mundo (mundo XZ sob câmera 45°).
                _targetXY = ClampCenter(_targetXY + new Vector2(-d.x, -d.y * World3D.GroundStretch) * worldPerPixel * dragSpeed);
                return true;
            }

            // Edge-pan
            if (enableEdgePan && Application.isFocused && !PointerInput.IsTouch && Mouse.current != null)
            {
                Vector2 m = Mouse.current.position.ReadValue();
                bool inside = m.x > 1f && m.y > 1f && m.x < Screen.width - 1f && m.y < Screen.height - 1f;
                if (inside)
                {
                    Vector2 dir = Vector2.zero;
                    if (m.x <= edgeMargin)                      dir.x -= 1f;
                    else if (m.x >= Screen.width  - edgeMargin) dir.x += 1f;
                    if (m.y <= edgeMargin)                      dir.y -= 1f;
                    else if (m.y >= Screen.height - edgeMargin) dir.y += 1f;

                    if (dir != Vector2.zero)
                    {
                        float speed = edgePanSpeed * (_curSize / Mathf.Max(1f, Tuning.Get().edgePanReferenceZoom));
                        // dir.y (tela) → Z mundo
                        Vector2 pan = new Vector2(dir.x, dir.y).normalized;
                        pan.y *= World3D.GroundStretch; // chão inclinado: 1 de tela = 2 de profundidade
                        _targetXY = ClampCenter(_targetXY + pan * speed * Time.deltaTime);
                        return true;
                    }
                }
            }

            return false;
        }

        private Vector2 ClampCenter(Vector2 c)
        {
            c.x = Mathf.Clamp(c.x, _panMinXY.x, _panMaxXY.x);
            c.y = Mathf.Clamp(c.y, _panMinXY.y, _panMaxXY.y);
            return c;
        }

        private void ApplyToCamera()
        {
            // _curXY = (X mundo, Z mundo). Câmera reto de cima, altura fixa.
            _cam.orthographicSize = _curSize * (1f + _zoomPunch);
            // _curXY é o ponto do CHÃO no centro da tela; a câmera recua ao longo da visão.
            _cam.transform.position = new Vector3(_curXY.x, 0f, _curXY.y)
                                      - (World3D.CamRotation * Vector3.forward) * CAM_HEIGHT;

            // Roll em torno do próprio forward: gira a imagem na tela sem mudar o
            // ângulo de visão com o chão, então NÃO reintroduz a dupla distorção
            // isométrica (que vinha de inclinar a câmera, não de rolá-la).
            var baseRot = World3D.CamRotation;
            _cam.transform.rotation = Mathf.Abs(_rollDeg) < 0.01f
                ? baseRot
                : Quaternion.AngleAxis(_rollDeg, baseRot * Vector3.forward) * baseRot;
        }
    }
}
