using System.IO;
using RagnarockEditor.Core;
using UnityEngine;

namespace RagnarockEditor.FileSystem
{
    /// <summary>
    /// Менеджер файлов песен: копирование, проверка, подготовка.
    /// </summary>
    public static class SongFileManager
    {
        /// <summary>
        /// Копирует аудиофайл в папку уровня с именем "song.ogg".
        /// Если исходный файл не .ogg, конвертирует (или просто копирует с переименованием).
        /// </summary>
        /// <param name="sourcePath">Путь к исходному аудиофайлу</param>
        /// <param name="targetFolder">Папка назначения (папка уровня)</param>
        /// <returns>Путь к скопированному файлу или null при ошибке</returns>
        public static string CopySongToLevelFolder(string sourcePath, string targetFolder)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    Debug.LogError($"[SongFileManager] Исходный файл не найден: {sourcePath}");
                    return null;
                }

                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                string targetPath = Path.Combine(targetFolder, "song.ogg");

                // Если исходный файл уже .ogg — просто копируем
                string sourceExt = Path.GetExtension(sourcePath).ToLower();
                if (sourceExt == ".ogg")
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                    Debug.Log($"[SongFileManager] Скопирован .ogg: {sourcePath} → {targetPath}");
                }
                else
                {
                    // Для других форматов: копируем как есть, но переименовываем в .ogg
                    // (Ragnarock ожидает song.ogg; для не-.ogg может потребоваться реальная конвертация через FFmpeg)
                    File.Copy(sourcePath, targetPath, overwrite: true);
                    Debug.LogWarning($"[SongFileManager] Файл {sourceExt} скопирован как song.ogg. " +
                                    "Возможно, потребуется конвертация через FFmpeg на ПК.");
                }

                return targetPath;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SongFileManager] Ошибка копирования: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Копирует изображение обложки в папку уровня.
        /// </summary>
        /// <param name="sourcePath">Путь к изображению (.jpg/.png)</param>
        /// <param name="targetFolder">Папка назначения</param>
        /// <returns>Путь к скопированному файлу или null</returns>
        public static string CopyCoverToLevelFolder(string sourcePath, string targetFolder)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    Debug.LogWarning($"[SongFileManager] Файл обложки не найден: {sourcePath}. Используется заглушка.");
                    return null;
                }

                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                string targetPath = Path.Combine(targetFolder, "cover.jpg");
                File.Copy(sourcePath, targetPath, overwrite: true);
                Debug.Log($"[SongFileManager] Обложка скопирована: {sourcePath} → {targetPath}");
                return targetPath;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SongFileManager] Ошибка копирования обложки: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Возвращает папку экспорта карт (выбирается в предпросмотре, по умолчанию —
        /// persistentDataPath/CustomSongs).
        /// </summary>
        public static string GetCustomSongsPath()
        {
            return ExportSettings.EnsureRoot();
        }

        /// <summary>
        /// Создаёт папку для нового уровня.
        /// </summary>
        /// <param name="songName">Название песни (используется как имя папки)</param>
        /// <returns>Путь к созданной папке</returns>
        public static string CreateLevelFolder(string songName)
        {
            // Очищаем имя от недопустимых символов
            string safeName = SanitizeFolderName(songName);
            string basePath = GetCustomSongsPath();
            string folderPath = Path.Combine(basePath, safeName);

            // Если папка уже существует, добавляем суффикс
            int suffix = 1;
            string originalPath = folderPath;
            while (Directory.Exists(folderPath))
            {
                folderPath = $"{originalPath}_{suffix}";
                suffix++;
            }

            Directory.CreateDirectory(folderPath);
            Debug.Log($"[SongFileManager] Создана папка уровня: {folderPath}");
            return folderPath;
        }

        /// <summary>
        /// Очищает имя папки от недопустимых символов.
        /// </summary>
        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Untitled";

            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c.ToString(), "_");
            }

            // Обрезаем до разумной длины
            if (name.Length > 50) name = name.Substring(0, 50);

            return name.Trim();
        }
    }
}
