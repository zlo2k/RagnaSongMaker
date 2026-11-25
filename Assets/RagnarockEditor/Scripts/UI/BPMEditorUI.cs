using System;
using System.Collections.Generic;
using RagnarockEditor.Localization;
using UnityEngine;
using UnityEngine.UI;
using RagnarockEditor.Audio;
using RagnarockEditor.Data;

namespace RagnarockEditor.UI
{
    /// <summary>
    /// UI редактора BPM: показывает результат анализа, позволяет корректировать.
    /// </summary>
    public class BPMEditorUI : MonoBehaviour
    {
        [Header("Отображение BPM")]
        [SerializeField] private Text _bpmValueText;
        [SerializeField] private Text _confidenceText;
        [SerializeField] private Slider _bpmSlider;
        [SerializeField] private InputField _bpmInput;

        [Header("Tap Tempo")]
        [SerializeField] private Button _tapButton;
        [SerializeField] private Text _tapCountText;
        [SerializeField] private Text _tapBPMText;

        [Header("Действия")]
        [SerializeField] private Button _continueButton;

        /// <summary>Событие: пользователь подтвердил BPM кнопкой «Продолжить».</summary>
        public event Action<float> OnConfirmed;

        /// <summary>Текущий BPM, отображаемый в редакторе.</summary>
        public float CurrentBpm { get; private set; } = 120f;

        private BPMDetector.BPMResult _result;
        private List<float> _tapTimes = new List<float>();
        private float _lastTapTime;

        private void Start()
        {
            if (_bpmSlider != null)
            {
                _bpmSlider.minValue = 40f;
                _bpmSlider.maxValue = 300f;
                _bpmSlider.onValueChanged.AddListener(OnSliderChanged);
            }

            if (_bpmInput != null)
                _bpmInput.onEndEdit.AddListener(OnInputChanged);

            if (_tapButton != null)
                _tapButton.onClick.AddListener(OnTap);

            if (_continueButton != null)
                _continueButton.onClick.AddListener(OnContinue);

            // Пока идёт загрузка/анализ, редактировать нечего
            SetEditingEnabled(false);

            if (_bpmValueText != null)
                _bpmValueText.text = Loc.T(LocKeys.CommonLoading);

            if (_confidenceText != null)
                _confidenceText.text = string.Empty;

            if (_tapCountText != null)
                _tapCountText.text = string.Empty;

            if (_tapBPMText != null)
                _tapBPMText.text = string.Empty;
        }

        /// <summary>
        /// Показывает результат анализа BPM.
        /// </summary>
        public void ShowBPMResult(BPMDetector.BPMResult result)
        {
            if (result == null)
            {
                Debug.LogWarning("[BPMEditorUI] ShowBPMResult: результат анализа = null");
                return;
            }

            _result = result;
            SetEditingEnabled(true);
            UpdateDisplay(result.PrimaryBPM);

            if (_confidenceText != null)
                _confidenceText.text = Loc.T(LocKeys.BpmConfidence,
                    Mathf.RoundToInt(Mathf.Clamp01(result.Confidence) * 100f));

            if (_bpmSlider != null)
                _bpmSlider.SetValueWithoutNotify(result.PrimaryBPM);
        }

        private void UpdateDisplay(float bpm)
        {
            CurrentBpm = bpm;

            if (_bpmValueText != null)
                _bpmValueText.text = Loc.T(LocKeys.BpmValue, bpm.ToString("0.#",
                    System.Globalization.CultureInfo.InvariantCulture));

            if (_bpmInput != null)
                _bpmInput.SetTextWithoutNotify($"{bpm:F1}");
        }

        /// <summary>
        /// Задаёт BPM из внешнего контрола (каменный ползунок на пульте): двигает
        /// индикатор-жёлоб, обновляет цифру и пишет значение в сессию.
        /// </summary>
        public void SetBpmFromControl(float bpm)
        {
            bpm = Mathf.Clamp(bpm, 40f, 300f);

            if (_bpmSlider != null)
                _bpmSlider.SetValueWithoutNotify(bpm);

            UpdateDisplay(bpm);
            UpdateSessionBPM(bpm);
        }

        /// <summary>Позиция текущего BPM на шкале 40..300 для каменного ползунка (0..1).</summary>
        public float BpmNormalized => Mathf.InverseLerp(40f, 300f, CurrentBpm);

        private void OnSliderChanged(float value)
        {
            UpdateDisplay(value);
            UpdateSessionBPM(value);
        }

        private void OnInputChanged(string text)
        {
            if (float.TryParse(text, out float bpm) && bpm >= 40f && bpm <= 300f)
            {
                if (_bpmSlider != null) _bpmSlider.SetValueWithoutNotify(bpm);
                UpdateDisplay(bpm);
                UpdateSessionBPM(bpm);
            }
        }

        private void OnTap()
        {
            float currentTime = Time.time;

            if (_tapTimes.Count == 0 || currentTime - _lastTapTime > 2f)
            {
                // Новый цикл тапов
                _tapTimes.Clear();
            }

            _tapTimes.Add(currentTime);
            _lastTapTime = currentTime;

            if (_tapCountText != null)
                _tapCountText.text = Loc.T(LocKeys.BpmTaps, _tapTimes.Count);

            if (_tapTimes.Count >= 3)
            {
                float bpm = BPMDetector.CalculateTapTempo(_tapTimes);
                if (_tapBPMText != null)
                    _tapBPMText.text = Loc.T(LocKeys.BpmTapValue,
                        bpm.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));

                if (_bpmSlider != null)
                    _bpmSlider.SetValueWithoutNotify(bpm);

                UpdateDisplay(bpm);
                UpdateSessionBPM(bpm);
            }
        }

        /// <summary>
        /// Готовит панель к новому анализу: «Загрузка…», редактирование выключено,
        /// старые BPM/тапы не показываются.
        /// </summary>
        public void ResetForAnalysis()
        {
            SetEditingEnabled(false);
            _tapTimes.Clear();
            _result = null;

            if (_bpmValueText != null)
                _bpmValueText.text = Loc.T(LocKeys.CommonLoading);

            if (_confidenceText != null)
                _confidenceText.text = string.Empty;

            if (_tapCountText != null)
                _tapCountText.text = string.Empty;

            if (_tapBPMText != null)
                _tapBPMText.text = string.Empty;
        }

        /// <summary>Слайдер/ввод/Tap активны только после того, как BPM определён.</summary>
        private void SetEditingEnabled(bool value)
        {
            if (_bpmSlider != null)
                _bpmSlider.interactable = value;

            if (_bpmInput != null)
                _bpmInput.interactable = value;

            if (_tapButton != null)
                _tapButton.interactable = value;
        }

        private void UpdateSessionBPM(float bpm)        {
            var session = RecordSession.Instance;
            if (session.Song != null)
            {
                session.Song.BPM = bpm;
            }
        }

        private void OnContinue()
        {
            float bpm = CurrentBpm;

            // фиксируем подтверждённое значение в сессии
            UpdateSessionBPM(bpm);

            Debug.Log($"[BPMEditorUI] Подтверждён BPM: {bpm:F1}");

            // сообщаем оркестратору (BpmFlowController) — он перейдёт в RecordScene
            OnConfirmed?.Invoke(bpm);
        }
    }
}
