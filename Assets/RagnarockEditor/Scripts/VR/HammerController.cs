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

        private Transform _contactSpace;
        private Bounds _contactHead;
        private XRHandPoseDriver _poseDriver;
        private bool _boxHead;

        public bool HasTrackedPose
        {
            get
            {
                if (!isActiveAndEnabled) return false;
                if (_poseDriver == null) return true;
                var device = _poseDriver.Device;
                return device.isValid && (!device.TryGetFeatureValue(
                    UnityEngine.XR.CommonUsages.isTracked, out bool tracked) || tracked);
            }
        }

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

            _poseDriver = GetComponentInParent<XRHandPoseDriver>();
            var headBox = Head.GetComponent<BoxCollider>();
            if (headBox != null)
            {
                _contactSpace = headBox.transform;
                _contactHead = new Bounds(headBox.center, headBox.size);
                _boxHead = true;
                return;
            }

            // The FBX contains the handle and barrel in one mesh. In its own axes
            // the barrel occupies the broad end of the longest (handle) dimension.
            foreach (var filter in GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name != "StormMallet_Mesh" || filter.sharedMesh == null) continue;
                _contactSpace = filter.transform;
                var bounds = filter.sharedMesh.bounds;
                Vector3 extent = bounds.extents;
                int shaft = extent.x > extent.y ? 0 : 1;
                if (extent.z > extent[shaft]) shaft = 2;
                float radius = Mathf.Min(extent[(shaft+1)%3], extent[(shaft+2)%3]);
                float grip = _contactSpace.InverseTransformPoint(transform.position)[shaft];
                Vector3 center = bounds.center;
                center[shaft] += Mathf.Sign(center[shaft] - grip) * (extent[shaft] - radius);
                extent[shaft] = radius;
                _contactHead = new Bounds(center, 2f * extent);
                break;
            }
        }

        // Support point against the tilted head. The FBX barrel is approximated by
        // a rounded striking volume; mirrored and scaled hammers use the same math.
        public Vector3 ContactPoint(Vector3 normal)
        {
            if (_contactSpace == null) return HeadPosition;
            Vector3 e = _contactHead.extents;
            Vector3 x = _contactSpace.TransformVector(Vector3.right * e.x);
            Vector3 y = _contactSpace.TransformVector(Vector3.up * e.y);
            Vector3 z = _contactSpace.TransformVector(Vector3.forward * e.z);
            Vector3 weights = new Vector3(Vector3.Dot(x, normal), Vector3.Dot(y, normal), Vector3.Dot(z, normal));
            if (_boxHead)
            {
                // Use the middle of a flat face when it is parallel to the membrane.
                for (int axis = 0; axis < 3; axis++)
                    weights[axis] = Mathf.Abs(weights[axis]) < 0.00001f ? 0f : Mathf.Sign(weights[axis]);
            }
            else weights.Normalize();
            return _contactSpace.TransformPoint(_contactHead.center) - x*weights.x - y*weights.y - z*weights.z;
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
