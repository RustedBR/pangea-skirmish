using UnityEngine;
using UnityEngine.InputSystem;

namespace PangeaSkirmish
{
    /// <summary>
    /// Input de ponteiro unificado para mouse e toque (GDD-022).
    /// No toque: 1º toque só aponta (prévia); 2º toque no mesmo ponto vira clique.
    /// Arrastar com 1 dedo move a câmera; pinça com 2 dedos dá zoom.
    /// Lido uma vez por frame (lazy), de qualquer script.
    /// </summary>
    public static class PointerInput
    {
        /// <summary>Raio (px de tela) em que o 2º toque conta como "mesmo ponto".</summary>
        public const float ConfirmRadiusBase = 40f;
        /// <summary>Raio do 2º toque, escalado pela tela: celular tem resolução real alta (2026-11-14).</summary>
        public static float ConfirmRadius => Mathf.Max(ConfirmRadiusBase, ScreenScale() * 0.06f);
        /// <summary>Tempo máximo entre o 1º e o 2º toque.</summary>
        public const float ConfirmWindow = 3f;
        /// <summary>Movimento a partir do qual um toque vira arrasto (px).</summary>
        public const float DragThresholdBase = 14f;
        /// <summary>Quanto o dedo anda até virar arrasto. Fixo em 14 px, no celular (~2400 px) qualquer tremida virava arrasto.</summary>
        public static float DragThreshold => Mathf.Max(DragThresholdBase, ScreenScale() * 0.025f);

