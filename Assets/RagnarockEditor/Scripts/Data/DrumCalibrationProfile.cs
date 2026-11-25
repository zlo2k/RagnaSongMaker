using System;
using UnityEngine;

namespace RagnarockEditor.Data
{
    /// <summary>
    /// Профиль калибровки барабанов под конкретного игрока.
    /// Сохраняется в PlayerPrefs как JSON (ключ "ragnaGod.calibration").
    /// </summary>
    [Serializable]
    public class DrumCalibrationProfile
    {
        /// <summary>Ключ PlayerPrefs по умолчанию (M2).</summary>
        public const string DefaultPrefsKey = "ragnaGod.calibration";

        /// <summary>Старый ключ (до M2) — нужен только для миграции уже сохранённых профилей.</summary>
        public const string LegacyPrefsKey = "DrumCalibrationProfile";

        /// <summary>Разумный минимум расстояния до барабанов (м).</summary>
        public const float MinDistance = 0.35f;

        /// <summary>Разумный максимум расстояния до барабанов (м).</summary>
        public const float MaxDistance = 1.2f;

        /// <summary>Минимальный правдоподобный рост игрока (м).</summary>
        public const float MinPlayerHeight = 0.8f;

        /// <summary>Максимальный правдоподобный рост игрока (м).</summary>
        public const float MaxPlayerHeight = 2.5f;

        /// <summary>Рост игрока в метрах.</summary>
        public float PlayerHeight = 1.75f;

        /// <summary>Расстояние от игрока до линии барабанов (м).</summary>
        public float DistanceToDrums = 0.8f;

        /// <summary>Высота центра барабанов от пола (м).</summary>
        public float DrumHeight = 0.9f;

        /// <summary>Общая ширина расстановки 4-х барабанов (м).</summary>
        public float TotalWidth = 1.6f;

        /// <summary>Радиус дуги (0 = ровная линия).</summary>
        public float ArcRadius = 0.3f;

        /// <summary>Угол наклона поверхности барабана к игроку (градусы).</summary>
        public float DrumTiltAngle = 10f;

        /// <summary>Имя профиля.</summary>
        public string ProfileName = "Default";

        /// <summary>Дата последней калибровки ("yyyy-MM-dd HH:mm").</summary>
        public string LastCalibrated = "";

        /// <summary>
        /// Создаёт профиль с настройками по умолчанию.
        /// </summary>
        public static DrumCalibrationProfile CreateDefault()
        {
            return new DrumCalibrationProfile
            {
                PlayerHeight = 1.75f,
                DistanceToDrums = 0.8f,
                DrumHeight = 0.9f,
                TotalWidth = 1.6f,
                ArcRadius = 0.3f,
                DrumTiltAngle = 10f,
                ProfileName = "Default",
                LastCalibrated = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };
        }

        /// <summary>
        /// Есть ли уже сохранённый профиль (новый или старый ключ).
        /// </summary>
        public static bool HasSaved()
        {
            return PlayerPrefs.HasKey(DefaultPrefsKey) || PlayerPrefs.HasKey(LegacyPrefsKey);
        }

        /// <summary>
        /// Загружает профиль из PlayerPrefs по ключу по умолчанию.
        /// Возвращает null, если профиля нет или JSON не читается.
        /// Если найден только старый ключ ("DrumCalibrationProfile"), профиль переносится.
        /// </summary>
        public static DrumCalibrationProfile Load()
        {
            var profile = LoadFromKey(DefaultPrefsKey);
            if (profile != null)
                return profile;

            profile = LoadFromKey(LegacyPrefsKey);
            if (profile != null)
            {
                Debug.Log($"[Calibration] Профиль найден по старому ключу \"{LegacyPrefsKey}\" — переношу в \"{DefaultPrefsKey}\"");
                profile.Save();
            }

            return profile;
        }

        /// <summary>
        /// Загружает профиль по произвольному ключу (для нескольких профилей).
        /// Возвращает null, если профиля нет.
        /// </summary>
        public static DrumCalibrationProfile Load(string key)
        {
            if (string.IsNullOrEmpty(key))
                key = DefaultPrefsKey;

            return LoadFromKey(key);
        }

        /// <summary>
        /// Загружает профиль по имени (ключ "DrumCalibration_{profileName}").
        /// </summary>
        public static DrumCalibrationProfile LoadProfile(string profileName)
        {
            return Load($"DrumCalibration_{profileName}");
        }

        /// <summary>
        /// Сохраняет профиль в PlayerPrefs (ключ по умолчанию) и обновляет дату.
        /// </summary>
        public void Save()
        {
            LastCalibrated = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Save(DefaultPrefsKey);
        }

        /// <summary>
        /// Сохраняет профиль в PlayerPrefs по указанному ключу.
        /// </summary>
        public void Save(string key)
        {
            if (string.IsNullOrEmpty(key))
                key = DefaultPrefsKey;

            if (string.IsNullOrEmpty(ProfileName))
                ProfileName = "Default";

            Sanitize();

            string json = JsonUtility.ToJson(this, true);
            PlayerPrefs.SetString(key, json);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Статический вариант сохранения (удобно вызывать из UI/кода).
        /// </summary>
        public static void Save(DrumCalibrationProfile profile)
        {
            if (profile == null)
            {
                Debug.LogWarning("[Calibration] Save: профиль null — сохранять нечего");
                return;
            }

            profile.Save();
        }

        /// <summary>
        /// Удаляет сохранённый профиль (новый и старый ключ).
        /// </summary>
        public static void Reset()
        {
            PlayerPrefs.DeleteKey(DefaultPrefsKey);
            PlayerPrefs.DeleteKey(LegacyPrefsKey);
            PlayerPrefs.Save();
            Debug.Log("[Calibration] Сохранённый профиль калибровки сброшен");
        }

        /// <summary>
        /// Сохраняет профиль под отдельным именем (несколько игроков на устройстве).
        /// </summary>
        public void SaveAsProfile(string profileName)
        {
            ProfileName = string.IsNullOrEmpty(profileName) ? "Default" : profileName;
            LastCalibrated = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Save($"DrumCalibration_{profileName}");
        }

        /// <summary>
        /// Приводит значения к разумным рамкам (защита от битого JSON/старых профилей).
        /// </summary>
        public void Sanitize()
        {
            PlayerHeight = Mathf.Clamp(PlayerHeight, MinPlayerHeight, MaxPlayerHeight);
            DistanceToDrums = Mathf.Clamp(DistanceToDrums, MinDistance, MaxDistance);
            DrumHeight = Mathf.Clamp(DrumHeight, 0.5f, 1.3f);
            TotalWidth = Mathf.Clamp(TotalWidth, 0.8f, 2.5f);
            ArcRadius = Mathf.Clamp(ArcRadius, 0f, 1f);
            DrumTiltAngle = Mathf.Clamp(DrumTiltAngle, 0f, 30f);

            if (string.IsNullOrEmpty(ProfileName))
                ProfileName = "Default";
        }

        private static DrumCalibrationProfile LoadFromKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !PlayerPrefs.HasKey(key))
                return null;

            string json = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json))
                return null;

            DrumCalibrationProfile profile;
            try
            {
                profile = JsonUtility.FromJson<DrumCalibrationProfile>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Calibration] Не удалось прочитать профиль \"{key}\": {e.Message}");
                return null;
            }

            if (profile == null)
                return null;

            profile.Sanitize();
            return profile;
        }
    }
}
