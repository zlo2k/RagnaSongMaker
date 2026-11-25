using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Держит UI-риг «перед игроком», но без жёсткой привязки к голове:
    /// позиция и поворот догоняются только когда игрок ушёл/повернулся дальше порога,
    /// и сглаженно. Это lazy-follow паттерн (Meta не рекомендует head-locked панели,
    /// XRI делает то же в LazyFollow для отдельных панелей).
    ///
    /// Риг ставится в точку «ноги игрока» со смещением по высоте:
    /// y = headY - referenceEyeHeight, поэтому вся UI-раскладка, нарисованная
    /// вокруг уровня глаз, сохраняется и сидя, и стоя.
    /// </summary>
    public class PlayerSpaceFollower : MonoBehaviour
    {
        [Tooltip("Цель слежения (обычно камера). Если пусто — Camera.main")]
        [SerializeField] private Transform _target;

        [Tooltip("Опорная высота глаз, на которую рассчитана UI-раскладка")]
        [SerializeField] private float _referenceEyeHeight = 1.6f;

        [Tooltip("Порог по углу поворота головы, градусы")]
        [SerializeField] private float _rotationThreshold = 35f;

        [Tooltip("Порог по смещению игрока, метры")]
        [SerializeField] private float _moveThreshold = 0.4f;

        [Tooltip("Скорость сглаживания (чем больше, тем быстрее догоняет)")]
        [SerializeField] private float _smoothSpeed = 5f;

        [Tooltip("Задержка перед первичной установкой (позе шлема нужно примениться)")]
        [SerializeField] private float _snapDelay = 0.15f;

        private bool _initialized;
        private float _enabledAt;

        private void OnEnable()
        {
            _initialized = false;
            _enabledAt = Time.unscaledTime;
        }

        /// <summary>Мгновенно поставить UI перед игроком (можно звать из кода/кнопки).</summary>
        public void Recenter()
        {
            var head = ResolveHead();
            if (head == null)
                return;

            transform.position = DesiredPosition(head);
            transform.rotation = DesiredRotation(head);
            _initialized = true;
        }

        private void LateUpdate()
        {
            var head = ResolveHead();
            if (head == null)
                return;

            var desired = DesiredPosition(head);
            var desiredRotation = DesiredRotation(head);

            if (!_initialized)
            {
                // ждём, пока HMDPoseDriver применит позу шлема — иначе риг встанет в (0,0,0)
                if (Time.unscaledTime - _enabledAt < _snapDelay)
                    return;

                transform.position = desired;
                transform.rotation = desiredRotation;
                _initialized = true;
                return;
            }

            var flatDelta = transform.position - desired;
            flatDelta.y = 0f;

            var yawDelta = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, desiredRotation.eulerAngles.y));
            var needsFollow = flatDelta.magnitude > _moveThreshold || yawDelta > _rotationThreshold;

            if (!needsFollow)
                return;

            var t = 1f - Mathf.Exp(-_smoothSpeed * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, t);
        }

        private Transform ResolveHead()
        {
            if (_target != null)
                return _target;

            return Camera.main != null ? Camera.main.transform : null;
        }

        private Vector3 DesiredPosition(Transform head)
        {
            return new Vector3(
                head.position.x,
                head.position.y - _referenceEyeHeight,
                head.position.z);
        }

        private static Quaternion DesiredRotation(Transform head)
        {
            return Quaternion.Euler(0f, head.eulerAngles.y, 0f);
        }
    }
}
