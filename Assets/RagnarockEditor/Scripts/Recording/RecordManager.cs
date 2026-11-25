using System;
using UnityEngine;
using RagnarockEditor.Data;
using RagnarockEditor.VR;
using RagnarockEditor.Audio;

namespace RagnarockEditor.Recording
{
    /// <summary>
    /// Оркестратор записи уровня. Явный конечный автомат:
    /// Idle → Countdown (3-2-1) → Recording ⇄ Paused → Finished.
    ///
    /// Аудио берётся из SongPlaybackBootstrap (если он есть в сцене),
    /// иначе — из AudioSource на этом же объекте.
    /// Удары приходят из DrumManager.OnAnyDrumHit и складываются в NoteCollector.
    /// </summary>
    public class RecordManager : MonoBehaviour
    {
        public enum State { Idle, Countdown, Recording, Paused, Finished }

        [Header("Аудио")]
        [Tooltip("Запасной AudioSource, если в сцене нет SongPlaybackBootstrap.")]
        [SerializeField] private AudioSource _audioSource;
        [Tooltip("Проигрыватель выбранного трека. Если пусто — ищется в сцене.")]
        [SerializeField] private SongPlaybackBootstrap _songPlayback;

        [Header("Компоненты")]
        [SerializeField] private DrumManager _drumManager;
        [SerializeField] private NoteCollector _noteCollector;
        [SerializeField] private MetronomePlayer _metronomePlayer;
        [SerializeField] private MetronomeVisualizer _metronomeVisualizer;

        [Header("Тайминг")]
        [SerializeField] private float _countdownDuration = 3f;
        [SerializeField] private float _rewindStep = 5f;
        [Tooltip("Допуск, с которым позиция считается концом трека.")]
        [SerializeField] private float _endTolerance = 0.1f;

        private State _state = State.Idle;
        private RecordSession _session;
        private float _countdownElapsed;
        private int _lastCountdownValue = -1;
        private float _fallbackElapsed;
        private float _recordedLength;
        private bool _hitsSubscribed;
        private int _lastMetronomeBeat = -1;

        // ------------------------------------------------------------ Свойства

        /// <summary>Текущее состояние автомата.</summary>
        /// <remarks>
        /// Имя свойства — CurrentState, а не State: в C# член класса не может
        /// называться так же, как вложенный тип (CS0102), а enum называется State.
        /// </remarks>
        public State CurrentState => _state;

        /// <summary>Идёт отсчёт перед записью.</summary>
        public bool IsCountingDown => _state == State.Countdown;

        /// <summary>Запись активна (идёт или на паузе).</summary>
        public bool IsActive => _state == State.Countdown || _state == State.Recording || _state == State.Paused;

        /// <summary>Текущая позиция в треке (секунды от начала).</summary>
        public float ElapsedSeconds
        {
            get
            {
                if (_state == State.Finished)
                    return Mathf.Max(0f, _recordedLength);

                return Mathf.Max(0f, CurrentSongTime);
            }
        }

        /// <summary>Количество сырых ударов, записанных в текущем дубле.</summary>
        public int HitCount => _session != null ? _session.TotalRawHits : 0;

        /// <summary>Текущее время песни (AudioSource.time / SongPlaybackBootstrap.Position).</summary>
        public float CurrentSongTime
        {
            get
            {
                if (_songPlayback != null && _songPlayback.IsReady)
                    return _songPlayback.Position;

                if (_audioSource != null && _audioSource.clip != null)
                    return _audioSource.time;

                return _fallbackElapsed;
            }
        }

        /// <summary>Общая длительность песни (0, если трек не задан).</summary>
        public float SongDuration
        {
            get
            {
                if (_songPlayback != null && _songPlayback.IsReady && _songPlayback.Length > 0f)
                    return _songPlayback.Length;

                if (_audioSource != null && _audioSource.clip != null)
                    return _audioSource.clip.length;

                if (_session != null && _session.Song != null)
                    return _session.Song.DurationSeconds;

                return 0f;
            }
        }

        // -------------------------------------------------------------- События

        /// <summary>Смена состояния автомата.</summary>
        public event Action<State> OnStateChanged;

        /// <summary>Текущая позиция при записи/паузе (для HUD и таймлайна).</summary>
        public event Action<float> OnTimeUpdated;

        /// <summary>Тик отсчёта: 3, 2, 1.</summary>
        public event Action<int> OnCountdownTick;

        /// <summary>Запись остановлена (вручную или по концу трека).</summary>
        public event Action OnRecordingFinished;

