using System;
using UnityEngine;

namespace RagnarockEditor.Data
{
    /// <summary>
    /// Сырой удар пользователя — момент времени и индекс барабана.
    /// Записывается в реальном времени во время игровой сессии.
    /// </summary>
    [Serializable]
    public struct RawHit
    {
        /// <summary>Момент удара в секундах от начала песни (AudioSource.time).</summary>
        public float TimeSeconds;

        /// <summary>Индекс барабана: 0-3 (слева направо).</summary>
        public int DrumIndex;

        public RawHit(float timeSeconds, int drumIndex)
        {
            TimeSeconds = timeSeconds;
            DrumIndex = Mathf.Clamp(drumIndex, 0, 3);
        }

        public override string ToString()
        {
            return $"RawHit(t={TimeSeconds:F3}s, drum={DrumIndex})";
        }
    }
}
