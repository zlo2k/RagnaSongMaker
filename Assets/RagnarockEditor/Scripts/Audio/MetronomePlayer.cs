using System;
using System.Collections;
using UnityEngine;

namespace RagnarockEditor.Audio
{
    /// <summary>
    /// Метроном: тикает с заданным BPM.
    ///
    /// Звук берётся из Resources/SFX/Metronome/Tick(.wav|ogg) и TickAccent — если файлов нет,
    /// генерируется короткий синтезированный клик, чтобы метроном работал всегда.
    /// Громкость хранится в PlayerPrefs и общая для экрана BPM и записи.
    /// </summary>
    public class MetronomePlayer : MonoBehaviour
    {
        private const string VolumeKey = "ragna.metronome.volume";
        private const string TickResource = "SFX/Metronome/Tick";
        private const string AccentResource = "SFX/Metronome/TickAccent";

        [Header("Звук")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _tickSound;
        [SerializeField] private AudioClip _accentTickSound;

        [Header("Громкость")]
        [Range(0f, 1f)]
        [SerializeField] private float _volume = 0.7f;

        [Header("Размер такта")]
        [SerializeField] private int _beatsPerMeasure = 4;

        private float _bpm = 120f;
        private bool _isPlaying;
        private bool _volumeChanged;
        private int _beatCount;
        private Coroutine _metronomeCoroutine;

        /// <summary>Тик метронома: true — акцент на первую долю.</summary>
        public event Action<bool> OnTick;

        /// <summary>BPM метронома.</summary>
        public float BPM
        {
            get => _bpm;
            set => _bpm = Mathf.Max(40f, value);
        }

        /// <summary>Играет ли метроном (собственный цикл; запись тикает по времени песни).</summary>
        public bool IsPlaying => _isPlaying;

        /// <summary>Текущий номер бита (1-based).</summary>
        public int CurrentBeat => _beatCount;

        /// <summary>Громкость 0..1, сохраняется в PlayerPrefs.</summary>
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(VolumeKey, _volume);
                _volumeChanged = true;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveVolume();
        }

        private void OnDisable() => SaveVolume();

        private void SaveVolume()
        {
            if (!_volumeChanged) return;
            PlayerPrefs.Save();
            _volumeChanged = false;
        }

        private void Awake()
        {
            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();

            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.playOnAwake = false;
                _audioSource.spatialBlend = 0f;
            }

            if (_tickSound == null)
                _tickSound = Resources.Load<AudioClip>(TickResource);

            if (_accentTickSound == null)
                _accentTickSound = Resources.Load<AudioClip>(AccentResource);

            // Файлов может не быть — синтезируем клик, чтобы метроном не молчал
            if (_tickSound == null)
                _tickSound = CreateClick("MetronomeTick", 1200f, 0.045f, 0.55f);

            if (_accentTickSound == null)
                _accentTickSound = CreateClick("MetronomeAccent", 1800f, 0.06f, 0.8f);

            if (PlayerPrefs.HasKey(VolumeKey))
                _volume = PlayerPrefs.GetFloat(VolumeKey, _volume);
        }

        /// <summary>Запускает собственный цикл метронома (экран BPM).</summary>
        public void StartMetronome()
        {
            if (_isPlaying)
                return;

            _isPlaying = true;
            _beatCount = 0;
            _metronomeCoroutine = StartCoroutine(MetronomeLoop());
            Debug.Log($"[MetronomePlayer] Запущен: BPM={_bpm:0.#}, volume={_volume:F2}");
        }

        /// <summary>Останавливает собственный цикл.</summary>
        public void StopMetronome()
        {
            _isPlaying = false;

            if (_metronomeCoroutine != null)
            {
                StopCoroutine(_metronomeCoroutine);
                _metronomeCoroutine = null;
            }
        }

        /// <summary>Сбрасывает счётчик битов.</summary>
        public void ResetBeatCount() => _beatCount = 0;

        private IEnumerator MetronomeLoop()
        {
            int beatsPerMeasure = Mathf.Max(1, _beatsPerMeasure);
            double nextTick = AudioSettings.dspTime;

            while (_isPlaying)
            {
                _beatCount++;
                PlayTick((_beatCount - 1) % beatsPerMeasure == 0);

                // Расписание по dspTime — без накопления дрейфа WaitForSeconds
                nextTick += 60.0 / _bpm;

                while (_isPlaying && AudioSettings.dspTime < nextTick)
                    yield return null;
            }
        }

        /// <summary>Проигрывает один тик и сообщает подписчикам (индикатор, запись).</summary>
        public void PlayTick(bool accent = false)
        {
            AudioClip clip = accent && _accentTickSound != null ? _accentTickSound : _tickSound;

            if (clip != null && _audioSource != null)
                _audioSource.PlayOneShot(clip, _volume);

            OnTick?.Invoke(accent);
        }

        private static AudioClip CreateClick(string name, float frequency, float duration, float amplitude)
        {
            const int sampleRate = 44100;
            int samples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
            var data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Exp(-t * 90f);
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * amplitude;
            }

            var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