        // -------------------------------------------------------- Unity-цикл

        private void Awake()
        {
            _session = RecordSession.Instance;
            ResolveReferences();
        }

        private void Start()
        {
            // Повторно — на случай, если объекты сцены появились позже.
            ResolveReferences();
        }

        private void OnEnable()
        {
            SubscribeDrumHits();
        }

        private void OnDisable()
        {
            UnsubscribeDrumHits();
        }

        private void Update()
        {
            switch (_state)
            {
                case State.Countdown:
                    TickCountdown();
                    break;

                case State.Recording:
                    _fallbackElapsed += Time.unscaledDeltaTime;
                    UpdateRecording();
                    break;
            }
        }

        // ------------------------------------------------------ Публичный API

        /// <summary>
        /// Начинает отсчёт 3-2-1 и затем запись. Новый дубль: прошлые удары очищаются.
        /// </summary>
        public void StartRecording()
        {
            if (IsActive)
            {
                Debug.LogWarning($"[RecordManager] Запись уже идёт ({_state}) — StartRecording проигнорирован.");
                return;
            }

            if (_session == null)
                _session = RecordSession.Instance;

            if (_session.Song == null)
            {
                // Свободная игра: пишем по внутреннему таймеру.
                Debug.LogWarning("[RecordManager] SongData не задана — запись по внутреннему таймеру.");
            }

            // Чистим результаты прошлого дубля.
            _session.ClearHits();

            _fallbackElapsed = 0f;
            _recordedLength = 0f;
            _countdownElapsed = 0f;
            _lastCountdownValue = -1;

            SetState(State.Countdown);
            StartMetronome();

            int firstTick = Mathf.Max(0, Mathf.CeilToInt(_countdownDuration));
            if (firstTick > 0)
            {
                _lastCountdownValue = firstTick;
                OnCountdownTick?.Invoke(firstTick);
                SfxPlayer.Instance.PlayUi(SfxPlayer.UiSound.CountIn);
            }

            if (_countdownDuration <= 0f)
            {
                BeginRecording();
                return;
            }

            Debug.Log($"[RecordManager] Отсчёт {_countdownDuration:0.#}с...");
        }

        /// <summary>Пауза записи (и звука, и метронома).</summary>
        public void PauseRecording()
        {
            if (_state != State.Recording)
            {
                Debug.LogWarning($"[RecordManager] PauseRecording недоступен в состоянии {_state}.");
                return;
            }

            SetState(State.Paused);
            PauseAudio();
            StopMetronome();
            OnTimeUpdated?.Invoke(ElapsedSeconds);

            Debug.Log("[RecordManager] Пауза");
        }

        /// <summary>Продолжение записи после паузы.</summary>
        public void ResumeRecording()
        {
            if (_state != State.Paused)
            {
                Debug.LogWarning($"[RecordManager] ResumeRecording недоступен в состоянии {_state}.");
                return;
            }

            SetState(State.Recording);
            ResumeAudio();
            StartMetronome();

            Debug.Log("[RecordManager] Продолжение");
        }

        /// <summary>Пауза / продолжение одним вызовом (совместимость со StoneButton).</summary>
        public void TogglePause()
        {
            if (_state == State.Recording)
                PauseRecording();
            else if (_state == State.Paused)
                ResumeRecording();
        }

        /// <summary>
        /// Перемотка назад на <paramref name="seconds"/> секунд с удалением
        /// ударов, записанных после новой позиции.
        /// </summary>
        public void RewindBack(float seconds = 5f)
        {
            if (_state != State.Recording && _state != State.Paused)
            {
                Debug.LogWarning($"[RecordManager] Перемотка недоступна в состоянии {_state}.");
                return;
            }

            float step = Mathf.Max(0f, seconds);
            float newTime = Mathf.Max(0f, CurrentSongTime - step);

            SeekAudio(newTime);
            _fallbackElapsed = newTime;
            _lastMetronomeBeat = -1;

            // Согласованно удаляем "будущие" удары из NoteCollector/сессии.
            if (_noteCollector != null)
                _noteCollector.RemoveAfter(newTime);
            else
                RemoveSessionHitsAfter(newTime);

            OnTimeUpdated?.Invoke(newTime);

            Debug.Log($"[RecordManager] Перемотка назад на {step:0.#}с → {newTime:F2}с (ударов: {HitCount})");
        }

