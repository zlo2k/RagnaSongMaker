using System.Collections.Generic;
using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Регистрирует проход бойка через мембрану сверху вниз. Радиус зоны удара
    /// проверяется в точке пересечения двух последовательных позиций бойка.
    /// </summary>
    [DefaultExecutionOrder(200)] // after AimAlignedObject.LateUpdate
    public class DrumHitDetector : MonoBehaviour
    {
        [SerializeField] private DrumController _drumController;

        [Header("Плоскость удара")]
        [Tooltip("Радиус пятна удара, метры. Меньше половины шага между барабанами (0.178 м).")]
        [SerializeField] private float _hitRadius = 0.16f;

        [Tooltip("На сколько нужно поднять молот над плоскостью, чтобы он снова мог ударить, метры.")]
        [SerializeField] private float _rearmHeight = 0.025f;

        [Header("Скорости")]
        [Tooltip("Минимальная общая скорость головки молота, м/с.")]
        [SerializeField] private float _minSpeed = 0.6f;

        [Tooltip("Минимальная скорость движения ВНИЗ, м/с — удар должен идти сверху.")]
        [SerializeField] private float _minDownSpeed = 0.35f;

        private readonly List<HammerController> _hammers = new List<HammerController>();
        private readonly HashSet<HammerController> _disarmed = new HashSet<HammerController>();
        private readonly Dictionary<HammerController, Vector3> _previousTip =
            new Dictionary<HammerController, Vector3>();

        private DrumManager _drumManager;
        private float _nextRescan;


        public float HitRadius
        {
            get => _hitRadius;
            set => _hitRadius = Mathf.Max(0.03f, value);
        }

        public void Configure(DrumController controller, float radius)
        {
            if (controller != null)
                _drumController = controller;

            if (radius > 0.01f)
                _hitRadius = radius;
        }

        private void Awake()
        {
            if (_drumController == null)
                _drumController = GetComponentInParent<DrumController>();
        }

        private void OnDisable()
        {
            _previousTip.Clear();
            _disarmed.Clear();
        }

        private void LateUpdate()
        {
            if (_drumController == null)
                return;

            if (Time.time >= _nextRescan)
            {
                _nextRescan = Time.time + 1f;
                _hammers.Clear();
                _hammers.AddRange(Object.FindObjectsByType<HammerController>(FindObjectsSortMode.None));
                _disarmed.RemoveWhere(hammer => hammer == null);
                PrunePreviousPositions();
            }

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || dt > 0.1f)
            {
                _previousTip.Clear();
                _disarmed.Clear();
                return;
            }

            Vector3 center = _drumController.StrikeCenter;
            Vector3 normal = _drumController.transform.up;
            float radiusSqr = _hitRadius * _hitRadius;

            for (int i = 0; i < _hammers.Count; i++)
            {
                var hammer = _hammers[i];
                if (hammer == null)
                    continue;

                if (!hammer.HasTrackedPose)
                {
                    _previousTip.Remove(hammer);
                    _disarmed.Remove(hammer);
                    continue;
                }

                Vector3 headPosition = hammer.ContactPoint(normal);
                bool hasPrevious = _previousTip.TryGetValue(hammer, out Vector3 previous);
                _previousTip[hammer] = headPosition;
                if (!hasPrevious)
                {
                    if (Vector3.Dot(headPosition - center, normal) < _rearmHeight)
                        _disarmed.Add(hammer);
                    continue;
                }

                float currentY = Vector3.Dot(headPosition - center, normal);
                float previousY = Vector3.Dot(previous - center, normal);

                // Подняли молот — снова «заряжен» и кулдаун больше не мешает
                if (currentY > _rearmHeight)
                    _disarmed.Remove(hammer);

                if (_disarmed.Contains(hammer))
                    continue;

                // Нужен именно проход сверху вниз через плоскость
                if (currentY > 0f || previousY <= 0f)
                    continue;

                Vector3 contact = Vector3.Lerp(previous, headPosition, previousY / (previousY - currentY));
                if ((contact - center).sqrMagnitude > radiusSqr)
                    continue;

                Vector3 velocity = (headPosition - previous) / dt;

                if (Vector3.Dot(velocity, normal) > -_minDownSpeed)
                    continue;

                if (velocity.magnitude < _minSpeed)
                    continue;

                // Только ближайший барабан: один замах не заденет соседний
                if (!IsClosestDrum(contact))
                {
                    _disarmed.Add(hammer);
                    continue;
                }

                _disarmed.Add(hammer);
                if (!_drumController.TryRegisterHit(velocity.magnitude))
                    continue;
                HapticFeedback.TriggerHaptic(hammer.IsRightHand, velocity.magnitude);

                Debug.Log($"[Hit] drum={_drumController.DrumIndex}, vy={velocity.y:F2}, " +
                          $"speed={velocity.magnitude:F2}, hammer={hammer.name}");
            }
        }

        /// <summary>Барабан ближе всех к головке молота по горизонтали.</summary>
        private bool IsClosestDrum(Vector3 headPosition)
        {
            if (_drumManager == null)
                _drumManager = GetComponentInParent<DrumManager>();

            if (_drumManager == null || _drumManager.Drums == null)
                return true;

            int bestIndex = -1;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < _drumManager.Drums.Length; i++)
            {
                var drum = _drumManager.Drums[i];
                if (drum == null)
                    continue;

                float sqr = (drum.StrikeCenter - headPosition).sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestIndex = i;
                }
            }

            return bestIndex < 0 || bestIndex == _drumController.DrumIndex;
        }

        private void PrunePreviousPositions()
        {
            if (_previousTip.Count == 0)
                return;

            var stale = new List<HammerController>();

            foreach (var pair in _previousTip)
            {
                if (pair.Key == null)
                    stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++)
                _previousTip.Remove(stale[i]);
        }
    }
}
