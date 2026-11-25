using UnityEngine;

namespace RagnarockEditor.Data
{
    /// <summary>
    /// ScriptableObject с конфигурацией расположения барабанов.
    /// Мембраны образуют общий ряд и слегка наклонены к игроку.
    /// Создаётся через Assets → Create → Ragnarock Editor → Drum Layout Config.
    /// </summary>
    [CreateAssetMenu(menuName = "Ragnarock Editor/Drum Layout Config", fileName = "DrumLayoutConfig")]
    public class DrumLayoutConfig : ScriptableObject
    {
        [Header("Позиция относительно игрока")]
        [Tooltip("Расстояние от игрока до линии барабанов (вперёд), метры")]
        [Range(0.3f, 1.5f)]
        public float DistanceFromPlayer = 0.8f;

        [Tooltip("Высота центра барабанов относительно пола, метры")]
        [Range(0.5f, 1.3f)]
        public float DrumHeight = 0.9f;

        [Tooltip("Общая ширина расстановки 4-х барабанов, метры")]
        [Range(0.8f, 2.5f)]
        public float TotalWidth = 1.08f;

        [Header("Радиус полукруга")]
        [Tooltip("0 = ровная линия; >0 = изгиб полукругом к игроку")]
        [Range(0f, 1f)]
        public float ArcRadius = 0f;

        [Header("Наклон барабана")]
        [Tooltip("Угол наклона поверхности к игроку, градусы (0 = строго вверх)")]
        [Range(0f, 30f)]
        public float DrumTiltAngle = 8f;

        [Header("Размеры барабана")]
        [Tooltip("Диаметр бочки барабана, метры")]
        [Range(0.2f, 0.6f)]
        public float DrumDiameter = 0.35f;

        [Tooltip("Высота корпуса барабана, метры")]
        [Range(0.05f, 0.3f)]
        public float DrumHeightSize = 0.30f;

        [Header("Пресет")]
        public string PresetName = "Стандартный";

        /// <summary>
        /// Применяет настройки из профиля калибровки.
        /// </summary>
        public void ApplyProfile(DrumCalibrationProfile profile)
        {
            DistanceFromPlayer = profile.DistanceToDrums;
            DrumHeight = profile.DrumHeight;
            TotalWidth = profile.TotalWidth;
            ArcRadius = profile.ArcRadius;
            DrumTiltAngle = profile.DrumTiltAngle;
        }

        /// <summary>
        /// Экспортирует текущие настройки в профиль калибровки.
        /// </summary>
        public DrumCalibrationProfile ToProfile()
        {
            return new DrumCalibrationProfile
            {
                DistanceToDrums = DistanceFromPlayer,
                DrumHeight = DrumHeight,
                TotalWidth = TotalWidth,
                ArcRadius = ArcRadius,
                DrumTiltAngle = DrumTiltAngle
            };
        }
    }
}
