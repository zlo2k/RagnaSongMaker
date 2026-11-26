using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RagnarockEditor.Data;
using RagnarockEditor.FileSystem;
using RagnarockEditor.Quantization;
using RagnarockEditor.Serialization;
using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Экспорт записанной сессии в уровень Ragnarock:
    /// persistentDataPath/CustomSongs/&lt;safeName&gt;/{info.dat, &lt;Difficulty&gt;.dat, song.ogg, cover.jpg}.
    ///
    /// Порядок работы:
    ///   1) квантование session.RawHits через BeatQuantizer (Snap-to-Beat);
    ///   2) сборка BeatmapDatDTO.Notes и InfoDatDTO;
    ///   3) валидация LevelValidator (ошибки — экспорт не пишется, предупреждения — попадают в результат);
    ///   4) аудио: OggEncoder (нативный libvorbis) → song.ogg, иначе копия исходного файла
    ///      с флагом AudioConvertedToOgg = false;
    ///   5) запись info.dat и &lt;Difficulty&gt;.dat через DatSerializer.
    ///
    /// Наружу исключения не выходят: любой сбой превращается в ExportResult.Success == false.
    /// </summary>
    public class LevelExporter : MonoBehaviour
    {
        /// <summary>Результат экспорта (никаких исключений — всё здесь).</summary>
        public class ExportResult
        {
            /// <summary>true, если оба .dat файла и аудио записаны.</summary>
            public bool Success;

            /// <summary>Папка уровня (persistentDataPath/CustomSongs/&lt;safeName&gt;).</summary>
            public string FolderPath;

            /// <summary>Человекочитаемая сводка (для UI/лога).</summary>
            public string Message;

            /// <summary>Сколько нот попало в Beatmap.dat.</summary>
            public int NoteCount;

            /// <summary>true — аудио перекодировано в OGG; false — скопировано как есть.</summary>
            public bool AudioConvertedToOgg;

            /// <summary>Путь к записанному song.ogg (null, если аудио нет).</summary>
            public string AudioPath;

            /// <summary>Путь к info.dat.</summary>
            public string InfoDatPath;

            /// <summary>Путь к карте нот.</summary>
            public string BeatmapDatPath;

            /// <summary>Предупреждения валидатора и экспортёра.</summary>
            public List<string> Warnings = new List<string>();

            /// <summary>Ошибки (при Success == false).</summary>
            public List<string> Errors = new List<string>();

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append(Success ? "OK" : "FAIL");
                sb.Append(" | notes=").Append(NoteCount);
                sb.Append(" | ogg=").Append(AudioConvertedToOgg ? "yes" : "no");
                if (!string.IsNullOrEmpty(FolderPath))
                    sb.Append(" | ").Append(FolderPath);
                if (!string.IsNullOrEmpty(Message))
                    sb.Append(" | ").Append(Message);
                return sb.ToString();
            }
        }

        /// <summary>Необязательный слот AudioSource: если задан, клип берётся из него.</summary>
        public AudioSource AudioSourceRef;

        /// <summary>Необязательный клип-источник (приоритетнее AudioSourceRef).</summary>
        public AudioClip SourceClip;

        /// <summary>Переопределение пути к исходному аудиофайлу (по умолчанию — session.Song.FilePath).</summary>
        public string SourceAudioPath;

        /// <summary>Необязательная обложка: текстура (иначе генерируется заглушка).</summary>
        public Texture2D CoverTexture;

        /// <summary>Необязательная обложка: путь к готовому .jpg/.png.</summary>
        public string SourceCoverPath;

        /// <summary>Качество Vorbis: -0.1..1.0 (0.4 ≈ 128 kbps стерео).</summary>
        [Range(-0.1f, 1f)]
        public float OggQuality = 0.5f;

        [Header("Отладка (проверка без шлема)")]
        [Tooltip("При старте (Play в редакторе) экспортировать синтетическую сессию.")]
        public bool DebugExportOnStart;

        [Tooltip("Аудиофайл для отладочного экспорта. Пусто — берётся выбранная песня (SongSelectionState).")]
        public string DebugAudioPath;

        [Tooltip("Сколько синтетических ударов сгенерировать.")]
        public int DebugSyntheticHits = 64;

        [Tooltip("BPM синтетической сессии.")]
        public float DebugBpm = 120f;

        [Tooltip("Длительность синтетической сессии, сек.")]
        public float DebugDurationSeconds = 30f;

        // ------------------------------------------------------------------
        // Публичный API
        // ------------------------------------------------------------------

        /// <summary>
        /// Экспортирует уровень из сессии. Клип берётся из SourceClip/AudioSourceRef (если заданы),
        /// аудиофайл — из session.Song.FilePath (или SourceAudioPath).
        /// </summary>
        public ExportResult Export(RecordSession session, QuantizeSettings settings)
        {
            return Export(session, settings, ResolveClip());
        }

        /// <summary>
        /// Экспорт с явно переданным клипом (например, загруженным с compressed = false,
        /// чтобы AudioClip.GetData вернул PCM).
        /// </summary>
        public ExportResult Export(RecordSession session, QuantizeSettings settings, AudioClip clip)
        {
            var result = new ExportResult();
            string folder = null;

            try
            {
                // ---------- Проверки входа ----------
                if (session == null)
                    return Fail(result, "RecordSession не задан.");

                if (session.Song == null)
                    return Fail(result, "SongData не задан в сессии (песня не выбрана).");

                var song = session.Song;

                string songName = string.IsNullOrWhiteSpace(song.SongName)
                    ? (string.IsNullOrWhiteSpace(song.FilePath)
                        ? "Untitled"
                        : Path.GetFileNameWithoutExtension(song.FilePath))
                    : song.SongName;

                if (string.IsNullOrWhiteSpace(song.SongName))
                    song.SongName = songName;

                float bpm = song.BPM;
                if (bpm <= 0f)
                    return Fail(result, $"BPM не задан (={bpm:F2}). Укажите BPM перед экспортом.");

                float duration = song.DurationSeconds;
                if (duration <= 0f && clip != null)
                    duration = clip.length;
                if (duration <= 0f)
                    return Fail(result, "Длительность песни не задана (SongData.DurationSeconds <= 0).");

                if (session.RawHits == null || session.RawHits.Count == 0)
                    return Fail(result, "Нет записанных ударов (RawHits пуст) — нечего квантовать.");

                // ---------- 1. Квантование ----------
                List<Note> notes;
                try
                {
                    var quantizer = new BeatQuantizer(settings);
                    notes = quantizer.Quantize(session.RawHits, bpm, duration, song.FirstBeatSeconds);
                }
                catch (Exception e)
                {
                    return Fail(result, $"Ошибка квантования: {e.GetType().Name}: {e.Message}");
                }

                if (notes == null)
                    return Fail(result, "Квантователь вернул null.");

                // Защита от мусора: отрицательное время/некорректный барабан отбрасываем.
                notes.RemoveAll(n => n == null || n.Time < 0f || n.LineIndex < 0 || n.LineIndex > 3);

                if (notes.Count == 0)
                    return Fail(result, "После квантования не осталось ни одной валидной ноты.");

                // Кладём обратно в сессию — чтобы её видели CustomSongsExporter/PlaybackController.
                session.QuantizedNotes = notes;

                // ---------- 2. Имена файлов ----------
                string safeFolderName = SanitizeName(songName);
                string difficultyName = SanitizeDifficultyName(session.DifficultyName);
                string beatmapFileName = difficultyName + ".dat";
                int difficultyRank = Mathf.Clamp(session.DifficultyRank <= 0 ? 3 : session.DifficultyRank, 1, 10);

                // ---------- 3. Аудио (до info.dat: имя файла должно быть окончательным) ----------
                string sourcePath = ResolveAudioPath(session);
                bool hasSource = !string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath);

                if (clip == null && !hasSource)
                    return Fail(result, "Нет источника аудио: ни клип, ни SongData.FilePath не заданы.");

                string root = SongFileManager.GetCustomSongsPath();
                folder = Path.Combine(root, safeFolderName);
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                else
                {
                    result.Warnings.Add($"Папка уже существовала — файлы перезаписаны: {folder}");
                }

                result.FolderPath = folder;

                var audio = WriteAudio(folder, sourcePath, clip, result);
                if (!audio.Ok)
                {
                    result.FolderPath = folder;
                    return Fail(result, audio.Message);
                }

                // ---------- 4. Обложка ----------
                WriteCover(folder, result);

                // ---------- 5. info.dat ----------
                var info = new InfoDatDTO
                {
                    SongName = songName,
                    SongAuthorName = string.IsNullOrWhiteSpace(song.AuthorName) ? "Unknown" : song.AuthorName,
                    LevelAuthorName = string.IsNullOrWhiteSpace(session.LevelAuthorName)
                        ? "VR Editor User"
                        : session.LevelAuthorName,
                    BeatsPerMinute = bpm,
                    SongFilename = audio.FileName,
                    CoverImageFilename = "cover.jpg",
                    EnvironmentName = string.IsNullOrWhiteSpace(session.EnvironmentName)
                        ? "Midgard"
                        : session.EnvironmentName,
                    SongApproximativeDuration = Mathf.Max(1, Mathf.RoundToInt(duration)),
                    PreviewStartTime = Mathf.Clamp(duration * 0.33f, 0f, Mathf.Max(0f, duration - 11f)),
                    PreviewDuration = Mathf.Min(10f, duration)
                };

                info.AddDifficulty(difficultyName, difficultyRank, beatmapFileName);

                // ---------- 6. Beatmap.dat ----------
                var beatmap = new BeatmapDatDTO();
                beatmap.Notes.AddRange(notes);

                // ---------- 7. Валидация ----------
                var validation = LevelValidator.ValidateFull(info, beatmap);
                if (validation != null)
                {
                    if (validation.Warnings != null && validation.Warnings.Count > 0)
                        result.Warnings.AddRange(validation.Warnings);

                    // ВАЖНО: у ValidationResult.IsValid нет установки в true (наследие старого кода),
                    // поэтому опираемся на Errors, а не на IsValid.
                    if (validation.Errors != null && validation.Errors.Count > 0)
                    {
                        result.Errors.AddRange(validation.Errors);
                        result.FolderPath = folder;
                        return Fail(result, "Валидация не пройдена: " + string.Join("; ", validation.Errors.ToArray()));
                    }
                }

                // ---------- 8. Запись .dat ----------
                bool infoOk = DatSerializer.SaveInfoDat(folder, info);
                bool beatmapOk = DatSerializer.SaveBeatmapDat(folder, beatmap, beatmapFileName);

                if (!infoOk)
                    result.Errors.Add("Не удалось сохранить info.dat.");
                if (!beatmapOk)
                    result.Errors.Add($"Не удалось сохранить {beatmapFileName}.");

                result.NoteCount = beatmap.Notes.Count;
                result.InfoDatPath = Path.Combine(folder, "info.dat");
                result.BeatmapDatPath = Path.Combine(folder, beatmapFileName);
                result.AudioConvertedToOgg = audio.Converted;
                result.AudioPath = Path.Combine(folder, audio.FileName);
                result.Success = infoOk && beatmapOk && audio.Ok;

                var summary = new StringBuilder();
                summary.Append(result.Success ? "Экспорт завершён: " : "Экспорт с ошибками: ");
                summary.Append(folder);
                summary.Append($"; нот: {result.NoteCount}");
                summary.Append($"; BPM: {bpm:F1}");
                summary.Append(audio.Converted
                    ? "; аудио: OGG (перекодировано)"
                    : "; аудио: копия исходного файла (без перекодирования)");
                result.Message = summary.ToString();

                if (result.Success)
                {
                    Debug.Log($"[LevelExporter] {result.Message}");
                    if (result.Warnings.Count > 0)
                        Debug.LogWarning($"[LevelExporter] Предупреждения:\n  {string.Join("\n  ", result.Warnings.ToArray())}");
                }
                else
                {
                    Debug.LogError($"[LevelExporter] {result.Message}");
                }

                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LevelExporter] Необработанное исключение: {e}");
                result.Success = false;
                result.FolderPath = folder;
                result.Message = $"Ошибка экспорта: {e.GetType().Name}: {e.Message}";
                result.Errors.Add(e.ToString());
                result.AudioConvertedToOgg = false;
                return result;
            }
        }

        /// <summary>
        /// Экспорт текущей RecordSession (синглтон создаётся лениво).
        /// Удобно вешать на VR-кнопку «Сохранить уровень».
        /// </summary>
        public ExportResult ExportCurrentSession(QuantizeSettings settings)
        {
            try
            {
                // Настройки квантования берём из Resources, иначе останутся дефолтные
                if (settings == null)
                    settings = Resources.Load<QuantizeSettings>("QuantizeSettings");

                return Export(RecordSession.Instance, settings);
            }
            catch (Exception e)
            {
                var result = new ExportResult
                {
                    Success = false,
                    Message = $"Ошибка экспорта: {e.GetType().Name}: {e.Message}"
                };
                result.Errors.Add(e.ToString());
                Debug.LogError($"[LevelExporter] {result.Message}");
                return result;
            }
        }

        /// <summary>
        /// Убирает из имени всё, что нельзя использовать как имя папки/файла
        /// (Windows + Android). Недопустимые символы заменяются на '_'.
        /// </summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Untitled";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim())
            {
                bool invalid = char.IsControl(c) || c == '/' || c == '\\' || c == ':' || c == '*' ||
                               c == '?' || c == '"' || c == '<' || c == '>' || c == '|' ||
                               c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

                sb.Append(invalid ? '_' : c);
            }

            string safe = Collapse(sb.ToString().Trim());

            if (safe.Length > 64)
                safe = Collapse(safe.Substring(0, 64));

            safe = safe.Trim().Trim('.', ' ');

            if (string.IsNullOrWhiteSpace(safe) || safe == "." || safe == "..")
                return "Untitled";

            // Имя из одних подчёркиваний («...» → «_») — тоже бессмысленно.
            if (safe.Trim('_').Length == 0)
                return "Untitled";

            // Зарезервированные имена Windows (на всякий случай — экспорт может идти и на PC).
            if (IsReservedWindowsName(safe))
                safe = "_" + safe;

            return safe;
        }

        /// <summary>
        /// Приводит имя к «плоскому» виду: никаких «..» (чтобы нельзя было выйти из папки)
        /// и никаких серий подчёркиваний.
        /// </summary>
        private static string Collapse(string name)
        {
            string safe = name ?? string.Empty;

            while (safe.Contains(".."))
                safe = safe.Replace("..", "_");

            while (safe.Contains("__"))
                safe = safe.Replace("__", "_");

            return safe;
        }

        // ------------------------------------------------------------------
        // Отладка без шлема
        // ------------------------------------------------------------------

        private void Start()
        {
            if (DebugExportOnStart)
                DebugExport();
        }

        /// <summary>
        /// Собирает синтетическую сессию (BPM/длительность из инспектора, удары — случайные
        /// по четырём барабанам с лёгким «человеческим» разбросом) и экспортирует её.
        /// Аудио: DebugAudioPath → иначе SongSelectionState.SelectedPath.
        /// </summary>
        [ContextMenu("Отладка: экспорт синтетической сессии")]
        public void DebugExport()
        {
            QuantizeSettings settings = null;
            GameObject sessionGo = null;

            try
            {
                settings = ScriptableObject.CreateInstance<QuantizeSettings>();

                sessionGo = new GameObject("DebugRecordSession");
                var session = sessionGo.AddComponent<RecordSession>();
                session.Song = new SongData
                {
                    SongName = "Debug Song",
                    AuthorName = "Ragnarock Editor",
                    BPM = DebugBpm,
                    DurationSeconds = DebugDurationSeconds,
                    FilePath = ResolveDebugAudioPath()
                };
                session.LevelAuthorName = "VR Editor User";
                session.EnvironmentName = "Midgard";

                FillSyntheticHits(session, DebugBpm, DebugDurationSeconds, DebugSyntheticHits);

                var result = Export(session, settings, ResolveClip());
                Debug.Log($"[LevelExporter] Отладочный экспорт: {result}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LevelExporter] Отладочный экспорт упал: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                if (settings != null)
                {
                    if (Application.isPlaying) Destroy(settings);
                    else DestroyImmediate(settings);
                }

                if (sessionGo != null)
                {
                    if (Application.isPlaying) Destroy(sessionGo);
                    else DestroyImmediate(sessionGo);
                }
            }
        }

        // ------------------------------------------------------------------
        // Аудио / обложка
        // ------------------------------------------------------------------

        private class AudioOutcome
        {
            public bool Ok;
            public bool Converted;
            public string FileName = "song.ogg";
            public string Message = string.Empty;
        }

        /// <summary>
        /// Пишет song.ogg: перекодирует в OGG, если есть нативная библиотека и читаемый
        /// PCM (клип или WAV-файл), иначе копирует исходный файл как есть.
        /// </summary>
        private AudioOutcome WriteAudio(string folder, string sourcePath, AudioClip clip, ExportResult result)
        {
            var outcome = new AudioOutcome();
            string targetPath = Path.Combine(folder, "song.ogg");
            bool hasSource = !string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath);

            try
            {
                // Уже OGG — перекодировать нечего, просто копируем (качество не теряем).
                if (hasSource && HasExtension(sourcePath, ".ogg"))
                {
                    File.Copy(sourcePath, targetPath, true);
                    outcome.Ok = true;
                    outcome.Converted = false;
                    result.AudioConvertedToOgg = false;
                    result.Warnings.Add("Исходный файл уже OGG — скопирован как song.ogg без перекодирования.");
                    Debug.Log($"[LevelExporter] Аудио: копия OGG → {targetPath}");
                    return outcome;
                }

                if (OggEncoder.IsAvailable)
                {
                    byte[] ogg = null;
                    bool encoded = false;

                    if (clip != null)
                    {
                        encoded = OggEncoder.TryEncode(clip, OggQuality, out ogg);
                        if (!encoded)
                            result.Warnings.Add("AudioClip не читаем — пробуем WAV-файл/копирование.");
                    }

                    if (!encoded && hasSource && HasExtension(sourcePath, ".wav"))
                        encoded = OggEncoder.TryEncodeWavFile(sourcePath, OggQuality, out ogg);

                    if (encoded && ogg != null && ogg.Length > 0)
                    {
                        File.WriteAllBytes(targetPath, ogg);
                        outcome.Ok = true;
                        outcome.Converted = true;
                        result.Warnings.Add($"Аудио перекодировано в OGG ({ogg.Length / 1024} КБ, q={OggQuality:F2}).");
                        Debug.Log($"[LevelExporter] Аудио: OGG ({ogg.Length} байт) → {targetPath}");
                        return outcome;
                    }

                    result.Warnings.Add("Библиотека ragnaogg доступна, но кодирование не удалось — " +
                                        "используется копия исходного файла.");
                }
                else
                {
                    result.Warnings.Add("Библиотека ragnaogg недоступна (ожидаемо в редакторе/на x86) — " +
                                        "исходный файл скопирован как есть, AudioConvertedToOgg = false.");
                }

                if (hasSource)
                {
                    File.Copy(sourcePath, targetPath, true);
                    outcome.Ok = true;
                    outcome.Converted = false;
                    result.AudioConvertedToOgg = false;
                    string ext = Path.GetExtension(sourcePath);
                    if (!HasExtension(sourcePath, ".ogg"))
                    {
                        result.Warnings.Add($"Скопирован {ext}-файл под именем song.ogg. Ragnarock ожидает OGG — " +
                                            "на устройстве с libragnaogg включите конвертацию (WAV-источник) " +
                                            "или подготовьте OGG вручную.");
                    }

                    Debug.Log($"[LevelExporter] Аудио: копия {ext} → {targetPath}");
                    return outcome;
                }

                outcome.Ok = false;
                outcome.Message = "Не удалось получить аудио: нет клипа и нет доступного исходного файла.";
                return outcome;
            }
            catch (Exception e)
            {
                outcome.Ok = false;
                outcome.Message = $"Ошибка записи аудио: {e.GetType().Name}: {e.Message}";
                Debug.LogError($"[LevelExporter] {outcome.Message}");
                return outcome;
            }
        }

        /// <summary>
        /// Копирует готовую обложку или генерирует простую заглушку cover.jpg.
        /// </summary>
        private bool WriteCover(string folder, ExportResult result)
        {
            Texture2D generated = null;

            try
            {
                string targetPath = Path.Combine(folder, "cover.jpg");

                if (!string.IsNullOrEmpty(SourceCoverPath) && File.Exists(SourceCoverPath))
                {
                    File.Copy(SourceCoverPath, targetPath, true);
                    Debug.Log($"[LevelExporter] Обложка скопирована → {targetPath}");
                    return true;
                }

                Texture2D texture = CoverTexture != null ? CoverTexture : (generated = CreatePlaceholderCover());
                if (texture == null)
                {
                    result.Warnings.Add("Обложка не создана (текстура недоступна).");
                    return false;
                }

                byte[] jpg = texture.EncodeToJPG(85);
                if (jpg == null || jpg.Length == 0)
                {
                    result.Warnings.Add("Обложка: EncodeToJPG вернул пустой массив.");
                    return false;
                }

                File.WriteAllBytes(targetPath, jpg);
                Debug.Log($"[LevelExporter] Обложка записана → {targetPath} ({jpg.Length} байт)");
                return true;
            }
            catch (Exception e)
            {
                result.Warnings.Add($"Обложка не создана: {e.GetType().Name}: {e.Message}");
                Debug.LogWarning($"[LevelExporter] Обложка не создана: {e.GetType().Name}: {e.Message}");
                return false;
            }
            finally
            {
                if (generated != null)
                {
                    if (Application.isPlaying) Destroy(generated);
                    else DestroyImmediate(generated);
                }
            }
        }

        private static Texture2D CreatePlaceholderCover()
        {
            const int size = 512;

            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                float t = size > 1 ? (float)y / (size - 1) : 0f;
                byte r = (byte)Mathf.Lerp(22f, 58f, t);
                byte g = (byte)Mathf.Lerp(26f, 42f, t);
                byte b = (byte)Mathf.Lerp(44f, 92f, t);

                for (int x = 0; x < size; x++)
                {
                    // Лёгкая «каменная» сетка, чтобы заглушка не была плоским квадратом.
                    byte shade = (byte)((x % 64 == 0 || y % 64 == 0) ? 14 : 0);
                    pixels[y * size + x] = new Color32(
                        (byte)Mathf.Min(255, r + shade),
                        (byte)Mathf.Min(255, g + shade),
                        (byte)Mathf.Min(255, b + shade),
                        255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
            texture.name = "cover_placeholder";
            return texture;
        }

        // ------------------------------------------------------------------
        // Вспомогательное
        // ------------------------------------------------------------------

        private AudioClip ResolveClip()
        {
            if (SourceClip != null)
                return SourceClip;

            if (AudioSourceRef != null && AudioSourceRef.clip != null)
                return AudioSourceRef.clip;

            // Основной путь экспорта: клип лежит разжатым в SongPlaybackBootstrap
            // (загружен с compressed = false как раз для перекодирования в OGG).
            var playback = FindFirstObjectByType<SongPlaybackBootstrap>();
            if (playback != null)
            {
                var source = playback.GetComponent<AudioSource>();
                if (source != null && source.clip != null)
                    return source.clip;
            }

            return null;
        }

        private string ResolveAudioPath(RecordSession session)
        {
            if (!string.IsNullOrEmpty(SourceAudioPath) && File.Exists(SourceAudioPath))
                return SourceAudioPath;

            return session != null && session.Song != null ? session.Song.FilePath : null;
        }

        private string ResolveDebugAudioPath()
        {
            if (!string.IsNullOrEmpty(DebugAudioPath) && File.Exists(DebugAudioPath))
                return DebugAudioPath;

            string selected = SongSelectionState.SelectedPath;
            if (!string.IsNullOrEmpty(selected) && File.Exists(selected))
                return selected;

            return null;
        }

        private static void FillSyntheticHits(RecordSession session, float bpm, float durationSeconds, int count)
        {
            session.RawHits.Clear();

            if (count <= 0 || bpm <= 0f || durationSeconds <= 0f)
                return;

            float beat = 60f / bpm;
            float time = beat;
            var random = new System.Random(12345);

            for (int i = 0; i < count && time < durationSeconds; i++)
            {
                // Лёгкий «человеческий» разброс, чтобы проверить работу порога SnapToBeat.
                float jitter = (float)(random.NextDouble() - 0.5) * 0.06f;
                int drum = random.Next(0, 4);
                session.RawHits.Add(new RawHit(time + jitter, drum));
                time += beat * (random.Next(0, 3) == 0 ? 2f : 1f);
            }
        }

        private static string SanitizeDifficultyName(string name)
        {
            string safe = SanitizeName(name);

            if (safe.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                safe = safe.Substring(0, safe.Length - 4).Trim();

            if (string.IsNullOrWhiteSpace(safe))
                safe = "Normal";

            return safe;
        }

        private static bool HasExtension(string path, string extension)
        {
            return string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReservedWindowsName(string name)        {
            string upper = name.ToUpperInvariant();
            if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL")
                return true;

            if (upper.Length == 4 &&
                (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal)) &&
                upper[3] >= '1' && upper[3] <= '9')
                return true;

            return false;
        }

        private static ExportResult Fail(ExportResult result, string message)
        {
            result.Success = false;
            result.Message = message;
            result.AudioConvertedToOgg = false;
            if (!result.Errors.Contains(message))
                result.Errors.Add(message);

            Debug.LogError($"[LevelExporter] {message}");
            return result;
        }
    }
}