        /// <summary>Menor lado da tela em px (base das tolerâncias de toque).</summary>
        private static float ScreenScale() => Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height));

        private static int _frame = -1;
        private static Vector2 _pos;
        private static bool _click, _available, _back;
        private static Vector2 _dragDelta;
        private static float _pinchDelta;

        // Estado do toque
        private static bool _touchDown, _dragging, _multi;
        private static Vector2 _downPos;
        private static float _lastPinchDist = -1f;
        private static Vector2 _lastMid, _lastDragPos, _lastUpPos;
        private static Vector2 _lastTapPos;
        private static float _lastTapTime = -99f;
        private static float _lastTouchTime = -99f;
        private static bool _backRequested;
        // Acumulado entre leituras: o toque é amostrado a cada update do Input System,
        // para não perder um toque rápido que encosta e solta dentro do mesmo frame.
        private static bool _accClick, _accActive, _hooked;
        private static Vector2 _accDrag;
        private static float _accPinch;

        private static void EnsureHooked()
        {
            if (_hooked) return;
            _hooked = true;
            InputSystem.onAfterUpdate += () => TickTouch();
        }

        /// <summary>True quando o último input foi toque (ou o aparelho é celular).</summary>
        public static bool IsTouch { get; private set; }
        public static bool IsMobile => Application.isMobilePlatform;

        public static bool Available { get { Tick(); return _available; } }
        public static Vector2 Position { get { Tick(); return _pos; } }
        /// <summary>Clique esquerdo / 2º toque confirmando.</summary>
        public static bool ClickThisFrame { get { Tick(); return _click; } }
        /// <summary>Arrasto de câmera do frame, em px (botão direito ou 1–2 dedos).</summary>
        public static Vector2 DragDelta { get { Tick(); return _dragDelta; } }
        public static bool Dragging { get { Tick(); return _dragDelta != Vector2.zero; } }
        /// <summary>Variação da pinça em px (positivo = abrindo os dedos = aproximar).</summary>
        public static float PinchDelta { get { Tick(); return _pinchDelta; } }
        /// <summary>Esc ou botão "Voltar" da HUD.</summary>
        public static bool BackThisFrame { get { Tick(); return _back; } }

        /// <summary>Chamado pelo botão "Voltar" da HUD de toque.</summary>
        public static void RequestBack() => _backRequested = true;

        /// <summary>Esquece o 1º toque (ex.: depois de mudar de modo).</summary>
        public static void ResetPendingTap()
        {
            _lastTapTime = -99f; _touchDown = false; _dragging = false; _multi = false; _lastPinchDist = -1f;
            _accClick = false; _accActive = false; _accDrag = Vector2.zero; _accPinch = 0f;
            _hooked = false;   // testes trocam a instância do Input System
        }

        private static void Tick()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _click = false; _dragDelta = Vector2.zero; _pinchDelta = 0f;

            var kb = Keyboard.current;
            _back = _backRequested || (kb != null && kb.escapeKey.wasPressedThisFrame);
            _backRequested = false;

            EnsureHooked();
            TickTouch();   // garante uma amostra mesmo sem onAfterUpdate (ex.: testes)
            if (_accActive || _accClick)
            {
                _available = true;
                _click = _accClick; _dragDelta = _accDrag; _pinchDelta = _accPinch;
                _accClick = false; _accDrag = Vector2.zero; _accPinch = 0f; _accActive = false;
                return;
            }
            if (IsTouch && Touchscreen.current != null && !_touchDown && Time.unscaledTime - _lastTouchTime < 0.6f)
            { _available = true; return; }

            var mouse = Mouse.current;
            // Navegadores de celular emulam mouse depois do toque: ignora por um instante.
            bool mouseMuted = Time.unscaledTime - _lastTouchTime < 0.6f;
            if (mouse != null && !mouseMuted)
            {
                _available = true;
                Vector2 mp = mouse.position.ReadValue();
                if (mouse.delta.ReadValue() != Vector2.zero || mouse.leftButton.wasPressedThisFrame) IsTouch = false;
                if (!IsTouch) _pos = mp;
                _click = !IsTouch && mouse.leftButton.wasPressedThisFrame;
                if (!IsTouch && mouse.rightButton.isPressed) _dragDelta = mouse.delta.ReadValue();
                return;
            }
            _available = IsTouch || mouse != null;
        }

        /// <summary>Processa o toque. Devolve true se houve atividade de toque neste frame.
        /// Detecta encostar/soltar comparando com o frame anterior (não usa wasReleasedThisFrame,
        /// que some se o Input System atualizar mais de uma vez no mesmo frame).</summary>
        private static bool TickTouch()
        {
            var ts = Touchscreen.current;
            if (ts == null) return false;

            int count = 0;
            Vector2 p0 = default, p1 = default;
            foreach (var t in ts.touches)
            {
                if (!t.press.isPressed) continue;
                if (count == 0) p0 = t.position.ReadValue();
                else if (count == 1) p1 = t.position.ReadValue();
                count++;
            }
            bool wasDown = _touchDown;
            if (count == 0 && !wasDown) return IsTouch;   // sem dedo: mantém a última posição

            IsTouch = true;
            _lastTouchTime = Time.unscaledTime;
            _accActive = true;

            if (count >= 2)
            {
                if (!wasDown) { _touchDown = true; _dragging = false; _downPos = p0; }
                _multi = true;
                float dist = Vector2.Distance(p0, p1);
                Vector2 mid = (p0 + p1) * 0.5f;
                if (_lastPinchDist > 0f) { _accPinch += dist - _lastPinchDist; _accDrag += mid - _lastMid; }
                _lastPinchDist = dist; _lastMid = mid;
                return true;
            }
            _lastPinchDist = -1f;

            if (count == 1)
            {
                Vector2 pos = p0;
                if (!wasDown) { _touchDown = true; _dragging = false; _multi = false; _downPos = pos; _lastDragPos = pos; }
                if (!_dragging && !_multi && Vector2.Distance(pos, _downPos) > DragThreshold) _dragging = true;
                if (_dragging && !_multi) _accDrag += pos - _lastDragPos;
                else if (!_dragging && !_multi) _pos = pos;   // segurando parado: aponta
                _lastDragPos = pos;
                _lastUpPos = pos;
                return true;
            }

            // count == 0 && wasDown → o dedo saiu agora.
            _touchDown = false;
            if (!_dragging && !_multi)
            {
                Vector2 pos = _lastUpPos;
                _pos = pos;
                float now = Time.unscaledTime;
                if (now - _lastTapTime <= ConfirmWindow && Vector2.Distance(pos, _lastTapPos) <= ConfirmRadius)
                {
                    _accClick = true;
                    _lastTapTime = -99f;
                }
                else
                {
                    _lastTapPos = pos;
                    _lastTapTime = now;
                }
            }
            _dragging = false; _multi = false;
            return true;
        }
    }
}
