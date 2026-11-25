using RagnarockEditor.Audio;
using RagnarockEditor.Core;
using RagnarockEditor.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Строка «Папка: …» в предпросмотре: нажатие переключает готовые папки экспорта,
    /// ниже показывается полный путь. Выбор сохраняется в PlayerPrefs.
    /// </summary>
    public class ExportFolderSelector : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private Text _label;
        [SerializeField] private Text _pathLabel;

        private void Start()
        {
            Loc.Initialize();

            if (_button != null)
                _button.onClick.AddListener(NextFolder);

            Loc.OnLanguageChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            Loc.OnLanguageChanged -= Refresh;

            if (_button != null)
                _button.onClick.RemoveListener(NextFolder);
        }

        /// <summary>Переключает папку экспорта на следующую готовую.</summary>
        public void NextFolder()
        {
            ExportSettings.NextPreset();
            SfxPlayer.Instance.PlayUi(SfxPlayer.UiSound.Select);
            Refresh();
            Debug.Log("[Export] папка сохранения: " + ExportSettings.RootPath);
        }

        private void Refresh()
        {
            if (_label != null)
                _label.text = Loc.T(LocKeys.ExportFolder, Loc.T(PresetKey(ExportSettings.CurrentPresetIndex)));

            if (_pathLabel != null)
                _pathLabel.text = ExportSettings.RootPath;
        }

        private static string PresetKey(int index)
        {
            switch (index)
            {
                case 1:
                    return LocKeys.ExportFolderMusic;
                case 2:
                    return LocKeys.ExportFolderDownloads;
                default:
                    return LocKeys.ExportFolderApp;
            }
        }
    }
}
