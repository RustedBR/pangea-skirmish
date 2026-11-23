using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish.UI
{
    /// <summary>
    /// Fade dinâmico da HUD (2026-11-23). Quando a câmera se mexe, só os painéis que NÃO estão em uso ficam
    /// translúcidos (nunca somem, nunca saem do layout); o que o jogador está usando continua inteiro.
    /// Tudo volta ao normal assim que ele começa a jogar de novo (clique ou tecla de jogo) ou a câmera para.
    ///
    /// "Em uso" é decidido por painel: o mouse está em cima, ou o jogador está planejando, ou acabou de
    /// inspecionar uma unidade (predicado de cada item).
    /// </summary>
    public class HudAutoFade
    {
        /// <summary>Quanto tempo depois do último movimento de câmera ainda conta como "mexendo".</summary>
        public const float CameraHold = 0.7f;
        /// <summary>Velocidades: sai devagar (para não piscar), volta rápido (para não atrapalhar).</summary>
        public const float FadeOutSeconds = 0.35f, FadeInSeconds = 0.12f;

        private class Item
        {
            public VisualElement El;
            public Func<bool> InUse;
            public float Dim;          // opacidade quando a câmera mexe e o painel não está em uso
            public float Current = 1f;
            public bool Hover;
        }

        private readonly List<Item> _items = new List<Item>();
        private float _lastInteraction = -99f;

        /// <summary>Opacidade-alvo no último quadro, por painel (testes).</summary>
        public float OpacityOf(VisualElement el)
        {
            foreach (var i in _items) if (i.El == el) return i.Current;
            return 1f;
        }

        /// <summary>Painel controlado. inUse null = só o mouse em cima conta como uso.</summary>
        public void Add(VisualElement el, Func<bool> inUse, float dimOpacity)
        {
            if (el == null) return;
            foreach (var i in _items) if (i.El == el) return;
            var item = new Item { El = el, InUse = inUse, Dim = Mathf.Clamp01(dimOpacity) };
            el.RegisterCallback<PointerEnterEvent>(_ => item.Hover = true);
            el.RegisterCallback<PointerLeaveEvent>(_ => item.Hover = false);
            _items.Add(item);
        }

        /// <summary>O jogador começou a jogar (clique, ação, unidade inspecionada): tudo volta já.</summary>
        public void NotifyInteraction() => _lastInteraction = Time.unscaledTime;

        /// <summary>Chamar uma vez por quadro. disabled = sem fade nenhum (tutorial).</summary>
        public void Update(bool disabled)
        {
            float now = Time.unscaledTime;
            float lastMove = CameraController.LastManualMoveTime;
            // Só escurece se a câmera mexeu E o jogador não agiu depois do movimento.
            bool dimming = !disabled && now - lastMove < CameraHold && lastMove > _lastInteraction;
            float dt = Time.unscaledDeltaTime;

            foreach (var i in _items)
            {
                if (i.El == null) continue;
                bool inUse = i.Hover || (i.InUse != null && i.InUse());
                float target = dimming && !inUse ? i.Dim : 1f;
                float speed = (1f - i.Dim) / (target < i.Current ? FadeOutSeconds : FadeInSeconds);
                i.Current = Mathf.MoveTowards(i.Current, target, speed * dt);
                i.El.style.opacity = i.Current;
            }
        }
    }
}
