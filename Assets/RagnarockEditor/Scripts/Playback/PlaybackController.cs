using System;
using System.Collections.Generic;
using UnityEngine;
using RagnarockEditor.Data;
using RagnarockEditor.Quantization;

namespace RagnarockEditor.Playback
{
    /// <summary>
    /// Контроллер предпросмотра уровня: ведёт время песни (по аудио, а если его нет —
    /// по Time.deltaTime), спавнит руны через <see cref="RuneSpawner"/> и сообщает о попаданиях.
    /// Если готовых нот нет, квантует сырые удары из <see cref="RecordSession"/>.
    /// </summary>
    public class PlaybackController : MonoBehaviour
    {
        public enum PlaybackState { Stopped, Playing, Paused }

        [Header("Компоненты")]
        [SerializeField] private RuneSpawner _runeSpawner;
        [SerializeField] private VR.DrumManager _drumManager;
        [Tooltip("Запасной AudioSource, если в сцене нет SongPlaybackBootstrap.")]
        [SerializeField] private AudioSource _audioSource;

        [Header("Аудио")]
        [Tooltip("Проигрыватель трека (SongPlaybackBootstrap). Если пусто — ищется в сцене.")]
        [SerializeField] private VR.SongPlaybackBootstrap _songPlayback;

        [Tooltip("Создать SongPlaybackBootstrap в рантайме, если в сцене его нет, а трек выбран.")]
        [SerializeField] private bool _autoCreateSongPlayback = true;

        [Header("Скорость")]
        [Range(0.25f, 2f)]
        [SerializeField] private float _playbackSpeed = 1f;

        [Header("Квантование")]
        [Tooltip("Если готовых нот нет — квантовать RecordSession.RawHits при Prepare().")]
        [SerializeField] private bool _quantizeRawHits = true;

        [Tooltip("BPM по умолчанию, если в сессии нет песни.")]
        [SerializeField] private float _defaultBpm = 120f;

        [Header("Завершение")]
        [Tooltip("Допуск после конца трека, секунды.")]
        [SerializeField] private float _endTolerance = 0.25f;

        private RecordSession _session;
        private List<Note> _notes;
        private int _currentNoteIndex;
        private float _playbackTime;
        private float _bpm = 120f;
        private float _songDuration;
        private float _lastNoteTime;
        private bool _waitingForAudio;
        private bool _songCreateAttempted;
        private bool _spawnerSubscribed;
        private AudioSource _songPlaybackSource;

        // -------------------------------------------------------------- События

        /// <summary>Текущая позиция предпросмотра в секундах (каждый кадр во время проигрывания).</summary>
        public event Action<float> OnPlaybackTime;

        /// <summary>Руна дошла до барабана (индекс барабана 0-3).</summary>
        public event Action<int> OnNoteHit;

        /// <summary>Предпросмотр завершён: ноты закончились или трек доиграл.</summary>
        public event Action OnFinished;

        /// <summary>Смена состояния (для UI).</summary>
        public event Action<PlaybackState> OnStateChanged;

        // -------------------------------------------------------------- Свойства

        /// <summary>Текущее состояние.</summary>
        public PlaybackState CurrentState { get; private set; } = PlaybackState.Stopped;

        /// <summary>Идёт воспроизведение.</summary>
        public bool IsPlaying => CurrentState == PlaybackState.Playing;

        /// <summary>Воспроизведение на паузе.</summary>
        public bool IsPaused => CurrentState == PlaybackState.Paused;

        /// <summary>Текущее время предпросмотра в секундах.</summary>
        public float PlaybackTime => _playbackTime;

        /// <summary>Количество подготовленных нот.</summary>
        public int NoteCount => _notes != null ? _notes.Count : 0;

        /// <summary>BPM, с которым ноты переводятся из битов в секунды.</summary>
        public float BPM => _bpm;

        /// <summary>Длительность песни (0 — неизвестна).</summary>
        public float SongDuration => _songDuration;

        /// <summary>Есть ли готовое аудио (SongPlaybackBootstrap или AudioSource).</summary>
        public bool HasAudio => IsSongAudioReady || HasLocalAudio;

        /// <summary>Подготовленные ноты (только чтение).</summary>
        public IReadOnlyList<Note> Notes => _notes;

