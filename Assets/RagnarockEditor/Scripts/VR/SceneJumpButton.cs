using UnityEngine;
using UnityEngine.UI;
using RagnarockEditor.Core;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Кнопка перехода в другую сцену (или выхода) — без зависимости от GameManager/EventSystem.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class SceneJumpButton : MonoBehaviour
    {
        [SerializeField] private string _sceneName = "MainMenuScene";
        [SerializeField] private bool _quit;

        private void Start()
        {
            var button = GetComponent<Button>();
            if (button != null)
                button.onClick.AddListener(OnClick);
        }

        private void OnClick()
        {
            if (_quit)
            {
                Debug.Log("[UI] quit");
                Application.Quit();
                return;
            }

            Debug.Log("[UI] jump to " + _sceneName);
            SceneLoader.Instance.LoadScene(_sceneName);
        }
    }
}
