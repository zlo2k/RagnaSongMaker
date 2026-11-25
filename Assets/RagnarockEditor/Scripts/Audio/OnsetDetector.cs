using System;
using System.Collections.Generic;
using UnityEngine;

namespace RagnarockEditor.Audio
{
    /// <summary>
    /// Детектор onset'ов (моментов атаки) в аудиосигнале.
    /// Использует Spectral Flux для нахождения резких изменений энергии.
    /// Необходим для работы BPMDetector.
    /// </summary>
    public static class OnsetDetector
    {
        /// <summary>
        /// Находит onset'ы в аудиоклипе методом Spectral Flux.
        /// PCM читается здесь (AudioClip.GetData допустим только в главном потоке),
        /// сам расчёт вынесен в перегрузку с float[] — её можно звать из фонового потока.
        /// </summary>
        /// <param name="clip">Аудиоклип для анализа</param>
        /// <param name="fftSize">Размер окна FFT (степень двойки)</param>
        /// <param name="hopSize">Шаг окна в семплах</param>
        /// <param name="sensitivity">Чувствительность (0-1, по умолчанию 0.5)</param>
        /// <returns>Список моментов времени (в секундах), где обнаружены onset'ы</returns>
        public static List<float> DetectOnsets(AudioClip clip, int fftSize = 1024, int hopSize = 512, float sensitivity = 0.5f)
        {
            if (clip == null || clip.samples == 0)
            {
                Debug.LogWarning("[OnsetDetector] AudioClip пуст или не загружен.");
                return new List<float>();
            }

            // Получаем все семплы (берём только первый канал для моно)
            float[] samples = new float[clip.samples];
            if (!clip.GetData(samples, 0))
            {
                Debug.LogWarning("[OnsetDetector] Не удалось прочитать PCM из AudioClip.");
                return new List<float>();
            }

            return DetectOnsets(samples, clip.frequency, fftSize, hopSize, sensitivity);
        }

        /// <summary>
        /// Находит onset'ы в готовом PCM-буфере (один канал).
        /// Не обращается к Unity API, поэтому безопасна для вызова из фонового потока.
        /// </summary>
        /// <param name="samples">PCM-семплы (моно), полученные через AudioClip.GetData</param>
        /// <param name="sampleRate">Частота дискретизации (AudioClip.frequency)</param>
        /// <param name="fftSize">Размер окна FFT (степень двойки)</param>
        /// <param name="hopSize">Шаг окна в семплах</param>
        /// <param name="sensitivity">Чувствительность (0-1, по умолчанию 0.5)</param>
        /// <returns>Список моментов времени (в секундах), где обнаружены onset'ы</returns>
        public static List<float> DetectOnsets(float[] samples, int sampleRate, int fftSize = 1024, int hopSize = 512, float sensitivity = 0.5f)
        {
            List<float> onsets = new List<float>();

            if (samples == null || samples.Length == 0 || sampleRate <= 0)
            {
                Debug.LogWarning("[OnsetDetector] Нет PCM или некорректная частота дискретизации.");
                return onsets;
            }

            if (fftSize <= 0 || hopSize <= 0 || samples.Length < fftSize)
            {
                Debug.LogWarning($"[OnsetDetector] Фрагмент слишком короткий: {samples.Length} семплов, окно {fftSize}.");
                return onsets;
            }

            int numFrames = (samples.Length - fftSize) / hopSize + 1;

            float[] prevSpectrum = new float[fftSize / 2];
            float[] currSpectrum = new float[fftSize / 2];
            float[] window = CreateHannWindow(fftSize);

            // Рабочие буферы переиспользуются: иначе на каждый кадр приходится 3 аллокации
            float[] frameData = new float[fftSize];
            float[] fftRe = new float[fftSize];
            float[] fftIm = new float[fftSize];

            float maxFlux = 0f;
            float[] fluxes = new float[numFrames];

            // Вычисляем Spectral Flux для каждого кадра
            for (int frame = 0; frame < numFrames; frame++)
            {
                int startSample = frame * hopSize;

                Array.Clear(frameData, 0, fftSize);
                int available = Math.Min(fftSize, samples.Length - startSample);
                for (int i = 0; i < available; i++)
                    frameData[i] = samples[startSample + i] * window[i];

                PerformFFT(frameData, currSpectrum, fftRe, fftIm);

                // Spectral Flux: сумма положительных разностей
                float flux = 0f;
                for (int bin = 0; bin < currSpectrum.Length; bin++)
                {
                    float diff = currSpectrum[bin] - prevSpectrum[bin];
                    if (diff > 0)
                        flux += diff;
                }

                fluxes[frame] = flux;
                if (flux > maxFlux) maxFlux = flux;

                // Копируем текущий спектр в предыдущий
                Array.Copy(currSpectrum, prevSpectrum, currSpectrum.Length);
            }

            if (maxFlux <= 0f)
            {
                Debug.LogWarning("[OnsetDetector] Не обнаружено значимых изменений в сигнале.");
                return onsets;
            }

            // Порог для детекции onset'ов
            float threshold = maxFlux * (0.3f + sensitivity * 0.4f); // 0.3 ... 0.7 от максимума

            // Ищем пики в Spectral Flux
            for (int frame = 1; frame < numFrames - 1; frame++)
            {
                // Локальный максимум выше порога
                if (fluxes[frame] > threshold &&
                    fluxes[frame] > fluxes[frame - 1] &&
                    fluxes[frame] >= fluxes[frame + 1])
                {
                    float time = (float)(frame * hopSize) / sampleRate;
                    onsets.Add(time);
                }
            }

            Debug.Log($"[OnsetDetector] Найдено onset'ов: {onsets.Count}");
            return onsets;
        }