        /// <summary>Спавнер рун, которым управляет контроллер.</summary>
        public RuneSpawner RuneSpawner => _runeSpawner;

        /// <summary>Менеджер барабанов.</summary>
        public VR.DrumManager DrumManager => _drumManager;

        /// <summary>
        /// Скорость предпросмотра (0.5x / 1x). Влияет и на движение рун, и на аудио (pitch).
        /// </summary>
        public float PlaybackSpeed
        {
            get => _playbackSpeed;
            set
            {
                _playbackSpeed = Mathf.Clamp(value, 0.25f, 2f);
                ApplySpeedToAudio();
            }
        }

        // -------------------------------------------------------- Unity-цикл

        private void Awake()
        {
            _session = RecordSession.Instance;
            ResolveReferences();
        }

        private void Start()
        {
            // Повторно: барабаны (DrumSceneBootstrap) и SongPlaybackBootstrap появляются позже.
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeSpawner();
        }

        private void OnDisable()
        {
            UnsubscribeSpawner();

            if (CurrentState != PlaybackState.Stopped)
                Stop();
        }

        private void OnDestroy()
        {
            UnsubscribeSpawner();
        }

        private void Update()
        {
            if (CurrentState != PlaybackState.Playing)
                return;

            TryStartPendingAudio();

            float time = AdvanceTime();
            SpawnDueNotes(time);

            if (_runeSpawner != null)
                _runeSpawner.UpdateRunes(time, Time.deltaTime);

            OnPlaybackTime?.Invoke(time);

            if (IsPreviewFinished(time))
                FinishPreview();
        }

        // ------------------------------------------------------ Публичный API

        /// <summary>
        /// Подготавливает предпросмотр: берёт ноты (аргумент или RecordSession.QuantizedNotes),
        /// при их отсутствии квантует RecordSession.RawHits.
        /// </summary>
        public void Prepare(List<Note> notes = null)
        {
            if (CurrentState != PlaybackState.Stopped)
                Stop();

            _session = RecordSession.Instance;
            _bpm = ResolveBpm();

            List<Note> prepared = BuildNoteList(notes);
            prepared.Sort((a, b) => a.Time.CompareTo(b.Time));

            _notes = prepared;
            _currentNoteIndex = 0;
            _playbackTime = 0f;
            _waitingForAudio = false;

            ResolveReferences();
            _songDuration = ResolveSongDuration();
            _lastNoteTime = prepared.Count > 0 ? prepared[prepared.Count - 1].GetTimeInSeconds(_bpm) : 0f;

            if (_runeSpawner != null)
                _runeSpawner.Initialize(_notes, _bpm, _songDuration);

            SubscribeSpawner();
            OnPlaybackTime?.Invoke(0f);

            Debug.Log($"[PlaybackController] Подготовлено нот: {_notes.Count}, BPM={_bpm:0.#}, " +
                      $"длительность={_songDuration:F1}с");
        }

        /// <summary>
        /// Запускает предпросмотр с начала (если был на паузе — продолжает).
        /// </summary>
        public void Play()
        {
            if (CurrentState == PlaybackState.Playing)
                return;

            if (CurrentState == PlaybackState.Paused)
            {
                Resume();
                return;
            }

            if (_notes == null || _notes.Count == 0)
                Prepare();

            if (_notes == null || _notes.Count == 0)
            {
                Debug.LogWarning("[PlaybackController] Нет нот для воспроизведения.");
                return;
            }

            ResolveReferences();

            _playbackTime = 0f;
            _currentNoteIndex = 0;
            _waitingForAudio = false;

            if (_runeSpawner != null)
                _runeSpawner.ClearAllRunes();

            CurrentState = PlaybackState.Playing;

            StartAudioFromStart();

            OnPlaybackTime?.Invoke(0f);
            OnStateChanged?.Invoke(CurrentState);

            Debug.Log($"[PlaybackController] Воспроизведение началось: нот={_notes.Count}, BPM={_bpm:0.#}, " +
                      $"скорость={_playbackSpeed:0.##}x, аудио={(HasAudio ? "есть" : "нет (таймер)")}");
        }

