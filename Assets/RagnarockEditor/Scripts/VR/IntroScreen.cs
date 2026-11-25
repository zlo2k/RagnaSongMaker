using UnityEngine;
using UnityEngine.UI;
using RagnarockEditor.Core;
using RagnarockEditor.Localization;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Стартовый экран: короткое объяснение «что это и как играть», переключатель языка
    /// и кнопка «Начать». Показывается при первом запуске, а дальше открывается
    /// каменной кнопкой Settings / Help.
    ///
    /// На время интро главное меню (Stone UI) спрятано, чтобы на экране был один
    /// понятный следующий шаг.
    /// </summary>
    public class IntroScreen : MonoBehaviour
    {
        [Header("Корни")]
        [SerializeField] private GameObject _introRoot;
        [SerializeField] private GameObject _menuRoot;

        [Header("Кнопки")]
        [SerializeField] private Button _startButton;
        [SerializeField] private LocalizedText _startLabel;

        private bool _openedManually;

        /// <summary>Интро сейчас на экране.</summary>
        public bool IsVisible => _introRoot != null && _introRoot.activeSelf;

        private void Start()
        {
            Loc.Initialize();
            if (_introRoot && _menuRoot)
            {
                var maps = _menuRoot.transform.parent.gameObject.AddComponent<MyMapsScreen>();
                maps.Initialize(_menuRoot, _introRoot.transform);
            }

            if (_startButton != null)
                _startButton.onClick.AddListener(OnStartPressed);

            // Первый запуск — показываем объяснение. Дальше сразу меню.
            Apply(!AppFlow.Instance.TutorialSeen);
        }

        /// <summary>Открыть интро повторно (кнопка «Как пользоваться»).</summary>
        public void ShowIntro()
        {
            _openedManually = true;
            Apply(true);
            Debug.Log("[Intro] открыт повторно");
        }

        /// <summary>Кнопка Back: интро закрывается и не выпускает из приложения.</summary>
        public bool HandleBack()
        {
            if (!IsVisible)
                return false;

            Close();
            return true;
        }

        private void OnStartPressed()
        {
            AppFlow.Instance.TutorialSeen = true;
            Close();
        }

        private void Close()
        {
            _openedManually = false;
            Apply(false);
        }

        private void Apply(bool introVisible)
        {
            if (_introRoot != null)
                _introRoot.SetActive(introVisible);

            if (_menuRoot != null)
                _menuRoot.SetActive(!introVisible);

            if (_startLabel != null)
                _startLabel.SetKey(introVisible && _openedManually
                    ? LocKeys.IntroClose
                    : LocKeys.IntroStart);

            Debug.Log($"[Intro] интро={(introVisible ? "вкл" : "выкл")}, меню={(!introVisible ? "вкл" : "выкл")}");
        }
    }
}
