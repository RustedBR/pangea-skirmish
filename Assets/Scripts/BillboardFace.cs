using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>Faz o objeto sempre encarar a câmera principal (sprite billboard 2.5D).</summary>
    public class BillboardFace : MonoBehaviour
    {
        private Camera _cam;
        private void Start() => _cam = Camera.main;
        private void LateUpdate()
        {
            if (_cam != null) transform.rotation = _cam.transform.rotation;
        }
    }
}
