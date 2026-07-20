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
        // Fix (2026-07-20): câmera acima do chão, olhando reto para baixo
        // (ver Configure — reto de cima, não mais inclinada). Em ortográfica a
        // altura exata não importa para a projeção, só precisa ficar acima do
        // chão com clip planes normais (não mais negativo).
        private const float CAM_HEIGHT = 20f;

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
            _cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
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

        /// <summary>Vira a vista em um passo de 90° (clockwise=true → +90/Leste,
        /// false → -90/Oeste). Reindexação lógica instantânea no GridManager —
        /// não gira mais nenhum transform físico (nem grid, nem câmera). Re-deriva
        /// o facing de todas as unidades para encarar o novo norte.</summary>
        public void CycleView(bool clockwise)
        {
            if (!enableViewRotate) return;
            _orientation = ((_orientation + (clockwise ? 90 : -90)) % 360 + 360) % 360;
            if (GridManager.Instance != null)
                GridManager.Instance.SetViewOrientation(_orientation);
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
                GridManager.Instance.SetViewOrientation(_orientation);
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
            // _curXY = (X mundo, Z mundo). Câmera reto de cima, altura fixa.
            _cam.orthographicSize = _curSize;
            _cam.transform.position = new Vector3(_curXY.x, CAM_HEIGHT, _curXY.y);
        }
    }
}
