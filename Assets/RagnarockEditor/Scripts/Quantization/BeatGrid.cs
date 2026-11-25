using System.Collections.Generic;
using UnityEngine;

namespace RagnarockEditor.Quantization
{
    /// <summary>
    /// Строит сетку битов на основе BPM для квантования.
    /// </summary>
    public class BeatGrid
    {
        /// <summary>BPM сетки.</summary>
        public float BPM { get; private set; }

        /// <summary>Интервал между битами в секундах.</summary>
        public float BeatInterval => 60f / BPM;

        /// <summary>Делитель доли (1=целые, 2=половинные, 4=четверти, 8=восьмые).</summary>
        public int Division { get; private set; }

        /// <summary>Интервал между делениями в секундах.</summary>
        public float DivisionInterval => BeatInterval / Division;

        /// <summary>Смещение первого бита в секундах.</summary>
        public float FirstBeatOffset { get; set; }

        private float _durationSeconds;

        /// <summary>
        /// Создаёт сетку битов.
        /// </summary>
        public BeatGrid(float bpm, float durationSeconds, int division = 4, float firstBeatOffset = 0f)
        {
            BPM = bpm;
            Division = division;
            FirstBeatOffset = firstBeatOffset;
            _durationSeconds = durationSeconds;
        }

        /// <summary>
        /// Находит ближайшее деление сетки к заданному времени.
        /// </summary>
        /// <returns>Время ближайшего деления в секундах</returns>
        public float SnapToGrid(float timeSeconds)
        {
            float adjusted = timeSeconds - FirstBeatOffset;
            if (adjusted < 0f) adjusted = 0f;

            float nearestDivision = Mathf.Round(adjusted / DivisionInterval) * DivisionInterval;
            return nearestDivision + FirstBeatOffset;
        }

        /// <summary>
        /// Возвращает индекс ближайшего деления.
        /// </summary>
        public int GetNearestDivisionIndex(float timeSeconds)
        {
            float adjusted = timeSeconds - FirstBeatOffset;
            if (adjusted < 0f) adjusted = 0f;
            return Mathf.RoundToInt(adjusted / DivisionInterval);
        }

        /// <summary>
        /// Конвертирует время в секундах в биты.
        /// </summary>
        public float SecondsToBeats(float timeSeconds)
        {
            return (timeSeconds - FirstBeatOffset) * BPM / 60f;
        }

        /// <summary>
        /// Конвертирует биты в секунды.
        /// </summary>
        public float BeatsToSeconds(float beats)
        {
            return beats * 60f / BPM + FirstBeatOffset;
        }

        /// <summary>
        /// Проверяет, находится ли время в пределах допустимого отклонения от сетки.
        /// </summary>
        public bool IsWithinThreshold(float timeSeconds, float thresholdSeconds)
        {
            float snapped = SnapToGrid(timeSeconds);
            return Mathf.Abs(timeSeconds - snapped) <= thresholdSeconds;
        }

        /// <summary>
        /// Возвращает все деления сетки в заданном диапазоне.
        /// </summary>
        public List<float> GetGridTimes(float startTime, float endTime)
        {
            List<float> times = new List<float>();
            float t = FirstBeatOffset;
            int index = 0;

            while (t <= endTime)
            {
                if (t >= startTime)
                    times.Add(t);

                index++;
                t = FirstBeatOffset + index * DivisionInterval;
            }

            return times;
        }
    }
}
