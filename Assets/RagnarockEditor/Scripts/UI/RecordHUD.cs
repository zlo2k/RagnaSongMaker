using UnityEngine;
using UnityEngine.UI;
using RagnarockEditor.Recording;
using RagnarockEditor.Data;

namespace RagnarockEditor.UI
{
    /// <summary>
    /// HUD во время записи: прогресс, счётчики ударов по барабанам, пауза, перемотка.
    /// Данные берутся из RecordManager (если он есть) или из RecordSession.
    /// </summary>
    public class RecordHUD : MonoBehaviour
    {
        [Header("Прогресс")]
        [SerializeField] private Image _progressFill;
        [SerializeField] private Text _timeText;

        [Header("Счётчики ударов")]
        [Tooltip("Тексты счётчиков по барабанам: индекс 0-3. Пустые элементы игнорируются.")]
        [SerializeField] private Text[] _drumHitCounts; // 4 текста

        [Header("Кнопки")]
        [SerializeField] private Button _pauseButton;
        [SerializeField] private Text _pauseButtonText;
        [SerializeField] private Button _rewindButton;
        [SerializeField] private Button _stopButton;

        [Header("Состояние")]
        [SerializeField] private Text _stateText;

        [Header("Источник данных")]
        [Tooltip("Если пусто — RecordManager ищется в сцене.")]
        [SerializeField] private RecordManager _recordManager;

        private RecordSession _session;
        private int[] _lastCounts = new int[4] { -1, -1, -1, -1 };

        private void Start()
        {
            if (_recordManager == null)
                _recordManager = FindFirstObjectByType<RecordManager>();

            _session = RecordSession.Instance;

            if (_recordManager != null)
            {
                _recordManager.OnTimeUpdated += UpdateTimeDisplay;
                _recordManager.OnStateChanged += OnStateChanged;
                _recordManager.OnCountdownTick += OnCountdownTick;

                // Сразу показываем актуальное состояние и время.
                OnStateChanged(_recordManager.CurrentState);
                UpdateTimeDisplay(_recordManager.ElapsedSeconds);
            }

            if (_pauseButton != null)
                _pauseButton.onClick.AddListener(OnPause);

            if (_rewindButton != null)
                _rewindButton.onClick.AddListener(OnRewind);

            if (_stopButton != null)
                _stopButton.onClick.AddListener(OnStop);
        }

        private void Update()
        {
            UpdateDrumCounters();
        }

        /// <summary>
        /// Счётчики по каждому барабану (выводятся, только если поля заданы).
        /// </summary>
        private void UpdateDrumCounters()
        {
            if (_drumHitCounts == null || _drumHitCounts.Length == 0)
                return;

            int[] counts = null;

            if (_recordManager != null)
                counts = _recordManager.GetHitsPerDrum();
            else if (_session != null)
                counts = _session.GetHitsPerDrum();

            if (counts == null)
                return;

            int length = Mathf.Min(Mathf.Min(counts.Length, _drumHitCounts.Length), _lastCounts.Length);

            for (int i = 0; i < length; i++)
            {
                if (_lastCounts[i] == counts[i])
                    continue;

                _lastCounts[i] = counts[i];

                if (_drumHitCounts[i] != null)
                    _drumHitCounts[i].text = $"Барабан {i + 1}: {counts[i]}";
            }
        }

        private void UpdateTimeDisplay(float time)
        {
            if (_timeText != null)
            {
                int mins = Mathf.FloorToInt(time / 60f);
                int secs = Mathf.FloorToInt(time % 60f);
                _timeText.text = $"{mins:D2}:{secs:D2}";
            }

            if (_progressFill != null && _recordManager != null)
            {
                float duration = _recordManager.SongDuration;
                _progressFill.fillAmount = duration > 0f ? Mathf.Clamp01(time / duration) : 0f;
            }
        }

        private void OnStateChanged(RecordManager.State state)
        {
            if (_stateText != null)
            {
                _stateText.text = state switch
                {
                    RecordManager.State.Idle => "Готов",
                    RecordManager.State.Countdown => "Приготовьтесь...",
                    RecordManager.State.Recording => "Запись",
                    RecordManager.State.Paused => "Пауза",
                    RecordManager.State.Finished => "Завершено",
                    _ => ""
                };
            }

            // Новый дубль — счётчики нужно перерисовать, даже если они нулевые.
            if (state == RecordManager.State.Countdown)
            {
                for (int i = 0; i < _lastCounts.Length; i++)
                    _lastCounts[i] = -1;
            }

            UpdatePauseButton();
        }

        /// <summary>
        /// Тик отсчёта 3-2-1.
        /// </summary>
        private void OnCountdownTick(int value)
        {
            if (_stateText != null && value > 0)
                _stateText.text = $"Старт через {value}";
        }

        private void OnPause()
        {
            if (_recordManager != null)
            {
                _recordManager.TogglePause();
            }

            UpdatePauseButton();
        }

        private void UpdatePauseButton()
        {
            if (_pauseButtonText == null)
                return;

            bool paused = _recordManager != null &&
                          _recordManager.CurrentState == RecordManager.State.Paused;

            _pauseButtonText.text = paused ? "▶" : "⏸";
        }

        private void OnRewind()
        {
            if (_recordManager != null)
                _recordManager.RewindBack(5f);
        }

        private void OnStop()
        {
            if (_recordManager != null)
                _recordManager.StopRecording();
        }

        private void OnDestroy()
        {
            if (_recordManager != null)
            {
                _recordManager.OnTimeUpdated -= UpdateTimeDisplay;
                _recordManager.OnStateChanged -= OnStateChanged;
                _recordManager.OnCountdownTick -= OnCountdownTick;
            }
        }
    }
}
