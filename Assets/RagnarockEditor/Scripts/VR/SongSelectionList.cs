using System.Collections;
using System.Collections.Generic;
using RagnarockEditor.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Строит список треков из памяти шлема и листает страницы.
    /// Кнопки создаются клонированием шаблона из сцены (шрифт и материалы берутся из него).
    /// </summary>
    public class SongSelectionList : MonoBehaviour
    {
        [SerializeField] private RectTransform _container;
        [SerializeField] private GameObject _template;
        [SerializeField] private Text _statusLabel;
        [SerializeField] private Text _pageLabel;
        [SerializeField] private Button _prevButton;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _grantButton;
        [SerializeField] private int _perPage = 5;

        private readonly List<SongLibrary.Entry> _songs = new List<SongLibrary.Entry>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private int _page;

        private string NoSongsText =>
            Loc.T(LocKeys.SongsEmpty, SongLibrary.PreferredFolder);

        private int MaxPage => _songs.Count == 0 ? 0 : (_songs.Count - 1) / _perPage;

        private IEnumerator Start()
        {
            Loc.Initialize();

            if (_template != null)
                _template.SetActive(false);

            if (_prevButton != null)
                _prevButton.onClick.AddListener(() => ChangePage(-1));
            if (_nextButton != null)
                _nextButton.onClick.AddListener(() => ChangePage(1));
            if (_grantButton != null)
                _grantButton.onClick.AddListener(OnGrantClicked);

            Loc.OnLanguageChanged += Refresh;
            Refresh();

            yield return EnsurePermission();
            Rescan();
        }

        private void OnDestroy()
        {
            Loc.OnLanguageChanged -= Refresh;
        }

        private void OnGrantClicked()
        {
            MediaPermission.Request();
            StartCoroutine(WaitAndRescan());
        }

        private IEnumerator WaitAndRescan()
        {
            var started = Time.realtimeSinceStartup;
            while (!MediaPermission.IsGranted && Time.realtimeSinceStartup - started < 30f)
                yield return new WaitForSeconds(0.4f);

            Rescan();
        }

        private IEnumerator EnsurePermission()
        {
            if (MediaPermission.IsGranted)
                yield break;

            if (_statusLabel != null)
                _statusLabel.text = Loc.T(LocKeys.SongsPermissionWait);

            MediaPermission.Request();

            var started = Time.realtimeSinceStartup;
            while (!MediaPermission.IsGranted && Time.realtimeSinceStartup - started < 45f)
            {
                if (_statusLabel != null)
                    _statusLabel.text = Loc.T(LocKeys.SongsPermissionHint);
                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log("[Songs] разрешение получено: " + MediaPermission.IsGranted);
        }

        /// <summary>Перерисовывает список и статус (например, после возврата с экрана BPM).</summary>
        public void RefreshStatus() => Refresh();

        public void Rescan()
        {
            _songs.Clear();
            _songs.AddRange(SongLibrary.Scan());
            _page = 0;
            Refresh();
        }

        private void ChangePage(int delta)
        {
            _page = Mathf.Clamp(_page + delta, 0, MaxPage);
            Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                    Destroy(_spawned[i]);
            }
            _spawned.Clear();

            if (_statusLabel != null)
                _statusLabel.text = _songs.Count == 0 ? NoSongsText : Loc.T(LocKeys.SongsCount, _songs.Count);

            if (_pageLabel != null)
                _pageLabel.text = _songs.Count == 0
                    ? string.Empty
                    : Loc.T(LocKeys.SongsPage, _page + 1, MaxPage + 1);

            if (_prevButton != null)
                _prevButton.interactable = _page > 0;
            if (_nextButton != null)
                _nextButton.interactable = _page < MaxPage;

            if (_grantButton != null)
                _grantButton.gameObject.SetActive(!MediaPermission.IsGranted);

            if (_template == null || _container == null)
                return;

            var start = _page * _perPage;
            for (int i = 0; i < _perPage && start + i < _songs.Count; i++)
            {
                var entry = _songs[start + i];

                var go = Instantiate(_template, _container);
                go.name = "Song_" + i;
                go.SetActive(true);

                var rect = go.GetComponent<RectTransform>();
                if (rect != null)
                    rect.anchoredPosition = new Vector2(0f, 140f - i * 74f);

                var button = go.GetComponent<SongButton>();
                if (button == null)
                    button = go.AddComponent<SongButton>();
                button.Bind(entry.Name, entry.Path);

                _spawned.Add(go);
            }
        }
    }
}
