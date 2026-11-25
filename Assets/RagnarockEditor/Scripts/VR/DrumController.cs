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
        [SerializeField] private Material _hoopMaterial;
        [SerializeField] private Material _defaultMaterial;
        [SerializeField] private Material _hitMaterial;
        [SerializeField] private float _hitFlashDuration = 0.1f;

        [Header("Звук")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _hitSound;

        [Header("Параметры детекции")]
        [Tooltip("Минимальная скорость молота для регистрации удара (м/с)")]
        [SerializeField] private float _minHitVelocity = 1.5f;

        /// <summary>Индекс барабана (0-3).</summary>
        public int DrumIndex => _drumIndex;

        // Top face of the prefab's built-in cylinder (local Y = +1).
        public Vector3 StrikeCenter => _drumHeadRenderer != null
            ? _drumHeadRenderer.transform.TransformPoint(Vector3.up)
            : transform.position;
        public Vector3 RuneCenter => StrikeCenter + transform.up * (DiscVisual.Thickness * 0.5f);
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

        private MaterialPropertyBlock _propBlock;
        private float _flashTimer;
        private bool _isFlashing;

        private void Awake()
        {
            if (_drumHeadRenderer == null)
                _drumHeadRenderer = GetComponentInChildren<MeshRenderer>();

            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();

            var body = transform.Find("Body");
            if (body != null && body.TryGetComponent<MeshFilter>(out var filter) &&
                body.TryGetComponent<MeshRenderer>(out var shell))
            {
                var wood = shell.sharedMaterial;
                filter.sharedMesh = DrumShell.SharedMesh;
                shell.sharedMaterials = new[] { wood, _hoopMaterial != null ? _hoopMaterial : wood, _defaultMaterial };
            }

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
            TryRegisterHit(hammerVelocity);
        }

        public bool TryRegisterHit(float hammerVelocity)
        {
            if (hammerVelocity < _minHitVelocity)
                return false;

            // Визуальная обратная связь
            FlashDrumHead();

            // Звуковая обратная связь
            PlayHitSound(hammerVelocity);

            // Как в игре: от барабана улетает кружок в плоскости его верха, прочь от игрока
            HitDiscEffect.Spawn(RuneCenter, transform.rotation, _drumIndex, hammerVelocity);

            // Событие
            OnDrumHit?.Invoke(_drumIndex, hammerVelocity);

            Debug.Log($"[DrumController] Удар по барабану {_drumIndex}, скорость={hammerVelocity:F1} м/с");
            return true;
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
