using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using RagnarockEditor.Audio;
using RagnarockEditor.Core;
using RagnarockEditor.Data;
using RagnarockEditor.Localization;
using RagnarockEditor.UI;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// M1: оркестратор этапа BPM на экране выбора трека.
    /// Поток: StartAnalysis → AudioLoader.LoadAudio → PCM снимается в главном потоке →
    /// BPMDetector.Analyze в фоновой задаче → BPMEditorUI.ShowBPMResult →
    /// Continue → RecordSession + SongSelectionState → SceneLoader("RecordScene").
    /// </summary>
    [DisallowMultipleComponent]
    public class BpmFlowController : MonoBehaviour
    {
        [Header("Компоненты (если пусто — ищутся в сцене)")]
        [SerializeField] private AudioLoader _audioLoader;
        [SerializeField] private BPMEditorUI _bpmEditor;

        [Header("UI (необязательно)")]
        [SerializeField] private Text _statusText;
        [Tooltip("Строка статуса на экране списка треков — показывает загрузку/анализ до открытия BPM-панели.")]
        [SerializeField] private Text _listStatusText;
        [SerializeField] private Button _continueButton;
        [SerializeField] private GameObject _bpmEditorRoot;
        [SerializeField] private GameObject _selectionRoot;
        [Tooltip("Каменный пульт BPM (ползунок BPM, метроном): виден только на экране BPM.")]
        [SerializeField] private GameObject _bpmConsole;
        [Tooltip("Плита-заголовок списка треков: на экране BPM её прячем, иначе она перекрывает планшет.")]
        [SerializeField] private GameObject _listHeader;

        [Header("Анализ")]
        [Tooltip("Считать BPM в фоновом потоке. Выключение = анализ в главном потоке (фриз на время расчёта).")]
        [SerializeField] private bool _analyzeInBackground = true;

        private const float MinBpm = 40f;
        private const float MaxBpm = 300f;
        private const float DefaultBpm = 120f;

        private Task<BPMDetector.BPMResult> _analysisTask;
        private string _pendingPath;
        private string _pendingName;
        private float _clipLength;
        private bool _waitingForClip;
        private bool _navigating;
        private int _lastProgressPercent = -1;

        /// <summary>Идёт ли сейчас фоновый анализ.</summary>
        public bool IsAnalyzing => _analysisTask != null;

        /// <summary>
        /// Компонент действительно подключён к BPM-канвасу. Нужно, чтобы отличить
        /// его от устаревших дубликатов, которые остались в старых сценах.
        /// </summary>
        public bool IsWiredToBpmCanvas => _bpmEditorRoot != null;

        /// <summary>Последний полученный результат анализа (может быть null).</summary>
        public BPMDetector.BPMResult LastResult { get; private set; }

        /// <summary>Имя трека, выбранного для анализа.</summary>
        public string PendingSongName => _pendingName;

        private void Start()
        {
            if (_continueButton != null)
                _continueButton.onClick.AddListener(Continue);

            ResolveAudioLoader();
            ResolveBpmEditor();

            // сцена открывается на списке треков: пульт спрятан, заголовок списка на месте
            if (_bpmEditorRoot == null || !_bpmEditorRoot.activeSelf)
                SetBpmScreensVisible(false);
        }

        private void OnDestroy()
        {
            UnsubscribeLoader();

            if (_bpmEditor != null)
                _bpmEditor.OnConfirmed -= HandleEditorConfirmed;
        }

        private void Update()
        {
            UpdateLoadStatus();
            PollAnalysis();
        }

        // ------------------------------------------------------------------
        // Публичный API этапа
        // ------------------------------------------------------------------

        /// <summary>
        /// Запускает загрузку трека и анализ BPM. Повторный вызов перезапускает этап.
        /// </summary>
        /// <param name="filePath">Полный путь к аудиофайлу</param>
        /// <param name="songName">Отображаемое имя трека</param>
        public void StartAnalysis(string filePath, string songName)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                SetStatus(Loc.T(LocKeys.SongsAnalysisError, "empty path"));
                Debug.LogError("[BpmFlow] StartAnalysis: пустой путь к файлу");
                return;
            }

            _pendingPath = filePath;
            _pendingName = string.IsNullOrEmpty(songName)
                ? System.IO.Path.GetFileNameWithoutExtension(filePath)
                : songName;

            _navigating = false;
            _analysisTask = null;
            _clipLength = 0f;
            LastResult = null;
            _lastProgressPercent = -1;

            var loader = ResolveAudioLoader();
            if (loader == null)
            {
                SetStatus(Loc.T(LocKeys.SongsAnalysisError, "AudioLoader not found"));
                Debug.LogError("[BpmFlow] AudioLoader не найден — анализ BPM невозможен");
                return;
            }

            // подписываемся ровно на одну загрузку
            UnsubscribeLoader();
            loader.OnAudioLoaded += HandleAudioLoaded;
            loader.OnLoadError += HandleLoadError;

            // Сразу переключаем экран на BPM: нажатие по треку должно давать видимую реакцию,
            // даже если загрузка и анализ займут несколько секунд.
            ShowBpmStage();
            SetContinueInteractable(false);
            ResolveBpmEditor()?.ResetForAnalysis();

            _waitingForClip = true;
            SetStatus(Loc.T(LocKeys.SongsLoading, _pendingName));
            Debug.Log($"[BpmFlow] Старт этапа BPM: {_pendingName} ({filePath})");

            loader.LoadAudio(filePath);
        }

        /// <summary>
        /// Подтверждает BPM, пишет его в RecordSession/SongSelectionState и грузит RecordScene.
        /// Можно вешать прямо на кнопку «Продолжить» в сцене.
        /// </summary>
        public void Continue()
        {
            if (_navigating)
                return;

            if (IsAnalyzing)
            {
                SetStatus(Loc.T(LocKeys.SongsAnalyzing, _pendingName));
                Debug.LogWarning("[BpmFlow] Continue: анализ BPM ещё не завершён");
                return;
            }

            float bpm = ResolveCurrentBpm();

            SaveSongToSession(bpm);
            SongSelectionState.SetBpm(bpm);

            var song = RecordSession.Instance.Song;
            if (song == null)
            {
                SetStatus(Loc.T(LocKeys.SongsAnalysisError, "song data is missing"));
                Debug.LogError("[BpmFlow] Continue: RecordSession.Song = null");
                return;
            }

            _navigating = true;

            UnsubscribeLoader();
            if (_bpmEditor != null)
                _bpmEditor.OnConfirmed -= HandleEditorConfirmed;

            SetStatus(Loc.T(LocKeys.BpmStatus, song.SongName,
                song.BPM.ToString("0.#", CultureInfo.InvariantCulture)));
            Debug.Log($"[BpmFlow] Продолжить → RecordScene: {song}");

            SceneLoader.Instance.LoadScene("RecordScene");
        }

        // ------------------------------------------------------------------
        // Загрузка и анализ
        // ------------------------------------------------------------------

        private void HandleAudioLoaded(AudioClip clip)
        {
            UnsubscribeLoader();
            _waitingForClip = false;

            if (clip == null)
            {
                SetStatus(Loc.T(LocKeys.SongsAnalysisError, "audio clip is null"));
                Debug.LogError("[BpmFlow] AudioLoader вернул null-clip");
                return;
            }

            _clipLength = clip.length;
            SetStatus(Loc.T(LocKeys.SongsAnalyzing, _pendingName));

            // PCM читаем только здесь: AudioClip.GetData обязан вызываться в главном потоке.
            int sampleRate = clip.frequency;
            float[] samples = null;

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
                Debug.LogWarning("[BpmFlow] Не удалось прочитать PCM: " + e.Message);
                samples = null;
            }

            if (!_analyzeInBackground || samples == null || samples.Length == 0 || sampleRate <= 0)
            {
                RunAnalysisSync(samples, sampleRate, clip);
                return;
            }

            // В задачу передаём локальные копии: поля компонента могут измениться при перезапуске этапа.
            float[] samplesForTask = samples;
            int rateForTask = sampleRate;

            Debug.Log($"[BpmFlow] Анализ в фоне: {samplesForTask.Length} семплов, {rateForTask} Гц");

            try
            {
                _analysisTask = Task.Run(() => BPMDetector.Analyze(samplesForTask, rateForTask));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BpmFlow] Не удалось запустить фоновую задачу, считаю синхронно: " + e.Message);
                _analysisTask = null;
                RunAnalysisSync(samples, sampleRate, clip);
            }
        }

        private void HandleLoadError(string error)
        {
            UnsubscribeLoader();
            _waitingForClip = false;

            SetStatus(Loc.T(LocKeys.SongsAnalysisError, error));
            Debug.LogError("[BpmFlow] Ошибка загрузки аудио: " + error);
        }

        private void RunAnalysisSync(float[] samples, int sampleRate, AudioClip clip)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            BPMDetector.BPMResult result;

            try
            {
                result = (samples != null && samples.Length > 0 && sampleRate > 0)
                    ? BPMDetector.Analyze(samples, sampleRate)
                    : BPMDetector.Analyze(clip);
            }
            catch (Exception e)
            {
                Debug.LogError("[BpmFlow] Синхронный анализ BPM упал: " + e);
                result = CreateFallbackResult(e.Message);
            }

            stopwatch.Stop();
            Debug.Log($"[BpmFlow] Синхронный анализ занял {stopwatch.ElapsedMilliseconds} мс");

            ApplyResult(result);
        }

        private void PollAnalysis()
        {
            if (_analysisTask == null || !_analysisTask.IsCompleted)
                return;

            var task = _analysisTask;
            _analysisTask = null;

            if (task.IsFaulted || task.IsCanceled)
            {
                string error = "задача отменена";
                if (task.Exception != null)
                    error = task.Exception.GetBaseException().Message;

                Debug.LogError("[BpmFlow] Анализ BPM завершился ошибкой: " + error);
                ApplyResult(CreateFallbackResult(error));
                return;
            }

            ApplyResult(task.Result);
        }

        private void ApplyResult(BPMDetector.BPMResult result)
        {
            if (result == null)
            {
                SetStatus(Loc.T(LocKeys.SongsAnalysisError, "empty analysis result"));
                Debug.LogError("[BpmFlow] Результат анализа = null");
                return;
            }

            LastResult = result;
            SetContinueInteractable(true);

            // BPM сразу кладём в сессию: BPMEditorUI.UpdateSessionBPM пишет именно в RecordSession.Song
            SaveSongToSession(result.PrimaryBPM);

            var editor = ResolveBpmEditor();
            if (editor != null)
            {
                if (_bpmEditorRoot != null && !_bpmEditorRoot.activeSelf)
                    _bpmEditorRoot.SetActive(true);

                HideSelectionScreen();
                SetBpmScreensVisible(true);

                editor.OnConfirmed -= HandleEditorConfirmed;
                editor.OnConfirmed += HandleEditorConfirmed;
                editor.ShowBPMResult(result);
            }
            else
            {
                Debug.LogWarning("[BpmFlow] BPMEditorUI не найден: BPM уже в сессии, " +
                                 "дальше — кнопка, привязанная к BpmFlowController.Continue()");
            }

            string bpmText = result.PrimaryBPM.ToString("0.#", CultureInfo.InvariantCulture);

            SetStatus(result.Confidence < 0.2f
                ? Loc.T(LocKeys.BpmLowConfidence) + " · " + bpmText + " BPM"
                : Loc.T(LocKeys.BpmStatus, _pendingName, bpmText));

            Debug.Log($"[BpmFlow] Результат анализа: {result}");
        }

        private void UpdateLoadStatus()
        {
            if (!_waitingForClip || _statusText == null)
                return;

            var loader = _audioLoader;
            if (loader == null)
                return;

            int percent = Mathf.RoundToInt(Mathf.Clamp01(loader.Progress) * 100f);
            if (percent == _lastProgressPercent)
                return;

            _lastProgressPercent = percent;
            SetStatus(Loc.T(LocKeys.SongsLoading, _pendingName) + "  " + percent + "%");
        }

        /// <summary>
        /// Возврат с экрана BPM к списку треков (кнопка Back на BPM-панели).
        /// Сам канвас BPM выключается, но компонент на нём остаётся жив:
        /// следующий выбор трека снова его включит.
        /// </summary>
        public void Cancel()
        {
            UnsubscribeLoader();

            _waitingForClip = false;
            _analysisTask = null;
            _navigating = false;
            _lastProgressPercent = -1;

            if (_bpmEditor != null)
                _bpmEditor.OnConfirmed -= HandleEditorConfirmed;

            SetStatus(string.Empty);

            if (_bpmEditorRoot != null)
                _bpmEditorRoot.SetActive(false);

            if (_selectionRoot != null)
                _selectionRoot.SetActive(true);

            SetBpmScreensVisible(false);

            SongSelectionState.Clear();

            var session = RecordSession.Instance;
            if (session != null)
                session.Song = null;

            Debug.Log("[BpmFlow] отмена — назад к списку треков");
        }

        // ------------------------------------------------------------------
        // Сессия и переход
        // ------------------------------------------------------------------

        private void HandleEditorConfirmed(float bpm)
        {
            SongSelectionState.SetBpm(bpm);
            SaveSongToSession(bpm);
            Continue();
        }

        private void SaveSongToSession(float bpm)
        {
            if (float.IsNaN(bpm) || bpm <= 0f)
                bpm = DefaultBpm;

            bpm = Mathf.Clamp(bpm, MinBpm, MaxBpm);

            var session = RecordSession.Instance;
            var song = session.Song;

            if (song == null)
            {
                song = new SongData
                {
                    FilePath = _pendingPath,
                    SongName = string.IsNullOrEmpty(_pendingName) ? "Unknown" : _pendingName,
                    AuthorName = "Unknown",
                    BPM = bpm,
                    DurationSeconds = _clipLength
                };
                session.Song = song;
            }
            else
            {
                if (!string.IsNullOrEmpty(_pendingPath))
                    song.FilePath = _pendingPath;

                if (string.IsNullOrEmpty(song.SongName))
                    song.SongName = string.IsNullOrEmpty(_pendingName) ? "Unknown" : _pendingName;

                if (string.IsNullOrEmpty(song.AuthorName))
                    song.AuthorName = "Unknown";

                song.BPM = bpm;

                if (_clipLength > 0f)
                    song.DurationSeconds = _clipLength;
            }

            // дублируем выбор (и BPM) в статическое состояние, которое переживает смену сцены
            if (!string.IsNullOrEmpty(song.FilePath))
                SongSelectionState.Select(song.FilePath, song.SongName, bpm);
            else
                SongSelectionState.SetBpm(bpm);
        }

        private float ResolveCurrentBpm()
        {
            var editor = ResolveBpmEditor();
            if (editor != null && editor.CurrentBpm > 0f)
                return Mathf.Clamp(editor.CurrentBpm, MinBpm, MaxBpm);

            var session = RecordSession.Instance;
            if (session.Song != null && session.Song.BPM > 0f)
                return Mathf.Clamp(session.Song.BPM, MinBpm, MaxBpm);

            if (LastResult != null && LastResult.PrimaryBPM > 0f)
                return Mathf.Clamp(LastResult.PrimaryBPM, MinBpm, MaxBpm);

            return DefaultBpm;
        }

        /// <summary>
        /// Прячет экран выбора, но никогда — контейнер, внутри которого живёт сам оркестратор
        /// (иначе кнопка «Продолжить» станет недоступной).
        /// </summary>
        private void HideSelectionScreen()
        {
            if (_selectionRoot == null || _selectionRoot == _bpmEditorRoot)
                return;

            // редактор BPM может быть дочерним объектом панели выбора — прятать её нельзя
            if (_bpmEditor != null && _bpmEditor.transform.IsChildOf(_selectionRoot.transform))
                return;

            if (_selectionRoot.transform.IsChildOf(transform) || transform.IsChildOf(_selectionRoot.transform))
                return;

            if (_selectionRoot.activeSelf)
                _selectionRoot.SetActive(false);
        }

        // ------------------------------------------------------------------
        // Вспомогательное
        // ------------------------------------------------------------------

        private AudioLoader ResolveAudioLoader()
        {
            if (_audioLoader == null)
                _audioLoader = FindFirstObjectByType<AudioLoader>(FindObjectsInactive.Include);

            return _audioLoader;
        }

        private BPMEditorUI ResolveBpmEditor()
        {
            if (_bpmEditor == null)
                _bpmEditor = FindFirstObjectByType<BPMEditorUI>(FindObjectsInactive.Include);

            return _bpmEditor;
        }

        private void UnsubscribeLoader()
        {
            var loader = _audioLoader;
            if (loader == null)
                return;

            loader.OnAudioLoaded -= HandleAudioLoaded;
            loader.OnLoadError -= HandleLoadError;
        }

        private void SetStatus(string text)
        {
            if (_statusText != null)
                _statusText.text = text;

            // статус дублируется на экране списка: пока идёт загрузка/анализ, BPM-панель ещё скрыта
            if (_listStatusText != null)
                _listStatusText.text = text;
        }

        /// <summary>Показывает экран BPM и прячет список треков.</summary>
        private void ShowBpmStage()
        {
            if (_bpmEditorRoot == null)
            {
                // без ссылки на канвас список прятать нельзя — иначе получится пустой экран
                Debug.LogError("[BpmFlow] _bpmEditorRoot не назначен — BPM-экран не показать");
                return;
            }

            if (!_bpmEditorRoot.activeSelf)
                _bpmEditorRoot.SetActive(true);

            HideSelectionScreen();
            SetBpmScreensVisible(true);
        }

        /// <summary>
        /// Пульт и плита-заголовок списка: на экране BPM нужен пульт, на списке — заголовок.
        /// Раньше пульт висел на экране списка, а плита «Открыть MP3» перекрывала планшет BPM.
        /// </summary>
        private void SetBpmScreensVisible(bool bpmOpen)
        {
            if (_bpmConsole != null && _bpmConsole.activeSelf != bpmOpen)
                _bpmConsole.SetActive(bpmOpen);

            if (_listHeader != null && _listHeader.activeSelf == bpmOpen)
                _listHeader.SetActive(!bpmOpen);
        }

        /// <summary>«Продолжить» доступна только когда BPM реально есть.</summary>
        private void SetContinueInteractable(bool value)
        {
            if (_continueButton != null)
                _continueButton.interactable = value;
        }

        private static BPMDetector.BPMResult CreateFallbackResult(string reason)
        {
            Debug.LogWarning("[BpmFlow] Резервный BPM 120: " + reason);

            var result = new BPMDetector.BPMResult
            {
                PrimaryBPM = DefaultBpm,
                AlternativeBPM = 0f,
                Confidence = 0f,
                Onsets = new List<float>(),
                FirstBeatOffset = 0f
            };

            return result;
        }
    }
}