        /// <summary>Перемотка назад на шаг по умолчанию (5с).</summary>
        public void Rewind() => RewindBack(_rewindStep);

        /// <summary>Перемотка назад на указанное число секунд.</summary>
        public void Rewind(float seconds) => RewindBack(seconds);

        /// <summary>Останавливает запись и переводит автомат в Finished.</summary>
        public void StopRecording()
        {
            if (_state == State.Idle || _state == State.Finished)
                return;

            // Фиксируем длительность дубля ДО остановки аудио (оно сбросит позицию в 0).
            _recordedLength = Mathf.Max(0f, CurrentSongTime);

            SetState(State.Finished);
            StopAudio();
            StopMetronome();

            Debug.Log($"[RecordManager] Запись завершена на {_recordedLength:F2}с. Сырых ударов: {HitCount}");
            OnRecordingFinished?.Invoke();
        }

        /// <summary>Синоним StopRecording (совместимость с TimelineController/RecordHUD).</summary>
        public void FinishRecording() => StopRecording();

        /// <summary>Вручную добавляет удар (используется NoteCollector/внешними вызовами).</summary>
        public void AddHit(int drumIndex, float timeSeconds)
        {
            if (_state != State.Recording)
                return;

            if (_noteCollector != null)
                _noteCollector.AddHit(drumIndex, timeSeconds);
            else
                AddSessionHit(timeSeconds, drumIndex);
        }

        /// <summary>Счётчики ударов по каждому барабану (индексы 0-3).</summary>
        public int[] GetHitsPerDrum()
        {
            if (_session == null)
                return new int[4];

            return _session.GetHitsPerDrum();
        }

        // ---------------------------------------------------------- Внутреннее

        private void TickCountdown()
        {
            _countdownElapsed += Time.unscaledDeltaTime;

            float remaining = _countdownDuration - _countdownElapsed;
            int value = Mathf.CeilToInt(remaining);

            if (value > 0 && value != _lastCountdownValue)
            {
                _lastCountdownValue = value;
                OnCountdownTick?.Invoke(value);
                SfxPlayer.Instance.PlayUi(SfxPlayer.UiSound.CountIn);
            }

            if (_countdownElapsed >= _countdownDuration)
                BeginRecording();
        }

        private void BeginRecording()
        {
            SetState(State.Recording);

            _fallbackElapsed = 0f;
            _lastMetronomeBeat = -1;
            OnTimeUpdated?.Invoke(0f);

            PlayAudioFromStart();
            SfxPlayer.Instance.PlayUi(SfxPlayer.UiSound.RecordStart);

            float bpm = _session != null && _session.Song != null ? _session.Song.BPM : 120f;
            Debug.Log($"[RecordManager] Запись началась. BPM={bpm:0.#}");
        }

        private void UpdateRecording()
        {
            float time = CurrentSongTime;
            OnTimeUpdated?.Invoke(time);
            TickMetronome(time);

            float duration = SongDuration;
            if (duration <= 0.01f)
                return;

            // Конец трека: позиция дошла до конца ИЛИ воспроизведение уже остановилось
            // (AudioSource после окончания может сбросить time в 0).
            bool reachedEnd = time >= duration - _endTolerance;
            bool playbackStopped = time > 0.5f && !IsAudioPlaying;

            if (reachedEnd || playbackStopped)
            {
                Debug.Log("[RecordManager] Достигнут конец трека — автостоп.");
                StopRecording();
            }
        }

        /// <summary>
        /// Тик метронома по времени песни: ровно один тик на бит BPM.
        /// Фаза привязана к треку, поэтому метроном не уплывает относительно музыки.
        /// </summary>
        private void TickMetronome(float songTime)
        {
            if (_metronomePlayer == null || !MetronomeRuntime.Enabled)
                return;

            float bpm = _session != null && _session.Song != null ? _session.Song.BPM : 120f;
            if (bpm <= 0f)
                bpm = 120f;

            float beatInterval = 60f / bpm;
            if (beatInterval <= 0.01f)
                return;

            int beatIndex = Mathf.FloorToInt(Mathf.Max(0f, songTime) / beatInterval);
            if (beatIndex <= _lastMetronomeBeat)
                return;

            _lastMetronomeBeat = beatIndex;
            _metronomePlayer.PlayTick(beatIndex % 4 == 0);
        }

        /// <summary>Играет ли сейчас аудио (при работе по внутреннему таймеру — всегда true).</summary>
        private bool IsAudioPlaying
        {
            get
            {
                if (_songPlayback != null && _songPlayback.IsReady)
                    return _songPlayback.IsPlaying;

                if (_audioSource != null && _audioSource.clip != null)
                    return _audioSource.isPlaying;

                return true;
            }
        }

