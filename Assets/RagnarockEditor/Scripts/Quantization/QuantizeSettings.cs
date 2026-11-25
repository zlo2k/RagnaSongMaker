using UnityEngine;

namespace RagnarockEditor.Quantization
{
    /// <summary>
    /// ScriptableObject с настройками квантования ритма.
    /// </summary>
    [CreateAssetMenu(menuName = "Ragnarock Editor/Quantize Settings", fileName = "QuantizeSettings")]
    public class QuantizeSettings : ScriptableObject
    {
        [Header("Snap-to-Beat")]
        [Tooltip("Максимальное расстояние (сек) от удара до ближайшего бита для привязки")]
        [Range(0.02f, 0.5f)]
        public float SnapThreshold = 0.15f;

        [Tooltip("К каким долям привязывать (1 = целые, 2 = половинные, 4 = четвертные, 8 = восьмые)")]
        [Range(1, 8)]
        public int SnapDivision = 4;

        [Header("Фильтрация")]
        [Tooltip("Минимальное расстояние между нотами на одном барабане (сек)")]
        [Range(0.02f, 0.5f)]
        public float MinNoteSpacing = 0.06f;

        [Tooltip("Удалять ли изолированные ноты (без соседей в пределах окна)")]
        public bool RemoveIsolatedNotes = false;

        [Tooltip("Размер окна для проверки изоляции (сек)")]
        [Range(0.5f, 5f)]
        public float IsolationWindow = 2f;
    }
}
