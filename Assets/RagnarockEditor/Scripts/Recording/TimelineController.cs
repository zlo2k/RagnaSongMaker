using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.Recording
{
    /// <summary>
    /// Управляет временной шкалой: отображение прогресса, пауза, перемотка.
    /// </summary>
    public class TimelineController : MonoBehaviour
    {
        [SerializeField] private RecordManager _recordManager;
        [SerializeField] private Slider _progressSlider;
        [SerializeField] private Text _currentTimeText;
        [SerializeField] private Text _totalTimeText;
        [SerializeField] private Button _pauseButton;
        [SerializeField] private Button _rewindButton;
        [SerializeField] private Button _stopButton;
        [SerializeField] private Text _pauseButtonText;

        private bool _isPaused;
        private bool _isUserSeeking; // Защита от рекурсии при обновлении слайдера

        private void Start()
        {
            if (_recordManager != null)
            {
                _recordManager.OnTimeUpdated += UpdateTimeDisplay;
                _recordManager.OnStateChanged += OnStateChanged;
            }

            if (_pauseButton != null)
                _pauseButton.onClick.AddListener(OnPauseClicked);

            if (_rewindButton != null)
                _rewindButton.onClick.AddListener(OnRewindClicked);

            if (_stopButton != null)
                _stopButton.onClick.AddListener(OnStopClicked);

            if (_progressSlider != null)
                _progressSlider.onValueChanged.AddListener(OnSliderChanged);

            // Отображаем общую длительность
            if (_totalTimeText != null && _recordManager != null)
            {
                _totalTimeText.text = FormatTime(_recordManager.SongDuration);
            }
        }

        private void UpdateTimeDisplay(float time)
        {
            if (_currentTimeText != null)
                _currentTimeText.text = FormatTime(time);

            if (_progressSlider != null && !_isUserSeeking)
            {
                float duration = _recordManager.SongDuration;
                _progressSlider.SetValueWithoutNotify(duration > 0f ? time / duration : 0f);
            }
        }

        private void OnSliderChanged(float value)
        {
            if (!_isUserSeeking) return;

            float time = value * _recordManager.SongDuration;
            // Перемотка через слайдер
            if (_recordManager.CurrentState == RecordManager.State.Recording ||
                _recordManager.CurrentState == RecordManager.State.Paused)
            {
                // Здесь можно реализовать seek через AudioSource.time
                Debug.Log($"[TimelineController] Seek to {FormatTime(time)}");
            }
        }

        public void OnSliderBeginDrag()
        {
            _isUserSeeking = true;
        }

        public void OnSliderEndDrag()
        {
            _isUserSeeking = false;
        }

        private void OnPauseClicked()
        {
            _recordManager?.TogglePause();
            SyncPauseButton();
        }

        private void SyncPauseButton()
        {
            _isPaused = _recordManager != null &&
                        _recordManager.CurrentState == RecordManager.State.Paused;

            if (_pauseButtonText != null)
                _pauseButtonText.text = _isPaused ? "▶" : "⏸";
        }

        private void OnRewindClicked()
        {
            _recordManager?.Rewind();
        }

        private void OnStopClicked()
        {
            _recordManager?.FinishRecording();
        }

        private void OnStateChanged(RecordManager.State state)
        {
            // После завершения кнопки блокируются, при новом отсчёте — снова доступны.
            bool interactable = state != RecordManager.State.Finished;

            if (_pauseButton != null) _pauseButton.interactable = interactable;
            if (_rewindButton != null) _rewindButton.interactable = interactable;
            if (_stopButton != null) _stopButton.interactable = interactable;

            SyncPauseButton();
        }

        private static string FormatTime(float seconds)
        {
            int mins = Mathf.FloorToInt(seconds / 60f);
            int secs = Mathf.FloorToInt(seconds % 60f);
            return $"{mins:D2}:{secs:D2}";
        }

        private void OnDestroy()
        {
            if (_recordManager != null)
            {
                _recordManager.OnTimeUpdated -= UpdateTimeDisplay;
                _recordManager.OnStateChanged -= OnStateChanged;
            }
        }
    }
}