        /// <summary>Пауза (останавливается и звук, и движение рун).</summary>
        public void Pause()
        {
            if (CurrentState != PlaybackState.Playing)
                return;

            CurrentState = PlaybackState.Paused;
            PauseAudio();

            OnPlaybackTime?.Invoke(_playbackTime);
            OnStateChanged?.Invoke(CurrentState);

            Debug.Log("[PlaybackController] Пауза");
        }

        /// <summary>Продолжение после паузы.</summary>
        public void Resume()
        {
            if (CurrentState != PlaybackState.Paused)
                return;

            CurrentState = PlaybackState.Playing;

            if (HasAudio)
                ResumeAudio();
            else if (_songPlayback != null && _songPlayback.HasSong)
                _waitingForAudio = true;

            OnStateChanged?.Invoke(CurrentState);

            Debug.Log("[PlaybackController] Продолжение");
        }

        /// <summary>Пауза / продолжение одним вызовом (совместимость с PreviewUI/StoneButton).</summary>
        public void TogglePause()
        {
            if (CurrentState == PlaybackState.Playing)
                Pause();
            else if (CurrentState == PlaybackState.Paused)
                Resume();
        }

        /// <summary>Останавливает предпросмотр и сбрасывает время в 0.</summary>
        public void Stop()
        {
            bool wasActive = CurrentState != PlaybackState.Stopped;

            CurrentState = PlaybackState.Stopped;
            StopAudio();

            if (_runeSpawner != null)
                _runeSpawner.ClearAllRunes();

            _currentNoteIndex = 0;
            _playbackTime = 0f;
            _waitingForAudio = false;

            OnPlaybackTime?.Invoke(0f);
            if (wasActive)
                OnStateChanged?.Invoke(CurrentState);

            Debug.Log("[PlaybackController] Остановлено");
        }

        /// <summary>
        /// Перемотка предпросмотра на указанное время (секунды от начала песни).
        /// </summary>
        public void Seek(float seconds)
        {
            if (_notes == null || _notes.Count == 0)
                Prepare();

            float limit = Mathf.Max(_songDuration, _lastNoteTime);
            float target = Mathf.Max(0f, seconds);
            if (limit > 0.01f && target > limit)
                target = limit;

            _playbackTime = target;

            if (_runeSpawner != null)
                _runeSpawner.ClearAllRunes();

            PrepareNoteCursor(target);
            SpawnDueNotes(target);
            SeekAudio(target);

            OnPlaybackTime?.Invoke(target);

            Debug.Log($"[PlaybackController] Перемотка на {target:F2}с");
        }

        // ---------------------------------------------------------- Внутреннее

        private void FinishPreview()
        {
            if (CurrentState == PlaybackState.Stopped)
                return;

            CurrentState = PlaybackState.Stopped;
            StopAudio();

            if (_runeSpawner != null)
                _runeSpawner.ClearAllRunes();

            OnPlaybackTime?.Invoke(_playbackTime);
            OnStateChanged?.Invoke(CurrentState);

            Debug.Log($"[PlaybackController] Предпросмотр завершён на {_playbackTime:F2}с");
            OnFinished?.Invoke();
        }

        private float AdvanceTime()
        {
            float delta = Time.deltaTime * _playbackSpeed;

            if (IsAudioPlaying)
            {
                float audioTime = GetAudioPosition();

                // Аудио — ведущий источник времени; таймер только на случай ещё не стартовавшего звука.
                if (audioTime > 0f && audioTime >= _playbackTime - 0.1f)
                    _playbackTime = audioTime;
                else
                    _playbackTime += delta;
            }
            else
            {
                _playbackTime += delta;
            }

            if (_playbackTime < 0f)
                _playbackTime = 0f;

            return _playbackTime;
        }

        private void SpawnDueNotes(float songTime)
        {
            if (_runeSpawner == null || _notes == null)
                return;

            float jumpTime = Mathf.Max(0.1f, _runeSpawner.NoteJumpTime);

            while (_currentNoteIndex < _notes.Count)
            {
                Note note = _notes[_currentNoteIndex];
                if (note == null)
                {
                    _currentNoteIndex++;
                    continue;
                }

                float noteTime = note.GetTimeInSeconds(_bpm);
                if (noteTime - jumpTime > songTime)
                    break;

                _runeSpawner.SpawnRune(note, _currentNoteIndex);
                _currentNoteIndex++;
            }
        }

