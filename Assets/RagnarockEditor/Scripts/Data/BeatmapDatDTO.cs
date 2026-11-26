using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RagnarockEditor.Data
{
    /// <summary>
    /// DTO для файла карты сложности (&lt;Difficulty&gt;.dat) — массив нот (_notes).
    /// Содержимое парсит Ragnarock; _events и _obstacles игра не читает, но парсер
    /// ждёт их как массивы (см. SESSION_NOTES, сверка с реальной картой divadance).
    /// </summary>
    [Serializable]
    public class BeatmapDatDTO
    {
        /// <summary>
        /// Версия формата карты. Edda и все карты с RagnaCustoms пишут "1";
        /// игру значение не проверяет, но держим его таким же, как в экосистеме.
        /// </summary>
        [JsonProperty("_version")]
        public string Version = "1";

        [JsonProperty("_customData")]
        public CustomData CustomDataField = new CustomData();

        /// <summary>
        /// Ragnarock события не использует, но поле должно быть массивом.
        /// ВАЖНО: не помечать [NonSerialized] — Newtonsoft такие поля не пишет.
        /// </summary>
        [JsonProperty("_events")]
        public List<object> Events = new List<object>();

        /// <summary>
        /// Массив нот (рун) — единственное, что реально читает Ragnarock.
        /// </summary>
        [JsonProperty("_notes")]
        public List<Note> Notes = new List<Note>();

        /// <summary>
        /// Ragnarock препятствия не использует, но поле должно быть массивом.
        /// </summary>
        [JsonProperty("_obstacles")]
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
        /// Высота ноты. Раскладка Ragnarock одномерная: руны едут к четырём барабанам
        /// в один ряд, и _lineLayer игра не использует (принимает 0..3).
        /// В картах Edda/MMA2 здесь всегда 1 — пишем 1, чтобы файл был неотличим от них.
        /// </summary>
        [JsonProperty("_lineLayer")]
        public int LineLayer = 1;

        /// <summary>
        /// Тип ноты. Ragnarock принимает только 0 (любое другое значение — невалидная карта).
        /// </summary>
        [JsonProperty("_type")]
        public int Type = 0;

        /// <summary>
        /// Направление удара. Ragnarock принимает только 1.
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
            LineLayer = 1;
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

    /// <summary>
    /// _customData карты сложности. Тайминговые изменения (BPMChanges) и закладки
    /// редактора Ragnarock не читает, но поля должны быть массивами.
    /// </summary>
    [Serializable]
    public class CustomData
    {
        [JsonProperty("_time")]
        public float Time = 0f;

        [JsonProperty("_BPMChanges")]
        public List<object> BPMChanges = new List<object>();

        [JsonProperty("_bookmarks")]
        public List<object> Bookmarks = new List<object>();
    }
}
