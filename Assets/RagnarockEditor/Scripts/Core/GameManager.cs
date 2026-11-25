using UnityEngine;
using RagnarockEditor.Data;

namespace RagnarockEditor.Core
{
    /// <summary>
    /// Главный оркестратор приложения. Управляет потоком сцен и состоянием.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public enum AppState
        {
            MainMenu,
            SelectingSong,
            AnalyzingBPM,
            Calibrating,
            Recording,
            Previewing,
            Exporting
        }

        [Header("Конфигурация")]
        [SerializeField] private AppConfig _config;

        public AppState CurrentState { get; private set; } = AppState.MainMenu;
        public RecordSession Session => RecordSession.Instance;

        private static GameManager _instance;
        public static GameManager Instance => _instance;

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
        /// Начинает создание нового уровня: выбор песни.
        /// </summary>
        public void StartNewLevel()
        {
            SetState(AppState.SelectingSong);
            SceneLoader.Instance.LoadScene(SceneLoader.Scene.SongSelection);
        }

        /// <summary>
        /// Песня выбрана и проанализирована — переход к калибровке.
        /// </summary>
        public void OnSongAnalyzed(SongData song)
        {
            Session.Song = song;
            Session.Clear();
            SetState(AppState.Calibrating);
            SceneLoader.Instance.LoadScene(SceneLoader.Scene.Record);
        }

        /// <summary>
        /// Калибровка завершена — начинаем запись.
        /// </summary>
        public void StartRecording()
        {
            SetState(AppState.Recording);
        }

        /// <summary>
        /// Запись завершена — переходим к предпросмотру.
        /// </summary>
        public void OnRecordingComplete()
        {
            SetState(AppState.Previewing);
            SceneLoader.Instance.LoadScene(SceneLoader.Scene.Preview);
        }

        /// <summary>
        /// Экспорт уровня.
        /// </summary>
        public void ExportLevel()
        {
            SetState(AppState.Exporting);
            // Экспорт запускается через CustomSongsExporter
        }

        /// <summary>
        /// Возврат в главное меню.
        /// </summary>
        public void ReturnToMainMenu()
        {
            SetState(AppState.MainMenu);
            Session.Clear();
            SceneLoader.Instance.LoadScene(SceneLoader.Scene.MainMenu);
        }

        private void SetState(AppState newState)
        {
            Debug.Log($"[GameManager] Состояние: {CurrentState} → {newState}");
            CurrentState = newState;
        }
    }
}
