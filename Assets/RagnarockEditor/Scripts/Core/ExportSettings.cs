using System.IO;
using UnityEngine;

namespace RagnarockEditor.Core
{
    /// <summary>
    /// Куда сохранять карты. На Quest нельзя свободно писать в чужие папки,
    /// поэтому предлагаем несколько готовых мест (переключаются кнопкой в предпросмотре):
    ///  - память приложения (по умолчанию, всегда доступна на запись);
    ///  - /sdcard/Music/RagnarockMaps (видно при подключении по USB);
    ///  - /sdcard/Download/RagnarockMaps.
    ///
    /// Выбор хранится в PlayerPrefs и переживает перезапуск.
    /// </summary>
    public static class ExportSettings
    {
        private const string PrefsKey = "ragna.export.root";

        /// <summary>Сколько готовых папок предлагаем.</summary>
        public const int PresetCount = 3;

        /// <summary>Папка по умолчанию — внутри приложения.</summary>
        public static string DefaultRoot => Path.Combine(Application.persistentDataPath, "CustomSongs");

        /// <summary>Текущая папка экспорта.</summary>
        public static string RootPath
        {
            get
            {
                string saved = PlayerPrefs.GetString(PrefsKey, string.Empty);
                return string.IsNullOrWhiteSpace(saved) ? DefaultRoot : saved;
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                PlayerPrefs.SetString(PrefsKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Путь одной из готовых папок.</summary>
        public static string PresetPath(int index)
        {
            switch (((index % PresetCount) + PresetCount) % PresetCount)
            {
                case 1:
                    return "/sdcard/Music/RagnarockMaps";
                case 2:
                    return "/sdcard/Download/RagnarockMaps";
                default:
                    return DefaultRoot;
            }
        }

        /// <summary>Индекс текущей папки в списке готовых (0, если путь свой).</summary>
        public static int CurrentPresetIndex
        {
            get
            {
                string current = RootPath;

                for (int i = 0; i < PresetCount; i++)
                {
                    if (string.Equals(current, PresetPath(i), System.StringComparison.Ordinal))
                        return i;
                }

                return 0;
            }
        }

        /// <summary>Переключает на следующую готовую папку и возвращает её индекс.</summary>
        public static int NextPreset()
        {
            int next = (CurrentPresetIndex + 1) % PresetCount;
            RootPath = PresetPath(next);
            return next;
        }

        /// <summary>Создаёт папку экспорта, если её ещё нет.</summary>
        public static string EnsureRoot()
        {
            string path = RootPath;

            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Export] не удалось создать папку '{path}': {e.Message}");
            }

            return path;
        }
    }
}
