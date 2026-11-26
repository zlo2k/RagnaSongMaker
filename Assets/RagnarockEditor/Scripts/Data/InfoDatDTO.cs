using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RagnarockEditor.Data
{
    /// <summary>
    /// DTO для файла info.dat — точка входа уровня Ragnarock.
    /// Содержит метаданные: название, BPM, обложку, ссылки на файлы сложностей.
    /// </summary>
    [Serializable]
    public class InfoDatDTO
    {
        /// <summary>
        /// Версия формата. Все карты RagnaCustoms/Edda пишут "1"; игра значение не сверяет,
        /// но держим его одинаковым с экосистемой.
        /// </summary>
        [JsonProperty("_version")]
        public string Version = "1";

        [JsonProperty("_songName")]
        public string SongName = "";

        [JsonProperty("_songSubName")]
        public string SongSubName = "";

        [JsonProperty("_songAuthorName")]
        public string SongAuthorName = "";

        [JsonProperty("_levelAuthorName")]
        public string LevelAuthorName = "";

        /// <summary>
        /// Явный контент. В формате Ragnarock это СТРОКА ("false"/"true"), а не bool —
        /// так пишут и Edda, и карты с RagnaCustoms.
        /// </summary>
        [JsonProperty("_explicit")]
        public string Explicit = "false";

        /// <summary>
        /// Главный параметр для расчета времени спавна рун.
        /// Время нот в битах: time_sec = _time * 60 / BPM
        /// </summary>
        [JsonProperty("_beatsPerMinute")]
        public float BeatsPerMinute = 120f;

        [JsonProperty("_songTimeOffset")]
        public float SongTimeOffset = 0f;

        [JsonProperty("_shuffle")]
        public float Shuffle = 0f;

        [JsonProperty("_shufflePeriod")]
        public float ShufflePeriod = 0.5f;

        [JsonProperty("_previewStartTime")]
        public float PreviewStartTime = 30f;

        [JsonProperty("_previewDuration")]
        public float PreviewDuration = 10f;

        [JsonProperty("_songFilename")]
        public string SongFilename = "song.ogg";

        [JsonProperty("_coverImageFilename")]
        public string CoverImageFilename = "cover.jpg";

        /// <summary>
        /// Окружение: Midgard, Alfheim, Asgard, Muspelheim, etc.
        /// </summary>
        [JsonProperty("_environmentName")]
        public string EnvironmentName = "Midgard";

        /// <summary>
        /// Длительность песни в секундах.
        /// </summary>
        [JsonProperty("_songApproximativeDuration")]
        public int SongApproximativeDuration;

        /// <summary>
        /// Данные редактора: кто и чем правил карту. Игра не читает,
        /// но в картах RagnaCustoms/Edda блок есть — оставляем для совместимости с инструментами.
        /// </summary>
        [JsonProperty("_customData")]
        public InfoCustomData CustomData = new InfoCustomData();

        [JsonProperty("_difficultyBeatmapSets")]
        public List<DifficultyBeatmapSet> DifficultyBeatmapSets = new List<DifficultyBeatmapSet>();

        /// <summary>
        /// Добавляет карту сложности в стандартный набор (Standard).
        /// </summary>
        public void AddDifficulty(string difficultyName, int difficultyRank, string beatmapFilename,
            float noteJumpSpeed = 15f, float noteJumpOffset = 0f)
        {
            // Ищем существующий набор "Standard" или создаём новый
            DifficultyBeatmapSet standardSet = null;
            foreach (var set in DifficultyBeatmapSets)
            {
                if (set.BeatmapCharacteristicName == "Standard")
                {
                    standardSet = set;
                    break;
                }
            }

            if (standardSet == null)
            {
                standardSet = new DifficultyBeatmapSet
                {
                    BeatmapCharacteristicName = "Standard",
                    DifficultyBeatmaps = new List<DifficultyBeatmap>()
                };
                DifficultyBeatmapSets.Add(standardSet);
            }

            standardSet.DifficultyBeatmaps.Add(new DifficultyBeatmap
            {
                Difficulty = difficultyName,
                DifficultyRank = difficultyRank,
                BeatmapFilename = beatmapFilename,
                NoteJumpMovementSpeed = noteJumpSpeed,
                NoteJumpStartBeatOffset = noteJumpOffset
            });
        }
    }

    [Serializable]
    public class DifficultyBeatmapSet
    {
        [JsonProperty("_beatmapCharacteristicName")]
        public string BeatmapCharacteristicName = "Standard";

        [JsonProperty("_difficultyBeatmaps")]
        public List<DifficultyBeatmap> DifficultyBeatmaps = new List<DifficultyBeatmap>();
    }

    [Serializable]
    public class DifficultyBeatmap
    {
        /// <summary>
        /// Имя сложности. Ragnarock/Edda знают три: Easy, Normal, Hard.
        /// </summary>
        [JsonProperty("_difficulty")]
        public string Difficulty = "Normal";

        /// <summary>
        /// Числовое отображение сложности (черепа) в меню: 1-10.
        /// </summary>
        [JsonProperty("_difficultyRank")]
        public int DifficultyRank = 3;

        [JsonProperty("_beatmapFilename")]
        public string BeatmapFilename = "Normal.dat";

        /// <summary>
        /// Скорость подлёта рун (Note Speed в игре). 15 — средняя по документации Edda,
        /// максимум, который разрешает игра, — 30.
        /// </summary>
        [JsonProperty("_noteJumpMovementSpeed")]
        public float NoteJumpMovementSpeed = 15f;

        [JsonProperty("_noteJumpStartBeatOffset")]
        public float NoteJumpStartBeatOffset = 0f;
    }

    /// <summary>
    /// _customData info.dat: авторы-контрибьюторы и редактор, которым карта сделана.
    /// </summary>
    [Serializable]
    public class InfoCustomData
    {
        [JsonProperty("_contributors")]
        public List<string> Contributors = new List<string>();

        [JsonProperty("_editors")]
        public EditorsInfo Editors = new EditorsInfo();
    }

    [Serializable]
    public class EditorsInfo
    {
        /// <summary>Ключ — имя редактора, ровно как в картах Edda/MMA2.</summary>
        [JsonProperty("RagnaSongMaker")]
        public EditorVersion RagnaSongMaker = new EditorVersion();

        [JsonProperty("_lastEditedBy")]
        public string LastEditedBy = "RagnaSongMaker";
    }

    [Serializable]
    public class EditorVersion
    {
        [JsonProperty("version")]
        public string Version = "1.0";
    }
}
