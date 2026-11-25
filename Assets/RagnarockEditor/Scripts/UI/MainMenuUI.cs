using UnityEngine;
using UnityEngine.UI;
using RagnarockEditor.Core;

namespace RagnarockEditor.UI
{
    /// <summary>
    /// Главное меню приложения.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("Кнопки")]
        [SerializeField] private Button _newLevelButton;
        [SerializeField] private Button _editLevelButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _quitButton;

        [Header("Панели")]
        [SerializeField] private GameObject _settingsPanel;

        private void Start()
        {
            if (_newLevelButton != null)
                _newLevelButton.onClick.AddListener(OnNewLevel);

            if (_editLevelButton != null)
                _editLevelButton.onClick.AddListener(OnEditLevel);

            if (_settingsButton != null)
                _settingsButton.onClick.AddListener(OnSettings);

            if (_quitButton != null)
                _quitButton.onClick.AddListener(OnQuit);

            if (_settingsPanel != null)
                _settingsPanel.SetActive(false);
        }

        private void OnNewLevel()
        {
            Debug.Log("[MainMenuUI] Новый уровень");
            GameManager.Instance?.StartNewLevel();
        }

        private void OnEditLevel()
        {
            Debug.Log("[MainMenuUI] Редактировать уровень — TODO");
        }

        private void OnSettings()
        {
            if (_settingsPanel != null)
                _settingsPanel.SetActive(!_settingsPanel.activeSelf);
        }

        private void OnQuit()
        {
            Debug.Log("[MainMenuUI] Выход");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
