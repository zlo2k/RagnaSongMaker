using System;
using System.Collections.Generic;
using UnityEngine;

namespace RagnarockEditor.Audio
{
    /// <summary>
    /// Детектор BPM (Beats Per Minute).
    /// Алгоритм: Onset Detection → интервалы между onset'ами → Autocorrelation → BPM.
    /// </summary>
    public static class BPMDetector
    {
        /// <summary>
        /// Результат анализа BPM.
        /// </summary>
        [Serializable]
        public class BPMResult
        {
            /// <summary>Основной BPM.</summary>
            public float PrimaryBPM;

            /// <summary>Альтернативный BPM (половинный/двойной).</summary>
            public float AlternativeBPM;

            /// <summary>Уверенность в результате (0-1).</summary>
            public float Confidence;

            /// <summary>Список обнаруженных onset'ов (секунды).</summary>
            public List<float> Onsets;

            /// <summary>Смещение первого бита (секунды).</summary>
            public float FirstBeatOffset;

            public override string ToString()
            {
                return $"BPM={PrimaryBPM:F1} (conf={Confidence:P0}), alt={AlternativeBPM:F1}";
            }
        }

        /// <summary>
        /// Анализирует AudioClip и возвращает BPM.
        /// PCM читается здесь же (GetData допустим только в главном потоке),
        /// тяжёлый расчёт уходит в перегрузку с float[] — её можно звать из Task.
        /// </summary>
        /// <param name="clip">Аудиоклип</param>
        /// <param name="minBPM">Минимальный допустимый BPM</param>
        /// <param name="maxBPM">Максимальный допустимый BPM</param>
        /// <returns>Результат анализа BPM</returns>
        public static BPMResult Analyze(AudioClip clip, float minBPM = 60f, float maxBPM = 220f)
        {
            if (clip == null)
            {
                Debug.LogError("[BPMDetector] AudioClip is null");
                var empty = new BPMResult();
                empty.PrimaryBPM = 120f;
                empty.Confidence = 0f;
                return empty;
            }

            float[] samples = null;
            int sampleRate = clip.frequency;

            try
            {
                if (clip.samples > 0)
                {
                    samples = new float[clip.samples];
                    if (!clip.GetData(samples, 0))
                        samples = null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BPMDetector] Не удалось прочитать PCM: " + e.Message);
                samples = null;
            }

            if (samples == null || samples.Length == 0)
                return CreateFallbackResult("нет PCM для анализа", 0.1f);

            return Analyze(samples, sampleRate, minBPM, maxBPM);
        }

        /// <summary>
        /// Анализирует готовый PCM-буфер (один канал) и возвращает BPM.
        /// Не обращается к Unity API — можно вызывать из фонового потока.
        /// </summary>
        /// <param name="samples">PCM-семплы (моно), полученные через AudioClip.GetData</param>
        /// <param name="sampleRate">Частота дискретизации (AudioClip.frequency)</param>
        /// <param name="minBPM">Минимальный допустимый BPM</param>
        /// <param name="maxBPM">Максимальный допустимый BPM</param>
        /// <returns>Результат анализа BPM</returns>
        public static BPMResult Analyze(float[] samples, int sampleRate, float minBPM = 60f, float maxBPM = 220f)
        {
            if (samples == null || samples.Length == 0 || sampleRate <= 0)
                return CreateFallbackResult("некорректный PCM-буфер", 0.1f);

            var result = new BPMResult();

            // Шаг 1: Детекция onset'ов
            result.Onsets = OnsetDetector.DetectOnsets(samples, sampleRate);

            if (result.Onsets.Count < 10)
            {
                Debug.LogWarning($"[BPMDetector] Недостаточно onset'ов ({result.Onsets.Count}). BPM = 120 (по умолчанию)");
                result.PrimaryBPM = 120f;
                result.Confidence = 0.1f;
                return result;
            }

            // Шаг 2: Вычисляем интервалы между onset'ами
            List<float> intervals = new List<float>();
            for (int i = 1; i < result.Onsets.Count; i++)
            {
                float interval = result.Onsets[i] - result.Onsets[i - 1];
                // Игнорируем слишком короткие (< 50ms) и слишком длинные (> 2s) интервалы
                if (interval > 0.05f && interval < 2.0f)
                {
                    intervals.Add(interval);
                }
            }

            if (intervals.Count < 5)
            {
                Debug.LogWarning("[BPMDetector] Недостаточно валидных интервалов. BPM = 120 (по умолчанию)");
                result.PrimaryBPM = 120f;
                result.Confidence = 0.1f;
                return result;
            }

            // Шаг 3: Автокорреляция интервалов
            float bestBPM = 0f;
            float bestScore = 0f;

            // Перебираем возможные BPM с шагом 0.5
            for (float bpm = minBPM; bpm <= maxBPM; bpm += 0.5f)
            {
                float beatInterval = 60f / bpm;
                float score = ScoreBPM(intervals, beatInterval);
                
                if (score > bestScore)
                {
                    bestScore = score;
                    bestBPM = bpm;
                }
            }

            // Шаг 4: Проверяем половинный и двойной BPM
            float halfBPM = bestBPM / 2f;
            float doubleBPM = bestBPM * 2f;
            float halfScore = (halfBPM >= minBPM) ? ScoreBPM(intervals, 60f / halfBPM) : 0f;
            float doubleScore = (doubleBPM <= maxBPM) ? ScoreBPM(intervals, 60f / doubleBPM) : 0f;

            result.PrimaryBPM = bestBPM;
            result.AlternativeBPM = (halfScore > doubleScore) ? halfBPM : doubleBPM;
            result.Confidence = Mathf.Clamp01(bestScore);

            // Шаг 5: Находим смещение первого бита
            result.FirstBeatOffset = FindFirstBeat(result.Onsets, 60f / bestBPM);

            Debug.Log($"[BPMDetector] Результат: {result}");
            return result;
        }

