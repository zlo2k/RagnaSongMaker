using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.Recording
{
    /// <summary>
    /// Визуализатор битов метронома (пульсирующая иконка/текст).
    /// </summary>
    public class MetronomeVisualizer : MonoBehaviour
    {
        [Header("Визуальные элементы")]
        [SerializeField] private Image _beatIcon;
        [SerializeField] private Text _beatCountText;
        [SerializeField] private Color _accentColor = Color.red;
        [SerializeField] private Color _normalColor = Color.white;

        [Header("Анимация")]
        [SerializeField] private float _pulseScale = 1.3f;
        [SerializeField] private float _pulseDuration = 0.1f;

        private float _bpm = 120f;
        private float _beatInterval;
        private int _beatCount;
        private bool _isActive;
        private Coroutine _visualizerRoutine;
        private Vector3 _originalScale;

        private void Awake()
        {
            if (_beatIcon != null)
                _originalScale = _beatIcon.transform.localScale;
        }

        /// <summary>
        /// Запускает визуализацию с указанным BPM.
        /// </summary>
        public void StartVisualization(float bpm)
        {
            _bpm = Mathf.Max(40f, bpm);
            _beatInterval = 60f / _bpm;
            _beatCount = 0;
            _isActive = true;

            if (_visualizerRoutine != null)
                StopCoroutine(_visualizerRoutine);

            _visualizerRoutine = StartCoroutine(VisualizerLoop());
        }

        /// <summary>
        /// Останавливает визуализацию.
        /// </summary>
        public void StopVisualization()
        {
            _isActive = false;
            if (_visualizerRoutine != null)
            {
                StopCoroutine(_visualizerRoutine);
                _visualizerRoutine = null;
            }

            // Возвращаем иконку в исходное состояние
            if (_beatIcon != null)
            {
                _beatIcon.transform.localScale = _originalScale;
                _beatIcon.color = _normalColor;
            }

            if (_beatCountText != null)
                _beatCountText.text = "";
        }

        private IEnumerator VisualizerLoop()
        {
            while (_isActive)
            {
                _beatCount++;
                bool isAccent = (_beatCount % 4 == 1);

                // Обновляем текст
                if (_beatCountText != null)
                    _beatCountText.text = isAccent ? $"<b>{_beatCount}</b>" : _beatCount.ToString();

                // Обновляем иконку
                if (_beatIcon != null)
                {
                    _beatIcon.color = isAccent ? _accentColor : _normalColor;
                    StartCoroutine(PulseIcon());
                }

                yield return new WaitForSeconds(_beatInterval);
            }
        }

        private IEnumerator PulseIcon()
        {
            if (_beatIcon == null) yield break;

            Transform t = _beatIcon.transform;
            Vector3 targetScale = _originalScale * _pulseScale;

            // Расширение
            float elapsed = 0f;
            while (elapsed < _pulseDuration)
            {
                t.localScale = Vector3.Lerp(_originalScale, targetScale, elapsed / _pulseDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Сжатие обратно
            elapsed = 0f;
            while (elapsed < _pulseDuration)
            {
                t.localScale = Vector3.Lerp(targetScale, _originalScale, elapsed / _pulseDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }

            t.localScale = _originalScale;
        }
    }
}
