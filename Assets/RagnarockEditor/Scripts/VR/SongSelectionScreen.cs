using UnityEngine;
using UnityEngine.UI;
using RagnarockEditor.Localization;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Экран выбора песни как маленький автомат: «список» → «BPM» → «список».
    /// Пока трек не выбран, на экране только список; панель BPM появляется поверх
    /// и прячет список, а Back возвращает к списку, не уводя в главное меню.
    ///
    /// Компонент висит на отдельном объекте SongSelectionFlow (не на канвасе),
    /// поэтому остаётся доступным, даже когда сам список скрыт.
    /// </summary>
    public class SongSelectionScreen : MonoBehaviour
    {
        [SerializeField] private GameObject _listRoot;
        [SerializeField] private GameObject _bpmRoot;
        [SerializeField] private Button _bpmBackButton;

        private BpmFlowController _flow;

        /// <summary>Открыт ли редактор BPM.</summary>
        public bool IsBpmOpen => _bpmRoot != null && _bpmRoot.activeSelf;

        private void Start()
        {
            Loc.Initialize();

            _flow = FindFirstObjectByType<BpmFlowController>(FindObjectsInactive.Include);

            if (_bpmBackButton != null)
                _bpmBackButton.onClick.AddListener(CancelBpm);

            ShowList();
        }

        /// <summary>Кнопка Back: на BPM — вернуться к списку, иначе не перехватываем.</summary>
        public bool HandleBack()
        {
            if (!IsBpmOpen)
                return false;

            CancelBpm();
            return true;
        }

        /// <summary>Закрыть BPM и вернуться к списку треков.</summary>
        public void CancelBpm()
        {
            if (_flow != null)
            {
                _flow.Cancel();
            }
            else
            {
                if (_bpmRoot != null)
                    _bpmRoot.SetActive(false);

                if (_listRoot != null)
                    _listRoot.SetActive(true);
            }

            Debug.Log("[Songs] возврат к списку треков");

            // статус на списке мог остаться от анализа — возвращаем «Треков: N»
            var list = FindFirstObjectByType<SongSelectionList>();
            if (list != null)
                list.RefreshStatus();

            var fjord = FindFirstObjectByType<FjordSongListAdapter>();
            if (fjord != null)
                fjord.RefreshStatus();
        }

        private void ShowList()
        {
            if (_bpmRoot != null)
                _bpmRoot.SetActive(false);

            if (_listRoot != null)
                _listRoot.SetActive(true);
        }
    }
}
