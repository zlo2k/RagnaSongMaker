using System;
using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.UI
{
    /// <summary>
    /// Диалоговое окно с сообщением и кнопками.
    /// </summary>
    public class MessageBox : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _messageText;
        [SerializeField] private Button _okButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Text _okButtonText;
        [SerializeField] private Text _cancelButtonText;

        private Action _onOk;
        private Action _onCancel;

        private static MessageBox _instance;

        public static MessageBox Instance
        {
            get
            {
                if (_instance == null)
                {
                    var prefab = Resources.Load<GameObject>("MessageBox");
                    if (prefab != null)
                    {
                        var go = Instantiate(prefab);
                        _instance = go.GetComponent<MessageBox>();
                    }
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

            if (_okButton != null)
                _okButton.onClick.AddListener(OnOk);

            if (_cancelButton != null)
                _cancelButton.onClick.AddListener(OnCancel);

            if (_canvas != null)
                _canvas.gameObject.SetActive(false);
        }

        /// <summary>
        /// Показывает сообщение с кнопкой OK.
        /// </summary>
        public void Show(string title, string message, Action onOk = null)
        {
            Show(title, message, "OK", null, onOk, null);
        }

        /// <summary>
        /// Показывает диалог с двумя кнопками.
        /// </summary>
        public void Show(string title, string message, string okText, string cancelText,
            Action onOk = null, Action onCancel = null)
        {
            if (_titleText != null) _titleText.text = title;
            if (_messageText != null) _messageText.text = message;

            _onOk = onOk;
            _onCancel = onCancel;

            if (_okButtonText != null) _okButtonText.text = okText ?? "OK";
            if (_cancelButtonText != null) _cancelButtonText.text = cancelText ?? "Отмена";

            if (_okButton != null) _okButton.gameObject.SetActive(!string.IsNullOrEmpty(okText));
            if (_cancelButton != null) _cancelButton.gameObject.SetActive(!string.IsNullOrEmpty(cancelText));

            if (_canvas != null) _canvas.gameObject.SetActive(true);
        }

        /// <summary>
        /// Скрывает диалог.
        /// </summary>
        public void Hide()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        private void OnOk()
        {
            _onOk?.Invoke();
            Hide();
        }

        private void OnCancel()
        {
            _onCancel?.Invoke();
            Hide();
        }
    }
}
