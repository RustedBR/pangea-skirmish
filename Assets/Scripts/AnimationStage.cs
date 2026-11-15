using System.Collections.Generic;
using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>
    /// Palco de animação (GDD-025, frente 2): um ator sozinho numa grade pequena, com câmera própria
    /// numa RenderTexture. Toca qualquer animação do UnitModel3D por nome. Serve a duas ferramentas:
    /// a folha de quadros (teste AnimationSheetTest, para o Claude ler) e o visualizador no jogo
    /// (AnimViewerScreen, para o Marcus assistir em câmera lenta e anotar).
    /// </summary>
    public class AnimationStage : MonoBehaviour
    {
        public enum View { Game, Side, Front, Free }

        /// <summary>Animações disponíveis: id, nome na tela e quanto tempo observar (s).</summary>
        public static readonly (string id, string label, float seconds)[] Clips =
        {
            ("idle", "Parado", 2.0f), ("walk", "Andar", 1.0f),
            ("attack", "Atacar", 0.7f), ("attack-power", "Golpe poderoso", 0.9f),
            ("hit", "Levar dano", 0.5f), ("block", "Bloquear", 0.9f), ("dodge", "Esquivar", 0.6f),
            ("cast", "Conjurar", 1.2f), ("release", "Soltar magia", 0.6f), ("flex", "Magia física", 0.7f),
            ("leap", "Salto", 0.6f), ("death", "Morrer", 2.2f),
        };

        /// <summary>
        /// Atores: toda arma do catálogo (2026-11-15; antes eram 5 fixas) e a Fera. O id é o da arma.
        /// </summary>
        public static (string id, string label)[] Actors
        {
            get
            {
                if (_actors != null) return _actors;
                var list = new List<(string, string)>();
                foreach (var w in WeaponCatalog.All())
                    list.Add((w.id, $"{w.displayName} ({w.id}){(Equipment.IsTwoHanded(w) ? " · 2 mãos" : "")}"));
                list.Add(("Fera", "Fera (morder)"));
                return _actors = list.ToArray();
            }
        }
        private static (string id, string label)[] _actors;

        /// <summary>Mão esquerda possível: nada, armas de 1 mão e escudos.</summary>
        public static (string id, string label)[] Offhands
        {
            get
            {
                if (_offhands != null) return _offhands;
                var list = new List<(string, string)> { ("", "Nada") };
                foreach (var w in WeaponCatalog.All()) if (!Equipment.IsTwoHanded(w)) list.Add((w.id, w.displayName + " (arma)"));
                foreach (var it in ItemCatalog.All) if (it.isShield) list.Add((it.id, it.displayName + " (escudo)"));
                return _offhands = list.ToArray();
            }
        }
        private static (string id, string label)[] _offhands;

        public RenderTexture Texture { get; private set; }
        public Unit Actor { get; private set; }
        public string ActorId { get; private set; } = "IronSword";
        public string ClipId { get; private set; } = "idle";
        public View CurrentView { get; private set; } = View.Game;
        /// <summary>Câmera livre do editor de rig (2026-11-15): giro em graus e zoom (1 = enquadra o ator).</summary>
        public float Yaw = -20f, Pitch = 20f, Zoom = 1f;   // -20: frente em 3/4 (o ator olha para StageForward)

        private GridManager _grid;
        private Camera _cam;
        private float _walkLeft;
        private Vector3 _center; private float _height = 2f;

        public static AnimationStage Create(int size = 512)
        {
            var go = new GameObject("AnimationStage");
            var s = go.AddComponent<AnimationStage>();
            s.Build(size);
            return s;
        }

        private void Build(int size)
        {
            if (RuntimeTuning.Active == null)
            {
                var tuning = Resources.Load<GameTuning>("GameTuning");
                RuntimeTuning.Active = tuning;
                if (tuning != null) AttributeStats.Formulas = tuning.statFormulas;
            }
            _grid = new GameObject("AnimationStageGrid").AddComponent<GridManager>();
            _grid.transform.SetParent(transform, false);
            _grid.width = 9; _grid.height = 9; _grid.Build();

            Texture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { name = "AnimationStageRT" };
            Texture.Create();
            var camGo = new GameObject("AnimationStageCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 200f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.13f, 0.15f, 0.22f, 1f);
            _cam.targetTexture = Texture;
            _cam.depth = -60;
            SetActor(ActorId);
        }

        public static float ClipSeconds(string clip)
        {
            foreach (var c in Clips) if (c.id == clip) return c.seconds;
            return 1f;
        }

        public void SetActor(string actorId)
        {
            ActorId = actorId;
            Respawn();
        }

        /// <summary>Corpos que o editor de rig pode escolher (vazio = o padrão da arma).</summary>
        public static readonly string[] Bodies = { "", "Warrior", "Mage", "Archer", "Tank", "Character" };
        /// <summary>Corpo forçado (Unit.modelId); vazio usa o da arma, como no jogo.</summary>
        public string BodyId { get; private set; } = "";
        public void SetBody(string body) { BodyId = body ?? ""; Respawn(); }
        /// <summary>Item da mão esquerda (arma de 1 mão ou escudo); ignorado com arma de 2 mãos.</summary>
        public string OffhandId { get; private set; } = "";
        public void SetOffhand(string id) { OffhandId = id ?? ""; Respawn(); }

        public void SetView(View v) { CurrentView = v; PlaceCamera(); }

        private void Respawn()
        {
            if (Actor != null) DestroyImmediate(Actor.gameObject);
            bool monster = ActorId == "Fera";
            var u = new GameObject(monster ? "Fera do palco" : "Ator do palco").AddComponent<Unit>();
            u.transform.SetParent(transform, false);
            u.unitName = monster ? "Fera" : "Ator";
            u.team = Team.Player;
            u.weaponId = monster ? "Claws" : ActorId;
            if (!monster && !string.IsNullOrEmpty(BodyId)) u.modelId = BodyId;
            if (!monster && !Equipment.IsTwoHanded(ActorId)) u.offhandId = OffhandId;
            u.stats = new UnitStatBlock { STR = 5, VIT = 5, DEX = 5, AGI = 5, INT = 5, WIS = 5, Footprint = monster ? 2 : 3 }.ToAttributeStats();
            var tint = RuntimeTuning.Active != null ? RuntimeTuning.Active.playerTeamColor : Color.white;
            u.Init(_grid, new Vector2Int(3, 3), tint, "Sprites/TinyTactics/Warrior");
            u.currentHP = u.stats.MaxHP;
            Actor = u;
            _walkLeft = 0f;
            // Placa de nome/vida fora do palco: cobre a arma erguida e não é animação.
            foreach (var t in u.GetComponentsInChildren<Transform>(true))
                if (t.name == "WorldBars") t.gameObject.SetActive(false);
            MeasureActor();
            PlaceCamera();
        }

        private void MeasureActor()
        {
            var b = new Bounds(Actor.transform.position, Vector3.zero); bool any = false;
            foreach (var r in Actor.GetComponentsInChildren<MeshRenderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            _height = any ? Mathf.Max(0.5f, b.size.y) : 2f;
            _center = Actor.transform.position + Vector3.up * _height * 0.5f - Actor.transform.position;   // relativo ao ator
        }

        /// <summary>Frente do palco: fixa (a de repouso do modelo). Se seguisse o corpo, a câmera
        /// lateral giraria junto quando ele torce no golpe.</summary>
        private static readonly Vector3 StageForward = new Vector3(0.7071f, 0f, -0.7071f);
        private Vector3 Forward => StageForward;

        private void PlaceCamera()
        {
            if (_cam == null || Actor == null) return;
            Vector3 f = Forward; f.y = 0f; f.Normalize();
            Quaternion rot;
            switch (CurrentView)
            {
                case View.Side:  rot = Quaternion.LookRotation(-Vector3.Cross(Vector3.up, f) + Vector3.down * 0.12f); break;
                case View.Front: rot = Quaternion.LookRotation(-f + Vector3.down * 0.12f); break;
                case View.Free:  rot = Quaternion.Euler(Mathf.Clamp(Pitch, -80f, 85f), Yaw, 0f); break;
                default:         rot = World3D.CamRotation; break;
            }
            Vector3 look = Actor.transform.position + _center;
            _cam.transform.SetPositionAndRotation(look - rot * Vector3.forward * 60f, rot);
            _cam.orthographicSize = _height * 1.05f / Mathf.Max(0.1f, Zoom);     // folga para arma erguida, investida e salto
        }

        /// <summary>Recria o ator parado. Espere alguns quadros antes de Play: ao nascer, o modelo ainda
        /// se assenta (a pose de "andar" aparece por um instante) e isso sujaria o começo da animação.</summary>
        public void ResetActor() => Respawn();

        /// <summary>Quadros de assentamento recomendados depois de ResetActor/SetActor.</summary>
        public const int SettleFrames = 6;

        /// <summary>Toca a animação no ator atual (recrie antes com ResetActor). Devolve por quanto tempo observar.</summary>
        public float Play(string clip)
        {
            ClipId = clip;
            if (Actor == null) Respawn();
            var m = Actor.Model3D;
            if (m == null) return ClipSeconds(clip);
            Vector3 pos = Actor.transform.position;
            Vector3 target = pos + Forward * 2.4f;
            switch (clip)
            {
                case "walk": _walkLeft = ClipSeconds(clip); break;
                case "attack": m.PlayAttack(0.6f, target, false); break;
                case "attack-power": m.PlayAttack(0.6f, target, true); break;
                case "hit": m.PlayHit(); break;
                case "block": m.PlayBlock(0.7f); break;
                case "dodge": m.PlayDodge(); break;
                case "cast":
                    Actor.queue.Clear();
                    Actor.queue.Add(new QueuedAction { Type = ActionType.Spell, Spell = new PlannedSpell { SpellId = "Ember", TargetTile = Actor.anchor }, Duration = 99 });
                    break;
                case "release": m.PlayCastRelease(new Color(1f, 0.45f, 0.1f)); break;
                case "flex": m.PlayFlex(); break;
                case "leap": m.PlayLeap(pos - Forward * 3f, 0.45f); break;
                case "death": Actor.currentHP = 0; m.PlayDeath(); break;
            }
            return ClipSeconds(clip);
        }

        private void Update()
        {
            if (Actor == null) return;
            if (_walkLeft > 0f)
            {
                _walkLeft -= Time.deltaTime;
                Actor.transform.position += Forward * 1.8f * Time.deltaTime;   // o modelo anda quando a posição muda
            }
        }

        private void LateUpdate()
        {
            PlaceCamera();   // a câmera segue o ator (andar no lugar, salto)
            // A placa de nome/vida nasce depois do Init e cobre a arma erguida: fora do palco.
            if (Actor != null)
            {
                var bars = Actor.transform.Find("WorldBars");
                if (bars != null) foreach (var r in bars.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
        }

        /// <summary>Renderiza a vista pedida agora e devolve os pixels (folha de quadros).</summary>
        public Texture2D Capture(View v)
        {
            var keep = CurrentView;
            CurrentView = v; PlaceCamera();
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = Texture;
            var tex = new Texture2D(Texture.width, Texture.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Texture.width, Texture.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            CurrentView = keep; PlaceCamera();
            return tex;
        }

        private void OnDestroy()
        {
            if (Texture != null) { Texture.Release(); Destroy(Texture); }
        }
    }
}
