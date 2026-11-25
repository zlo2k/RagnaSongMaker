using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RagnarockEditor.Core;
using RagnarockEditor.Data;
using RagnarockEditor.FileSystem;
using RagnarockEditor.Audio;

namespace RagnarockEditor.UI
{
    /// <summary>
    /// UI выбора песни: список файлов, загрузка, анализ BPM.
    /// </summary>
    public class SongSelectionUI : MonoBehaviour
    {
        [Header("Список песен")]
        [SerializeField] private Transform _songListParent;
        [SerializeField] private GameObject _songItemPrefab;
        [SerializeField] private InputField _searchInput;
        [SerializeField] private Button _scanButton;

        [Header("Загрузка")]
        [SerializeField] private Slider _loadProgress;
        [SerializeField] private Text _statusText;
        [SerializeField] private Button _continueButton;

        [Header("Компоненты")]
        [SerializeField] private AudioLoader _audioLoader;

        private List<FileBrowser.AudioFileInfo> _foundFiles;
        private string _selectedFilePath;

        private void Start()
        {
            if (_scanButton != null)
                _scanButton.onClick.AddListener(ScanForSongs);

            if (_searchInput != null)
                _searchInput.onValueChanged.AddListener(OnSearchChanged);

            if (_continueButton != null)
            {
                _continueButton.onClick.AddListener(OnContinue);
                _continueButton.interactable = false;
            }

            if (_audioLoader != null)
            {
                _audioLoader.OnAudioLoaded += OnAudioLoaded;
                _audioLoader.OnLoadError += OnLoadError;
            }

            ScanForSongs();
        }

        private void ScanForSongs()
        {
            var paths = FileBrowser.GetDefaultSearchPaths();
            _foundFiles = new List<FileBrowser.AudioFileInfo>();

            foreach (var path in paths)
            {
                _foundFiles.AddRange(FileBrowser.ScanDirectory(path));
            }

            RefreshSongList();
            if (_statusText != null)
                _statusText.text = $"Найдено песен: {_foundFiles.Count}";
        }

        private void RefreshSongList()
        {
            if (_songListParent == null || _songItemPrefab == null) return;

            // Очищаем список
            foreach (Transform child in _songListParent)
                Destroy(child.gameObject);

            var displayList = string.IsNullOrWhiteSpace(_searchInput?.text)
                ? _foundFiles
                : FileBrowser.FilterBySearch(_foundFiles, _searchInput.text);

            foreach (var file in displayList)
            {
                GameObject item = Instantiate(_songItemPrefab, _songListParent);
                var text = item.GetComponentInChildren<Text>();
                if (text != null) text.text = file.FileName;

                var button = item.GetComponent<Button>();
                if (button != null)
                {
                    string path = file.FullPath;
                    button.onClick.AddListener(() => SelectSong(path));
                }
            }
        }

        private void SelectSong(string filePath)
        {
            _selectedFilePath = filePath;
            if (_statusText != null)
                _statusText.text = $"Загрузка: {System.IO.Path.GetFileName(filePath)}...";

            _audioLoader?.LoadAudio(filePath);
        }

        private void OnAudioLoaded(AudioClip clip)
        {
            if (_statusText != null)
                _statusText.text = $"Загружено: {clip.name}. Анализ BPM...";

            // Анализируем BPM
            var result = BPMDetector.Analyze(clip);

            // Сохраняем в сессию
            var session = RecordSession.Instance;
            session.Song = new SongData
            {
                FilePath = _selectedFilePath,
                SongName = System.IO.Path.GetFileNameWithoutExtension(_selectedFilePath),
                AuthorName = "Unknown",
                BPM = result.PrimaryBPM,
                DurationSeconds = clip.length
            };

            if (_statusText != null)
                _statusText.text = $"BPM: {result.PrimaryBPM:F1} (уверенность: {result.Confidence:P0})";

            if (_continueButton != null)
                _continueButton.interactable = true;

            // Переходим к редактору BPM
            FindObjectOfType<BPMEditorUI>()?.ShowBPMResult(result);
        }

        private void OnLoadError(string error)
        {
            if (_statusText != null)
                _statusText.text = $"Ошибка: {error}";
        }

        private void OnSearchChanged(string text)
        {
            RefreshSongList();
        }

        private void OnContinue()
        {
            GameManager.Instance?.OnSongAnalyzed(RecordSession.Instance.Song);
        }

        private void OnDestroy()
        {
            if (_audioLoader != null)
            {
                _audioLoader.OnAudioLoaded -= OnAudioLoaded;
                _audioLoader.OnLoadError -= OnLoadError;
            }
        }
    }
}
