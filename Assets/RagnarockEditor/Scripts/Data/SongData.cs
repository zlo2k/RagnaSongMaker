using System;
using System.Collections.Generic;
using UnityEngine;

namespace RagnarockEditor.Data
{
    /// <summary>
    /// Данные песни во время выполнения (runtime).
    /// Передаётся между сценами через синглтон RecordSession.
    /// </summary>
    [Serializable]
    public class SongData
    {
        /// <summary>Путь к аудиофайлу на устройстве.</summary>
        public string FilePath;

        /// <summary>Название песни (из метаданных или имени файла).</summary>
        public string SongName;

        /// <summary>Имя исполнителя.</summary>
        public string AuthorName;

        /// <summary>Beats Per Minute.</summary>
        public float BPM;

        /// <summary>Длительность в секундах.</summary>
        public float DurationSeconds;

        /// <summary>
        /// Вычисляет длительность песни в битах.
        /// </summary>
        public float DurationBeats => DurationSeconds * BPM / 60f;

        /// <summary>
        /// Конвертирует секунды в биты.
        /// </summary>
        public float SecondsToBeats(float seconds)
        {
            return seconds * BPM / 60f;
        }

        /// <summary>
        /// Конвертирует биты в секунды.
        /// </summary>
        public float BeatsToSeconds(float beats)
        {
            return beats * 60f / BPM;
        }

        public override string ToString()
        {
            return $"Song: {SongName} by {AuthorName}, BPM={BPM}, Duration={DurationSeconds:F1}s";
        }
    }
}
