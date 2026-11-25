using System.IO;
using RagnarockEditor.Data;
using RagnarockEditor.Serialization;
using UnityEngine;

namespace RagnarockEditor.FileSystem
{
    /// <summary>
    /// Экспортёр уровня в папку CustomSongs.
    /// Собирает все файлы уровня: song.ogg, cover.jpg, info.dat, Beatmap.dat.
    /// </summary>
    public class CustomSongsExporter : MonoBehaviour
    {
        /// <summary>Событие: экспорт успешно завершён.</summary>
        public event System.Action<string> OnExportComplete; // folderPath

        /// <summary>Событие: ошибка экспорта.</summary>
        public event System.Action<string> OnExportError; // error message

        /// <summary>
        /// Экспортирует уровень из RecordSession.
        /// </summary>
        /// <param name="session">Сессия записи</param>
        /// <param name="sourceAudioPath">Путь к исходному аудиофайлу</param>
        /// <param name="sourceCoverPath">Путь к обложке (может быть null)</param>
        public void ExportLevel(RecordSession session, string sourceAudioPath, string sourceCoverPath = null)
        {
            if (session == null || session.Song == null)
            {
                OnExportError?.Invoke("RecordSession или SongData не заданы.");
                return;
            }

            if (session.QuantizedNotes.Count == 0)
            {
                OnExportError?.Invoke("Нет квантованных нот для экспорта. Сначала выполните квантование.");
                return;
            }

            // 1. Создаём папку уровня
            string folderPath = SongFileManager.CreateLevelFolder(session.Song.SongName);

            // 2. Копируем аудиофайл
            string audioTarget = SongFileManager.CopySongToLevelFolder(sourceAudioPath, folderPath);
            if (audioTarget == null)
            {
                OnExportError?.Invoke("Не удалось скопировать аудиофайл.");
                return;
            }

            // 3. Копируем обложку (или создаём заглушку)
            if (!string.IsNullOrEmpty(sourceCoverPath) && File.Exists(sourceCoverPath))
            {
                SongFileManager.CopyCoverToLevelFolder(sourceCoverPath, folderPath);
            }
            else
            {
                Debug.Log("[CustomSongsExporter] Обложка не указана. Создайте cover.jpg вручную.");
            }

            // 4. Валидируем данные
            // Формируем info.dat для валидации
            var info = new InfoDatDTO
            {
                SongName = session.Song.SongName,
                SongAuthorName = session.Song.AuthorName,
                LevelAuthorName = session.LevelAuthorName,
                BeatsPerMinute = session.Song.BPM,
                SongApproximativeDuration = Mathf.RoundToInt(session.Song.DurationSeconds),
                EnvironmentName = session.EnvironmentName
            };

            var beatmap = new BeatmapDatDTO();
            beatmap.Notes.AddRange(session.QuantizedNotes);

            var validation = LevelValidator.ValidateFull(info, beatmap);
            if (!validation.IsValid)
            {
                Debug.LogWarning($"[CustomSongsExporter] Валидация не пройдена:\n{validation}");
                // Не прерываем экспорт при наличии только предупреждений
            }

            // 5. Сохраняем .dat файлы
            bool success = DatSerializer.SaveFullLevel(folderPath, session);

            if (success)
            {
                Debug.Log($"[CustomSongsExporter] Уровень экспортирован в: {folderPath}");
                OnExportComplete?.Invoke(folderPath);
            }
            else
            {
                OnExportError?.Invoke("Ошибка сохранения .dat файлов.");
            }
        }

        /// <summary>
        /// Возвращает путь для ручного копирования на Quest.
        /// </summary>
        public static string GetQuestTargetPath()
        {
            return "Android/data/com.wanadev.ragnarockquest/files/UE4Game/Ragnarock/Ragnarock/Saved/CustomSongs/";
        }

        /// <summary>
        /// Выводит инструкцию по установке уровня на Quest.
        /// </summary>
        public static string GetInstallInstructions(string levelFolderName)
        {
            return 
                $"Для установки уровня на Quest 3:\n" +
                $"1. Подключите Quest к ПК через USB-кабель\n" +
                $"2. Скопируйте папку '{levelFolderName}' из:\n" +
                $"   {SongFileManager.GetCustomSongsPath()}\n" +
                $"3. Вставьте в:\n" +
                $"   {GetQuestTargetPath()}\n" +
                $"4. Перезапустите Ragnarock на Quest";
        }
    }
}
