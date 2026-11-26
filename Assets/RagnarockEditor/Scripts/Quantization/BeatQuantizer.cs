using System.Collections.Generic;
using UnityEngine;
using RagnarockEditor.Data;

namespace RagnarockEditor.Quantization
{
    /// <summary>
    /// Алгоритм Snap-to-Beat: преобразует сырые удары (RawHit, секунды)
    /// в квантованные ноты (Note, биты).
    /// </summary>
    public class BeatQuantizer
    {
        private QuantizeSettings _settings;
        private BeatGrid _grid;

        /// <summary>
        /// Создаёт квантователь.
        /// </summary>
        public BeatQuantizer(QuantizeSettings settings = null)
        {
            _settings = settings ?? ScriptableObject.CreateInstance<QuantizeSettings>();
        }

        /// <summary>
        /// Квантует сырые удары в ноты.
        /// </summary>
        /// <param name="rawHits">Сырые удары (в секундах)</param>
        /// <param name="bpm">BPM песни</param>
        /// <param name="durationSeconds">Длительность песни</param>
        /// <param name="firstBeatOffset">Смещение первого бита</param>
        /// <returns>Список квантованных нот (в битах!)</returns>
        public List<Note> Quantize(List<RawHit> rawHits, float bpm, float durationSeconds, float firstBeatOffset = 0f)
        {
            if (rawHits == null || rawHits.Count == 0)
            {
                Debug.LogWarning("[BeatQuantizer] Нет сырых ударов для квантования.");
                return new List<Note>();
            }

            _grid = new BeatGrid(bpm, durationSeconds, _settings.SnapDivision, firstBeatOffset);

            List<Note> notes = new List<Note>();

            foreach (var hit in rawHits)
            {
                // Шаг 1: Привязка к сетке (можно выключить каменным чекбоксом)
                float snappedTime = hit.TimeSeconds;

                if (QuantizeRuntime.SnapEnabled)
                {
                    float distance = Mathf.Abs(hit.TimeSeconds - _grid.SnapToGrid(hit.TimeSeconds));

                    if (distance <= _settings.SnapThreshold)
                        snappedTime = _grid.SnapToGrid(hit.TimeSeconds);
                }

                // Шаг 2: Конвертация в биты.
                // _time в карте — АБСОЛЮТНЫЕ биты от начала песни (time_sec = _time*60/BPM),
                // поэтому фазу сетки (firstBeatOffset) здесь НЕ вычитаем: она уже учтена
                // в snappedTime. Иначе трек с интро уехал бы на фазу.
                float timeInBeats = snappedTime * _grid.BPM / 60f;

                // Шаг 3: Создаём ноту
                var note = new Note(timeInBeats, hit.DrumIndex);
                notes.Add(note);
            }

            // Шаг 4: Сортировка по времени
            notes.Sort((a, b) =>
            {
                int cmp = a.Time.CompareTo(b.Time);
                if (cmp == 0) cmp = a.LineIndex.CompareTo(b.LineIndex);
                return cmp;
            });

            // Шаг 5: Удаление слишком близких нот на одном барабане
            notes = RemoveCloseNotes(notes, _settings.MinNoteSpacing * bpm / 60f);

            // Шаг 6: Удаление изолированных нот (опционально)
            if (_settings.RemoveIsolatedNotes)
            {
                notes = RemoveIsolatedNotes(notes, _settings.IsolationWindow * bpm / 60f);
            }

            Debug.Log($"[BeatQuantizer] Квантовано: {rawHits.Count} ударов → {notes.Count} нот " +
                      $"(BPM={bpm}, threshold={_settings.SnapThreshold:F3}с, division=1/{_settings.SnapDivision})");

            return notes;
        }

        /// <summary>
        /// Удаляет ноты, которые слишком близко на одном барабане.
        /// </summary>
        private List<Note> RemoveCloseNotes(List<Note> notes, float minBeatSpacing)
        {
            if (notes.Count < 2) return notes;

            List<Note> filtered = new List<Note> { notes[0] };

            for (int i = 1; i < notes.Count; i++)
            {
                var prev = filtered[filtered.Count - 1];
                var curr = notes[i];

                // Если та же позиция и время слишком близко — пропускаем
                if (curr.LineIndex == prev.LineIndex &&
                    Mathf.Abs(curr.Time - prev.Time) < minBeatSpacing)
                {
                    continue;
                }

                filtered.Add(curr);
            }

            int removed = notes.Count - filtered.Count;
            if (removed > 0)
            {
                Debug.Log($"[BeatQuantizer] Удалено {removed} слишком близких нот.");
            }

            return filtered;
        }

        /// <summary>
        /// Удаляет ноты, у которых нет соседей в заданном окне.
        /// </summary>
        private List<Note> RemoveIsolatedNotes(List<Note> notes, float windowBeats)
        {
            List<Note> filtered = new List<Note>();

            for (int i = 0; i < notes.Count; i++)
            {
                bool hasNeighbor = false;

                // Проверяем соседей слева
                for (int j = i - 1; j >= 0; j--)
                {
                    if (Mathf.Abs(notes[i].Time - notes[j].Time) <= windowBeats)
                    {
                        hasNeighbor = true;
                        break;
                    }
                    if (notes[i].Time - notes[j].Time > windowBeats)
                        break;
                }

                // Проверяем соседей справа
                if (!hasNeighbor)
                {
                    for (int j = i + 1; j < notes.Count; j++)
                    {
                        if (Mathf.Abs(notes[j].Time - notes[i].Time) <= windowBeats)
                        {
                            hasNeighbor = true;
                            break;
                        }
                        if (notes[j].Time - notes[i].Time > windowBeats)
                            break;
                    }
                }

                if (hasNeighbor)
                    filtered.Add(notes[i]);
            }

            int removed = notes.Count - filtered.Count;
            if (removed > 0)
            {
                Debug.Log($"[BeatQuantizer] Удалено {removed} изолированных нот.");
            }

            return filtered;
        }
    }
}
