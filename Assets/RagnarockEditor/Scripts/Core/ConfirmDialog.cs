using System;
using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.Core
{
    /// <summary>
    /// Модальный диалог подтверждения (выход, несохранённые данные, ошибки экспорта).
    ///
    /// Живёт на отдельном world-space канвасе, который ставится ближе остальных панелей,
    /// поэтому луч руки попадает в его фон-блокер и не «продавливает» кнопки за диалогом.
    /// Тексты приходят уже локализованными — диалог не знает про ключи.
    /// </summary>
    public class ConfirmDialog : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private GameObject _titleObject;
        [SerializeField] private Text _title;
        [SerializeField] private Text _message;

        [Header("Кнопки")]
        [SerializeField] private Button _primaryButton;
        [SerializeField] private Text _primaryLabel;
        [SerializeField] private Button _secondaryButton;
        [SerializeField] private Text _secondaryLabel;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Text _cancelLabel;

        private Action _onPrimary;
        private Action _onSecondary;

        /// <summary>Диалог текущей сцены (для роутера и блокировки лучей).</summary>
        public static ConfirmDialog Instance { get; private set; }

        /// <summary>Диалог сейчас на экране.</summary>
        public bool IsOpen => _root != null && _root.activeSelf;

        private void Awake()
        {
            Instance = this;

            if (_primaryButton != null)
                _primaryButton.onClick.AddListener(Confirm);

            if (_secondaryButton != null)
                _secondaryButton.onClick.AddListener(ConfirmSecondary);

            if (_cancelButton != null)
                _cancelButton.onClick.AddListener(Hide);

            Hide();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Показывает диалог. primary — основное действие, secondary — необязательное
        /// («Сохранить и выйти»), cancel — отмена. Пустая строка скрывает кнопку.
        /// </summary>
        public void Show(string title, string message, string primaryText, Action onPrimary,
            string secondaryText = null, Action onSecondary = null, string cancelText = null)
        {
            if (_root == null)
                return;

            _onPrimary = onPrimary;
            _onSecondary = onSecondary;

            _root.SetActive(true);
            transform.SetAsLastSibling();

            if (_title != null)
                _title.text = title ?? string.Empty;

            if (_titleObject != null)
                _titleObject.SetActive(!string.IsNullOrEmpty(title));

            if (_message != null)
                _message.text = message ?? string.Empty;

            ApplyButton(_primaryButton, _primaryLabel, primaryText);
            ApplyButton(_secondaryButton, _secondaryLabel, secondaryText);
            ApplyButton(_cancelButton, _cancelLabel, cancelText);

            Debug.Log("[Dialog] " + (title ?? string.Empty) + " | " + (message ?? string.Empty));
        }

        /// <summary>Диалог «занято»: сообщение без кнопок (например, на время экспорта).</summary>
        public void ShowBusy(string message)
        {
            if (_root == null)
                return;

            _onPrimary = null;
            _onSecondary = null;

            _root.SetActive(true);
            transform.SetAsLastSibling();

            if (_titleObject != null)
                _titleObject.SetActive(false);

            if (_message != null)
                _message.text = message ?? string.Empty;

            ApplyButton(_primaryButton, _primaryLabel, null);
            ApplyButton(_secondaryButton, _secondaryLabel, null);
            ApplyButton(_cancelButton, _cancelLabel, null);
        }

        /// <summary>Скрывает диалог и сбрасывает отложенные действия.</summary>
        public void Hide()
        {
            _onPrimary = null;
            _onSecondary = null;

            if (_root != null)
                _root.SetActive(false);
        }

        /// <summary>Основная кнопка.</summary>
        public void Confirm()
        {
            var action = _onPrimary;
            Hide();
            action?.Invoke();
        }

        /// <summary>Дополнительная кнопка («Сохранить и выйти»).</summary>
        public void ConfirmSecondary()
        {
            var action = _onSecondary;
            Hide();
            action?.Invoke();
        }

        private static void ApplyButton(Button button, Text label, string text)
        {
            if (button == null)
                return;

            bool visible = !string.IsNullOrEmpty(text);
            button.gameObject.SetActive(visible);

            if (visible && label != null)
                label.text = text;
        }
    }
}
