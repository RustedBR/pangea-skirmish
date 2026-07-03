using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>Rótulo de texto no mundo (TextMesh) que sempre encara a câmera. Usado para
    /// mostrar a rolagem de iniciativa acima de cada unidade.</summary>
    public class FloatingLabel : MonoBehaviour
    {
        private TextMesh _text;
        private Camera _cam;

        public static FloatingLabel Create(Camera cam, Vector3 worldPos, string text)
        {
            var go = new GameObject("FloatingLabel");
            go.transform.position = worldPos;
            var fl = go.AddComponent<FloatingLabel>();
            fl._cam = cam;

            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            var T = Tuning.Get();
            tm.fontSize = T.floatingLabelFontSize;
            tm.characterSize = T.floatingLabelCharacterSize;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            fl._text = tm;
            fl.FaceCamera();
            return fl;
        }

        public void SetText(string t) { if (_text != null) _text.text = t; }
        public void SetColor(Color c) { if (_text != null) _text.color = c; }

        private void LateUpdate() => FaceCamera();

        private void FaceCamera()
        {
            if (_cam == null) return;
            // mantém o texto legível e de frente para a câmera inclinada
            transform.rotation = _cam.transform.rotation;
        }

        public void Dismiss() => Destroy(gameObject);
    }
}
