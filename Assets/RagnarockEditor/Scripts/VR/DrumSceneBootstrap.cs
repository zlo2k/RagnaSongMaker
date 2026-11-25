using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Создаёт 4 барабана и расставляет их вокруг игрока при старте сцены.
    /// Раньше CreateDrums() не вызывался ниоткуда, поэтому в сцене записи барабанов не было.
    /// </summary>
    [RequireComponent(typeof(DrumManager))]
    public class DrumSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private bool _createOnStart = true;

        private void Start()
        {
            var manager = GetComponent<DrumManager>();
            if (manager == null)
                manager = Object.FindFirstObjectByType<DrumManager>();

            if (manager == null)
            {
                Debug.LogError("[Drums] DrumManager не найден");
                return;
            }

            if (_createOnStart && !HasDrums(manager))
                manager.CreateDrums();

            // Страховка: индексы должны совпадать с позициями в массиве, даже если
            // барабаны уже лежат в сцене (в префабе у всех _drumIndex = 0).
            AssignDrumIndices(manager);

            var positioner = GetComponent<DrumPositioner>();
            if (positioner == null)
                positioner = Object.FindFirstObjectByType<DrumPositioner>();

            if (positioner == null)
            {
                Debug.LogWarning("[Drums] DrumPositioner не найден — барабаны останутся в точке спавна");
                return;
            }

            positioner.Calculate();
            manager.PositionDrums(positioner.DrumPositions, positioner.DrumRotation);

            // Крупные барабаны как в игре: размер задаётся конфигом, префаб — 0.35 м
            var config = positioner.Config;
            var diameter = config != null ? config.DrumDiameter : 0.35f;
            manager.SetDrumDiameter(diameter);

            AttachDetectors(manager, diameter, config);
            Debug.Log("[Drums] барабаны созданы и расставлены");
        }

        /// <summary>
        /// Присваивает барабанам индексы по порядку в массиве DrumManager.Drums.
        /// </summary>
        private static void AssignDrumIndices(DrumManager manager)
        {
            if (manager == null || manager.Drums == null)
                return;

            for (int i = 0; i < manager.Drums.Length; i++)
            {
                if (manager.Drums[i] != null)
                    manager.Drums[i].SetDrumIndex(i);
            }
        }

        /// <summary>
        /// Вешает DrumHitDetector на «Head» каждого барабана — в префабе его нет,
        /// поэтому удары раньше не регистрировались вообще.
        /// </summary>
        private static void AttachDetectors(DrumManager manager, float diameter, RagnarockEditor.Data.DrumLayoutConfig config)
        {
            if (manager == null || manager.Drums == null)
                return;

            // Радиус пятна удара: чуть меньше половины шага между барабанами,
            // чтобы один замах не засчитывался соседу.
            float spacing = config != null ? config.TotalWidth / 3f : 0.357f;
            float radius = Mathf.Min(Mathf.Max(0.06f, diameter * 0.44f), spacing * 0.45f);

            for (int i = 0; i < manager.Drums.Length; i++)
            {
                var drum = manager.Drums[i];
                if (drum == null)
                    continue;

                var head = drum.transform.Find("Head");
                var target = head != null ? head.gameObject : drum.gameObject;

                // На всякий случай убираем дубликаты детекторов (могли остаться от старых версий)
                var detectors = target.GetComponents<DrumHitDetector>();
                for (int d = detectors.Length - 1; d > 0; d--)
                    Object.Destroy(detectors[d]);

                var detector = detectors.Length > 0 ? detectors[0] : target.AddComponent<DrumHitDetector>();
                detector.Configure(drum, radius);
            }

            Debug.Log("[Drums] зоны удара подключены");
        }

        private static bool HasDrums(DrumManager manager)
        {
            var drums = manager.Drums;
            return drums != null && drums.Length > 0 && drums[0] != null;
        }
    }
}
