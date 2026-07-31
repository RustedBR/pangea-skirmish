using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Varredura ANGULAR do terreno na rotação de vista (Q/E) — a "vassoura" que
    /// gira em torno do pivô acendendo o chão por onde passa.
    ///
    /// Por que não animar a POSIÇÃO dos tiles: os sprites do TinyTactics usam
    /// SpriteMeshType.Tight, ou seja, o mesh É o contorno do losango. Losango só
    /// encaixa com losango nos 4 ângulos exatos; qualquer posição intermediária
    /// abre fresta e produz moiré — foi exatamente o que quebrou o jogo em 20/07.
    ///
    /// O que este efeito toca é seguro por construção:
    ///   • COR do sprite — não tem relação nenhuma com geometria;
    ///   • ESCALA levemente PARA CIMA — losangos maiores se SOBREPÕEM em vez de
    ///     abrir buraco (encolher, esse sim, abriria). Os highlights de alcance são
    ///     filhos do tile, então acompanham a escala de graça.
    ///
    /// A fase de cada célula é o ÂNGULO dela em torno do pivô: a onda percorre os
    /// ângulos no sentido do giro, que é o que o olho lê como rotação — uma onda
    /// radial diria só "algo aconteceu", não "o mundo virou".
    /// </summary>
    public static class TileSweep
    {
        // Snapshot da varredura em curso. Precisa ser acessível de fora porque a
        // coroutine pode ser interrompida no meio (jogador girando rápido): sem
        // restaurar antes de recomeçar, a varredura nova capturaria a cor JÁ
        // clareada como se fosse a original e o brilho se acumularia a cada Q/E.
        private static SpriteRenderer[] _ativos;
        private static Color[] _cor0;
        private static Vector3[] _escala0;

        /// <summary>
        /// Devolve os tiles ao estado anterior à varredura em curso, se houver.
        /// Chamado no início de cada nova varredura.
        /// </summary>
        public static void RestoreNow()
        {
            if (_ativos == null) return;
            for (int i = 0; i < _ativos.Length; i++)
            {
                var sr = _ativos[i];
                if (sr == null) continue;
                sr.color = _cor0[i];
                sr.transform.localScale = _escala0[i];
            }
            _ativos = null; _cor0 = null; _escala0 = null;
        }

        /// <summary>
        /// Fase da célula na varredura: 0 = a vassoura passa primeiro, 1 = por último.
        /// Compartilhada com os objetos esparsos (ViewSlide) para que chão e objetos
        /// façam parte da MESMA onda.
        /// </summary>
        public static float PhaseAt(Vector3 worldPos, Vector3 pivot, bool clockwise)
        {
            float ang = Mathf.Atan2(worldPos.z - pivot.z, worldPos.x - pivot.x) * Mathf.Rad2Deg;
            // sentido do giro decide de que lado a vassoura entra
            if (clockwise) ang = -ang;
            return Mathf.Repeat(ang, 360f) / 360f;
        }

        /// <summary>
        /// Pulso que cada célula sofre quando a frente de onda passa por ela.
        /// t é o progresso global [0,1]; phase é a posição da célula na varredura.
        /// </summary>
        private static float Pulse(float t, float phase, float width)
        {
            // distância (circular) entre a frente de onda e esta célula
            float d = Mathf.Abs(Mathf.DeltaAngle(phase * 360f, t * 360f)) / 360f;
            if (d > width) return 0f;
            // meio-cosseno: sobe e desce suave, sem canto
            return 0.5f * (1f + Mathf.Cos(d / width * Mathf.PI));
        }

        /// <summary>
        /// Peso da vinheta pela distância ao pivô: 0 no miolo, subindo suave até 1 na
        /// borda do tabuleiro.
        ///
        /// Substituiu a sombra que VIAJAVA pelo chão. O problema daquela era
        /// semântico: uma sombra em movimento sem nada projetando ela faz o olho
        /// procurar a fonte e não achar — foi o "parece estranho" relatado. A
        /// vinheta é lida como FOCO, não como sombra de alguém, e reforça o que a
        /// ancoragem já faz: manter parado no meio da tela o ponto que você observa.
        /// </summary>
        private static float Vignette(float distNorm)
        {
            const float Miolo = 0.35f;   // fração central que fica intacta
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Miolo, 1f, distNorm));
        }

        /// <summary>
        /// Distância do tile ao centro DA TELA, onde 1 = borda da viewport.
        ///
        /// Fix (2026-07-30): a primeira versão normalizava pelo raio do TABULEIRO.
        /// Como no zoom de combate a tela mostra só o miolo do mapa (≈5–8 unidades
        /// de um raio de 19), tudo o que aparecia caía dentro da faixa isenta e o
        /// escurecimento no topo da tela ficava em 0–1% — imperceptível, que foi o
        /// relato. Ancorado na viewport, a vinheta vale o mesmo em qualquer zoom.
        /// </summary>
        private static float ScreenDist(Camera cam, Vector3 worldPos)
        {
            var vp = cam.WorldToViewportPoint(worldPos);
            float dx = (vp.x - 0.5f) * 2f;   // -1 na esquerda, +1 na direita
            float dy = (vp.y - 0.5f) * 2f;   // -1 embaixo, +1 em cima
            return Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
        }

        /// <summary>
        /// Roda a varredura sobre os tiles do grid. Restaura cor e escala originais
        /// ao final — inclusive se for interrompida, porque o estado é reaplicado a
        /// partir dos valores capturados no início.
        /// </summary>
        public static IEnumerator Run(GridManager grid, Camera cam, Vector3 pivot, bool clockwise,
                                      float duration, float brightness, float scalePunch,
                                      float uniforme)
        {
            if (grid == null || cam == null || duration <= 0f) yield break;

            // Uma varredura anterior interrompida deixaria cor/escala alteradas.
            RestoreNow();

            var tiles = new List<SpriteRenderer>();
            var cores = new List<Color>();
            var escalas = new List<Vector3>();
            var fases = new List<float>();
            var dists = new List<float>();   // distância ao pivô, para a vinheta

            for (int x = 0; x < grid.width; x++)
            for (int y = 0; y < grid.height; y++)
            {
                var sr = grid.TileRendererAt(x, y);
                if (sr == null) continue;
                tiles.Add(sr);
                cores.Add(sr.color);
                escalas.Add(sr.transform.localScale);
                fases.Add(PhaseAt(sr.transform.position, pivot, clockwise));
                dists.Add(ScreenDist(cam, sr.transform.position));
            }
            if (tiles.Count == 0) yield break;

            _ativos = tiles.ToArray();
            _cor0 = cores.ToArray();
            _escala0 = escalas.ToArray();
            var fase = fases.ToArray();

            // dists já vem em coordenada de TELA (1 = borda da viewport), então a
            // vinheta independe do zoom e do tamanho do mapa.
            var vinheta = new float[dists.Count];
            for (int i = 0; i < dists.Count; i++) vinheta[i] = Vignette(dists[i]);

            const float Largura = 0.35f;   // fração do círculo que a frente de onda cobre
            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                float tc = Mathf.Clamp01(t);

                // Batimento: fecha e abre uma vez durante o giro. Elevado a 0.6 para
                // ALARGAR o topo — um sin() puro cruza o pico rápido demais e, numa
                // janela de ~0.3s, o olho não chega a registrar.
                float respiro = Mathf.Pow(Mathf.Sin(tc * Mathf.PI), 0.6f);

                for (int i = 0; i < _ativos.Length; i++)
                {
                    var sr = _ativos[i];
                    if (sr == null) continue;

                    var c = _cor0[i];
                    // Vinheta PURA só escurece a periferia — exatamente onde o jogador
                    // NÃO está olhando (ele acompanha a própria unidade, no meio da
                    // tela), e por isso passava despercebida. A parcela `uniforme`
                    // atinge a tela inteira, inclusive o centro; o restante continua
                    // concentrado nas bordas, preservando a leitura de foco.
                    float perfil = uniforme + (1f - uniforme) * vinheta[i];
                    float escurece = brightness * perfil * respiro;
                    if (escurece > 0.001f)
                    {
                        float f = 1f - escurece;
                        sr.color = new Color(c.r * f, c.g * f, c.b * f, c.a);
                    }
                    else sr.color = c;

                    float p = Pulse(tc, fase[i], Largura);
                    sr.transform.localScale = p > 0f
                        ? _escala0[i] * (1f + scalePunch * p)
                        : _escala0[i];
                }
                yield return null;
            }

            RestoreNow();
        }
    }
}
