using UnityEngine;

namespace PangeaSkirmish
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager I { get; private set; }

        // Gameplay
        public AudioClip sfxStep;
        public AudioClip sfxAttack;
        public AudioClip sfxHit;
        public AudioClip sfxDeath;
        public AudioClip sfxDice;
        public AudioClip sfxRound;

        // Combat
        public AudioClip sfxCritical;
        public AudioClip sfxMiss;

        // Notifications
        public AudioClip sfxVictory;
        public AudioClip sfxDefeat;
        public AudioClip sfxTimerWarning;

        // UI
        public AudioClip sfxUIClick;
        public AudioClip sfxUIHover;
        public AudioClip sfxUIConfirm;

        // Steps
        public AudioClip sfxStepGrass;
        public AudioClip sfxDash;

        // BGM
        public AudioClip bgmBattle;
        public AudioClip bgmMenu;

        private AudioSource _sfxSource;
        private AudioSource _musicSource;

        private void Awake()
        {
            if (I != null) { Destroy(gameObject); return; }
            I = this;
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<AudioListener>();

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;

            string path = "Audio/";
            sfxStep          = Resources.Load<AudioClip>(path + "sfx_step");
            sfxAttack        = Resources.Load<AudioClip>(path + "sfx_attack");
            sfxHit           = Resources.Load<AudioClip>(path + "sfx_hit");
            sfxDeath         = Resources.Load<AudioClip>(path + "sfx_death");
            sfxDice          = Resources.Load<AudioClip>(path + "sfx_dice");
            sfxRound         = Resources.Load<AudioClip>(path + "sfx_round");
            sfxCritical      = Resources.Load<AudioClip>(path + "sfx_critical");
            sfxMiss          = Resources.Load<AudioClip>(path + "sfx_miss");
            sfxVictory       = Resources.Load<AudioClip>(path + "sfx_victory");
            sfxDefeat        = Resources.Load<AudioClip>(path + "sfx_defeat");
            sfxTimerWarning  = Resources.Load<AudioClip>(path + "sfx_timer_warning");
            sfxUIClick       = Resources.Load<AudioClip>(path + "sfx_ui_click");
            sfxUIHover       = Resources.Load<AudioClip>(path + "sfx_ui_hover");
            sfxUIConfirm     = Resources.Load<AudioClip>(path + "sfx_ui_confirm");
            sfxStepGrass     = Resources.Load<AudioClip>(path + "sfx_step_grass");
            sfxDash          = Resources.Load<AudioClip>(path + "sfx_dash");
            bgmBattle        = Resources.Load<AudioClip>(path + "bgm_battle");
            bgmMenu          = Resources.Load<AudioClip>(path + "bgm_menu");
        }

        public void Play(AudioClip clip)
        {
            if (clip == null || _sfxSource == null) return;
            _sfxSource.PlayOneShot(clip, Tuning.Get().sfxVolume);
        }

        public void PlayAtPoint(AudioClip clip, Vector3 pos)
        {
            if (clip == null) return;
            AudioSource.PlayClipAtPoint(clip, pos, Tuning.Get().sfxVolume);
        }

        public void PlayMusic(AudioClip clip)
        {
            if (clip == null || _musicSource == null) return;
            _musicSource.volume = Tuning.Get().musicVolume;
            if (_musicSource.clip == clip && _musicSource.isPlaying) return;
            _musicSource.Stop();
            _musicSource.clip = clip;
            _musicSource.Play();
        }

        public void StopMusic()
        {
            if (_musicSource != null && _musicSource.isPlaying)
            {
                _musicSource.Stop();
                _musicSource.clip = null;
            }
        }
    }
}
