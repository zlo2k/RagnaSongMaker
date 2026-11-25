using System;
using System.Collections;
using RagnarockEditor.Audio;
using RagnarockEditor.Data;
using RagnarockEditor.Localization;
using RagnarockEditor.VR;
using UnityEngine;

namespace RagnarockEditor.Core
{
    /// <summary>
    /// Сквозной поток приложения: «показан ли туториал», «есть ли несохранённая запись»,
    /// «можно ли уйти из сцены/выйти из приложения».
    ///
    /// Все выходы и сброс сессии идут через этот класс, поэтому предупреждение о
    /// несохранённых данных появляется в одном месте и одинаково во всех сценах.
    /// Переходы между рабочими сценами (BPM ↔ запись ↔ предпросмотр) не спрашивают
    /// ничего: RecordSession живёт между сценами и не теряется.
    /// Объект создаётся лениво и живёт между сценами (DontDestroyOnLoad).
    /// </summary>
    [DisallowMultipleComponent]
    public class AppFlow : MonoBehaviour
    {
        public const string MainMenuScene = "MainMenuScene";
        public const string SongSelectionScene = "SongSelectionScene";
        public const string RecordScene = "RecordScene";
        public const string PreviewScene = "PreviewScene";

        private const string TutorialKey = "ragna.tutorialSeen";

        private static AppFlow _instance;

        private bool _dirty;

        /// <summary>Синглтон: создаётся при первом обращении.</summary>
        public static AppFlow Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("AppFlow");
                    _instance = go.AddComponent<AppFlow>();
                    DontDestroyOnLoad(go);
                }

