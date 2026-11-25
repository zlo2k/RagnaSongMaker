using System.Collections.Generic;
using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Детект удара молота по барабану.
    ///
    /// Работает не по «объёму», а по пересечению горизонтальной плоскости над барабаном:
    /// удар засчитывается ровно один раз за проход головки сверху вниз через плоскость
    /// (y = центр головки + _hitPlane). Геометрия молота (одна/две грани, толщина)
    /// роли не играет — используется одна точка HeadPosition.
    ///
    /// После удара молот нужно ПОДНЯТЬ выше _rearmHeight, иначе следующие проходы
    /// игнорируются. Это исключает двойные срабатывания от отскока, проскальзывания
    /// сквозь барабан и «второй грани».
    ///
    /// Дополнительно:
    ///  - удар только сверху (вертикальная скорость вниз);
    ///  - регистрирует только ближайший к головке барабан (сосед не задевается);
    ///  - кулдаун на барабан;
    ///  - кулдаун и перезарядка сбрасываются при подъёме.
    /// </summary>
    public class DrumHitDetector : MonoBehaviour
    {
        [SerializeField] private DrumController _drumController;

        [Header("Плоскость удара")]
        [Tooltip("Высота плоскости удара над центром головки барабана, метры.")]
        [SerializeField] private float _hitPlane = 0.03f;

        [Tooltip("Радиус пятна удара, метры. Меньше половины шага между барабанами (0.178 м).")]
        [SerializeField] private float _hitRadius = 0.16f;

        [Tooltip("Ниже этой высоты относительно детектора удар не считается (молот уже прошёл сквозь).")]
        [SerializeField] private float _bottomTolerance = 0.12f;

        [Tooltip("На сколько нужно поднять молот над плоскостью, чтобы он снова мог ударить, метры.")]
        [SerializeField] private float _rearmHeight = 0.09f;

        [Header("Скорости")]
        [Tooltip("Минимальная общая скорость головки молота, м/с.")]
        [SerializeField] private float _minSpeed = 0.6f;

        [Tooltip("Минимальная скорость движения ВНИЗ, м/с — удар должен идти сверху.")]
        [SerializeField] private float _minDownSpeed = 0.35f;

        [Header("Антидребезг")]
        [Tooltip("Кулдаун между ударами по этому барабану, секунды.")]
        [SerializeField] private float _minHitInterval = 0.12f;

        private readonly List<HammerController> _hammers = new List<HammerController>();
        private readonly HashSet<HammerController> _disarmed = new HashSet<HammerController>();
        private readonly Dictionary<HammerController, float> _previousY =
            new Dictionary<HammerController, float>();

        private DrumManager _drumManager;
        private float _nextRescan;
        private float _lastHitTime = -1f;

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

        private void Update()
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

            Vector3 center = transform.position;
            float radiusSqr = _hitRadius * _hitRadius;

            for (int i = 0; i < _hammers.Count; i++)
            {
                var hammer = _hammers[i];
                if (hammer == null)
                    continue;

                Vector3 headPosition = hammer.HeadPosition;
                Vector3 delta = headPosition - center;
                float horizontalSqr = delta.x * delta.x + delta.z * delta.z;
                float currentY = delta.y;

                float previousY;

                if (!_previousY.TryGetValue(hammer, out previousY))
                {
                    // Первый кадр для этого молота: запоминаем высоту, но не считаем удар,
                    // чтобы «телепорт» внутрь зоны не дал ложного срабатывания.
                    _previousY[hammer] = currentY;
                    continue;
                }

                _previousY[hammer] = currentY;

                // Подняли молот — снова «заряжен» и кулдаун больше не мешает
                if (currentY > _rearmHeight)
                    _disarmed.Remove(hammer);

                if (_disarmed.Contains(hammer))
                    continue;

                // Нужен именно проход сверху вниз через плоскость
                if (currentY > _hitPlane || previousY <= _hitPlane)
                    continue;

                // Молот уже провалился ниже барабана — это не удар
                if (currentY < -_bottomTolerance)
                    continue;

                if (horizontalSqr > radiusSqr)
                    continue;

                Vector3 velocity = hammer.Velocity;

                if (velocity.y > -_minDownSpeed)
                    continue;

                if (velocity.magnitude < _minSpeed)
                    continue;

                // Только ближайший барабан: один замах не заденет соседний
                if (!IsClosestDrum(headPosition))
                {
                    _disarmed.Add(hammer);
                    continue;
                }

                // Кулдаун: пересечение «съедаем», чтобы отскок не дал второй удар
                if (_lastHitTime >= 0f && Time.time - _lastHitTime < _minHitInterval)
                {
                    _disarmed.Add(hammer);
                    continue;
                }

                _lastHitTime = Time.time;
                _disarmed.Add(hammer);

                _drumController.RegisterHit(velocity.magnitude);
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

                Vector3 position = drum.transform.position;
                float dx = position.x - headPosition.x;
                float dz = position.z - headPosition.z;
                float sqr = dx * dx + dz * dz;

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
            if (_previousY.Count == 0)
                return;

            var stale = new List<HammerController>();

            foreach (var pair in _previousY)
            {
                if (pair.Key == null)
                    stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++)
                _previousY.Remove(stale[i]);
        }
    }
}
