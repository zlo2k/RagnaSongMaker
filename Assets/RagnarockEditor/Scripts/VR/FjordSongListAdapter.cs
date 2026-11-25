using System.Collections;
using System.Collections.Generic;
using RagnarockEditor.Audio;
using RagnarockEditor.Core;
using RagnarockEditor.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Наполняет каменную панель FjordScrollList реальными треками из памяти шлема
    /// и связывает выбор строки с этапом BPM.
    ///
    /// Список листается страницами по 4 строки (ровно видимая область панели),
    /// поэтому невидимых строк с коллайдерами не остаётся.
    /// </summary>
    public class FjordSongListAdapter : MonoBehaviour
    {
        [SerializeField] private FjordTrackList _list;
        [SerializeField] private Button _pageUpButton;
        [SerializeField] private Button _pageDownButton;
        [SerializeField] private GameObject _oldCanvas;

        [SerializeField] private int _perPage = 4;

        private readonly List<SongLibrary.Entry> _songs = new List<SongLibrary.Entry>();
        private int _page;
        private bool _subscribed;

        private int MaxPage => _songs.Count == 0 ? 0 : (_songs.Count - 1) / Mathf.Max(1, _perPage);

        private IEnumerator Start()
        {
            Loc.Initialize();

            // Старый uGUI-список прячем: его заменяет каменная панель
            if (_oldCanvas != null)
                _oldCanvas.SetActive(false);

            if (_pageUpButton != null)
                _pageUpButton.onClick.AddListener(PreviousPage);

            if (_pageDownButton != null)
                _pageDownButton.onClick.AddListener(NextPage);

            Loc.OnLanguageChanged += UpdateStatus;

            if (!MediaPermission.IsGranted)
            {
                SetStatus(Loc.T(LocKeys.SongsPermissionWait));
                MediaPermission.Request();

                float started = Time.realtimeSinceStartup;
                while (!MediaPermission.IsGranted && Time.realtimeSinceStartup - started < 30f)
                {
                    SetStatus(Loc.T(LocKeys.SongsPermissionHint));
                    yield return new WaitForSeconds(0.5f);
                }
            }

            Rescan();
        }

        private void OnDestroy()
        {
            Loc.OnLanguageChanged -= UpdateStatus;

            if (_pageUpButton != null)
                _pageUpButton.onClick.RemoveListener(PreviousPage);

            if (_pageDownButton != null)
                _pageDownButton.onClick.RemoveListener(NextPage);

            if (_list != null)
                _list.onSelected.RemoveListener(OnSelected);
        }

        /// <summary>Перечитывает список треков и возвращается на первую страницу.</summary>
        public void Rescan()        {
            _songs.Clear();
            _songs.AddRange(SongLibrary.Scan());
            _page = 0;

            if (!_subscribed && _list != null)
            {
                _list.onSelected.AddListener(OnSelected);
                _subscribed = true;
            }

            ShowPage(0);
            Debug.Log($"[FjordList] треков: {_songs.Count}");
        }

        private void ShowPage(int page)
        {
            if (_list == null)
                return;

            _page = Mathf.Clamp(page, 0, MaxPage);

            var names = new List<string>();
            int start = _page * _perPage;

            for (int i = 0; i < _perPage && start + i < _songs.Count; i++)
                names.Add(_songs[start + i].Name);

            _list.SetTracks(names);
            AttachRowColliders();
            UpdateStatus();
        }

        /// <summary>
        /// Строки панели — uGUI-кнопки, а луч руки работает по физическим коллайдерам.
        /// Добавляем каждой строке BoxCollider по её прямоугольнику.
        /// </summary>
        private void AttachRowColliders()
        {
            if (_list == null || _list.content == null)
                return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_list.content);

            var rows = _list.content.GetComponentsInChildren<Button>(true);

            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                var rect = row.GetComponent<RectTransform>();
                if (rect == null)
                    continue;

                var box = row.GetComponent<BoxCollider>();
                if (box == null)
                    box = row.gameObject.AddComponent<BoxCollider>();

                box.isTrigger = true;
                box.size = new Vector3(rect.rect.width, rect.rect.height, 20f);

                if (row.GetComponent<SfxButton>() == null)
                    row.gameObject.AddComponent<SfxButton>();
            }
        }

        private void OnSelected(int index)
        {
            int globalIndex = _page * _perPage + index;

            if (globalIndex < 0 || globalIndex >= _songs.Count)
                return;

            var entry = _songs[globalIndex];
            SongSelectionState.Select(entry.Path, entry.Name);
            Debug.Log("[FjordList] выбран: " + entry.Path);

            var flow = FindFirstObjectByType<BpmFlowController>(FindObjectsInactive.Include);
            if (flow != null)
            {
                flow.StartAnalysis(entry.Path, entry.Name);
                return;
            }

            AppFlow.Instance.TryLeaveTo(AppFlow.RecordScene);
        }

        private void PreviousPage()
        {
            if (_page <= 0)
                return;

            ShowPage(_page - 1);
        }

        private void NextPage()
        {
            if (_page >= MaxPage)
                return;

            ShowPage(_page + 1);
        }

        /// <summary>Обновляет строку статуса (например, после возврата с экрана BPM).</summary>
        public void RefreshStatus() => UpdateStatus();

        private void UpdateStatus()        {
            if (_songs.Count == 0)
            {
                SetStatus(Loc.T(LocKeys.SongsEmpty, SongLibrary.PreferredFolder));
                return;
            }

            int pages = MaxPage + 1;
            SetStatus($"{_page + 1}/{pages}   ·   {Loc.T(LocKeys.SongsCount, _songs.Count)}");
        }

        private void SetStatus(string text)
        {
            if (_list != null && _list.status != null)
                _list.status.text = text;
        }
    }
}
