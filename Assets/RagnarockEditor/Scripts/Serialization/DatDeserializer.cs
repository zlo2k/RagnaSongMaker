using System.IO;
using Newtonsoft.Json;
using RagnarockEditor.Data;
using UnityEngine;

namespace RagnarockEditor.Serialization
{
    /// <summary>
    /// Десериализатор .dat файлов Ragnarock.
    /// Читает info.dat и Beatmap.dat из папки уровня.
    /// </summary>
    public static class DatDeserializer
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>
        /// Загружает info.dat из папки уровня.
        /// </summary>
        /// <param name="folderPath">Путь к папке уровня</param>
        /// <returns>InfoDatDTO или null при ошибке</returns>
        public static InfoDatDTO LoadInfoDat(string folderPath)
        {
            try
            {
                string filePath = Path.Combine(folderPath, "info.dat");
                if (!File.Exists(filePath))
                {
                    Debug.LogError($"[DatDeserializer] info.dat не найден: {filePath}");
                    return null;
                }

                string json = File.ReadAllText(filePath);
                var info = JsonConvert.DeserializeObject<InfoDatDTO>(json, JsonSettings);
                Debug.Log($"[DatDeserializer] info.dat загружен: {info.SongName}, BPM={info.BeatsPerMinute}");
                return info;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DatDeserializer] Ошибка загрузки info.dat: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Загружает Beatmap.dat (карту нот).
        /// </summary>
        /// <param name="folderPath">Путь к папке уровня</param>
        /// <param name="filename">Имя файла (напр. "Normal.dat")</param>
        /// <returns>BeatmapDatDTO или null при ошибке</returns>
        public static BeatmapDatDTO LoadBeatmapDat(string folderPath, string filename)
        {
            try
            {
                string filePath = Path.Combine(folderPath, filename);
                if (!File.Exists(filePath))
                {
                    Debug.LogError($"[DatDeserializer] {filename} не найден: {filePath}");
                    return null;
                }

                string json = File.ReadAllText(filePath);
                var beatmap = JsonConvert.DeserializeObject<BeatmapDatDTO>(json, JsonSettings);

                // Валидируем загруженные ноты
                int removedCount = 0;
                beatmap.Notes.RemoveAll(note =>
                {
                    bool invalid = note.LineIndex < 0 || note.LineIndex > 3;
                    if (invalid) removedCount++;
                    return invalid;
                });

                if (removedCount > 0)
                {
                    Debug.LogWarning($"[DatDeserializer] Удалено {removedCount} нот с некорректным _lineIndex.");
                }

                Debug.Log($"[DatDeserializer] {filename} загружен: {beatmap.Notes.Count} нот");
                return beatmap;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DatDeserializer] Ошибка загрузки {filename}: {e.Message}");
                return null;
            }
        }
    }
}
