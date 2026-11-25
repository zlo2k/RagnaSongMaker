using System.Collections.Generic;
using RagnarockEditor.Data;
using UnityEngine;

namespace RagnarockEditor.Serialization
{
    /// <summary>
    /// Валидатор уровня перед сохранением.
    /// Проверяет корректность данных info.dat и Beatmap.dat.
    /// </summary>
    public static class LevelValidator
    {
        /// <summary>
        /// Результат валидации.
        /// </summary>
        public class ValidationResult
        {
            public List<string> Errors = new List<string>();
            public List<string> Warnings = new List<string>();

            /// <summary>
            /// Ошибок нет — значит данные валидны.
            /// Раньше это было поле, которое нигде не выставлялось в true,
            /// поэтому валидация всегда считалась проваленной.
            /// </summary>
            public bool IsValid => Errors.Count == 0;

            public void AddError(string message)
            {
                Errors.Add(message);
            }

            public void AddWarning(string message)
            {
                Warnings.Add(message);
            }

            public override string ToString()
            {
                if (IsValid && Warnings.Count == 0)
                    return "Валидация пройдена успешно.";
                
                string result = $"Валидация: {(IsValid ? "OK" : "ОШИБКИ")}\n";
                foreach (var e in Errors) result += $"  ❌ {e}\n";
                foreach (var w in Warnings) result += $"  ⚠️ {w}\n";
                return result;
            }
        }

        /// <summary>
        /// Проверяет info.dat.
        /// </summary>
        public static ValidationResult ValidateInfoDat(InfoDatDTO info)
        {
            var result = new ValidationResult();

            if (info == null)
            {
                result.AddError("InfoDatDTO is null");
                return result;
            }

            if (string.IsNullOrWhiteSpace(info.SongName))
                result.AddError("SongName не может быть пустым.");

            if (info.BeatsPerMinute <= 0)
                result.AddError("BeatsPerMinute должен быть > 0.");
            else if (info.BeatsPerMinute < 40)
                result.AddWarning($"BeatsPerMinute ({info.BeatsPerMinute}) очень низкий. Обычно BPM 60-220.");
            else if (info.BeatsPerMinute > 300)
                result.AddWarning($"BeatsPerMinute ({info.BeatsPerMinute}) очень высокий. Обычно BPM 60-220.");

            if (info.SongApproximativeDuration <= 0)
                result.AddError("SongApproximativeDuration должен быть > 0.");

            if (info.DifficultyBeatmapSets.Count == 0)
                result.AddError("Нет ни одного набора сложности (DifficultyBeatmapSets).");

            foreach (var set in info.DifficultyBeatmapSets)
            {
                if (set.DifficultyBeatmaps.Count == 0)
                    result.AddError($"Набор '{set.BeatmapCharacteristicName}' не содержит карт сложности.");

                foreach (var diff in set.DifficultyBeatmaps)
                {
                    if (diff.DifficultyRank < 1 || diff.DifficultyRank > 10)
                        result.AddError($"DifficultyRank = {diff.DifficultyRank} для '{diff.Difficulty}'. Допустимый диапазон: 1-10.");

                    if (string.IsNullOrWhiteSpace(diff.BeatmapFilename))
                        result.AddError($"BeatmapFilename пуст для сложности '{diff.Difficulty}'.");
                }
            }

            return result;
        }

        /// <summary>
        /// Проверяет Beatmap.dat (массив нот).
        /// </summary>
        public static ValidationResult ValidateBeatmapDat(BeatmapDatDTO beatmap, float bpm, float songDurationSeconds)
        {
            var result = new ValidationResult();

            if (beatmap == null)
            {
                result.AddError("BeatmapDatDTO is null");
                return result;
            }

            float maxBeats = songDurationSeconds * bpm / 60f;

            if (beatmap.Notes.Count == 0)
            {
                result.AddError("Массив _notes пуст. Уровень должен содержать хотя бы одну ноту.");
            }

            // Проверяем каждую ноту
            var seenNotes = new HashSet<(float time, int lineIndex)>();
            
            for (int i = 0; i < beatmap.Notes.Count; i++)
            {
                var note = beatmap.Notes[i];

                // _lineIndex: 0-3
                if (note.LineIndex < 0 || note.LineIndex > 3)
                    result.AddError($"Нота #{i}: _lineIndex={note.LineIndex} — должен быть 0-3.");

                // _lineLayer: всегда 0
                if (note.LineLayer != 0)
                    result.AddError($"Нота #{i}: _lineLayer={note.LineLayer} — должен быть 0 (Ragnarock).");

                // _type: 0 или 1
                if (note.Type != 0 && note.Type != 1)
                    result.AddError($"Нота #{i}: _type={note.Type} — должен быть 0 или 1.");

                // _cutDirection: всегда 1
                if (note.CutDirection != 1)
                    result.AddWarning($"Нота #{i}: _cutDirection={note.CutDirection} — обычно 1 для Ragnarock.");

                // _time: > 0 и в пределах песни
                if (note.Time < 0)
                    result.AddError($"Нота #{i}: _time={note.Time} — не может быть отрицательным.");
                else if (note.Time > maxBeats + 4) // Небольшой допуск
                    result.AddWarning($"Нота #{i}: _time={note.Time:F2} бит выходит за пределы песни ({maxBeats:F2} бит).");

                // Проверка дубликатов (одно время + один барабан)
                var key = (note.Time, note.LineIndex);
                if (!seenNotes.Add(key))
                    result.AddWarning($"Нота #{i}: дубликат — время={note.Time:F2}, барабан={note.LineIndex}. " +
                                     "Допустимы только одновременные удары по разным барабанам.");
            }

            return result;
        }

        /// <summary>
        /// Полная валидация уровня.
        /// </summary>
        public static ValidationResult ValidateFull(InfoDatDTO info, BeatmapDatDTO beatmap)
        {
            var result = new ValidationResult();

            if (info == null)
            {
                result.AddError("InfoDatDTO is null");
                return result;
            }

            if (beatmap == null)
            {
                result.AddError("BeatmapDatDTO is null");
                return result;
            }

            // Валидируем info
            var infoResult = ValidateInfoDat(info);
            result.Errors.AddRange(infoResult.Errors);
            result.Warnings.AddRange(infoResult.Warnings);

            // Валидируем beatmap
            var beatmapResult = ValidateBeatmapDat(beatmap, info.BeatsPerMinute, info.SongApproximativeDuration);
            result.Errors.AddRange(beatmapResult.Errors);
            result.Warnings.AddRange(beatmapResult.Warnings);

            return result;
        }
    }
}
