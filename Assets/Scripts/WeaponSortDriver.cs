using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Faz a ponte entre os clips de animação e o sortingOrder da arma (que não é animável
    /// diretamente). Os clips do Weapon Anim Editor animam sortOffset por frame:
    /// >= 0 → arma NA FRENTE do personagem (+1); < 0 → ATRÁS (-1).
    /// baseOrder é o sortingOrder do corpo, setado pelo WeaponOverlay a cada ApplySorting.
    /// </summary>
    public class WeaponSortDriver : MonoBehaviour
    {
        [Tooltip("Animado pelos clips: >= 0 = arma na frente do personagem, < 0 = atrás.")]
        public float sortOffset = 1f;

        [Tooltip("Metadata do editor: 1.0 = frame ativo, 0.0 = frame skipado. Não usado em runtime.")]
        public float _activeFrame = 1f;

        [System.NonSerialized] public int baseOrder;

        private SpriteRenderer _sr;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
        }

        private void LateUpdate()
        {
            if (_sr != null)
                _sr.sortingOrder = baseOrder + (sortOffset >= 0f ? 1 : -1);
        }
    }
}
