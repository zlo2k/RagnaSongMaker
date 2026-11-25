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

        [Header("Головка")]
        [Tooltip("Дочерний объект Head — плоскость барабана. Если пусто, ищется по имени.")]
        [SerializeField] private Transform _headTransform;

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

        /// <summary>Объект головки (плоскость удара).</summary>
        public Transform HeadTransform
        {
            get
            {
                if (_headTransform == null)
                    _headTransform = transform.Find("Head");

                return _headTransform;
            }
        }

        /// <summary>Центр плоскости головки в мире.</summary>
        public Vector3 FaceCenter
        {
            get
            {
                var head = HeadTransform;
                return head != null ? head.position : transform.position;
            }
        }

        /// <summary>Нормаль плоскости головки в мире — смотрит на игрока.</summary>
        public Vector3 FaceNormal
        {
            get
            {
                var head = HeadTransform;
                return head != null ? head.up : transform.up;
            }
        }

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

            EnsureRim();
        }

        /// <summary>
        /// Обод барабана: без него плоская головка читается как шляпка молотка.
        /// Кольцо из того же материала, что и корпус, чуть больше головки и выступает вперёд.
        /// </summary>
        private void EnsureRim()
        {
            if (transform.Find("Rim") != null)
                return;

            var body = transform.Find("Body");
            var bodyRenderer = body != null ? body.GetComponent<MeshRenderer>() : null;
            var material = bodyRenderer != null ? bodyRenderer.sharedMaterial : _defaultMaterial;

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "Rim";

            var collider = rim.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            rim.transform.SetParent(transform, false);
            rim.transform.localPosition = new Vector3(0f, 0.085f, 0f);
            rim.transform.localRotation = Quaternion.identity;
            rim.transform.localScale = new Vector3(0.40f, 0.018f, 0.40f);

            var renderer = rim.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>
        /// Масштаб барабана под диаметр из DrumLayoutConfig (префаб собран под 0.35 м).
        /// </summary>
        public void SetVisualScale(float scale)
        {
            float value = Mathf.Clamp(scale, 0.4f, 2.5f);
            transform.localScale = Vector3.one * value;
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

            // Как в игре: от плоскости головки улетает кружок в плоскости барабана, прочь от игрока
            HitDiscEffect.Spawn(FaceCenter + FaceNormal * 0.03f, _drumIndex, hammerVelocity);

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