        private void PrepareNoteCursor(float songTime)
        {
            _currentNoteIndex = 0;

            if (_notes == null)
                return;

            while (_currentNoteIndex < _notes.Count)
            {
                Note note = _notes[_currentNoteIndex];
                if (note == null || note.GetTimeInSeconds(_bpm) >= songTime - 0.0001f)
                    break;

                _currentNoteIndex++;
            }
        }

        private bool IsPreviewFinished(float songTime)
        {
            if (_notes == null || _notes.Count == 0)
                return true;

            bool allSpawned = _currentNoteIndex >= _notes.Count;
            bool noRunes = _runeSpawner == null || _runeSpawner.ActiveRuneCount == 0;

            if (allSpawned && noRunes)
                return true;

            float end = Mathf.Max(_songDuration, _lastNoteTime);
            if (end > 0.01f && songTime >= end + _endTolerance)
                return noRunes || songTime >= end + _endTolerance + 2f;

            return false;
        }

        private List<Note> BuildNoteList(List<Note> explicitNotes)
        {
            List<Note> result = new List<Note>();
            List<Note> source = explicitNotes;

            if (source == null && _session != null)
                source = _session.QuantizedNotes;

            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (source[i] != null)
                        result.Add(source[i]);
                }
            }

            if (result.Count > 0 || !_quantizeRawHits)
                return result;

            result = QuantizeRawHits();

            if (result.Count > 0 && _session != null)
                _session.QuantizedNotes = result;

