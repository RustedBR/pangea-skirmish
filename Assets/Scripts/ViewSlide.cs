using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Deslize visual em ARCO usado na rotação de vista (Q/E).
    ///
    /// Por que existe: a rotação é uma reindexação lógica — o tabuleiro é uma
    /// permutação de si mesmo. Com a câmera ancorada e o chão de grama uniforme, o
    /// resultado de girar 90° é uma imagem quase idêntica, e o jogador não percebe
    /// que o mundo virou ("o mapa parece totalmente estático"). Este componente
    /// devolve a sensação de giro fazendo os objetos ESPARSOS (unidades, ghosts de
    /// planejamento, marcadores, overlays de efeito) percorrerem o caminho curvo
    /// entre o slot antigo e o novo, em vez de aparecerem já no destino.
    ///
    /// Só mexe em APARÊNCIA: a posição lógica já é a final desde o primeiro frame.
    /// O deslocamento é aplicado como OFFSET no LateUpdate — depois de qualquer
    /// coroutine de movimento ter escrito a posição — então uma unidade que esteja
    /// andando durante o giro continua andando, com o arco somado por cima.
    /// </summary>
    [DisallowMultipleComponent]
    public class ViewSlide : MonoBehaviour
    {
        // Todos os deslizes em curso. Existe para que um giro novo possa devolver
        // os objetos às posições REAIS antes de medir de onde eles partem — sem
        // isso, girar rápido fazia cada arco partir de uma posição que já continha
        // o offset visual do arco anterior, e o erro se somava a cada Q/E.
        private static readonly List<ViewSlide> _ativos = new List<ViewSlide>();

        private Vector3 _from;      // posição física no slot ANTIGO
        private Vector3 _to;        // posição física no slot NOVO
        private Vector3 _pivot;     // centro do arco (o ponto que fica parado na tela)
        private Vector3 _applied;   // offset escrito no frame anterior (para desfazer)
        private float _elapsed;
        private float _duration;
        private float _delay;       // espera antes de começar (onda escalonada)

        /// <summary>
        /// Remove o deslocamento visual de todos os deslizes em curso, deixando cada
        /// objeto na posição lógica verdadeira. Chamar ANTES de medir posições para
        /// um giro novo.
        /// </summary>
        public static void ClearAll()
        {
            for (int i = _ativos.Count - 1; i >= 0; i--)
            {
                var s = _ativos[i];
                if (s == null) { _ativos.RemoveAt(i); continue; }
                if (s._applied != Vector3.zero)
                {
                    s.transform.position -= s._applied;
                    s._applied = Vector3.zero;
                }
                Destroy(s);
            }
            _ativos.Clear();
        }

        /// <summary>
        /// Faz o objeto percorrer o arco de <paramref name="from"/> até a posição em
        /// que ele já está, girando em torno de <paramref name="pivot"/>.
        /// Chamar de novo num objeto que já está deslizando reinicia o trajeto.
        /// </summary>
        public static void Play(GameObject go, Vector3 from, Vector3 to, Vector3 pivot,
                               float duration, float delay = 0f)
        {
            if (go == null || duration <= 0f) return;
            // Deslocamento irrelevante (ex.: objeto praticamente no pivô) não merece efeito.
            if ((from - to).sqrMagnitude < 0.0001f) return;

            var s = go.GetComponent<ViewSlide>();
            if (s == null)
            {
                s = go.AddComponent<ViewSlide>();
                _ativos.Add(s);
            }
            else
            {
                // Girou de novo antes do arco anterior terminar: limpa o offset
                // pendente para não somar dois deslocamentos.
                s.transform.position -= s._applied;
                s._applied = Vector3.zero;
            }
            s._from = from;
            s._to = to;
            s._pivot = pivot;
            s._duration = duration;
            s._delay = Mathf.Max(0f, delay);
            s._elapsed = 0f;
        }

        private void LateUpdate()
        {
            // Desfaz o offset do frame anterior ANTES de calcular o novo. Sem isto o
            // deslocamento se acumularia a cada frame (a posição já carrega o offset
            // antigo) e o objeto sairia voando. Desfazer e reaplicar também deixa a
            // posição lógica intacta: quem a escreveu neste frame (uma coroutine de
            // movimento, por exemplo) continua mandando nela.
            transform.position -= _applied;

            _elapsed += Time.deltaTime;
            // Antes da vez deste objeto na varredura, ele fica PARADO no ponto de
            // partida — é isso que faz a onda ser lida como algo que passa por cima
            // do tabuleiro, e não como todos saindo ao mesmo tempo.
            float k = _elapsed <= _delay
                ? 0f
                : Mathf.Clamp01((_elapsed - _delay) / _duration);
            // desacelera no fim — o giro "assenta" em vez de parar seco
            float e = 1f - (1f - k) * (1f - k);

            _applied = k >= 1f ? Vector3.zero : ArcPoint(e) - _to;
            transform.position += _applied;

            if (k >= 1f) Destroy(this);
        }

        // Se o componente morrer no meio (objeto destruído, nova rotação), não deixa
        // resíduo de deslocamento na posição.
        private void OnDisable()
        {
            _ativos.Remove(this);
            if (_applied == Vector3.zero) return;
            transform.position -= _applied;
            _applied = Vector3.zero;
        }

        /// <summary>
        /// Ponto do arco em e ∈ [0,1]: ângulo e raio em torno do pivô interpolados
        /// separadamente, no plano do chão (XZ). Interpolar em polar — e não em
        /// linha reta — é o que faz o olho ler "girou" em vez de "escorregou".
        /// </summary>
        private Vector3 ArcPoint(float e)
        {
            Vector2 p = new Vector2(_pivot.x, _pivot.z);
            Vector2 a = new Vector2(_from.x, _from.z) - p;
            Vector2 b = new Vector2(_to.x, _to.z) - p;

            float angA = Mathf.Atan2(a.y, a.x) * Mathf.Rad2Deg;
            float angB = Mathf.Atan2(b.y, b.x) * Mathf.Rad2Deg;
            // caminho curto: girar 90° para o lado certo, nunca 270° para o outro
            float ang = (angA + Mathf.DeltaAngle(angA, angB) * e) * Mathf.Deg2Rad;
            float r = Mathf.Lerp(a.magnitude, b.magnitude, e);

            return new Vector3(
                p.x + Mathf.Cos(ang) * r,
                Mathf.Lerp(_from.y, _to.y, e),
                p.y + Mathf.Sin(ang) * r);
        }
    }
}
