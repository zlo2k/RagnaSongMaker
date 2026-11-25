using RagnarockEditor.Audio;
using RagnarockEditor.Playback;
using RagnarockEditor.Quantization;
using RagnarockEditor.Recording;
using RagnarockEditor.UI;
using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Каменный пульт на экране BPM: ползунок BPM, тумблер метронома, громкость,
    /// привязка к сетке и полоса прогресса анализа. Все контролы — модели StoneControls.
    /// </summary>
    public class StoneControlsPanel : MonoBehaviour
    {
        [Header("BPM")]
        [SerializeField] private StoneControl _bpmSlider;

        [SerializeField] private StoneControl _metronomeSwitch;
        [SerializeField] private StoneControl _metronomeVolume;
        [SerializeField] private StoneControl _snapCheck;
        [SerializeField] private StoneControl _analysisProgress;

        [Header("Запись")]
        [SerializeField] private StoneControl _recordProgress;

        [Header("Предпросмотр")]
        [SerializeField] private StoneControl _previewSeek;
        [SerializeField] private StoneControl _previewProgress;
        [SerializeField] private StoneControl _previewSpeed;

        private const float MinBpm = 40f;
        private const float MaxBpm = 300f;

        private MetronomePlayer _player;
        private MetronomePreview _preview;
        private AudioLoader _loader;
        private RecordManager _record;
        private PlaybackController _playback;
        private BPMEditorUI _bpmEditor;

        private void Start()
        {
            _player = FindFirstObjectByType<MetronomePlayer>(FindObjectsInactive.Include);
            _preview = FindFirstObjectByType<MetronomePreview>(FindObjectsInactive.Include);
            _loader = FindFirstObjectByType<AudioLoader>(FindObjectsInactive.Include);
            _record = FindFirstObjectByType<RecordManager>(FindObjectsInactive.Include);
            _playback = FindFirstObjectByType<PlaybackController>(FindObjectsInactive.Include);
            _bpmEditor = FindFirstObjectByType<BPMEditorUI>(FindObjectsInactive.Include);

            if (_bpmSlider != null)
            {
                // шкала 40..300 BPM: положение ручки = положение текущего темпа
                _bpmSlider.SetValueSilent(_bpmEditor != null ? _bpmEditor.BpmNormalized : 0.3f);
                _bpmSlider.ValueChanged += OnBpmSlider;
            }

            if (_previewSeek != null)
            {
                _previewSeek.SetValueSilent(0f);
                _previewSeek.ValueChanged += OnPreviewSeek;
            }

            if (_previewSpeed != null)
            {
                float speed = _playback != null ? _playback.PlaybackSpeed : 1f;
                _previewSpeed.SetValueSilent(Mathf.InverseLerp(0.5f, 2f, speed));
                _previewSpeed.ValueChanged += OnPreviewSpeed;
            }

            if (_metronomeSwitch != null)
            {
                _metronomeSwitch.SetOn(MetronomeRuntime.Enabled);
                _metronomeSwitch.ValueChanged += OnMetronomeSwitch;
            }

            if (_metronomeVolume != null)
            {
                _metronomeVolume.SetValue(_player != null ? _player.Volume : 0.7f);
                _metronomeVolume.ValueChanged += OnMetronomeVolume;
            }

            if (_snapCheck != null)
            {
                _snapCheck.SetOn(QuantizeRuntime.SnapEnabled);
                _snapCheck.ValueChanged += OnSnapChanged;
            }
        }

        private void OnDestroy()
        {
            if (_bpmSlider != null)
                _bpmSlider.ValueChanged -= OnBpmSlider;

            if (_metronomeSwitch != null)
                _metronomeSwitch.ValueChanged -= OnMetronomeSwitch;

            if (_metronomeVolume != null)
                _metronomeVolume.ValueChanged -= OnMetronomeVolume;

            if (_snapCheck != null)
                _snapCheck.ValueChanged -= OnSnapChanged;

            if (_previewSeek != null)
                _previewSeek.ValueChanged -= OnPreviewSeek;

            if (_previewSpeed != null)
                _previewSpeed.ValueChanged -= OnPreviewSpeed;
        }

        private void Update()
        {
            UpdateBpmSlider();

            if (_analysisProgress != null)
                _analysisProgress.SetProgress(_loader != null ? Mathf.Clamp01(_loader.Progress) : 0f);

            if (_recordProgress != null)
            {
                float progress = 0f;

                if (_record != null && _record.IsActive)
                {
                    float duration = _record.SongDuration;
                    if (duration > 0.01f)
                        progress = Mathf.Clamp01(_record.CurrentSongTime / duration);
                }

                _recordProgress.SetProgress(progress);
            }

            if (_playback != null && _playback.SongDuration > 0.01f)
            {
                float progress = Mathf.Clamp01(_playback.PlaybackTime / _playback.SongDuration);

                if (_previewProgress != null)
                    _previewProgress.SetProgress(progress);

                if (_previewSeek != null && (_playback.IsPlaying || _playback.IsPaused))
                    _previewSeek.SetValueSilent(progress);
            }
        }

        /// <summary>
        /// Каменный ползунок ведёт BPM: результат анализа, Tap Tempo и Exact-поле
        /// двигают его так же, как и он их.
        /// </summary>
        private void UpdateBpmSlider()
        {
            if (_bpmSlider == null || _bpmEditor == null)
                return;

            float target = _bpmEditor.BpmNormalized;
            if (Mathf.Abs(_bpmSlider.Value - target) > 0.004f)
                _bpmSlider.SetValueSilent(target);
        }

        private void OnBpmSlider(float value)
        {
            if (_bpmEditor != null)
                _bpmEditor.SetBpmFromControl(Mathf.Lerp(MinBpm, MaxBpm, value));
        }

        private void OnPreviewSeek(float value)
        {
            if (_playback != null && _playback.HasAudio)
                _playback.Seek(value * _playback.SongDuration);
        }

        private void OnPreviewSpeed(float value)
        {
            if (_playback != null)
                _playback.PlaybackSpeed = Mathf.Lerp(0.5f, 2f, value);
        }

        private void OnMetronomeSwitch(float value)
        {
            bool on = value > 0.5f;
            MetronomeRuntime.Enabled = on;

            if (_preview != null)
                _preview.SetPreview(on);
        }

        private void OnMetronomeVolume(float value)
        {
            if (_player != null)
                _player.Volume = value;
        }

        private void OnSnapChanged(float value)
        {
            QuantizeRuntime.SnapEnabled = value > 0.5f;
        }
    }
}
