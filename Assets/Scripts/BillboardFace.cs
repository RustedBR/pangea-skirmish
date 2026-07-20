using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Billboard 2.5D (Y-only): o sprite fica sempre "em pé" (vertical na tela),
    /// encarando a câmera no YAW mas mantendo a inclinação vertical — NÃO inclina
    /// com o pitch da câmera (senão o sprite fica torto/achatado). Cancela também
    /// a rotação do pai (_gridRig, que gira em Y). Isso dá o look de FFTactics:
    /// chão em perspectiva, sprites de frente em pé.
    /// </summary>
    public class BillboardFace : MonoBehaviour
    {
        private Camera _cam;
        private void Start() => _cam = Camera.main;

        private void LateUpdate()
        {
            if (_cam == null) return;

            // Rotação do pai (ex: _gridRig girando em Y) — cancelamos pro sprite
            // não girar junto com o tabuleiro.
            Quaternion parentRot = transform.parent != null ? transform.parent.rotation : Quaternion.identity;

            // Extrai só o YAW (rotação em Y) da câmera, zera pitch/roll.
            Vector3 camEuler = _cam.transform.rotation.eulerAngles;
            Quaternion camYaw = Quaternion.Euler(0f, camEuler.y, 0f);

            // Sprite fica em pé (identity no pitch) e encara o yaw da câmera,
            // no espaço do pai. Cancelar o yaw do pai mantém o sprite de frente.
            Quaternion desiredWorld = camYaw; // em pé, encarando a câmera no yaw
            // Leva pro espaço local do pai:
            transform.rotation = Quaternion.Inverse(parentRot) * desiredWorld;
        }
    }
}