        private void HandleDrumHit(int drumIndex, float eventTimeSeconds, float velocity)
        {
            if (_state != State.Recording)
                return;

            // Время удара берём из позиции трека — это согласовано с перемоткой.
            float timeSeconds = Mathf.Max(0f, CurrentSongTime);

            if (_noteCollector != null)
                _noteCollector.AddHit(drumIndex, timeSeconds);
            else
                AddSessionHit(timeSeconds, drumIndex);
        }

        private void SubscribeDrumHits()
        {
            if (_hitsSubscribed || _drumManager == null)
                return;

            _drumManager.OnAnyDrumHit += HandleDrumHit;
            _hitsSubscribed = true;
        }

        private void UnsubscribeDrumHits()
        {
            if (!_hitsSubscribed)
                return;

            if (_drumManager != null)
                _drumManager.OnAnyDrumHit -= HandleDrumHit;

            _hitsSubscribed = false;
        }

        private void ResolveReferences()
        {
            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();

            if (_songPlayback == null)
                _songPlayback = FindFirstObjectByType<SongPlaybackBootstrap>();

            if (_drumManager == null)
                _drumManager = FindFirstObjectByType<DrumManager>();

            if (_noteCollector == null)
                _noteCollector = FindFirstObjectByType<NoteCollector>();

            if (_metronomePlayer == null)
                _metronomePlayer = FindFirstObjectByType<MetronomePlayer>();

            if (_metronomeVisualizer == null)
                _metronomeVisualizer = FindFirstObjectByType<MetronomeVisualizer>();

            SubscribeDrumHits();
        }

        private void SetState(State newState)
        {
            if (_state == newState)
                return;

            _state = newState;
            OnStateChanged?.Invoke(newState);
        }

        // ------------------------------------------------------------ Аудио

        private void PlayAudioFromStart()
        {
            if (_songPlayback != null)
            {
                if (_songPlayback.IsReady)
                {
                    _songPlayback.PlayFromStart();
                    return;
                }

                if (_songPlayback.HasSong)
                    Debug.LogWarning("[RecordManager] Трек ещё не загружен SongPlaybackBootstrap — старт без звука.");
            }

            if (_audioSource != null && _audioSource.clip != null)
            {
                _audioSource.Stop();
                _audioSource.time = 0f;
                _audioSource.Play();
            }
        }

        private void PauseAudio()
        {
            if (_songPlayback != null && _songPlayback.IsReady)
            {
                _songPlayback.Pause();
                return;
            }

            if (_audioSource != null)
                _audioSource.Pause();
        }

        private void ResumeAudio()
        {
            if (_songPlayback != null && _songPlayback.IsReady)
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
        }

        private void SeekAudio(float time)
        {
            if (_songPlayback != null && _songPlayback.IsReady)
            {
                _songPlayback.Seek(time);
                return;
            }

            if (_audioSource != null)
                _audioSource.time = time;
        }

        // --------------------------------------------------------- Метроном

        private void StartMetronome()
        {
            float bpm = _session != null && _session.Song != null ? _session.Song.BPM : 120f;
            if (bpm <= 0f)
                bpm = 120f;

            if (_metronomePlayer != null)
            {
                _metronomePlayer.BPM = bpm;

                // Во время записи тики идут по времени песни (TickMetronome), а не своим циклом —
                // иначе метроном уплывает относительно музыки.
                _metronomePlayer.StopMetronome();
            }

            if (_metronomeVisualizer != null)
                _metronomeVisualizer.StopVisualization();

            _lastMetronomeBeat = -1;
        }

        private void StopMetronome()
        {
            if (_metronomePlayer != null && _metronomePlayer.IsPlaying)
                _metronomePlayer.StopMetronome();

            if (_metronomeVisualizer != null)
                _metronomeVisualizer.StopVisualization();
        }

        // ---------------------------------------------------------- Сессия

        private void AddSessionHit(float timeSeconds, int drumIndex)
        {
            if (_session == null)
                _session = RecordSession.Instance;

            _session.AddRawHit(timeSeconds, drumIndex);
        }

        private void RemoveSessionHitsAfter(float timeSeconds)
        {
            if (_session == null)
                _session = RecordSession.Instance;

            _session.RemoveHitsAfter(timeSeconds);
        }
    }
}
