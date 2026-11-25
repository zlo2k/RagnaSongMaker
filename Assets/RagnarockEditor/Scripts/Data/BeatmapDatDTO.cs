using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RagnarockEditor.Data
{
    /// <summary>
    /// DTO для файла Beatmap.dat — содержит массив нот (_notes).
    /// Ragnarock игнорирует _obstacles и _events, парсит только _notes.
    /// </summary>
    [Serializable]
    public class BeatmapDatDTO
    {
        [JsonProperty("_version")]
        public string Version = "2.0.0";

        [JsonProperty("_customData")]
        public CustomData CustomDataField = new CustomData();

        /// <summary>
        /// Игнорируется движком Ragnarock. Оставляем пустым при сериализации.
        /// </summary>
        [JsonProperty("_events")]
        [NonSerialized]
        public List<object> Events = new List<object>();

        /// <summary>
        /// Массив нот (рун) — единственное, что парсит Ragnarock.
        /// </summary>
        [JsonProperty("_notes")]
        public List<Note> Notes = new List<Note>();

        /// <summary>
        /// Игнорируется движком Ragnarock. Оставляем пустым при сериализации.
        /// </summary>
        [JsonProperty("_obstacles")]
        [NonSerialized]
        public List<object> Obstacles = new List<object>();

        /// <summary>
        /// Сортирует ноты по времени (_time).
        /// </summary>
        public void SortNotes()
        {
            Notes.Sort((a, b) => a.Time.CompareTo(b.Time));
        }
    }

    /// <summary>
    /// Одна руна (нота) — падающий на барабан элемент.
    /// </summary>
    [Serializable]
    public class Note
    {
        /// <summary>
        /// Момент появления руны, измеряемый В БИТАХ (долях) от начала песни.
        /// Формула перевода в секунды: time_sec = _time * 60 / BPM.
        /// </summary>
        [JsonProperty("_time")]
        public float Time;

        /// <summary>
        /// Индекс дорожки (барабана): 0-3 (слева направо).
        /// 0 — крайний левый, 1 — средний левый, 2 — средний правый, 3 — крайний правый.
        /// ВАЛИДАЦИЯ: строго [0, 1, 2, 3].
        /// </summary>
        [JsonProperty("_lineIndex")]
        public int LineIndex;

        /// <summary>
        /// Высота ноты. В Ragnarock ВСЕГДА 0.
        /// </summary>
        [JsonProperty("_lineLayer")]
        public int LineLayer = 0;

        /// <summary>
        /// Тип ноты. 0 или 1 (цвет молота не разделяется).
        /// </summary>
        [JsonProperty("_type")]
        public int Type = 0;

        /// <summary>
        /// Направление удара. В Ragnarock ВСЕГДА 1 (удар вниз).
        /// </summary>
        [JsonProperty("_cutDirection")]
        public int CutDirection = 1;

        /// <summary>
        /// Создаёт ноту для Ragnarock с валидными значениями по умолчанию.
        /// </summary>
        /// <param name="time">Время в БИТАХ</param>
        /// <param name="lineIndex">Индекс барабана 0-3</param>
        public Note(float time, int lineIndex)
        {
            Time = time;
            LineIndex = Math.Clamp(lineIndex, 0, 3);
            LineLayer = 0;
            Type = 0;
            CutDirection = 1;
        }

        /// <summary>
        /// Преобразует время ноты из битов в секунды.
        /// </summary>
        public float GetTimeInSeconds(float bpm)
        {
            return Time * 60f / bpm;
        }

        public override string ToString()
        {
            return $"Note(time={Time:F2} beats, drum={LineIndex})";
        }
    }

    [Serializable]
    public class CustomData
    {
        [JsonProperty("_time")]
        public float Time = 0f;

        [JsonProperty("_BPMChanges")]
        [NonSerialized]
        public List<object> BPMChanges = new List<object>();
    }
}
