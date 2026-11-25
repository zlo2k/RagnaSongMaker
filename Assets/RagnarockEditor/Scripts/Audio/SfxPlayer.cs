using System;
using System.Collections.Generic;
using UnityEngine;

namespace RagnarockEditor.Audio
{
    /// <summary>
    /// Единая точка воспроизведения SFX:
    ///  - удары барабанов — 3D, для каждого барабана 3 вариации, громкость зависит от силы удара;
    ///  - UI — 2D: наведение, клик, подтверждение, «назад», пауза, начало записи, отсчёт, перемотка.
    ///
    /// Клипы лежат в Assets/Resources/SFX и грузятся сами — сценам ничего назначать не нужно.
    /// Объект создаётся лениво (и прогревается до первой сцены) и живёт между сценами.
    /// </summary>
    [DisallowMultipleComponent]
    public class SfxPlayer : MonoBehaviour
    {
        public enum UiSound
        {
            Hover = 0,
            Select = 1,
            Confirm = 2,
            Back = 3,
            Pause = 4,
            RecordStart = 5,
            CountIn = 6,
            SeekTick = 7,
        }

        private const string ResourceRoot = "SFX/";
        private const int VoiceCount = 8;
        private const float HoverCooldown = 0.05f;
        private const int MaxLoadAttempts = 3;

        private static SfxPlayer _instance;

        [Header("Громкость")]
        [Range(0f, 1f)] [SerializeField] private float _masterVolume = 0.9f;
        [Range(0f, 1f)] [SerializeField] private float _uiVolume = 0.75f;
        [Range(0f, 1f)] [SerializeField] private float _drumVolume = 1f;
        [SerializeField] private bool _muted;

        private AudioSource[] _voices;
        private int _nextVoice;
        private AudioClip[][] _drumClips;
        private readonly Dictionary<UiSound, AudioClip> _uiClips = new Dictionary<UiSound, AudioClip>();

        private bool _voicesReady;
        private bool _clipsReady;
        private int _loadAttempts;
        private float _lastHoverTime = -1f;

        /// <summary>Синглтон: создаётся при первом обращении.</summary>
        public static SfxPlayer Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("SfxPlayer");
                    _instance = go.AddComponent<SfxPlayer>();
                    DontDestroyOnLoad(go);
                }

                return _instance;
            }
        }

        /// <summary>Библиотека загружена.</summary>
        public bool IsReady => _clipsReady;

        /// <summary>Звук выключен целиком (например, из настроек).</summary>
        public bool IsMuted
        {
            get => _muted;
            set => _muted = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // прогреваем клипы до первой сцены, чтобы первый звук не залипал
            Instance.Initialize();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        private void Start()
        {
            // страховка: если Resources оказались недоступны на BeforeSceneLoad — дочитываем здесь
            EnsureClips();
        }

        private void Initialize()
        {
            EnsureVoices();
            EnsureClips();
        }

        private void EnsureVoices()
        {
            if (_voicesReady)
                return;

            _voicesReady = true;

            _voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                var voice = new GameObject("Voice" + i);
                voice.transform.SetParent(transform, false);

                var source = voice.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 0.4f;
                source.maxDistance = 14f;
                source.dopplerLevel = 0f;
                source.volume = 1f;
                _voices[i] = source;
            }
        }

        private void EnsureClips()
        {
            if (_clipsReady || _loadAttempts >= MaxLoadAttempts)
                return;

            _loadAttempts++;

            _drumClips = new AudioClip[4][];
            int drumCount = 0;

            for (int drum = 0; drum < 4; drum++)
            {
                _drumClips[drum] = new AudioClip[3];
                for (int variation = 0; variation < 3; variation++)
                {
                    var clip = Resources.Load<AudioClip>($"{ResourceRoot}Drums/Drum{drum + 1}_{variation + 1}");
                    _drumClips[drum][variation] = clip;
                    if (clip != null)
                        drumCount++;
                }
            }

            int uiCount = 0;
            foreach (UiSound sound in Enum.GetValues(typeof(UiSound)))
            {
                var clip = Resources.Load<AudioClip>(ResourceRoot + "UI/" + sound);
                _uiClips[sound] = clip;

                if (clip != null)
                    uiCount++;
                else
                    Debug.LogWarning("[Sfx] нет клипа Resources/" + ResourceRoot + "UI/" + sound);
            }

            // если вообще ничего не нашлось — попробуем ещё раз при следующем звуке
            _clipsReady = drumCount + uiCount > 0;

            Debug.Log($"[Sfx] библиотека: барабаны {drumCount}/12, UI {uiCount}/8" +
                      (_clipsReady ? string.Empty : " — клипы не найдены, попробую позже"));
        }

        // ------------------------------------------------------------ UI-звуки

        /// <summary>Проигрывает UI-звук (2D).</summary>
        public void PlayUi(UiSound sound, float volumeScale = 1f)
        {
            if (_muted)
                return;

            Initialize();

            if (!_uiClips.TryGetValue(sound, out var clip) || clip == null)
                return;

            var source = NextVoice();
            source.spatialBlend = 0f;
            source.PlayOneShot(clip, Mathf.Clamp01(_masterVolume * _uiVolume * volumeScale));
        }

        /// <summary>Наведение на кнопку: с антидребезгом, чтобы не стрекотало при дрожании луча.</summary>
        public void PlayHover()
        {
            if (_muted)
                return;

            if (Time.unscaledTime - _lastHoverTime < HoverCooldown)
                return;

            _lastHoverTime = Time.unscaledTime;
            PlayUi(UiSound.Hover, 0.55f);
        }

        // ------------------------------------------------------ удары барабанов

        /// <summary>
        /// Удар по барабану: 3D-звук в точке барабана, случайная вариация,
        /// громкость зависит от скорости молота.
        /// </summary>
        public void PlayDrum(int drumIndex, Vector3 position, float velocity)
        {
            if (_muted)
                return;

            Initialize();

            if (_drumClips == null)
                return;

            var variants = _drumClips[Mathf.Clamp(drumIndex, 0, _drumClips.Length - 1)];
            if (variants == null || variants.Length == 0)
                return;

            var clip = variants[UnityEngine.Random.Range(0, variants.Length)];
            if (clip == null)
            {
                for (int i = 0; i < variants.Length && clip == null; i++)
                    clip = variants[i];
            }

            if (clip == null)
                return;

            float loudness = Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(0.6f, 7f, velocity));

            var source = NextVoice();
            source.transform.position = position;
            source.spatialBlend = 1f;
            source.PlayOneShot(clip, Mathf.Clamp01(_masterVolume * _drumVolume * loudness));
        }

        // ---------------------------------------------------------------- утилиты

        /// <summary>Громкость: 0..1. Сохраняется в PlayerPrefs.</summary>
        public void SetVolumes(float master, float ui, float drums)
        {
            _masterVolume = Mathf.Clamp01(master);
            _uiVolume = Mathf.Clamp01(ui);
            _drumVolume = Mathf.Clamp01(drums);

            PlayerPrefs.SetFloat("ragna.sfx.master", _masterVolume);
            PlayerPrefs.SetFloat("ragna.sfx.ui", _uiVolume);
            PlayerPrefs.SetFloat("ragna.sfx.drums", _drumVolume);
            PlayerPrefs.Save();
        }

        private AudioSource NextVoice()
        {
            if (_voices == null || _voices.Length == 0)
                EnsureVoices();

            var source = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            source.Stop();
            return source;
        }
    }
}
