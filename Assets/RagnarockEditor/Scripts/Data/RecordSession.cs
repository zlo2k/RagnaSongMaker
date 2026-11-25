using System;
using System.Collections.Generic;
using UnityEngine;

namespace RagnarockEditor.Data
{
    /// <summary>
    /// Сессия записи уровня. Синглтон, передаётся между сценами.
    /// Хранит песню, сырые удары и квантованные ноты.
    /// </summary>
    public class RecordSession : MonoBehaviour
    {
        private static RecordSession _instance;

        public static RecordSession Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("RecordSession");
                    _instance = go.AddComponent<RecordSession>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        /// <summary>Данные выбранной песни.</summary>
        public SongData Song;

        /// <summary>Сырые удары пользователя (в секундах).</summary>
        public List<RawHit> RawHits = new List<RawHit>();

        /// <summary>Квантованные ноты после обработки (в битах).</summary>
        public List<Note> QuantizedNotes = new List<Note>();

        /// <summary>Уровень сложности по умолчанию.</summary>
        public int DifficultyRank = 3;

        /// <summary>Название сложности.</summary>
        public string DifficultyName = "Normal";

        /// <summary>Имя автора уровня.</summary>
        public string LevelAuthorName = "VR Editor User";

        /// <summary>Имя окружения.</summary>
        public string EnvironmentName = "Midgard";

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Добавляет сырой удар.
        /// </summary>
        public void AddRawHit(float timeSeconds, int drumIndex)
        {
            RawHits.Add(new RawHit(timeSeconds, drumIndex));
        }

        /// <summary>
        /// Удаляет все удары после указанного времени (используется при перемотке назад).
        /// </summary>
        public void RemoveHitsAfter(float timeSeconds)
        {
            RawHits.RemoveAll(h => h.TimeSeconds > timeSeconds);
        }

        /// <summary>
        /// Очищает все данные сессии.
        /// </summary>
        public void Clear()
        {
            RawHits.Clear();
            QuantizedNotes.Clear();
            Song = null;
        }

        /// <summary>
        /// Очищает только сырые удары (новый дубль записи, песня остаётся).
        /// Квантованные ноты тоже сбрасываются: они относятся к прошлому дублю, иначе
        /// предпросмотр показал бы старую карту после перезаписи.
        /// </summary>
        public void ClearHits()
        {
            RawHits.Clear();
            QuantizedNotes.Clear();
        }

        /// <summary>
        /// Возвращает количество ударов по каждому барабану.
        /// </summary>
        public int[] GetHitsPerDrum()
        {
            int[] counts = new int[4];
            foreach (var hit in RawHits)
            {
                if (hit.DrumIndex >= 0 && hit.DrumIndex < 4)
                    counts[hit.DrumIndex]++;
            }
            return counts;
        }

        /// <summary>
        /// Возвращает общее количество сырых ударов.
        /// </summary>
        public int TotalRawHits => RawHits.Count;

        /// <summary>
        /// Возвращает общее количество квантованных нот.
        /// </summary>
        public int TotalQuantizedNotes => QuantizedNotes.Count;
    }
}