            return result;
        }

        private List<Note> QuantizeRawHits()
        {
            if (_session == null)
                _session = RecordSession.Instance;

            if (_session == null || _session.RawHits == null || _session.RawHits.Count == 0)
            {
                Debug.LogWarning("[PlaybackController] Нет ни готовых нот, ни сырых ударов — предпросмотр пуст.");
                return new List<Note>();
            }

            QuantizeSettings settings = Resources.Load<QuantizeSettings>("QuantizeSettings");
            if (settings == null)
                settings = ScriptableObject.CreateInstance<QuantizeSettings>();

            float duration = _session.Song != null ? _session.Song.DurationSeconds : 0f;
            if (duration <= 0.01f)
            {
                for (int i = 0; i < _session.RawHits.Count; i++)
                {
                    if (_session.RawHits[i].TimeSeconds > duration)
                        duration = _session.RawHits[i].TimeSeconds;
                }
            }

            BeatQuantizer quantizer = new BeatQuantizer(settings);
            List<Note> notes = quantizer.Quantize(_session.RawHits, _bpm, duration);

            Debug.Log($"[PlaybackController] Ноты не были готовы — квантовано {_session.RawHits.Count} ударов " +
                      $"→ {notes.Count} нот (BPM={_bpm:0.#}).");

            return notes ?? new List<Note>();
        }

        private float ResolveBpm()
        {
            if (_session != null && _session.Song != null && _session.Song.BPM > 0.01f)
                return _session.Song.BPM;

            return _defaultBpm > 0.01f ? _defaultBpm : 120f;
        }

        private float ResolveSongDuration()
        {
            if (IsSongAudioReady && _songPlayback.Length > 0f)
                return _songPlayback.Length;

            if (HasLocalAudio)
                return _audioSource.clip.length;

            if (_session != null && _session.Song != null && _session.Song.DurationSeconds > 0f)
                return _session.Song.DurationSeconds;

            return 0f;
        }

        private void ResolveReferences()
        {
            if (_session == null)
                _session = RecordSession.Instance;

            if (_runeSpawner == null)
                _runeSpawner = GetComponent<RuneSpawner>();

            if (_runeSpawner == null)
                _runeSpawner = FindAnyObjectByType<RuneSpawner>();

            if (_drumManager == null)
                _drumManager = FindAnyObjectByType<VR.DrumManager>();

            ResolveAudioReferences();
            SubscribeSpawner();
        }

        private void ResolveAudioReferences()
        {
            if (_songPlayback == null)
            {
                _songPlayback = FindAnyObjectByType<VR.SongPlaybackBootstrap>();
                _songPlaybackSource = null;
            }

            if (_songPlayback == null && _autoCreateSongPlayback && !_songCreateAttempted &&
                !string.IsNullOrEmpty(VR.SongSelectionState.SelectedPath))
            {
                _songCreateAttempted = true;
                _songPlayback = gameObject.AddComponent<VR.SongPlaybackBootstrap>();
                _songPlaybackSource = null;
                Debug.Log("[PlaybackController] SongPlaybackBootstrap создан в рантайме (трек выбран, в сцене его нет).");
            }

            if (_audioSource == null && _songPlayback == null)
                _audioSource = GetComponent<AudioSource>();
        }

        private void SubscribeSpawner()
        {
            if (_spawnerSubscribed || _runeSpawner == null)
                return;

            _runeSpawner.OnRuneHit += HandleRuneHit;
            _spawnerSubscribed = true;
        }

        private void UnsubscribeSpawner()
        {
            if (!_spawnerSubscribed)
                return;

            if (_runeSpawner != null)
                _runeSpawner.OnRuneHit -= HandleRuneHit;

            _spawnerSubscribed = false;
        }

        private void HandleRuneHit(int noteIndex, int drumIndex)
        {
            OnNoteHit?.Invoke(drumIndex);
        }

        // ------------------------------------------------------------ Аудио

        private bool IsSongAudioReady => _songPlayback != null && _songPlayback.IsReady;

        private bool HasLocalAudio => _audioSource != null && _audioSource.clip != null;

        private bool IsAudioPlaying
        {
            get
            {
                if (IsSongAudioReady)
                    return _songPlayback.IsPlaying;

                if (HasLocalAudio)
                    return _audioSource.isPlaying;

                return false;
            }
        }

        private float GetAudioPosition()
        {
            if (IsSongAudioReady)
                return Mathf.Max(0f, _songPlayback.Position);

            if (HasLocalAudio)
                return Mathf.Max(0f, _audioSource.time);

            return 0f;
        }

        private AudioSource GetSongAudioSource()
        {
            if (_songPlaybackSource == null && _songPlayback != null)
                _songPlaybackSource = _songPlayback.GetComponent<AudioSource>();

            return _songPlaybackSource;
        }

        private void ApplySpeedToAudio()
        {
            float pitch = Mathf.Clamp(_playbackSpeed, 0.25f, 2f);

            if (_audioSource != null)
                _audioSource.pitch = pitch;

            AudioSource songSource = GetSongAudioSource();
            if (songSource != null && songSource != _audioSource)
                songSource.pitch = pitch;
        }

        private void StartAudioFromStart()
        {
            ApplySpeedToAudio();

            if (IsSongAudioReady)
            {
                _songPlayback.PlayFromStart();
                _waitingForAudio = false;
                return;
            }

            if (HasLocalAudio)
            {
                _audioSource.Stop();
                _audioSource.time = 0f;
                _audioSource.Play();
                _waitingForAudio = false;
                return;
            }

            // Трек выбран, но ещё декодируется — стартуем по таймеру и синхронизируемся позже.
            _waitingForAudio = _songPlayback != null && _songPlayback.HasSong;
        }

        private void TryStartPendingAudio()
        {
            if (!_waitingForAudio)
                return;

            if (!IsSongAudioReady)
                return;

            _waitingForAudio = false;
            ApplySpeedToAudio();

            _songPlayback.PlayFromStart();

            if (_playbackTime > 0.05f)
                _songPlayback.Seek(_playbackTime);

            Debug.Log($"[PlaybackController] Аудио готово — синхронизация на {_playbackTime:F2}с");
        }

        private void PauseAudio()
        {
            if (IsSongAudioReady)
            {
                _songPlayback.Pause();
                return;
            }

            if (_audioSource != null)
                _audioSource.Pause();
        }

        private void ResumeAudio()
        {
            if (IsSongAudioReady)
            {
                _songPlayback.Resume();
                return;
            }

            if (_audioSource != null)
                _audioSource.UnPause();
        }

        private void StopAudio()
        {
            if (_songPlayback != null)
                _songPlayback.StopPlayback();

            if (_audioSource != null)
                _audioSource.Stop();

            _waitingForAudio = false;
        }

        private void SeekAudio(float seconds)
        {
            if (IsSongAudioReady)
            {
                _songPlayback.Seek(seconds);
                return;
            }

            if (HasLocalAudio)
                _audioSource.time = Mathf.Clamp(seconds, 0f, _audioSource.clip.length);
        }
    }
}