                return _instance;
            }
        }

        /// <summary>Туториал уже показывали (не показываем при следующих запусках).</summary>
        public bool TutorialSeen
        {
            get => PlayerPrefs.GetInt(TutorialKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(TutorialKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Сколько ударов записано и ещё не экспортировано.</summary>
        public int UnsavedHitCount
        {
            get
            {
                var session = RecordSession.Instance;
                return session != null ? session.TotalRawHits : 0;
            }
        }

        /// <summary>Есть ли дубль, который потеряется при выходе.</summary>
        public bool IsDirty => _dirty && UnsavedHitCount > 0;

        /// <summary>Выбран ли трек (переживает смену сцены).</summary>
        public bool HasSong => !string.IsNullOrEmpty(SongSelectionState.SelectedPath);

        /// <summary>Имя выбранного трека или null.</summary>
        public string SongName => SongSelectionState.SelectedName;

        /// <summary>BPM выбранного трека (0, если не определён).</summary>
        public float SongBpm => SongSelectionState.SelectedBpm;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            Loc.Initialize();
        }

        // ------------------------------------------------------- состояние сессии

        /// <summary>Отмечает, что в сессии появились несохранённые удары.</summary>
        public void MarkDirty() => _dirty = true;

        /// <summary>Уровень экспортирован — терять нечего.</summary>
        public void MarkSaved() => _dirty = false;

        /// <summary>Начинает новый уровень: чистит сессию и выбранную песню.</summary>
        public void ResetSession()
        {
            var session = RecordSession.Instance;
            session.Clear();
            SongSelectionState.Clear();
            _dirty = false;
        }

        // --------------------------------------------------------------- переходы

        /// <summary>
        /// Переход в другую сцену. Спрашиваем только при уходе в главное меню
        /// с несохранённым дублем (рабочие переходы данные не теряют).
        /// </summary>
        public bool TryLeaveTo(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                return true;

            if (IsDirty && sceneName == MainMenuScene)
                return AskUnsaved(sceneName, resetSession: true);

            SceneLoader.Instance.LoadScene(sceneName);
            return true;
        }

        /// <summary>Выход из приложения. Всегда с подтверждением, с дублем — с предупреждением.</summary>
        public bool TryQuit()
        {
            if (IsDirty)
                return AskUnsaved(null, resetSession: true);

            var dialog = ResolveDialog();
            if (dialog == null)
            {
                QuitNow();
                return true;
            }

            dialog.Show(
                Loc.T(LocKeys.ExitTitle),
                Loc.T(LocKeys.ExitMessage),
                Loc.T(LocKeys.MenuExit),
                QuitNow,
                null,
                null,
                Loc.T(LocKeys.ExitCancel));

            return false;
        }

        /// <summary>Новый уровень. Если в памяти есть дубль — сначала спрашиваем.</summary>
        public bool TryStartNewLevel()
        {
            if (IsDirty)
                return AskUnsaved(SongSelectionScene, resetSession: true);

            ResetSession();
            SceneLoader.Instance.LoadScene(SongSelectionScene);
            return true;
        }

        /// <summary>
        /// Диалог «есть несохранённые данные»: выйти без сохранения / экспортировать и выйти / отмена.
        /// </summary>
        private bool AskUnsaved(string sceneName, bool resetSession)
        {
            var dialog = ResolveDialog();
            if (dialog == null)
            {
                Debug.LogWarning("[AppFlow] нет ConfirmDialog в сцене — ухожу без предупреждения");
                FinishTake(resetSession);
                GoTo(sceneName);
                return true;
            }

            dialog.Show(
                Loc.T(LocKeys.ExitUnsavedTitle),
                Loc.T(LocKeys.ExitUnsavedMessage, UnsavedHitCount),
                Loc.T(LocKeys.ExitLeave),
                () =>
                {
                    FinishTake(resetSession);
                    GoTo(sceneName);
                },
                Loc.T(LocKeys.ExitSaveAndLeave),
                () => SaveThen(_ =>
                {
                    FinishTake(resetSession);
                    GoTo(sceneName);
                }),
                Loc.T(LocKeys.ExitCancel));

            return false;
        }

        /// <summary>
        /// Завершает работу с дублем: либо полностью чистит сессию (новый уровень/выход
        /// в меню), либо только стирает удары, оставляя выбранную песню.
        /// </summary>
        private void FinishTake(bool resetSession)
        {
            if (resetSession)
                ResetSession();
            else
                DiscardTake();
        }

        /// <summary>Подтверждённый выход без сохранения: дубль стирается, чтобы не всплыть позже.</summary>
        private void DiscardTake()
        {
            MarkSaved();
            RecordSession.Instance.ClearHits();
        }

        private void GoTo(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                QuitNow();
            else
                SceneLoader.Instance.LoadScene(sceneName);
        }

        private static void QuitNow()
        {
            Debug.Log("[AppFlow] выход из приложения");
            Application.Quit();
        }

        // ---------------------------------------------------------------- экспорт

        /// <summary>
        /// Экспортирует текущий дубль. При ошибке показывает диалог с причиной.
        /// onSuccess вызывается только при успешном экспорте (с результатом).
        /// </summary>
        public void SaveThen(Action<LevelExporter.ExportResult> onSuccess)
        {
            StartCoroutine(SaveRoutine(onSuccess));
        }

        /// <summary>
        /// Экспорт в корутине: сначала даём отрисоваться диалогу «Экспортируем…»,
        /// потому что OGG-кодирование может занять несколько секунд и блокирует кадр.
        /// </summary>
        private IEnumerator SaveRoutine(Action<LevelExporter.ExportResult> onSuccess)
        {
            var exporter = ResolveExporter();
            var dialog = ResolveDialog();

            if (exporter == null)
            {
                ShowError(Loc.T(LocKeys.ExportFailed, "LevelExporter not found"));
                yield break;
            }

            if (UnsavedHitCount == 0)
            {
                ShowError(Loc.T(LocKeys.ExportNoHits));
                yield break;
            }

            dialog?.ShowBusy(Loc.T(LocKeys.ExportWorking));

            // кадр на отрисовку busy-диалога
            yield return null;

            LevelExporter.ExportResult result;
            try
            {
                result = exporter.ExportCurrentSession(null);
            }
            catch (Exception e)
            {
                Debug.LogError("[AppFlow] экспорт упал: " + e);
                result = null;
            }

            if (result != null && result.Success)
            {
                MarkSaved();
                dialog?.Hide();
                SfxPlayer.Instance.PlayUi(SfxPlayer.UiSound.Confirm);
                Debug.Log("[AppFlow] экспорт OK: " + result);
                onSuccess?.Invoke(result);
                yield break;
            }

            string reason = result != null && !string.IsNullOrEmpty(result.Message)
                ? result.Message
                : "unknown error";

            Debug.LogError("[AppFlow] экспорт не удался: " + reason);
            SfxPlayer.Instance.PlayUi(SfxPlayer.UiSound.Back);
            ShowError(Loc.T(LocKeys.ExportFailed, reason));
        }

        private void ShowError(string message)
        {
            var dialog = ResolveDialog();
            if (dialog == null)
            {
                Debug.LogError("[AppFlow] " + message);
                return;
            }

            dialog.Show(
                string.Empty,
                message,
                Loc.T(LocKeys.CommonOk),
                dialog.Hide,
                null,
                null,
                null);
        }

        private static ConfirmDialog ResolveDialog()
        {
            if (ConfirmDialog.Instance != null)
                return ConfirmDialog.Instance;

            return UnityEngine.Object.FindFirstObjectByType<ConfirmDialog>(FindObjectsInactive.Include);
        }

        private static LevelExporter ResolveExporter()
        {
            return UnityEngine.Object.FindFirstObjectByType<LevelExporter>(FindObjectsInactive.Include);
        }
    }
}
