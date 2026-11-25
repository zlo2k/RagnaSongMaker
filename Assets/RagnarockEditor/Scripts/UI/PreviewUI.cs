using UnityEngine;
using UnityEngine.UI;
using RagnarockEditor.Playback;
using RagnarockEditor.Quantization;
using RagnarockEditor.Data;
using RagnarockEditor.Core;
using RagnarockEditor.Localization;

namespace RagnarockEditor.UI
{
    /// <summary>
    /// UI предпросмотра уровня: управление воспроизведением, квантование, сохранение.
    /// </summary>
    public class PreviewUI : MonoBehaviour
    {
        [Header("Воспроизведение")]
        [SerializeField] private PlaybackController _playbackController;
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _pauseButton;
        [SerializeField] private Button _stopButton;
        [SerializeField] private Slider _speedSlider;
        [SerializeField] private Text _speedText;

        [Header("Квантование")]
        [SerializeField] private Slider _snapThresholdSlider;
        [SerializeField] private Text _snapThresholdText;
        [SerializeField] private Slider _snapDivisionSlider;
        [SerializeField] private Text _snapDivisionText;
        [SerializeField] private Button _quantizeButton;
        [SerializeField] private Text _notesCountText;

        [Header("Сохранение")]
        [SerializeField] private Button _saveButton;
        [SerializeField] private Button _backButton;

        private RecordSession _session;
        private BeatQuantizer _quantizer;
        private QuantizeSettings _quantizeSettings;

        private void Start()
        {
            _session = RecordSession.Instance;
            _quantizeSettings = Resources.Load<QuantizeSettings>("QuantizeSettings");
            if (_quantizeSettings == null)
                _quantizeSettings = ScriptableObject.CreateInstance<QuantizeSettings>();

            _quantizer = new BeatQuantizer(_quantizeSettings);

            // Playback
            if (_playButton != null)
                _playButton.onClick.AddListener(OnPlay);

            if (_pauseButton != null)
                _pauseButton.onClick.AddListener(OnPause);

            if (_stopButton != null)
                _stopButton.onClick.AddListener(OnStop);

            if (_speedSlider != null)
            {
                _speedSlider.onValueChanged.AddListener(OnSpeedChanged);
                _speedSlider.value = 1f;
            }

            // Quantization
            if (_snapThresholdSlider != null)
            {
                _snapThresholdSlider.minValue = 0.02f;
                _snapThresholdSlider.maxValue = 0.5f;
                _snapThresholdSlider.value = _quantizeSettings.SnapThreshold;
                _snapThresholdSlider.onValueChanged.AddListener(OnThresholdChanged);
            }

            if (_snapDivisionSlider != null)
            {
                _snapDivisionSlider.minValue = 1f;
                _snapDivisionSlider.maxValue = 4f; // 1,2,3,4 → 1,2,4,8
                _snapDivisionSlider.wholeNumbers = true;
                _snapDivisionSlider.value = Mathf.Log(_quantizeSettings.SnapDivision, 2) + 1;
                _snapDivisionSlider.onValueChanged.AddListener(OnDivisionChanged);
            }

            if (_quantizeButton != null)
                _quantizeButton.onClick.AddListener(OnQuantize);

            // Save
            if (_saveButton != null)
                _saveButton.onClick.AddListener(OnSave);

            if (_backButton != null)
                _backButton.onClick.AddListener(OnBack);

            // Показываем начальное состояние
            UpdateNotesCount();
            UpdateSnapDisplay();
        }

        private void OnPlay() => _playbackController?.Play();
        private void OnPause() => _playbackController?.TogglePause();
        private void OnStop() => _playbackController?.Stop();

        private void OnSpeedChanged(float value)
        {
            if (_playbackController != null)
                _playbackController.PlaybackSpeed = value;

            if (_speedText != null)
                _speedText.text = $"{value:F2}x";
        }

        private void OnThresholdChanged(float value)
        {
            _quantizeSettings.SnapThreshold = value;
            UpdateSnapDisplay();
        }

        private void OnDivisionChanged(float value)
        {
            int division = Mathf.RoundToInt(Mathf.Pow(2, value - 1));
            _quantizeSettings.SnapDivision = division;
            UpdateSnapDisplay();
        }

        private void UpdateSnapDisplay()
        {
            if (_snapThresholdText != null)
                _snapThresholdText.text = $"Порог: {_quantizeSettings.SnapThreshold * 1000:F0} мс";

            if (_snapDivisionText != null)
                _snapDivisionText.text = $"Доли: 1/{_quantizeSettings.SnapDivision}";
        }

        /// <summary>
        /// Запускает квантование сырых ударов.
        /// </summary>
        public void OnQuantize()
        {
            if (_session.RawHits.Count == 0)
            {
                Debug.LogWarning("[PreviewUI] Нет сырых ударов для квантования.");
                return;
            }

            _session.QuantizedNotes = _quantizer.Quantize(
                _session.RawHits,
                _session.Song.BPM,
                _session.Song.DurationSeconds
            );

            UpdateNotesCount();

            // Подготавливаем предпросмотр с новыми нотами
            _playbackController?.Prepare(_session.QuantizedNotes);
        }

        private void UpdateNotesCount()
        {
            if (_notesCountText != null)
            {
                _notesCountText.text = $"Ударов: {_session.TotalRawHits} → Нот: {_session.TotalQuantizedNotes}";
            }
        }

        private void OnSave()
        {
            // Тот же путь, что и у каменной ExportMap: квантование внутри LevelExporter
            // и обязательный диалог с результатом (раньше ошибка уходила в пустое событие).
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
            });
        }

        private void OnBack()
        {
            AppFlow.Instance.TryLeaveTo(AppFlow.MainMenuScene);
        }
    }
}
