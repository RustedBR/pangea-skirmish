using System.Collections;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Tag world-space que mostra a disputa de iniciativa acima de uma unidade.
    /// Fase 1: decomposição colorida (d20 + AGI + DEX = total) dentro de um retângulo.
    /// Fase 2: colapsa só o resultado final (vencedor verde, perdedores cinza).
    /// Renderiza no mundo (TextMesh + SpriteRenderer), encarando a câmera — igual FloatingLabel.
    /// </summary>
    public class InitiativeTag : MonoBehaviour
    {
        private Camera _cam;
        private TextMesh _text;

        public static InitiativeTag Create(Camera cam, Unit unit, int d20, int agiPart, int dexPart,
                                           int total, bool isWinner, float hideTime)
        {
            var T = Tuning.Get();
            var go = new GameObject("InitiativeTag");
            go.transform.position = unit.HeadWorld + Vector3.up * T.initiativeTagHeightOffset;
            var tag = go.AddComponent<InitiativeTag>();
            tag._cam = cam;

            // Fundo (retângulo) — filho atrás do texto
            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(go.transform, false);
            var bg = bgGo.AddComponent<SpriteRenderer>();
            bg.sprite = BgSprite();
            bg.sortingOrder = 20000;
            bgGo.transform.localScale = new Vector3(T.initiativeTagBgScale.x, T.initiativeTagBgScale.y, 1f);

            // Texto (TextMesh world-space) — PRECISA de font + sharedMaterial, senão não renderiza.
            string cD20 = ColorUtility.ToHtmlStringRGB(T.initiativeD20Color);
            string cAgi = ColorUtility.ToHtmlStringRGB(T.initiativeAgiColor);
            string cDex = ColorUtility.ToHtmlStringRGB(T.initiativeDexColor);
            var tm = go.AddComponent<TextMesh>();
            tm.text = $"<color=#{cD20}>{d20}</color> <color=#{cAgi}>+{agiPart}</color> " +
                      $"<color=#{cDex}>+{dexPart}</color> = <color=#ffffff>{total}</color>";
            tm.fontSize = T.initiativeTagFontSize;
            tm.characterSize = T.battleLabelCharacterSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.sortingOrder = 20001;
            tag._text = tm;

            tag.FaceCamera();
            return tag;
        }

        private static Sprite _bgSprite;
        private static Sprite BgSprite()
        {
            if (_bgSprite != null) return _bgSprite;
            const int W = 64, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            Color bg     = Tuning.Get().initiativeTagBgColor;
            Color border = Tuning.Get().initiativeTagBorderColor;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool edge = x == 0 || x == W - 1 || y == 0 || y == H - 1;
                tex.SetPixel(x, y, edge ? border : bg);
            }
            tex.Apply();
            _bgSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 32);
            return _bgSprite;
        }

        private void LateUpdate() => FaceCamera();

        private void FaceCamera()
        {
            if (_cam != null) transform.rotation = _cam.transform.rotation;
        }

        /// <summary>Fase 2: colapsa só o resultado final acima da unidade e some após `duration`.</summary>
        public IEnumerator ShowPhase2(int total, bool isWinner, float duration)
        {
            if (_text != null)
            {
                var T = Tuning.Get();
                string c = ColorUtility.ToHtmlStringRGB(isWinner ? T.initiativeWinnerColor : T.initiativeLoserColor);
                _text.text = $"<color=#{c}><size={T.initiativePhase2FontSize}>{total}</size></color>";
            }
            yield return new WaitForSeconds(duration);
            Destroy(gameObject);
        }
    }
}
