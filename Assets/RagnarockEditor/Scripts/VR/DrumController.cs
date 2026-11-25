using RagnarockEditor.Audio;
using RagnarockEditor.Playback;
using UnityEngine;
using UnityEngine.Events;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Контроллер одного барабана.
    /// Отвечает за визуал, коллизию и события удара.
    /// </summary>
    public class DrumController : MonoBehaviour
    {
        [Header("Индекс барабана")]
        [Range(0, 3)]
        [SerializeField] private int _drumIndex;

        [Header("Визуал")]
        [SerializeField] private MeshRenderer _drumHeadRenderer;
        [SerializeField] private Material _defaultMaterial;
        [SerializeField] private Material _hitMaterial;
        [SerializeField] private float _hitFlashDuration = 0.1f;

        [Header("Звук")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _hitSound;

        [Header("Параметры детекции")]
        [Tooltip("Минимальная скорость молота для регистрации удара (м/с)")]
        [SerializeField] private float _minHitVelocity = 1.5f;

        [Tooltip("Минимальный интервал между ударами (сек) — debounce")]
        [SerializeField] private float _debounceTime = 0.03f;

        /// <summary>Индекс барабана (0-3).</summary>
        public int DrumIndex => _drumIndex;
        /// <summary>
        /// Задаёт индекс барабана. Вызывается DrumManager при создании/расстановке:
        /// в префабе Drum.prefab индекс всегда 0, и без этого все удары
        /// записывались бы как «барабан 0» (и в предпросмотре все руны летели на один барабан).
        /// </summary>
        public void SetDrumIndex(int index)
        {
            _drumIndex = Mathf.Clamp(index, 0, 3);
        }

        /// <summary>Событие: по барабану ударили.</summary>
        public UnityEvent<int, float> OnDrumHit; // drumIndex, velocity

        private float _lastHitTime = -1f;
        private MaterialPropertyBlock _propBlock;
        private float _flashTimer;
        private bool _isFlashing;

        private void Awake()
        {
            if (_drumHeadRenderer == null)
                _drumHeadRenderer = GetComponentInChildren<MeshRenderer>();

            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();

            _propBlock = new MaterialPropertyBlock();
        }

        private void Update()
        {
            // Сбрасываем подсветку после удара
            if (_isFlashing)
            {
                _flashTimer -= Time.deltaTime;
                if (_flashTimer <= 0f)
                {
                    ResetDrumHeadMaterial();
                    _isFlashing = false;
                }
            }
        }

        /// <summary>
        /// Вызывается из DrumHitDetector при столкновении молота с барабаном.
        /// </summary>
        /// <param name="hammerVelocity">Скорость молота в момент удара</param>
        public void RegisterHit(float hammerVelocity)
        {
            // Debounce
            float currentTime = Time.time;
            if (currentTime - _lastHitTime < _debounceTime)
                return;

            if (hammerVelocity < _minHitVelocity)
                return;

            _lastHitTime = currentTime;

            // Визуальная обратная связь
            FlashDrumHead();

            // Звуковая обратная связь
            PlayHitSound(hammerVelocity);

            // Как в игре: от барабана улетает кружок в плоскости его верха, прочь от игрока
            HitDiscEffect.Spawn(transform.position + Vector3.up * 0.1f, _drumIndex, hammerVelocity);

            // Событие
            OnDrumHit?.Invoke(_drumIndex, hammerVelocity);

            Debug.Log($"[DrumController] Удар по барабану {_drumIndex}, скорость={hammerVelocity:F1} м/с");
        }

        /// <summary>
        /// Подсвечивает головку барабана при ударе.
        /// </summary>
        private void FlashDrumHead()
        {
            if (_drumHeadRenderer != null && _hitMaterial != null)
            {
                _drumHeadRenderer.SetPropertyBlock(null);
                _drumHeadRenderer.material = _hitMaterial;
                _isFlashing = true;
                _flashTimer = _hitFlashDuration;
            }
        }

        /// <summary>
        /// Возвращает материал барабана к исходному.
        /// </summary>
        private void ResetDrumHeadMaterial()
        {
            if (_drumHeadRenderer != null && _defaultMaterial != null)
            {
                _drumHeadRenderer.material = _defaultMaterial;
            }
        }

        /// <summary>
        /// Проигрывает звук удара: клип из префаба, если назначен, иначе — библиотека SFX
        /// (для каждого барабана свои вариации, громкость зависит от силы удара).
        /// </summary>
        private void PlayHitSound(float velocity)
        {
            if (_audioSource != null && _hitSound != null)
            {
                _audioSource.PlayOneShot(_hitSound);
                return;
            }

            SfxPlayer.Instance.PlayDrum(_drumIndex, transform.position, velocity);
        }

        /// <summary>
        /// Устанавливает позицию и поворот барабана.
        /// </summary>
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation)
        {
            transform.position = position;
            transform.rotation = rotation;
        }

        /// <summary>
        /// Подсвечивает барабан (для предпросмотра — какая руна куда летит).
        /// </summary>
        public void Highlight(bool active)
        {
            if (_drumHeadRenderer != null)
            {
                Color color = active ? Color.yellow : Color.white;
                _drumHeadRenderer.material.color = color;
            }
        }
    }
}
