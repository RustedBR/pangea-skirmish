using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Overlay de arma canvas-aligned. Os frames 32x48 do TinyTactics já trazem a arma
    /// desenhada na posição base de cada frame; ajustes finos POR FRAME (posição/rotação,
    /// feitos na janela Pangea → Weapon Anim Editor) viram curvas nos clips .anim.
    ///
    /// Estrutura:
    ///   WeaponOverlay (raiz)  — offset base de canvas + espelhamento (scale.x = -1 p/ SW/NW)
    ///   └─ Anim               — SpriteRenderer + Animator (clips animam sprite + pos + rot)
    ///
    /// Clips gerados pelo menu "Pangea/Gerar Animações de Armas" (estados CarrySE/CarryNE/
    /// AttackSE/AttackNE; um AnimatorOverrideController por arma). O gerador PRESERVA as
    /// curvas de posição/rotação editadas manualmente.
    /// </summary>
    public class WeaponOverlay : MonoBehaviour
    {
        // ══════════════════ AJUSTE MANUAL ══════════════════
        // Os DEFAULTS destes campos vêm do GameTuning (weaponAttackOffset / weaponCarryOffset /
        // weaponShowWhileIdle) e são aplicados no Init. O Inspector sobrepõe só nesta instância
        // (bom para experimentar em Play). Ajustes POR FRAME: janela Pangea → Weapon Anim Editor.
        [Tooltip("Offset base na pose de ataque. (0, 0.25) = alinhamento puro de canvas.")]
        public Vector2 attackOffset = new Vector2(0f, 0.25f);
        [Tooltip("Offset base na pose de descanso (idle/andar).")]
        public Vector2 carryOffset = new Vector2(0f, 0.25f);
        [Tooltip("Mostrar a arma parada em idle/andar. Desligado = arma só aparece no golpe " +
                 "(como o kit TinyTactics foi desenhado).")]
        public bool showWhileIdle = false;
        // ════════════════════════════════════════════════════

        private GameObject _overlayGo; // raiz
        private GameObject _animGo;    // filho animado
        private SpriteRenderer _sr;
        private Animator _anim;
        private WeaponSortDriver _sort;
        private string _pose = "Carry"; // "Carry" ou "Attack"
        private string _dir = "SE";
        private bool _flip;
        private bool _hidden;

        /// <summary>True se a arma tem controller/clips (armas sem spritesheet ficam ocultas).</summary>
        public bool HasSprites => _anim != null;

        /// <summary>
        /// Cria raiz + filho animado sob attachTo (o GO "Sprite" da unidade) e liga o
        /// AnimatorOverrideController da arma.
        /// </summary>
        public void Init(string weaponId, Transform attachTo)
        {
            // Defaults vindos do tuning (o Inspector pode sobrepor depois, por instância)
            var T = Tuning.Get();
            attackOffset = T.weaponAttackOffset;
            carryOffset = T.weaponCarryOffset;
            showWhileIdle = T.weaponShowWhileIdle;

            _overlayGo = new GameObject("WeaponOverlay");
            _overlayGo.transform.SetParent(attachTo, false);

            _animGo = new GameObject("Anim");
            _animGo.transform.SetParent(_overlayGo.transform, false);
            _sr = _animGo.AddComponent<SpriteRenderer>();
            _sort = _animGo.AddComponent<WeaponSortDriver>();

            var ctrl = string.IsNullOrEmpty(weaponId)
                ? null
                : Resources.Load<RuntimeAnimatorController>($"Animations/Weapons/{weaponId}Overlay");
            if (ctrl == null)
            {
                if (!string.IsNullOrEmpty(weaponId))
                    Debug.LogWarning($"[WeaponOverlay] Sem controller para '{weaponId}' — " +
                                     "rode Pangea/Gerar Animações de Armas (ou a arma não tem spritesheet).");
                _overlayGo.SetActive(false);
                return;
            }

            _anim = _animGo.AddComponent<Animator>();
            _anim.runtimeAnimatorController = ctrl;
            ShowCarry(_dir, _flip);
        }

        /// <summary>
        /// Toca o clip de ataque da direção, escalado para caber em duration segundos.
        /// O clip pode ter N frames (editável no Weapon Anim Editor).
        /// </summary>
        public void PlayAttack(string dir, bool flip, float duration)
        {
            if (_anim == null) return;
            _pose = "Attack"; _dir = dir; _flip = flip; _hidden = false;
            _overlayGo.SetActive(true);
            ApplyPose();
            float clipLen = GetClipLength("Attack" + dir);
            _anim.speed = duration > 0.01f ? clipLen / duration : 1f;
            _anim.Play("Attack" + dir, 0, 0f);
        }

        /// <summary>Retorna a duração real de um clip pelo nome no AnimatorOverrideController.</summary>
        private float GetClipLength(string stateName)
        {
            if (_anim == null || _anim.runtimeAnimatorController == null) return 0.4f;
            foreach (var clip in _anim.runtimeAnimatorController.animationClips)
                if (clip.name == stateName) return clip.length;
            return 0.4f; // fallback
        }

        /// <summary>Pose de descanso (frame 0 do sheet) — usada em idle/walk. Idempotente.
        /// Com showWhileIdle desligado, esconde a arma (ela só aparece no golpe).</summary>
        public void ShowCarry(string dir, bool flip)
        {
            if (_anim == null) return;
            if (!showWhileIdle) { _pose = "Carry"; _dir = dir; _flip = flip; Hide(); return; }
            if (!_hidden && _pose == "Carry" && _dir == dir && _flip == flip)
            {
                ApplyPose(); // pega ajustes feitos ao vivo no Inspector
                return;
            }
            _pose = "Carry"; _dir = dir; _flip = flip; _hidden = false;
            _overlayGo.SetActive(true);
            ApplyPose();
            _anim.speed = 1f;
            _anim.Play("Carry" + dir, 0, 0f);
        }

        /// <summary>
        /// Aplica na RAIZ o offset base da pose e o espelhamento (scale.x = -1 espelha
        /// também as curvas por frame do filho e o próprio sprite — sem flipX).
        /// </summary>
        private void ApplyPose()
        {
            Vector2 off = _pose == "Attack" ? attackOffset : carryOffset;
            _overlayGo.transform.localPosition = new Vector3(off.x, off.y, 0f);
            _overlayGo.transform.localScale = new Vector3(_flip ? -1f : 1f, 1f, 1f);
        }

        /// <summary>Reaplica a direção atual (chamado quando _baseDir/_flip da unidade mudam).</summary>
        public void RefreshDirection(string dir, bool flip)
        {
            if (_anim == null || _hidden) return;
            if (_pose == "Carry") ShowCarry(dir, flip);
            // Em ataque a direção é definida antes do PlayAttack; nada a fazer aqui.
        }

        /// <summary>Esconde o overlay (dano/morte).</summary>
        public void Hide()
        {
            _hidden = true;
            if (_overlayGo != null) _overlayGo.SetActive(false);
        }

        /// <summary>
        /// Recebe o sortingOrder do CORPO; o WeaponSortDriver aplica ±1 conforme o
        /// sortOffset animado pelos clips (frente/atrás por frame).
        /// </summary>
        public void SetSortingOrder(int bodyOrder)
        {
            if (_sort != null) _sort.baseOrder = bodyOrder;
        }
    }
}