        /// <summary>
        /// Резервный результат, когда анализ невозможен (без исключений для вызывающего).
        /// </summary>
        private static BPMResult CreateFallbackResult(string reason, float confidence)
        {
            Debug.LogWarning($"[BPMDetector] {reason}. BPM = 120 (по умолчанию)");

            var result = new BPMResult();
            result.PrimaryBPM = 120f;
            result.AlternativeBPM = 0f;
            result.Confidence = confidence;
            result.Onsets = new List<float>();
            result.FirstBeatOffset = 0f;
            return result;
        }

        /// <summary>
        /// Оценивает, насколько хорошо данный beat interval соответствует набору интервалов.
        /// </summary>
        private static float ScoreBPM(List<float> intervals, float beatInterval)
        {
            float score = 0f;
            
            foreach (float interval in intervals)
            {
                // Сколько битовых интервалов помещается в данный интервал
                float ratio = interval / beatInterval;
                float nearest = Mathf.Round(ratio);

                // Штраф за отклонение от целого числа битов
                float deviation = Mathf.Abs(ratio - nearest);
                
                // Близость к целому числу битов даёт высокий score
                if (deviation < 0.3f)
                {
                    score += 1f - deviation / 0.3f;
                }
            }

            return score / intervals.Count;
        }

        /// <summary>
        /// Находит смещение первого бита относительно начала песни.
        /// </summary>
        private static float FindFirstBeat(List<float> onsets, float beatInterval)
        {
            if (onsets.Count == 0) return 0f;

            // Ищем onset, ближайший к началу, который хорошо ложится на сетку
            float bestOffset = onsets[0] % beatInterval;
            int bestCount = 0;

            for (float testOffset = 0f; testOffset < beatInterval; testOffset += beatInterval / 20f)
            {
                int count = 0;
                foreach (float onset in onsets)
                {
                    float mod = (onset - testOffset + beatInterval) % beatInterval;
                    if (mod < beatInterval * 0.2f || mod > beatInterval * 0.8f)
                        count++;
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    bestOffset = testOffset;
                }
            }

            return bestOffset;
        }

        /// <summary>
        /// Вычисляет BPM методом Tap Tempo (по серии нажатий).
        /// </summary>
        /// <param name="tapTimes">Список моментов нажатий в секундах</param>
        /// <returns>BPM</returns>
        public static float CalculateTapTempo(List<float> tapTimes)
        {
            if (tapTimes.Count < 3)
                return 120f;

            List<float> intervals = new List<float>();
            for (int i = 1; i < tapTimes.Count; i++)
            {
                intervals.Add(tapTimes[i] - tapTimes[i - 1]);
            }

            // Медиана интервалов
            intervals.Sort();
            float medianInterval = intervals[intervals.Count / 2];

            return 60f / medianInterval;
        }
    }
}