        /// <summary>
        /// Создаёт окно Ханна.
        /// </summary>
        private static float[] CreateHannWindow(int size)
        {
            float[] window = new float[size];
            for (int i = 0; i < size; i++)
            {
                window[i] = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (size - 1)));
            }
            return window;
        }

        /// <summary>
        /// Быстрое преобразование Фурье (radix-2, на месте).
        /// Магнитуды положительных частот пишутся в spectrum (нормировка та же, что у DFT: /n).
        /// Для окон не-степеней двойки используется медленный, но корректный DFT.
        /// </summary>
        private static void PerformFFT(float[] data, float[] spectrum, float[] re, float[] im)
        {
            int n = data.Length;
            int m = spectrum.Length;

            if (n <= 0 || m <= 0)
                return;

            if (!IsPowerOfTwo(n) || re == null || im == null || re.Length < n || im.Length < n)
            {
                PerformDft(data, spectrum);
                return;
            }

            for (int i = 0; i < n; i++)
            {
                re[i] = data[i];
                im[i] = 0f;
            }

            // Перестановка битов
            int j = 0;
            for (int i = 1; i < n; i++)
            {
                int bit = n >> 1;
                while ((j & bit) != 0)
                {
                    j ^= bit;
                    bit >>= 1;
                }
                j ^= bit;

                if (i < j)
                {
                    float tmpRe = re[i];
                    re[i] = re[j];
                    re[j] = tmpRe;

                    float tmpIm = im[i];
                    im[i] = im[j];
                    im[j] = tmpIm;
                }
            }

            // «Бабочки»
            for (int len = 2; len <= n; len <<= 1)
            {
                float angle = -2f * Mathf.PI / len;
                float wRe = Mathf.Cos(angle);
                float wIm = Mathf.Sin(angle);
                int half = len >> 1;

                for (int start = 0; start < n; start += len)
                {
                    float curRe = 1f;
                    float curIm = 0f;

                    for (int k = 0; k < half; k++)
                    {
                        int a = start + k;
                        int b = a + half;

                        float tRe = re[b] * curRe - im[b] * curIm;
                        float tIm = re[b] * curIm + im[b] * curRe;

                        re[b] = re[a] - tRe;
                        im[b] = im[a] - tIm;
                        re[a] += tRe;
                        im[a] += tIm;

                        float nextRe = curRe * wRe - curIm * wIm;
                        curIm = curRe * wIm + curIm * wRe;
                        curRe = nextRe;
                    }
                }
            }

            for (int k = 0; k < m && k < n; k++)
                spectrum[k] = Mathf.Sqrt(re[k] * re[k] + im[k] * im[k]) / n;
        }

        /// <summary>
        /// Медленный DFT — резервный путь для окна не-степени двойки (прежняя реализация).
        /// </summary>
        private static void PerformDft(float[] data, float[] spectrum)
        {
            int n = data.Length;
            int m = spectrum.Length;

            for (int k = 0; k < m; k++)
            {
                float real = 0f;
                float imag = 0f;

                for (int t = 0; t < n; t++)
                {
                    float angle = -2f * Mathf.PI * k * t / n;
                    real += data[t] * Mathf.Cos(angle);
                    imag += data[t] * Mathf.Sin(angle);
                }

                spectrum[k] = Mathf.Sqrt(real * real + imag * imag) / n;
            }
        }

        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }
    }
}
