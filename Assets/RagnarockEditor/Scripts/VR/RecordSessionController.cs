using UnityEngine;
using UnityEngine.UI;
using RagnarockEditor.Core;
using RagnarockEditor.Data;
using RagnarockEditor.Localization;
using RagnarockEditor.Recording;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Экран записи как явный автомат состояний. От состояния зависит, какие
    /// элементы вообще видны игроку:
    ///
    ///   NoSong   — трек не выбран: только «выбрать песню» / «игра без песни», без Play/Pause/Stop;
    ///   Ready    — трек выбран: песня + BPM + Play, без Pause/Stop;
    ///   Countdown— отсчёт 3-2-1: HUD и Stop, без Play/Pause;
    ///   Recording— HUD, Pause/Stop/Replay;
    ///   Paused   — HUD и пауза/стоп/перемотка;
    ///   Finished — итог дубля, экспорт/предпросмотр/ещё дубль.
    ///
    /// Барабаны при этом видны всегда: их не прячет ни одно состояние.
    /// </summary>
    public class RecordSessionController : MonoBehaviour
    {
        public enum ScreenState
        {
            NoSong,
            Ready,
            Countdown,
            Recording,
            Paused,
            Finished
        }

        [Header("Панели состояний")]
        [SerializeField] private GameObject _noSongPanel;
        [SerializeField] private GameObject _readyPanel;
        [SerializeField] private GameObject _finishedPanel;
        [SerializeField] private GameObject _hudPanel;

        [Header("Тексты")]
        [SerializeField] private Text _songLabel;
        [SerializeField] private Text _bpmLabel;
        [SerializeField] private Text _finishedSummary;
        [SerializeField] private Text _timeLabel;
        [SerializeField] private Text _hitsLabel;
        [SerializeField] private Text _countdownLabel;

        [Header("Прогресс")]
        [SerializeField] private RectTransform _progressFill;

        [Header("uGUI-кнопки")]
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _chooseSongButton;
        [SerializeField] private Button _freePlayButton;
        [SerializeField] private Button _anotherSongButton;
        [SerializeField] private Button _exportButton;
        [SerializeField] private Button _previewButton;

        [Header("Stone-кнопки (видимость по состоянию)")]
        [SerializeField] private StoneButton _stonePlay;
        [SerializeField] private StoneButton _stonePause;
        [SerializeField] private StoneButton _stoneStop;
        [SerializeField] private StoneButton _stoneReplay;
        [SerializeField] private StoneButton _stoneExport;
        [SerializeField] private StoneButton _stoneSettings;

        private SongPlaybackBootstrap _song;
        private DrumManager _drums;
        private RecordManager _recordManager;

        private ScreenState _state = ScreenState.NoSong;
        private bool _freePlay;
        private int _hits;
        private float _startedAt;
        private int _lastObservedHits = -1;

        // ------------------------------------------------------------ Свойства

        /// <summary>Текущее состояние экрана записи.</summary>
        public ScreenState State => _state;

        /// <summary>Сессия идёт без трека (свободная игра).</summary>
        public bool IsFreePlay => _freePlay;

        /// <summary>Есть ли выбранный трек.</summary>
        private bool HasSong =>
            !string.IsNullOrEmpty(SongSelectionState.SelectedPath) ||
            (RecordSession.Instance != null && RecordSession.Instance.Song != null);

        // -------------------------------------------------------- Unity-цикл

        private void Start()
        {
            Loc.Initialize();

            _song = Object.FindFirstObjectByType<SongPlaybackBootstrap>();
            _drums = Object.FindFirstObjectByType<DrumManager>();
            _recordManager = Object.FindFirstObjectByType<RecordManager>();

            if (_recordManager != null)
            {
                _recordManager.OnStateChanged += HandleRecordStateChanged;
                _recordManager.OnCountdownTick += HandleCountdownTick;
            }

            if (_chooseSongButton != null)
                _chooseSongButton.onClick.AddListener(ChooseSong);

            if (_startButton != null)
                _startButton.onClick.AddListener(StartSession);

            if (_freePlayButton != null)
                _freePlayButton.onClick.AddListener(StartFreePlay);

            if (_anotherSongButton != null)
                _anotherSongButton.onClick.AddListener(ChooseSong);

            if (_exportButton != null)
                _exportButton.onClick.AddListener(ExportNow);

            if (_previewButton != null)
                _previewButton.onClick.AddListener(OpenPreview);

            Loc.OnLanguageChanged += RefreshTexts;

            SetState(HasSong ? ScreenState.Ready : ScreenState.NoSong);
        }

        private void OnDestroy()
        {
            Loc.OnLanguageChanged -= RefreshTexts;

            if (_recordManager != null)
            {
                _recordManager.OnStateChanged -= HandleRecordStateChanged;
                _recordManager.OnCountdownTick -= HandleCountdownTick;
            }
        }

        private void Update()
        {
            if (_state != ScreenState.Recording &&
                _state != ScreenState.Paused &&
                _state != ScreenState.Countdown)
                return;

            UpdateHud();
            TrackHits();
        }

        // ------------------------------------------------------------ Действия

        /// <summary>Начать дубль (Stone Play).</summary>
        public void StartSession()
        {
            if (_state == ScreenState.Recording || _state == ScreenState.Paused ||
                _state == ScreenState.Countdown)
                return;

            // Трек ещё грузится асинхронно: без звука записывать бессмысленно
            if (HasSong && _song != null && _song.HasSong && !_song.IsReady)
            {
                var dialog = ConfirmDialog.Instance;
                if (dialog != null)
                {
                    dialog.Show(
                        string.Empty,
                        Loc.T(LocKeys.SongsLoading, SongSelectionState.SelectedName),
                        Loc.T(LocKeys.CommonOk),
                        dialog.Hide);
                }

                Debug.Log("[Record] трек ещё не загружен — старт отложен");
                return;
            }

            _freePlay = !HasSong;
            _hits = 0;
            _lastObservedHits = -1;
            _startedAt = Time.time;

            AppFlow.Instance.MarkDirty();

            if (_recordManager != null)
            {
                _recordManager.StartRecording();
                Debug.Log("[Record] сессия начата (RecordManager)");
                return;
            }

            if (_song != null)
            {
                if (_song.IsReady)
                {
                    _song.PlayFromStart();
                }
                else if (_song.HasSong)
                {
                    Debug.LogWarning("[Record] трек ещё загружается — запись без звука");
                }
            }

            SetState(ScreenState.Recording);
            Debug.Log("[Record] сессия начата (без RecordManager)");
        }

        /// <summary>Свободная игра без трека (кнопка в состоянии NoSong).</summary>
        public void StartFreePlay()
        {
            _freePlay = true;
            StartSession();
        }

        /// <summary>Остановить запись (Stone Stop).</summary>
        public void StopSession()
        {
            if (_state == ScreenState.NoSong || _state == ScreenState.Ready ||
                _state == ScreenState.Finished)
            {
                return;
            }

            if (_recordManager != null)
            {
                _recordManager.StopRecording();
                Debug.Log($"[Record] стоп, ударов: {_recordManager.HitCount}");
                return;
            }

            if (_song != null)
                _song.StopPlayback();

            RestoreSongPosition();
            SetState(_hits > 0 ? ScreenState.Finished : ScreenState.Ready);
            Debug.Log($"[Record] стоп, ударов: {_hits}");
        }

        /// <summary>Пауза / продолжение (Stone Pause).</summary>
        public void TogglePause()
        {
            if (_recordManager != null)
            {
                _recordManager.TogglePause();
                return;
            }

            if (_song == null)
                return;

            if (_state == ScreenState.Recording)
            {
                _song.Pause();
                SetState(ScreenState.Paused);
            }
            else if (_state == ScreenState.Paused)
            {
                _song.Resume();
                SetState(ScreenState.Recording);
            }
        }

        /// <summary>Перемотка назад на 5 секунд (если включена в HUD).</summary>
        public void Rewind()
        {
            _recordManager?.RewindBack(5f);
        }

        /// <summary>Ещё один дубль: стоп и сразу старт с начала трека (Stone Replay).</summary>
        public void RestartSession()
        {
            if (_state == ScreenState.Recording || _state == ScreenState.Paused ||
                _state == ScreenState.Countdown)
            {
                StopSession();
            }

            StartSession();
        }

        /// <summary>
        /// Кнопка Back. Возвращает true, если экран сам обработал нажатие.
        /// Запись при этом останавливается, чтобы звук не «догонял» игрока в меню.
        /// </summary>
        public bool HandleBack()
        {
            if (_state == ScreenState.Recording || _state == ScreenState.Paused ||
                _state == ScreenState.Countdown)
            {
                StopSession();
                Debug.Log("[Record] Back во время записи — запись остановлена");
            }

            return false;
        }

        /// <summary>Открыть выбор песни.</summary>
        public void ChooseSong()
        {
            AppFlow.Instance.TryLeaveTo(AppFlow.SongSelectionScene);
        }

        /// <summary>Экспорт дубля (Stone Export / кнопка на панели итога).</summary>
        public void ExportNow()
        {
            AppFlow.Instance.SaveThen(result =>
            {
                var dialog = ConfirmDialog.Instance;
                if (dialog != null)
                {
                    dialog.Show(
                        string.Empty,
                        Loc.T(LocKeys.ExportSuccess, result.FolderPath),
                        Loc.T(LocKeys.CommonOk),
                        dialog.Hide);
                }

                ApplyState();
            });
        }

        /// <summary>Предпросмотр записанного дубля.</summary>
        public void OpenPreview()
        {
            AppFlow.Instance.TryLeaveTo(AppFlow.PreviewScene);
        }

        // ---------------------------------------------------------- Состояния

        private void SetState(ScreenState newState)
        {
            if (_state == newState)
            {
                ApplyState();
                return;
            }

            _state = newState;

            if (_state == ScreenState.Finished)
            {
                RefreshTexts();

                // Диагностика: видно, что удары распределились по разным барабанам
                var perDrum = RecordSession.Instance.GetHitsPerDrum();
                Debug.Log($"[Record] удары по барабанам: 0:{perDrum[0]} 1:{perDrum[1]} 2:{perDrum[2]} 3:{perDrum[3]}");
            }

            ApplyState();
            Debug.Log("[Record] состояние экрана: " + _state);
        }

        /// <summary>Показывает ровно те элементы, которые нужны в текущем состоянии.</summary>
        private void ApplyState()
        {
            SetActive(_noSongPanel, _state == ScreenState.NoSong);
            SetActive(_readyPanel, _state == ScreenState.Ready);
            SetActive(_finishedPanel, _state == ScreenState.Finished);

            bool hud = _state == ScreenState.Countdown ||
                       _state == ScreenState.Recording ||
                       _state == ScreenState.Paused;

            SetActive(_hudPanel, hud);
            SetActive(_countdownLabel != null ? _countdownLabel.gameObject : null, _state == ScreenState.Countdown);

            // Stone-кнопки: лишние физически исчезают, а не просто блокируются
            _stonePlay?.SetVisible(_state == ScreenState.Ready);
            _stonePause?.SetVisible(_state == ScreenState.Recording || _state == ScreenState.Paused);
            _stoneStop?.SetVisible(hud);
            _stoneReplay?.SetVisible(_state == ScreenState.Recording ||
                                     _state == ScreenState.Paused ||
                                     _state == ScreenState.Finished);
            _stoneExport?.SetVisible(_state == ScreenState.Finished);
            _stoneSettings?.SetVisible(_state == ScreenState.NoSong || _state == ScreenState.Ready);

            RefreshTexts();
        }

        private static void SetActive(GameObject go, bool value)
        {
            if (go != null && go.activeSelf != value)
                go.SetActive(value);
        }

        private void RefreshTexts()
        {
            if (_songLabel != null)
            {
                _songLabel.text = HasSong
                    ? SongSelectionState.SelectedName
                    : Loc.T(LocKeys.RecordNoSongTitle);
            }

            if (_bpmLabel != null)
            {
                float bpm = ResolveBpm();
                _bpmLabel.text = bpm > 0f
                    ? Loc.T(LocKeys.RecordBpm, bpm.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                    : string.Empty;

                _bpmLabel.gameObject.SetActive(bpm > 0f);
            }

            if (_finishedSummary != null)
            {
                _finishedSummary.text = Loc.T(LocKeys.RecordFinishedSummary, _hits, Format(ResolveDuration()));
            }

            if (_hitsLabel != null)
                _hitsLabel.text = Loc.T(LocKeys.HudHits, _hits);

            if (_timeLabel != null)
                UpdateTimeLabel();
        }

        // ------------------------------------------------------ запись и HUD

        private void HandleRecordStateChanged(RecordManager.State state)
        {
            switch (state)
            {
                case RecordManager.State.Countdown:
                    SetState(ScreenState.Countdown);
                    break;

                case RecordManager.State.Recording:
                    SetState(ScreenState.Recording);
                    break;

                case RecordManager.State.Paused:
                    SetState(ScreenState.Paused);
                    break;

                case RecordManager.State.Finished:
                    // счётчик берём до отрисовки итога: Update менеджера мог отработать раньше нашего
                    _hits = _recordManager != null ? _recordManager.HitCount : _hits;

                    // пустой дубль — не «итог», а повод попробовать ещё раз
                    SetState(_hits > 0 ? ScreenState.Finished : ScreenState.Ready);
                    break;

                case RecordManager.State.Idle:
                    SetState(HasSong ? ScreenState.Ready : ScreenState.NoSong);
                    break;
            }
        }

        private void HandleCountdownTick(int seconds)
        {
            if (_countdownLabel == null)
                return;

            _countdownLabel.text = Loc.T(LocKeys.RecordCountdown, seconds);
        }

        private void TrackHits()
        {
            int hits = _recordManager != null
                ? _recordManager.HitCount
                : _hits;

            if (hits == _lastObservedHits)
                return;

            _lastObservedHits = hits;
            _hits = hits;

            if (_hits > 0)
                AppFlow.Instance.MarkDirty();

            if (_hitsLabel != null)
                _hitsLabel.text = Loc.T(LocKeys.HudHits, _hits);
        }

        private void UpdateHud()
        {
            if (_recordManager != null)
            {
                _hits = _recordManager.HitCount;
            }

            UpdateProgress();
            UpdateTimeLabel();
        }

        private void UpdateProgress()
        {
            if (_progressFill == null)
                return;

            float duration = ResolveDuration();
            float position = _recordManager != null
                ? Mathf.Max(0f, _recordManager.ElapsedSeconds)
                : (_song != null ? Mathf.Max(0f, _song.Position) : Time.time - _startedAt);

            float value = duration > 0.01f ? Mathf.Clamp01(position / duration) : 0f;

            _progressFill.anchorMin = new Vector2(0f, 0f);
            _progressFill.anchorMax = new Vector2(value, 1f);
            _progressFill.offsetMin = Vector2.zero;
            _progressFill.offsetMax = Vector2.zero;
        }

        private void UpdateTimeLabel()
        {
            if (_timeLabel == null)
                return;

            float duration = ResolveDuration();
            float position = _recordManager != null
                ? Mathf.Max(0f, _recordManager.ElapsedSeconds)
                : (_song != null ? Mathf.Max(0f, _song.Position) : Time.time - _startedAt);

            _timeLabel.text = duration > 0.01f
                ? $"{Format(position)} / {Format(duration)}"
                : Format(position);
        }

        private float ResolveDuration()
        {
            if (_recordManager != null && _recordManager.SongDuration > 0.01f)
                return _recordManager.SongDuration;

            if (_song != null && _song.Length > 0.01f)
                return _song.Length;

            var session = RecordSession.Instance;
            return session != null && session.Song != null ? session.Song.DurationSeconds : 0f;
        }

        private float ResolveBpm()
        {
            if (SongSelectionState.SelectedBpm > 0f)
                return SongSelectionState.SelectedBpm;

            var session = RecordSession.Instance;
            return session != null && session.Song != null ? session.Song.BPM : 0f;
        }

        private void RestoreSongPosition()
        {
            if (_song != null && _song.IsReady)
                _song.Seek(0f);
        }

        private static string Format(float seconds)
        {
            if (seconds < 0f)
                seconds = 0f;

            var total = Mathf.FloorToInt(seconds);
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
