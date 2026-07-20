using System;
using System.Collections;
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
        // Migração XY→XZ (2026-07-20): altura da câmera. Pedido do Marcus (2026-07-20):
        // "coloque o cam height em 0" → câmera no NÍVEL do chão (y=0). Em câmera
        // ORTogrÁFICA a posição ao longo do eixo de visão não muda a projeção — só
        // importa rotação + ortho size. Pra não cortar o chão (near plane), os clip
        // planes são abertos em Configure (near negativo). Veja comentário lá.
        private const float CAM_HEIGHT = 0f;

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
            // Migração XY→XZ (2026-07-20): câmera no nível do chão (CAM_HEIGHT=0).
            // Em ortográfica a posição não afeta a projeção, mas o near/far clip precisam
            // ser abertos (near NEGATIVO) senão o chão em y=0 é cortado pela câmera que
            // está no mesmo plano. Setup padrão de câmera isométrica.
            _cam.nearClipPlane = -100f;
            _cam.farClipPlane  =  100f;

            // Migração XY→XZ (2026-07-20) COMPLETA: câmera isométrica PARADA e
            // INCLINADA (50° tilt X, Y=0) olhando o chão XZ. O _gridRig gira
            // em Y do mundo (eixo do chão). Sprites usam BillboardFace Y-only p/ ficar
            // em pé (não tortos). ScreenToGround (raycast plano y=0) funciona sob tilt.
            // _curXY agora mapeia (X mundo, Z mundo); a câmera fica numa altura fixa.
            // NOTA (2026-07-20, teste Marcus): Y=0 (sem yaw). O grid já é losango no
            // XZ; um yaw de 45° no Y transformava o losango em quadrado/shearado na
            // tela. X=50° (testado pelo Marcus) dá o ângulo de visão 2.5D desejado.
            _cam.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            _cam.transform.position = new Vector3(initialCenter.x, CAM_HEIGHT, initialCenter.z);

            _targetXY = _curXY = new Vector2(initialCenter.x, initialCenter.z);
            _targetSize = _curSize = initialSize;
            _orientation = 0;

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

        /// <summary>Vira a vista em um passo de 90° (clockwise=true → +90, false → -90).
        /// Caminho 3 (2026-07-14): a rotação é aplicada no GRID (GridManager._gridRig,
        /// eixo Z do mundo), NÃO na câmera. A câmera fica parada → plano não sobe.
        /// Re-deriva o facing de todas as unidades para encarar o novo norte.</summary>
        public void CycleView(bool clockwise)
        {
            if (!enableViewRotate) return;
            // Gira o GRID (snap 90°, lerp interno no GridManager).
            if (GridManager.Instance != null)
                GridManager.Instance.SetGridRotation(clockwise);
            // Estado lógico p/ o facing das unidades (4 estados: 0/90/180/270).
            _orientation = ((_orientation + (clockwise ? 90 : -90)) % 360 + 360) % 360;
            UnitRegistry.ApplyViewOrientation(_orientation);
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
            _orientation = ((degrees % 360) + 360) % 360;
            if (GridManager.Instance != null)
                GridManager.Instance.SetGridRotation(_orientation > 0 ? true : false); // aproximação p/ snap absoluto
            UnitRegistry.ApplyViewOrientation(_orientation);
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

        private void LateUpdate()
        {
            if (_cam == null) return;

            bool hadManualInput = HandleManualPan() || HandleZoom() || HandleViewRotate();

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
            bool q = Keyboard.current.qKey.wasPressedThisFrame;
            bool e = Keyboard.current.eKey.wasPressedThisFrame;
            if (!q && !e) return false;
            CycleView(e);   // E = +90 (horário), Q = -90 (anti-horário)
            return true;
        }

        private bool HandleZoom()
        {
            if (Mouse.current == null) return false;
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
            if (Mouse.current == null) return false;

            float worldPerPixel = (_curSize * 2f) / Mathf.Max(1, Screen.height);

            // Arraste com botão direito (migração XY→XZ: _curXY = (X mundo, Z mundo))
            if (enableDragPan && Mouse.current.rightButton.isPressed)
            {
                Vector2 d = Mouse.current.delta.ReadValue();
                // d.x → X mundo, d.y → Z mundo (mundo XZ sob câmera 45°).
                _targetXY = ClampCenter(_targetXY + new Vector2(-d.x, -d.y) * worldPerPixel * dragSpeed);
                return true;
            }

            // Edge-pan
            if (enableEdgePan && Application.isFocused)
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
                        _targetXY = ClampCenter(_targetXY + new Vector2(dir.x, dir.y).normalized * speed * Time.deltaTime);
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
            // Migração XY→XZ (2026-07-20): _curXY = (X mundo, Z mundo). A câmera fica
            // em altura fixa (CAM_HEIGHT) e olha o chão XZ inclinada. Só ortho size muda.
            _cam.orthographicSize = _curSize;
            _cam.transform.position = new Vector3(_curXY.x, CAM_HEIGHT, _curXY.y);
        }
    }
}
