using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Контроллер молота. Привязывается к XR Controller (руке).
    /// Отслеживает скорость для детекции ударов.
    /// </summary>
    public class HammerController : MonoBehaviour
    {
        [Header("Настройки молота")]
        [Tooltip("Коэффициент сглаживания скорости")]
        [Range(0f, 1f)]
        [SerializeField] private float _velocitySmoothing = 0.3f;

        [Tooltip("Какой руке принадлежит молот (для haptic)")]
        [SerializeField] private bool _isRightHand = true;

        /// <summary>Текущая скорость молота (м/с).</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Магнитуда скорости (м/с).</summary>
        public float Speed => Velocity.magnitude;

        /// <summary>Правая рука?</summary>
        public bool IsRightHand => _isRightHand;

        private Vector3 _previousPosition;
        private Vector3 _smoothedVelocity;

        [Tooltip("Дочерний объект головы молота — центр удара")]
        [SerializeField] private Transform _head;

        /// <summary>Голова молота (центр удара).</summary>
        public Transform Head => _head != null ? _head : transform;

        public Vector3 HeadPosition => Head.position;

        private void Awake()
        {
            if (_head == null)
                _head = transform.Find("Head");
        }

        private void Start()
        {
            _previousPosition = HeadPosition;
        }

        private void Update()
        {
            // Скорость считаем по голове молота: при замахе она летит, а рукоять почти стоит
            Vector3 currentPosition = HeadPosition;
            Vector3 instantVelocity = (currentPosition - _previousPosition) / Mathf.Max(Time.deltaTime, 0.0001f);

            // Сглаживаем
            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, instantVelocity, 1f - _velocitySmoothing);
            Velocity = _smoothedVelocity;

            _previousPosition = currentPosition;
        }

        /// <summary>
        /// Сбрасывает скорость (например, после паузы).
        /// </summary>
        public void ResetVelocity()
        {
            Velocity = Vector3.zero;
            _smoothedVelocity = Vector3.zero;
            _previousPosition = HeadPosition;
        }
    }
}
