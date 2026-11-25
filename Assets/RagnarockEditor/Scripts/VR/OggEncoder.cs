using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Обёртка над нативной библиотекой libragnaogg (libvorbis):
    /// PCM s16 interleaved -> OGG Vorbis в памяти.
    ///
    /// Библиотека лежит в Assets/Plugins/Android/libs/arm64-v8a/libragnaogg.so
    /// и экспортирует две функции (см. NativeBuild/ogg-android/jni/ragna_ogg.c):
    ///   unsigned char* ragna_encode_ogg(const short*, int frames, int channels, int sample_rate, float quality, int* out_size);
    ///   void ragna_free_ogg(unsigned char*);
    ///
    /// В редакторе Windows (и на x86-устройствах) нативной библиотеки нет:
    /// IsAvailable == false, TryEncode возвращает false с предупреждением — вызывающий код
    /// обязан предусмотреть запасной путь (копирование исходного файла).
    /// </summary>
    public static class OggEncoder
    {
        private const string LibName = "ragnaogg";

        /// <summary>Сколько кадров читаем из AudioClip за один вызов GetData.</summary>
        private const int ReadChunkFrames = 16384;

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ragna_encode_ogg(short[] pcm, int frames, int channels, int sampleRate,
            float quality, out int size);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ragna_free_ogg(IntPtr buffer);

        // 0 — ещё не проверяли, 1 — доступна, -1 — недоступна.
        private static int _availability;

        /// <summary>
        /// Доступна ли нативная библиотека. Проверяется один раз за сессию и кэшируется
        /// (DllNotFoundException кэшируется тоже — повторных бросков не будет).
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (_availability == 0)
                    _availability = Probe() ? 1 : -1;

                return _availability == 1;
            }
        }

        /// <summary>
        /// Кодирует AudioClip в OGG Vorbis.
        /// Если клип не читаем (AudioClip.GetData == false — потоковые/сжатые клипы)
        /// или библиотека недоступна, возвращает false и пишет причину в лог.
        /// </summary>
        /// <param name="clip">Клип, из которого берётся PCM</param>
        /// <param name="quality">Качество Vorbis: -0.1..1.0 (0.4 ≈ 128 kbps стерео)</param>
        /// <param name="ogg">Байты OGG-файла (null при неудаче)</param>
        public static bool TryEncode(AudioClip clip, float quality, out byte[] ogg)
        {
            ogg = null;

            if (clip == null)
            {
                Debug.LogError("[OggEncoder] AudioClip == null — кодировать нечего.");
                return false;
            }

            if (!IsAvailable)
            {
                Debug.LogWarning($"[OggEncoder] Библиотека '{LibName}' недоступна — конвертация в OGG невозможна " +
                                 "(в редакторе это ожидаемо: .so собран под Android arm64).");
                return false;
            }

            try
            {
                int channels = clip.channels;
                if (channels <= 0)
                    channels = 1;

                int frameCount = clip.samples;
                if (frameCount <= 0)
                {
                    Debug.LogError($"[OggEncoder] Клип \"{clip.name}\": пустой (samples = {frameCount}).");
                    return false;
                }

                long totalSamples = (long)frameCount * channels;
                if (totalSamples > int.MaxValue)
                {
                    Debug.LogError($"[OggEncoder] Клип \"{clip.name}\" слишком длинный: {totalSamples} сэмплов.");
                    return false;
                }

                short[] pcm = new short[(int)totalSamples];
                float[] chunk = new float[ReadChunkFrames * channels];

                // Читаем кусками, чтобы не держать в памяти float[] на всю песню.
                for (int frame = 0; frame < frameCount; frame += ReadChunkFrames)
                {
                    int framesLeft = Mathf.Min(ReadChunkFrames, frameCount - frame);
                    float[] buffer = framesLeft == ReadChunkFrames
                        ? chunk
                        : new float[framesLeft * channels];

                    if (!clip.GetData(buffer, frame))
                    {
                        Debug.LogError($"[OggEncoder] Клип \"{clip.name}\" не читаем (AudioClip.GetData вернул false). " +
                                       "Загрузите аудио с DecompressOnLoad/compressed=false, чтобы получить PCM.");
                        return false;
                    }

                    int dst = frame * channels;
                    for (int i = 0; i < buffer.Length; i++)
                        pcm[dst + i] = ToInt16(buffer[i]);
                }

                return EncodePcm(pcm, frameCount, channels, clip.frequency, quality, out ogg);
            }
            catch (Exception e)
            {
                Debug.LogError($"[OggEncoder] Ошибка кодирования AudioClip \"{clip?.name}\": {e.GetType().Name}: {e.Message}");
                ogg = null;
                return false;
            }
        }

        /// <summary>
        /// Кодирует уже готовый PCM (s16, interleaved) в OGG Vorbis.
        /// </summary>
        public static bool TryEncodePcm(short[] pcm, int frames, int channels, int sampleRate, float quality,
            out byte[] ogg)
        {
            ogg = null;

            if (!IsAvailable)
            {
                Debug.LogWarning($"[OggEncoder] Библиотека '{LibName}' недоступна — PCM не закодирован.");
                return false;
            }

            try
            {
                return EncodePcm(pcm, frames, channels, sampleRate, quality, out ogg);
            }
            catch (Exception e)
            {
                Debug.LogError($"[OggEncoder] Ошибка кодирования PCM: {e.GetType().Name}: {e.Message}");
                ogg = null;
                return false;
            }
        }

        /// <summary>
        /// Читает PCM-16 WAV-файл (RIFF) и кодирует его в OGG.
        /// Нужен как запасной путь, когда AudioClip получить в читаемом виде нельзя.
        /// </summary>
        public static bool TryEncodeWavFile(string wavPath, float quality, out byte[] ogg)
        {
            ogg = null;

            short[] pcm;
            int channels;
            int sampleRate;

            if (!TryReadWav16(wavPath, out pcm, out channels, out sampleRate))
                return false;

            if (pcm.Length == 0 || channels <= 0)
            {
                Debug.LogError($"[OggEncoder] WAV \"{wavPath}\": нет сэмплов.");
                return false;
            }

            return TryEncodePcm(pcm, pcm.Length / channels, channels, sampleRate, quality, out ogg);
        }

        /// <summary>
        /// Разбирает WAV (RIFF/WAVE, 16-bit PCM) в interleaved short[].
        /// Возвращает false для не-WAV, 8/24/32-bit и сжатых файлов.
        /// </summary>
        public static bool TryReadWav16(string wavPath, out short[] pcm, out int channels, out int sampleRate)
        {
            pcm = null;
            channels = 0;
            sampleRate = 0;

            try
            {
                if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
                    return false;

                byte[] bytes = File.ReadAllBytes(wavPath);
                if (bytes.Length < 44)
                    return false;

                if (bytes[0] != (byte)'R' || bytes[1] != (byte)'I' || bytes[2] != (byte)'F' || bytes[3] != (byte)'F')
                    return false;

                if (bytes[8] != (byte)'W' || bytes[9] != (byte)'A' || bytes[10] != (byte)'V' || bytes[11] != (byte)'E')
                    return false;

                int format = 0;
                int bits = 0;
                int dataOffset = -1;
                int dataLength = 0;

                int pos = 12;
                while (pos + 8 <= bytes.Length)
                {
                    string id = string.Concat((char)bytes[pos], (char)bytes[pos + 1],
                                              (char)bytes[pos + 2], (char)bytes[pos + 3]);

                    int chunkSize = BitConverter.ToInt32(bytes, pos + 4);
                    int body = pos + 8;

                    if (chunkSize < 0 || body + chunkSize > bytes.Length)
                        chunkSize = bytes.Length - body; // терпим обрезанные файлы

                    if (id == "fmt " && chunkSize >= 16)
                    {
                        format = BitConverter.ToUInt16(bytes, body);
                        channels = BitConverter.ToUInt16(bytes, body + 2);
                        sampleRate = BitConverter.ToInt32(bytes, body + 4);
                        bits = BitConverter.ToUInt16(bytes, body + 14);
                    }
                    else if (id == "data")
                    {
                        dataOffset = body;
                        dataLength = chunkSize;
                    }

                    pos = body + chunkSize + (chunkSize & 1); // чанки выровнены по 2 байта
                }

                if (dataOffset < 0 || channels <= 0 || sampleRate <= 0)
                    return false;

                // 1 = PCM, 0xFFFE = WAVE_FORMAT_EXTENSIBLE (обычно тоже PCM)
                if (format != 1 && format != 0xFFFE)
                {
                    Debug.LogWarning($"[OggEncoder] WAV \"{wavPath}\": формат {format} не PCM — пропускаем.");
                    return false;
                }

                if (bits != 16)
                {
                    Debug.LogWarning($"[OggEncoder] WAV \"{wavPath}\": поддерживается только 16-bit PCM (найдено {bits} bit).");
                    return false;
                }

                int sampleCount = dataLength / 2;
                if (sampleCount <= 0)
                    return false;

                pcm = new short[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                    pcm[i] = BitConverter.ToInt16(bytes, dataOffset + i * 2);

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OggEncoder] WAV \"{wavPath}\" не разобран: {e.GetType().Name}: {e.Message}");
                pcm = null;
                channels = 0;
                sampleRate = 0;
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Внутренности
        // ------------------------------------------------------------------

        private static bool EncodePcm(short[] pcm, int frames, int channels, int sampleRate, float quality,
            out byte[] ogg)
        {
            ogg = null;

            if (pcm == null || frames <= 0 || channels <= 0 || sampleRate <= 0)
            {
                Debug.LogError($"[OggEncoder] Некорректные параметры PCM: frames={frames}, channels={channels}, " +
                               $"sampleRate={sampleRate}.");
                return false;
            }

            long expected = (long)frames * channels;
            if (pcm.Length < expected)
            {
                Debug.LogError($"[OggEncoder] PCM короче заявленного: {pcm.Length} < {expected}.");
                return false;
            }

            float q = Mathf.Clamp(quality, -0.1f, 1f);
            IntPtr buffer;
            int size;

            try
            {
                buffer = ragna_encode_ogg(pcm, frames, channels, sampleRate, q, out size);
            }
            catch (Exception e)
            {
                Debug.LogError($"[OggEncoder] Вызов ragna_encode_ogg не удался: {e.GetType().Name}: {e.Message}");
                return false;
            }

            if (buffer == IntPtr.Zero || size <= 0)
            {
                Debug.LogError($"[OggEncoder] ragna_encode_ogg вернул пустой буфер (frames={frames}, " +
                               $"channels={channels}, rate={sampleRate}, q={q:F2}).");
                FreeNative(buffer);
                return false;
            }

            try
            {
                ogg = new byte[size];
                Marshal.Copy(buffer, ogg, 0, size);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[OggEncoder] Копирование результата не удалось: {e.GetType().Name}: {e.Message}");
                ogg = null;
                return false;
            }
            finally
            {
                FreeNative(buffer);
            }
        }

        private static void FreeNative(IntPtr buffer)
        {
            if (buffer == IntPtr.Zero)
                return;

            try
            {
                ragna_free_ogg(buffer);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OggEncoder] ragna_free_ogg: {e.GetType().Name}: {e.Message}");
            }
        }

        private static bool Probe()
        {
            try
            {
                // Нативная функция NULL-безопасна (см. ragna_free_ogg), поэтому это честная проверка линковки.
                ragna_free_ogg(IntPtr.Zero);
                Debug.Log($"[OggEncoder] Библиотека '{LibName}' загружена — конвертация в OGG доступна.");
                return true;
            }
            catch (DllNotFoundException)
            {
                Debug.Log($"[OggEncoder] Библиотека '{LibName}' не найдена (нормально для редактора/PC) — " +
                          "экспорт пойдёт копированием исходного файла.");
                return false;
            }
            catch (EntryPointNotFoundException e)
            {
                Debug.LogError($"[OggEncoder] В '{LibName}' нет ожидаемой точки входа: {e.Message}");
                return false;
            }
            catch (BadImageFormatException e)
            {
                Debug.LogError($"[OggEncoder] '{LibName}' не подходит под архитектуру процесса: {e.Message}");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[OggEncoder] Проверка '{LibName}' не удалась: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        private static short ToInt16(float sample)
        {
            if (float.IsNaN(sample))
                return 0;
            if (sample > 1f)
                sample = 1f;
            else if (sample < -1f)
                sample = -1f;

            return (short)Mathf.RoundToInt(sample * 32767f);
        }
    }
}
