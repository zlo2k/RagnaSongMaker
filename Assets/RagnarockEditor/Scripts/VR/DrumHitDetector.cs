using System.Collections.Generic;
using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Детект удара молота по барабану.
    ///
    /// Барабаны стоят вертикально, поэтому удар считается не по «высоте», а по
    /// пересечению плоскости головки по её нормали: головка смотрит на игрока,
    /// молот идёт со стороны игрока в сторону барабана. Удар засчитывается ровно
    /// один раз за проход через плоскость удара (на <see cref="_hitPlane"/> перед
    /// головкой). Геометрия молота (одна/две грани, толщина) роли не играет —
    /// используется одна точка HeadPosition.
    ///
    /// После удара молот нужно ОТВЕСТИ от барабана на <see cref="_rearmHeight"/>,
    /// иначе следующие проходы игнорируются. Это исключает двойные срабатывания
    /// от отскока и «второй грани».
    ///
    /// Дополнительно:
    ///  - удар только «в барабан» (скорость вдоль нормали к плоскости);
    ///  - регистрирует только ближайший к головке барабан (сосед не задевается);
    ///  - кулдаун на барабан, сбрасывается при отводе молота.
    /// </summary>
    public class DrumHitDetector : MonoBehaviour
    {
        [SerializeField] private DrumController _drumController;

        [Header("Плоскость удара")]
        [Tooltip("Расстояние от центра головки до плоскости удара в сторону игрока, метры.")]
        [SerializeField] private float _hitPlane = 0.025f;

        [Tooltip("Радиус пятна удара, метры. Меньше половины шага между барабанами (0.178 м).")]
        [SerializeField] private float _hitRadius = 0.16f;

        [Tooltip("Насколько молот должен уйти за головку, чтобы это считалось промахом, метры.")]
        [SerializeField] private float _bottomTolerance = 0.12f;

        [Tooltip("На сколько нужно отвести молот от барабана, чтобы он снова мог ударить, метры.")]
        [SerializeField] private float _rearmHeight = 0.09f;

        [Header("Скорости")]
        [Tooltip("Минимальная общая скорость головки молота, м/с.")]
        [SerializeField] private float _minSpeed = 0.6f;

        [Tooltip("Минимальная скорость движения к барабану (вдоль нормали), м/с.")]
        [SerializeField] private float _minApproachSpeed = 0.25f;

        [Header("Антидребезг")]
        [Tooltip("Кулдаун между ударами по этому барабану, секунды.")]
        [SerializeField] private float _minHitInterval = 0.12f;

        private readonly List<HammerController> _hammers = new List<HammerController>();
        private readonly HashSet<HammerController> _disarmed = new HashSet<HammerController>();
        private readonly Dictionary<HammerController, float> _previousAlong =
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

            // Нормаль плоскости головки (у вертикального барабана смотрит на игрока)
            Vector3 normal = transform.up;
            float radiusSqr = _hitRadius * _hitRadius;

            for (int i = 0; i < _hammers.Count; i++)
            {
                var hammer = _hammers[i];
                if (hammer == null)
                    continue;

                Vector3 headPosition = hammer.HeadPosition;
                Vector3 delta = headPosition - center;

                // Расстояние вдоль нормали: > 0 — молот перед барабаном (со стороны игрока)
                float along = Vector3.Dot(delta, normal);
                float inPlaneSqr = (delta - normal * along).sqrMagnitude;

                float previous;

                if (!_previousAlong.TryGetValue(hammer, out previous))
                {
                    // Первый кадр для этого молота: запоминаем положение, но не считаем удар,
                    // чтобы «телепорт» внутрь зоны не дал ложного срабатывания.
                    _previousAlong[hammer] = along;
                    continue;
                }

                _previousAlong[hammer] = along;

                // Молот отведён от барабана — снова «заряжен» и кулдаун больше не мешает
                if (along > _hitPlane + _rearmHeight)
                    _disarmed.Remove(hammer);

                if (_disarmed.Contains(hammer))
                    continue;

                // Нужен именно проход в сторону барабана через плоскость удара
                if (previous <= _hitPlane || along > _hitPlane)
                    continue;

                // Молот уже провалился за головку — это не удар
                if (along < -_bottomTolerance)
                    continue;

                if (inPlaneSqr > radiusSqr)
                    continue;

                Vector3 velocity = hammer.Velocity;

                // Скорость «в барабан»: проекция на -normal (к плоскости головки)
                float approach = Vector3.Dot(velocity, -normal);

                if (approach < _minApproachSpeed)
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

                Debug.Log($"[Hit] drum={_drumController.DrumIndex}, approach={approach:F2}, " +
                          $"speed={velocity.magnitude:F2}, hammer={hammer.name}");
            }
        }

        /// <summary>Барабан ближе всех к головке молота.</summary>
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

                // Расстояние до центра головки барабана (у всех барабанов он вынесен
                // одинаково, поэтому порядок тот же, что и по корпусу)
                float sqr = (drum.FaceCenter - headPosition).sqrMagnitude;

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
            if (_previousAlong.Count == 0)
                return;

            var stale = new List<HammerController>();

            foreach (var pair in _previousAlong)
            {
                if (pair.Key == null)
                    stale.Add(pair.Key);
            }

            for (int i = 0; i < stale.Count; i++)
                _previousAlong.Remove(stale[i]);
        }
    }
}
