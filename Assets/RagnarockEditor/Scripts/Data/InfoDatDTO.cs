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
        [JsonProperty("_version")]
        public string Version = "2.0.0";

        [JsonProperty("_songName")]
        public string SongName = "";

        [JsonProperty("_songSubName")]
        public string SongSubName = "";

        [JsonProperty("_songAuthorName")]
        public string SongAuthorName = "";

        [JsonProperty("_levelAuthorName")]
        public string LevelAuthorName = "";

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

        [JsonProperty("_difficultyBeatmapSets")]
        public List<DifficultyBeatmapSet> DifficultyBeatmapSets = new List<DifficultyBeatmapSet>();

        /// <summary>
        /// Добавляет карту сложности в стандартный набор (Standard).
        /// </summary>
        public void AddDifficulty(string difficultyName, int difficultyRank, string beatmapFilename,
            float noteJumpSpeed = 14f, float noteJumpOffset = 0f)
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
        [JsonProperty("_difficulty")]
        public string Difficulty = "Normal";

        /// <summary>
        /// Числовое отображение сложности (черепа) в меню: 1-10.
        /// </summary>
        [JsonProperty("_difficultyRank")]
        public int DifficultyRank = 3;

        [JsonProperty("_beatmapFilename")]
        public string BeatmapFilename = "Normal.dat";

        [JsonProperty("_noteJumpMovementSpeed")]
        public float NoteJumpMovementSpeed = 14f;

        [JsonProperty("_noteJumpStartBeatOffset")]
        public float NoteJumpStartBeatOffset = 0f;
    }
}
