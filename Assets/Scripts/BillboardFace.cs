using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Billboard 2.5D: o sprite (personagem, footprint, mira etc.) fica sempre de
    /// frente para a câmera, seja qual for a orientação dela — câmera reto de
    /// cima (padrão atual) ou tiltada. Também cancela a rotação do pai (ex:
    /// _gridRig girando em Y) para não girar junto com o tabuleiro. Isso dá o
    /// look de FFTactics: chão em perspectiva/top-down, sprites sempre de frente.
    ///
    /// Fix (2026-07-20): antes só cancelava o YAW da câmera (mantendo pitch
    /// zero), o que funcionava com a câmera tiltada de 45° mas fazia os sprites
    /// desaparecerem (ficarem de canto) com a câmera reto de cima — o "yaw" da
    /// câmera não tem mais o sentido de "direção horizontal de visão" quando ela
    /// olha puramente ao longo de Y. Agora copia a rotação inteira da câmera.
    /// </summary>
    public class BillboardFace : MonoBehaviour
    {
        private Camera _cam;

        private void LateUpdate()
        {
            // Fix (2026-07-20): antes buscava Camera.main SÓ no Start(). Se a
            // Unit for inicializada antes da câmera de batalha existir/ter a tag
            // MainCamera configurada (ordem de inicialização entre GameObjects
            // não é garantida), _cam ficava null PARA SEMPRE e o sprite nunca
            // aparecia. CameraController.Instance é um singleton setado no
            // Awake() da câmera — mais cedo e confiável que Camera.main — com
            // fallback e retentativa a cada frame até existir.
            if (_cam == null)
            {
                _cam = CameraController.Instance != null ? CameraController.Instance.Cam : Camera.main;
                if (_cam == null) return;
            }

            // Rotação do pai (ex: _gridRig girando em Y) — cancelamos pro sprite
            // não girar junto com o tabuleiro.
            Quaternion parentRot = transform.parent != null ? transform.parent.rotation : Quaternion.identity;

            // Sprite sempre de frente para a câmera, no espaço local do pai.
            transform.rotation = Quaternion.Inverse(parentRot) * _cam.transform.rotation;
        }
    }
}
