using RagnarockEditor.Audio;
using RagnarockEditor.Localization;
using RagnarockEditor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Метроном на экране определения BPM: кнопка вкл/выкл, ползунок громкости.
    /// Тикает с текущим BPM из BPMEditorUI, чтобы можно было подобрать темп на слух.
    /// </summary>
    public class MetronomePreview : MonoBehaviour
    {
        [SerializeField] private MetronomePlayer _player;
        [SerializeField] private Button _toggleButton;
        [SerializeField] private Text _toggleLabel;
        [SerializeField] private Slider _volumeSlider;
        [SerializeField] private BPMEditorUI _editor;

        private bool _playing;

        private void Start()
        {
            Loc.Initialize();

            if (_player == null)
                _player = FindFirstObjectByType<MetronomePlayer>(FindObjectsInactive.Include);

            if (_editor == null)
                _editor = FindFirstObjectByType<BPMEditorUI>(FindObjectsInactive.Include);

            if (_toggleButton != null)
                _toggleButton.onClick.AddListener(Toggle);

            if (_volumeSlider != null)
            {
                _volumeSlider.SetValueWithoutNotify(_player != null ? _player.Volume : 0.7f);
                _volumeSlider.onValueChanged.AddListener(SetVolume);
            }

            Loc.OnLanguageChanged += Refresh;
            Refresh();
        }

        private void OnEnable()
        {
            // Экран BPM открылся, а тумблер уже включён — метроном подхватывает
            if (MetronomeRuntime.Enabled && _player != null && !_player.IsPlaying)
                SetPreview(true);
        }

        private void OnDisable()
        {
            // Ушли с экрана BPM — метроном не должен тикать в фоне
            if (_playing && _player != null)
            {
                _player.StopMetronome();
                _playing = false;
                Refresh();
            }
        }

        private void OnDestroy()
        {
            Loc.OnLanguageChanged -= Refresh;

            if (_toggleButton != null)
                _toggleButton.onClick.RemoveListener(Toggle);

            if (_volumeSlider != null)
                _volumeSlider.onValueChanged.RemoveListener(SetVolume);

            if (_player != null)
                _player.StopMetronome();
        }

        private void Update()
        {
            // Глобальный выключатель метронома (каменный тумблер) имеет приоритет
            if (_playing && !MetronomeRuntime.Enabled)
            {
                SetPreview(false);
                return;
            }

            // Слайдер BPM поменяли — метроном подхватывает новый темп
            if (_playing && _player != null && _editor != null && _editor.CurrentBpm > 0f)
                _player.BPM = _editor.CurrentBpm;
        }

        /// <summary>Включить/выключить метроном.</summary>
        public void Toggle() => SetPreview(!_playing);

        /// <summary>Явное включение/выключение (используется каменным тумблером).</summary>
        public void SetPreview(bool on)
        {
            _playing = on;

            if (on)
            {
                MetronomeRuntime.Enabled = true;

                // Канвас BPM ещё выключен: стартуем при его включении (OnEnable)
                if (!gameObject.activeInHierarchy)
                {
                    Refresh();
                    return;
                }

                if (_player != null)
                {
                    if (_editor != null && _editor.CurrentBpm > 0f)
                        _player.BPM = _editor.CurrentBpm;

                    _player.StartMetronome();
                }
            }
            else if (_player != null)
            {
                _player.StopMetronome();
            }

            Refresh();
        }

        private void SetVolume(float value)
        {
            if (_player != null)
                _player.Volume = value;
        }

        private void Refresh()
        {
            if (_toggleLabel != null)
                _toggleLabel.text = Loc.T(_playing ? LocKeys.MetronomeStop : LocKeys.MetronomeStart);
        }
    }
}
