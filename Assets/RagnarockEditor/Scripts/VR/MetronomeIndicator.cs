using RagnarockEditor.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Иконка метронома в HUD записи: пульсирует на каждом тике, акцентная доля — цветом.
    /// Тики приходят от MetronomePlayer (во время записи их ведёт RecordManager по времени песни).
    /// </summary>
    public class MetronomeIndicator : MonoBehaviour
    {
        [SerializeField] private Text _icon;
        [SerializeField] private Color _accentColor = new Color(1f, 0.62f, 0.2f, 1f);
        [SerializeField] private Color _normalColor = new Color(0.9f, 0.92f, 0.95f, 1f);
        [SerializeField] private float _pulseScale = 1.35f;
        [SerializeField] private float _pulseDuration = 0.12f;

        private MetronomePlayer _player;
        private Vector3 _baseScale = Vector3.one;
        private float _timer;

        private void Start()
        {
            if (_icon != null)
                _baseScale = _icon.transform.localScale;

            _player = FindFirstObjectByType<MetronomePlayer>(FindObjectsInactive.Include);

            if (_player != null)
            {
                _player.OnTick += HandleTick;
                Debug.Log("[Metronome] индикатор подключён");
            }
            else
            {
                Debug.LogWarning("[Metronome] MetronomePlayer не найден — иконка не будет пульсировать");
            }

            ResetVisual();
        }

        private void OnDestroy()
        {
            if (_player != null)
                _player.OnTick -= HandleTick;
        }

        private void HandleTick(bool accent)
        {
            _timer = _pulseDuration;

            if (_icon == null)
                return;

            _icon.color = accent ? _accentColor : _normalColor;
            _icon.transform.localScale = _baseScale * _pulseScale;
        }

        private void Update()
        {
            if (_timer <= 0f)
                return;

            _timer -= Time.deltaTime;

            if (_timer <= 0f)
                ResetVisual();
        }

        private void ResetVisual()
        {
            if (_icon == null)
                return;

            _icon.color = _normalColor;
            _icon.transform.localScale = _baseScale;
        }
    }
}
