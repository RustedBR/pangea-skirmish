using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace PangeaSkirmish
{
    public enum CameraMode { Auto, Manual }

    /// <summary>
    /// Câmera ortográfica 2D pura. Modo Auto segue ações de batalha automaticamente.
    /// Quando o jogador interage (arrastar/zoom/edge-pan), entra em Manual por 2s,
    /// depois volta para Auto. FocusOn só funciona em modo Auto.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        private Camera _cam;

        private Vector2 _targetXY;
        private float   _targetSize;
        private Vector2 _curXY;
        private float   _curSize;
        private const float CamZ = -10f;

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

        // Shake state
        private float   _shakeDuration;
        private float   _shakeMagnitude;
        private float   _shakeTimer;
        private Vector2 _shakeOffset;

        public Camera Cam => _cam;

        // ── Setup ──

        public void Configure(Camera cam, Vector3 initialCenter, float initialSize)
        {
            _cam = cam;
            _cam.orthographic = true;
            _cam.transform.rotation = Quaternion.identity;
            _targetXY = _curXY = new Vector2(initialCenter.x, initialCenter.y);
            _targetSize = _curSize = initialSize;

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
            _targetXY = ClampCenter(new Vector2(center.x, center.y));
            if (size > 0f) _targetSize = size;
        }

        /// <summary>Foca em uma região do mundo, ajustando zoom para caber tudo. Só funciona em modo Auto.</summary>
        public void FocusOnArea(Vector3 center, float requiredSize)
        {
            if (_mode == CameraMode.Manual) return;
            _targetXY = ClampCenter(new Vector2(center.x, center.y));
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
            _targetXY = _curXY = ClampCenter(new Vector2(center.x, center.y));
            _targetSize = _curSize = size;
            ApplyToCamera();
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

            bool hadManualInput = HandleManualPan() || HandleZoom();

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

        private bool HandleZoom()
        {
            if (Mouse.current == null) return false;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

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

            // Arraste com botão direito
            if (enableDragPan && Mouse.current.rightButton.isPressed)
            {
                Vector2 d = Mouse.current.delta.ReadValue();
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
                        _targetXY = ClampCenter(_targetXY + dir.normalized * speed * Time.deltaTime);
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
            _cam.transform.position = new Vector3(_curXY.x + _shakeOffset.x, _curXY.y + _shakeOffset.y, CamZ);
            _cam.transform.rotation = Quaternion.identity;
            _cam.orthographicSize = _curSize;
        }
    }
}
