using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RagnarockEditor.FileSystem
{
    /// <summary>
    /// Браузер аудиофайлов на устройстве (Quest 3 / Android).
    /// Сканирует указанные папки на наличие поддерживаемых аудиоформатов.
    /// </summary>
    public static class FileBrowser
    {
        /// <summary>Поддерживаемые расширения аудиофайлов.</summary>
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>
        {
            ".ogg", ".mp3", ".wav", ".m4a", ".aac", ".flac"
        };

        /// <summary>
        /// Информация о найденном аудиофайле.
        /// </summary>
        [Serializable]
        public class AudioFileInfo
        {
            public string FullPath;
            public string FileName;
            public string Extension;
            public long FileSizeBytes;
            public DateTime LastModified;

            public string FileSizeFormatted
            {
                get
                {
                    if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
                    if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024f:F1} KB";
                    return $"{FileSizeBytes / (1024f * 1024f):F1} MB";
                }
            }

            public override string ToString() => $"{FileName} ({FileSizeFormatted})";
        }

        /// <summary>
        /// Сканирует папку на наличие аудиофайлов (рекурсивно).
        /// </summary>
        /// <param name="directoryPath">Путь к папке</param>
        /// <param name="recursive">Рекурсивный поиск</param>
        /// <returns>Список найденных аудиофайлов</returns>
        public static List<AudioFileInfo> ScanDirectory(string directoryPath, bool recursive = true)
        {
            List<AudioFileInfo> files = new List<AudioFileInfo>();

            if (!Directory.Exists(directoryPath))
            {
                Debug.LogWarning($"[FileBrowser] Папка не существует: {directoryPath}");
                return files;
            }

            try
            {
                SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*.*", option))
                {
                    string ext = Path.GetExtension(filePath).ToLower();
                    if (SupportedExtensions.Contains(ext))
                    {
                        FileInfo fi = new FileInfo(filePath);
                        files.Add(new AudioFileInfo
                        {
                            FullPath = filePath,
                            FileName = fi.Name,
                            Extension = ext,
                            FileSizeBytes = fi.Length,
                            LastModified = fi.LastWriteTime
                        });
                    }
                }

                // Сортируем по имени
                files.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileBrowser] Ошибка сканирования папки: {e.Message}");
            }

            Debug.Log($"[FileBrowser] Найдено аудиофайлов: {files.Count} в {directoryPath}");
            return files;
        }

        /// <summary>
        /// Возвращает стандартные пути для поиска музыки на Android/Quest.
        /// </summary>
        public static List<string> GetDefaultSearchPaths()
        {
            List<string> paths = new List<string>();

            // Внутренняя память устройства
            paths.Add(Application.persistentDataPath);

#if UNITY_ANDROID && !UNITY_EDITOR
            // Стандартные папки Android
            paths.Add("/storage/emulated/0/Music");
            paths.Add("/storage/emulated/0/Download");
            paths.Add("/storage/emulated/0/Android/data/com.wanadev.ragnarockquest/files/UE4Game/Ragnarock/Ragnarock/Saved/CustomSongs");
#endif

            return paths;
        }

        /// <summary>
        /// Проверяет, поддерживается ли расширение файла.
        /// </summary>
        public static bool IsSupportedFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            return SupportedExtensions.Contains(ext);
        }

        /// <summary>
        /// Фильтрует список по строке поиска.
        /// </summary>
        public static List<AudioFileInfo> FilterBySearch(List<AudioFileInfo> files, string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return files;

            string searchLower = search.ToLower();
            return files.Where(f => f.FileName.ToLower().Contains(searchLower)).ToList();
        }
    }
}
