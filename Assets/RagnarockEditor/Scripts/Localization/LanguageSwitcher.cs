using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.Localization
{
    /// <summary>
    /// Переключатель языка для VR: «◀ Русский ▶». Крутит каталог языков по кругу,
    /// показывает родное название и сохраняет выбор в PlayerPrefs.
    /// </summary>
    public class LanguageSwitcher : MonoBehaviour
    {
        [SerializeField] private Text _label;
        [SerializeField] private Button _previousButton;
        [SerializeField] private Button _nextButton;

        private void Start()
        {
            Loc.Initialize();

            if (_previousButton != null)
                _previousButton.onClick.AddListener(PreviousLanguage);

            if (_nextButton != null)
                _nextButton.onClick.AddListener(NextLanguage);

            Loc.OnLanguageChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            Loc.OnLanguageChanged -= Refresh;

            if (_previousButton != null)
                _previousButton.onClick.RemoveListener(PreviousLanguage);

            if (_nextButton != null)
                _nextButton.onClick.RemoveListener(NextLanguage);
        }

        public void NextLanguage() => Loc.NextLanguage();

        public void PreviousLanguage() => Loc.PreviousLanguage();

        private void Refresh()
        {
            if (_label != null)
                _label.text = Loc.CurrentNativeName;
        }
    }
}
