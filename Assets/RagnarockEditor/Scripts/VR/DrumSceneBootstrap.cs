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
            AttachDetectors(manager);
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
        private static void AttachDetectors(DrumManager manager)
        {
            if (manager == null || manager.Drums == null)
                return;

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

                // Радиус 0.16 < половины шага (0.178): зоны соседних барабанов не перекрываются,
                // а удар засчитывается только при заходе головки сверху в цилиндр.
                detector.Configure(drum, 0.16f);
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
