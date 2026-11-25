using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RagnarockEditor.Core
{
    /// <summary>
    /// Загрузчик сцен с экраном загрузки.
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        public enum Scene
        {
            MainMenu,
            SongSelection,
            Record,
            Preview
        }

        [Header("Экран загрузки")]
        [SerializeField] private Canvas _loadingCanvas;
        [SerializeField] private Slider _progressBar;
        [SerializeField] private Text _loadingText;

        private static SceneLoader _instance;

        public static SceneLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("SceneLoader");
                    _instance = go.AddComponent<SceneLoader>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

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
        /// Загружает сцену асинхронно.
        /// </summary>
        public void LoadScene(Scene scene)
        {
            StartCoroutine(LoadSceneAsync(scene));
        }

        /// <summary>
        /// Загружает сцену по имени.
        /// </summary>
        public void LoadScene(string sceneName)
        {
            StartCoroutine(LoadSceneAsync(sceneName));
        }

        private IEnumerator LoadSceneAsync(Scene scene)
        {
            return LoadSceneAsync(GetSceneName(scene));
        }

        private IEnumerator LoadSceneAsync(string sceneName)
        {
            if (_loadingCanvas != null)
                _loadingCanvas.gameObject.SetActive(true);

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
            operation.allowSceneActivation = false;

            while (operation.progress < 0.9f)
            {
                if (_progressBar != null)
                    _progressBar.value = operation.progress;

                if (_loadingText != null)
                    _loadingText.text = $"Загрузка... {operation.progress * 100:F0}%";

                yield return null;
            }

            // Минимальная задержка для плавности
            yield return new WaitForSeconds(0.3f);

            operation.allowSceneActivation = true;

            if (_loadingCanvas != null)
                _loadingCanvas.gameObject.SetActive(false);
        }

        private string GetSceneName(Scene scene)
        {
            return scene switch
            {
                Scene.MainMenu => "MainMenuScene",
                Scene.SongSelection => "SongSelectionScene",
                Scene.Record => "RecordScene",
                Scene.Preview => "PreviewScene",
                _ => "MainMenuScene"
            };
        }
    }
}
