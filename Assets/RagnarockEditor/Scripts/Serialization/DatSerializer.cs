using System.IO;
using Newtonsoft.Json;
using RagnarockEditor.Data;
using UnityEngine;

namespace RagnarockEditor.Serialization
{
    /// <summary>
    /// Сериализатор .dat файлов для Ragnarock.
    /// Записывает info.dat и Beatmap.dat в указанную папку уровня.
    /// </summary>
    public static class DatSerializer
    {
        /// <summary>
        /// Настройки JSON с читаемым форматированием.
        /// </summary>
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include
        };

        /// <summary>
        /// Сохраняет info.dat в указанную папку.
        /// </summary>
        /// <param name="folderPath">Путь к папке уровня</param>
        /// <param name="info">Данные info.dat</param>
        /// <returns>true если успешно</returns>
        public static bool SaveInfoDat(string folderPath, InfoDatDTO info)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string filePath = Path.Combine(folderPath, "info.dat");
                string json = JsonConvert.SerializeObject(info, JsonSettings);
                File.WriteAllText(filePath, json);

                Debug.Log($"[DatSerializer] info.dat сохранён: {filePath}");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DatSerializer] Ошибка сохранения info.dat: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Сохраняет Beatmap.dat (карту нот) в указанную папку.
        /// </summary>
        /// <param name="folderPath">Путь к папке уровня</param>
        /// <param name="beatmap">Данные Beatmap.dat</param>
        /// <param name="filename">Имя файла (напр. "Normal.dat", "Expert.dat")</param>
        /// <returns>true если успешно</returns>
        public static bool SaveBeatmapDat(string folderPath, BeatmapDatDTO beatmap, string filename)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                // Сортируем ноты перед сохранением
                beatmap.SortNotes();

                string filePath = Path.Combine(folderPath, filename);
                string json = JsonConvert.SerializeObject(beatmap, JsonSettings);
                File.WriteAllText(filePath, json);

                Debug.Log($"[DatSerializer] {filename} сохранён: {filePath} (нот: {beatmap.Notes.Count})");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DatSerializer] Ошибка сохранения {filename}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Сохраняет полный уровень (info.dat + Beatmap.dat) из RecordSession.
        /// </summary>
        /// <param name="folderPath">Путь к папке уровня</param>
        /// <param name="session">Сессия записи с данными</param>
        /// <returns>true если успешно</returns>
        public static bool SaveFullLevel(string folderPath, RecordSession session)
        {
            if (session == null || session.Song == null)
            {
                Debug.LogError("[DatSerializer] RecordSession или SongData не заданы.");
                return false;
            }

            // 1. Формируем info.dat
            var info = new InfoDatDTO
            {
                SongName = session.Song.SongName,
                SongAuthorName = session.Song.AuthorName,
                LevelAuthorName = session.LevelAuthorName,
                BeatsPerMinute = session.Song.BPM,
                SongFilename = "song.ogg",
                CoverImageFilename = "cover.jpg",
                EnvironmentName = session.EnvironmentName,
                SongApproximativeDuration = Mathf.RoundToInt(session.Song.DurationSeconds)
            };

            info.AddDifficulty(
                difficultyName: session.DifficultyName,
                difficultyRank: session.DifficultyRank,
                beatmapFilename: $"{session.DifficultyName}.dat"
            );

            // 2. Формируем Beatmap.dat
            var beatmap = new BeatmapDatDTO();
            beatmap.Notes.AddRange(session.QuantizedNotes);

            // 3. Сохраняем оба файла
            bool infoOk = SaveInfoDat(folderPath, info);
            bool beatmapOk = SaveBeatmapDat(folderPath, beatmap, $"{session.DifficultyName}.dat");

            return infoOk && beatmapOk;
        }
    }
}
