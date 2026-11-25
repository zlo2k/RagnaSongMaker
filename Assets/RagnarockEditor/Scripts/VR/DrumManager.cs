using System;
using UnityEngine;
using RagnarockEditor.Data;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Управляет всеми 4-мя барабанами: создание, позиционирование, события.
    /// </summary>
    public class DrumManager : MonoBehaviour
    {
        [Header("Префаб барабана")]
        [SerializeField] private GameObject _drumPrefab;

        [Header("Родительский объект для барабанов")]
        [SerializeField] private Transform _drumsParent;

        /// <summary>Массив контроллеров барабанов (индексы 0-3).</summary>
        public DrumController[] Drums { get; private set; } = new DrumController[4];

        /// <summary>Событие: удар по барабану (drumIndex, timeSeconds, velocity).</summary>
        public event Action<int, float, float> OnAnyDrumHit;

        /// <summary>XR Origin (для привязки позиций).</summary>
        public Transform XROrigin { get; set; }

        private void Awake()
        {
            if (_drumsParent == null)
            {
                _drumsParent = transform;
            }
        }

        /// <summary>
        /// Создаёт 4 барабана из префаба.
        /// </summary>
        public void CreateDrums()
        {
            // Защита от повторного создания: иначе на сцене окажется 8 барабанов,
            // и один удар будет регистрироваться дважды (старый + новый набор).
            if (Drums != null && Drums.Length > 0 && Drums[0] != null)
            {
                Debug.LogWarning("[DrumManager] барабаны уже созданы — повторный CreateDrums проигнорирован");
                return;
            }

            if (_drumPrefab == null)
            {
                Debug.LogError("[DrumManager] Drum Prefab не назначен!");
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                GameObject drumObj = Instantiate(_drumPrefab, _drumsParent);
                drumObj.name = $"Drum_{i}";

                DrumController controller = drumObj.GetComponent<DrumController>();
                if (controller == null)
                {
                    controller = drumObj.AddComponent<DrumController>();
                }

                // Подписываемся на события удара
                int drumIndex = i; // Захват переменной для замыкания
                controller.SetDrumIndex(drumIndex);
                controller.OnDrumHit.AddListener((index, velocity) =>
                {
                    float timeSeconds = GetCurrentSongTime();
                    // индекс берём из позиции в массиве, а не из события: так удар гарантированно
                    // относится к тому барабану, который стоит на этом месте
                    OnAnyDrumHit?.Invoke(drumIndex, timeSeconds, velocity);
                });

                Drums[i] = controller;
            }

            Debug.Log("[DrumManager] Создано 4 барабана.");
        }

        /// <summary>
        /// Позиционирует барабаны согласно массиву позиций и поворотов.
        /// </summary>
        public void PositionDrums(Vector3[] positions, Quaternion rotation)
        {
            if (positions.Length != 4)
            {
                Debug.LogError($"[DrumManager] Ожидается 4 позиции, получено {positions.Length}");
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                if (Drums[i] != null)
                {
                    // Индекс = позиция в массиве. Чинит и барабаны, созданные из префаба,
                    // и уже стоящие в сцене экземпляры с унаследованным _drumIndex = 0.
                    Drums[i].SetDrumIndex(i);
                    Drums[i].SetPositionAndRotation(positions[i], rotation);
                }
            }
        }

        /// <summary>
        /// Приводит размер всех барабанов к диаметру из конфига (префаб собран под 0.35 м).
        /// </summary>
        public void SetDrumDiameter(float diameter)
        {
            float scale = Mathf.Max(0.2f, diameter) / 0.35f;

            for (int i = 0; i < Drums.Length; i++)
            {
                if (Drums[i] != null)
                    Drums[i].SetVisualScale(scale);
            }
        }

        /// <summary>
        /// Включает/выключает коллизии на всех барабанах (для паузы).
        /// </summary>
        public void SetDrumsActive(bool active)
        {
            foreach (var drum in Drums)
            {
                if (drum != null)
                {
                    Collider col = drum.GetComponent<Collider>();
                    if (col != null) col.enabled = active;
                }
            }
        }

        /// <summary>
        /// Подсвечивает указанный барабан (для предпросмотра).
        /// </summary>
        public void HighlightDrum(int index, bool active)
        {
            if (index >= 0 && index < 4 && Drums[index] != null)
            {
                Drums[index].Highlight(active);
            }
        }

        /// <summary>
        /// Сбрасывает подсветку всех барабанов.
        /// </summary>
        public void ClearAllHighlights()
        {
            foreach (var drum in Drums)
            {
                if (drum != null) drum.Highlight(false);
            }
        }

        /// <summary>
        /// Возвращает текущее время песни.
        /// Если AudioSource не задан, возвращает Time.time.
        /// </summary>
        private float GetCurrentSongTime()
        {
            // Будет переопределено через RecordManager
            var recordManager = FindObjectOfType<Recording.RecordManager>();
            if (recordManager != null)
            {
                return recordManager.CurrentSongTime;
            }
            return Time.time;
        }
    }
}
