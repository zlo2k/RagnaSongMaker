using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.Localization
{
    /// <summary>
    /// Ставит текст из таблицы локализации в uGUI-Text и обновляет его при смене языка.
    /// Динамические строки (счётчики, время) ставятся кодом через Loc.T(key, args).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public class LocalizedText : MonoBehaviour
    {
        [SerializeField] private string _key;

        private Text _text;

        public string Key => _key;

        private void Awake()
        {
            _text = GetComponent<Text>();
        }

        private void OnEnable()
        {
            Loc.OnLanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            Loc.OnLanguageChanged -= Refresh;
        }

        /// <summary>Меняет ключ и сразу перерисовывает текст.</summary>
        public void SetKey(string key)
        {
            _key = key;
            Refresh();
        }

        /// <summary>Перерисовывает текст текущим языком.</summary>
        public void Refresh()
        {
            if (_text == null)
                _text = GetComponent<Text>();

            if (_text == null || string.IsNullOrEmpty(_key))
                return;

            _text.text = Loc.T(_key);
        }

        /// <summary>Ставит динамическую строку по ключу с аргументами (не подписывается на смену языка).</summary>
        public void SetFormatted(string key, params object[] args)
        {
            if (_text == null)
                _text = GetComponent<Text>();

            if (_text != null)
                _text.text = Loc.T(key, args);
        }
    }
}
