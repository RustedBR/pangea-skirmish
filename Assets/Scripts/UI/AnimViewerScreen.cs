using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace PangeaSkirmish.UI
{
    /// <summary>
    /// Visualizador de animações (GDD-025, frente 2): escolhe ator e animação, assiste em câmera lenta,
    /// pausa, avança quadro a quadro, troca de vista e anota o que está ruim. As notas ficam salvas
    /// (PlayerPrefs) e "Salvar e copiar" junta todas num texto — no editor, também grava docs/anim/notas.md,
    /// que é onde o Claude lê.
    /// </summary>
    public class AnimViewerScreen : PangeaScreen
    {
        protected override string UxmlResource => "UI/Screens/AnimViewer";
        public Action OnBack;

        private const string NotesKey = "pangea.animnotes.v1";
        private static readonly float[] Speeds = { 1f, 0.5f, 0.25f, 0.1f };
        private const float StepSeconds = 1f / 30f;

        [Serializable] private class Note { public string key, text; }
        [Serializable] private class NoteBook { public List<Note> notes = new List<Note>(); }

        private AnimationStage _stage;
        private NoteBook _book = new NoteBook();
        private int _actorIdx, _clipIdx;
        private float _speed = 1f, _elapsed, _duration, _settle;
        private bool _paused, _loop = true, _playing;
        private Label _actorLbl, _timeLbl, _notesFor, _notesStatus;
        private TextField _notes;
        private Button _pauseBtn, _loopBtn;
        private readonly List<Button> _clipBtns = new List<Button>(), _speedBtns = new List<Button>(), _viewBtns = new List<Button>();

        protected override void Bind()
        {
            var r = Root;
            _stage = AnimationStage.Create(520);
            var view = r.Q<VisualElement>("view");
            if (view != null) view.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_stage.Texture));
            _actorLbl = r.Q<Label>("actor-label"); _timeLbl = r.Q<Label>("time-label");
            _notesFor = r.Q<Label>("notes-for"); _notesStatus = r.Q<Label>("notes-status");
            _notes = r.Q<TextField>("notes");
            _pauseBtn = r.Q<Button>("btn-pause"); _loopBtn = r.Q<Button>("btn-loop");

            try { _book = JsonUtility.FromJson<NoteBook>(PlayerPrefs.GetString(NotesKey, "")) ?? new NoteBook(); }
            catch { _book = new NoteBook(); }
            if (_book.notes == null) _book.notes = new List<Note>();

            r.Q<Button>("actor-prev").clicked += () => StepActor(-1);
            r.Q<Button>("actor-next").clicked += () => StepActor(+1);
            r.Q<Button>("btn-back").clicked += () => { SaveNote(); OnBack?.Invoke(); };
            r.Q<Button>("btn-replay").clicked += Replay;
            _pauseBtn.clicked += () => { _paused = !_paused; ApplyTime(); };
            r.Q<Button>("btn-step").clicked += () => { _paused = true; ApplyTime(); StartCoroutine(StepOnce()); };
            _loopBtn.clicked += () => { _loop = !_loop; _loopBtn.text = _loop ? "Repetir: sim" : "Repetir: não"; };
            r.Q<Button>("btn-export").clicked += Export;
            _notes?.RegisterCallback<FocusOutEvent>(_ => SaveNote());

            var list = r.Q<ScrollView>("clip-list");
            for (int i = 0; i < AnimationStage.Clips.Length; i++)
            {
                int idx = i;
                var b = new Button(() => SelectClip(idx)) { text = AnimationStage.Clips[i].label };
                b.AddToClassList("pg-button"); b.style.marginBottom = 3;
                list.Add(b); _clipBtns.Add(b);
            }
            var speeds = r.Q<VisualElement>("speeds");
            foreach (float sp in Speeds)
            {
                float v = sp;
                var b = new Button(() => { _speed = v; ApplyTime(); }) { text = v >= 1f ? "1×" : $"{v:0.##}×" };
                b.AddToClassList("pg-button"); b.style.width = 78;
                speeds.Add(b); _speedBtns.Add(b);
            }
            var views = r.Q<VisualElement>("views");
            foreach (var (v, name) in new[] { (AnimationStage.View.Game, "Vista do jogo"), (AnimationStage.View.Side, "De lado"), (AnimationStage.View.Front, "De frente") })
            {
                var vv = v;
                var b = new Button(() => { _stage.SetView(vv); RefreshButtons(); }) { text = name };
                b.AddToClassList("pg-button"); b.style.width = 130;
                views.Add(b); _viewBtns.Add(b);
            }
            // Sem transição de cor nos botões desta tela: ela segue o tempo do jogo, que aqui fica
            // lento ou parado, e o destaque do botão escolhido ficaria atrasado ou congelado.
            var instant = new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(0f) });
            r.Query<Button>().ForEach(b => b.style.transitionDuration = instant);
            SelectActor(0);
        }

        private string NoteKeyNow => $"{AnimationStage.Actors[_actorIdx].id}/{AnimationStage.Clips[_clipIdx].id}";

        private Note FindNote(string key) { foreach (var n in _book.notes) if (n.key == key) return n; return null; }

        private void SaveNote()
        {
            if (_notes == null) return;
            string text = (_notes.value ?? "").Trim();
            var n = FindNote(NoteKeyNow);
            if (text.Length == 0) { if (n != null) _book.notes.Remove(n); }
            else if (n != null) n.text = text;
            else _book.notes.Add(new Note { key = NoteKeyNow, text = text });
            PlayerPrefs.SetString(NotesKey, JsonUtility.ToJson(_book)); PlayerPrefs.Save();
        }

        private void LoadNote()
        {
            if (_notes != null) _notes.SetValueWithoutNotify(FindNote(NoteKeyNow)?.text ?? "");
            if (_notesFor != null) _notesFor.text = $"{AnimationStage.Actors[_actorIdx].label} · {AnimationStage.Clips[_clipIdx].label}";
        }

        private void StepActor(int d) => SelectActor((_actorIdx + d + AnimationStage.Actors.Length) % AnimationStage.Actors.Length);

        private void SelectActor(int idx)
        {
            SaveNote();
            _actorIdx = idx;
            if (_actorLbl != null) _actorLbl.text = AnimationStage.Actors[idx].label;
            _stage.SetActor(AnimationStage.Actors[idx].id);
            LoadNote();
            Replay();
        }

        private void SelectClip(int idx)
        {
            SaveNote();
            _clipIdx = idx;
            LoadNote();
            Replay();
        }

        /// <summary>Recria o ator, espera assentar e toca a animação escolhida.</summary>
        private void Replay()
        {
            _stage.ResetActor();
            _settle = 0.35f; _playing = false; _elapsed = 0f;
            _duration = AnimationStage.ClipSeconds(AnimationStage.Clips[_clipIdx].id);
            RefreshButtons();
        }

        private void ApplyTime()
        {
            Time.timeScale = _paused ? 0f : _speed;
            RefreshButtons();
        }

        private IEnumerator StepOnce()
        {
            Time.timeScale = 1f; Time.captureDeltaTime = StepSeconds;
            yield return null;
            Time.captureDeltaTime = 0f; Time.timeScale = 0f;
        }

        private void RefreshButtons()
        {
            for (int i = 0; i < _clipBtns.Count; i++) _clipBtns[i].EnableInClassList("pg-button--primary", i == _clipIdx);
            for (int i = 0; i < _speedBtns.Count; i++) _speedBtns[i].EnableInClassList("pg-button--primary", Mathf.Approximately(Speeds[i], _speed));
            for (int i = 0; i < _viewBtns.Count; i++) _viewBtns[i].EnableInClassList("pg-button--primary", (int)_stage.CurrentView == i);
            if (_pauseBtn != null) _pauseBtn.text = _paused ? "Continuar" : "Pausar";
        }

        private void Update()
        {
            if (_stage == null) return;
            float dt = Time.deltaTime;
            if (!_playing)
            {
                _settle -= dt;
                if (_settle <= 0f) { _stage.Play(AnimationStage.Clips[_clipIdx].id); _playing = true; _elapsed = 0f; }
            }
            else
            {
                _elapsed += dt;
                if (_elapsed > _duration + 0.4f && _loop) Replay();
            }
            if (_timeLbl != null)
                _timeLbl.text = _playing
                    ? $"t = {Mathf.Min(_elapsed, _duration):0.00} s de {_duration:0.00} s · quadro {Mathf.FloorToInt(Mathf.Min(_elapsed, _duration) / StepSeconds)} (30 por segundo)"
                    : "preparando…";
        }

        /// <summary>Todas as notas num texto só (o que vai para o Claude).</summary>
        public string AllNotesText()
        {
            SaveNote();   // inclui o que está no campo agora, mesmo sem ter saído dele
            var sb = new StringBuilder("# Notas de animação\n\n");
            if (_book.notes.Count == 0) sb.Append("(sem notas)\n");
            foreach (var n in _book.notes) sb.Append("## ").Append(n.key).Append('\n').Append(n.text).Append("\n\n");
            return sb.ToString();
        }

        private void Export()
        {
            SaveNote();
            string text = AllNotesText();
            GUIUtility.systemCopyBuffer = text;
            string where = "copiadas para a área de transferência";
#if UNITY_EDITOR
            try
            {
                string dir = System.IO.Path.Combine(Application.dataPath, "..", "docs", "anim");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "notas.md"), text);
                where = "gravadas em docs/anim/notas.md e copiadas";
            }
            catch (Exception e) { Debug.LogWarning("[Animações] não gravou as notas: " + e.Message); }
#endif
            if (_notesStatus != null) _notesStatus.text = $"{_book.notes.Count} nota(s) {where}.";
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Time.timeScale = 1f; Time.captureDeltaTime = 0f;    // nunca deixar o jogo em câmera lenta
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f; Time.captureDeltaTime = 0f;
            if (_stage != null) Destroy(_stage.gameObject);
        }
    }
}
