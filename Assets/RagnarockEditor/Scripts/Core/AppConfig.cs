using UnityEngine;

namespace RagnarockEditor.Core
{
    /// <summary>
    /// ScriptableObject с глобальными настройками приложения.
    /// </summary>
    [CreateAssetMenu(menuName = "Ragnarock Editor/App Config", fileName = "AppConfig")]
    public class AppConfig : ScriptableObject
    {
        [Header("Аудио")]
        [Range(0f, 1f)]
        public float MasterVolume = 0.8f;
        [Range(0f, 1f)]
        public float MetronomeVolume = 0.7f;
        [Range(0f, 0.5f)]
        public float AudioLatencyCompensation = 0.05f; // Компенсация задержки (сек)

        [Header("Haptic")]
        [Range(0f, 1f)]
        public float HapticIntensity = 1f;
        public bool HapticEnabled = true;

        [Header("Квантование (по умолчанию)")]
        [Range(0.02f, 0.5f)]
        public float DefaultSnapThreshold = 0.15f;
        [Range(1, 8)]
        public int DefaultSnapDivision = 4;

        [Header("Барабаны (по умолчанию)")]
        [Range(0.3f, 1.5f)]
        public float DefaultDrumDistance = 0.8f;
        [Range(0.5f, 1.3f)]
        public float DefaultDrumHeight = 0.9f;
        [Range(0.8f, 2.5f)]
        public float DefaultDrumWidth = 1.6f;

        [Header("Язык")]
        public SystemLanguage Language = SystemLanguage.Russian;
    }
}
