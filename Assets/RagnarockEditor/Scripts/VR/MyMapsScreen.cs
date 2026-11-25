using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RagnarockEditor.Core;
using RagnarockEditor.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.VR
{
    public class MyMapsScreen : MonoBehaviour
    {
        private const int PageSize = 4;
        private GameObject _menu, _panel;
        private RectTransform _content;
        private Button _previous, _next;
        private Text _status;
        private readonly List<string> _maps = new List<string>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private int _page;
        private bool _busy;
        private int _visit;

        public void Initialize(GameObject menu, Transform intro)
        {
            _menu = menu;
            var entry = MakeCanvas(menu.transform, "Maps menu", new Vector3(0, 1.56f, 1.2f));
            WorldPanel.Button(entry, "maps.title", 0, 0, 250, Open);

            _content = MakeCanvas(intro.parent, "My maps", intro.localPosition);
            _content.localRotation = intro.localRotation;
            _content.localScale = intro.localScale;
            _panel = _content.gameObject;
            WorldPanel.Plate(_content, new Color(.23f, .18f, .14f));
            var title = WorldPanel.Label(WorldPanel.Rect(_content, "Title", 0, 177, 640, 50), 34);
            title.gameObject.AddComponent<LocalizedText>().SetKey("maps.title");
            _status = WorldPanel.Label(WorldPanel.Rect(_content, "Status", 0, -115, 660, 44), 20);
            _previous = WorldPanel.Button(_content, "maps.previous", -225, -176, 170, () => Page(-1));
            _next = WorldPanel.Button(_content, "maps.next", 225, -176, 170, () => Page(1));
            WorldPanel.Button(_content, "bpm.back", 0, -176, 170, () => Close());
            _panel.SetActive(false);
        }

        private static RectTransform MakeCanvas(Transform parent, string name, Vector3 position)
        {
            var rect = WorldPanel.Rect(parent, name, 0, 0, 720, 440);
            var canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            rect.sizeDelta = new Vector2(720, 440);
            rect.localPosition = position;
            rect.localScale = Vector3.one * .0011f;
            rect.gameObject.AddComponent<GraphicRaycaster>();
            return rect;
        }

        private void Open()
        {
            _maps.Clear();
            var roots = new HashSet<string> { ExportSettings.RootPath };
            for (int i = 0; i < ExportSettings.PresetCount; i++) roots.Add(ExportSettings.PresetPath(i));
            foreach (var root in roots)
            {
                try
                {
                    if (Directory.Exists(root))
                        _maps.AddRange(Directory.GetDirectories(root).Where(dir => File.Exists(Path.Combine(dir, "info.dat"))));
                }
                catch (IOException e) { Debug.LogWarning(e.Message); }
                catch (UnauthorizedAccessException e) { Debug.LogWarning(e.Message); }
            }
            _maps.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(Path.GetFileName(a), Path.GetFileName(b)));
            _menu.SetActive(false);
            _panel.SetActive(true);
            _page = 0;
            RenderPage();
        }

        public bool Close()
        {
            if (!_panel || !_panel.activeSelf) return false;
            _visit++;
            _panel.SetActive(false);
            _menu.SetActive(true);
            return true;
        }

        private void Page(int delta)
        {
            _page += delta;
            RenderPage();
        }

        private void RenderPage()
        {
            foreach (var row in _rows) { row.SetActive(false); Destroy(row); }
            _rows.Clear();
            int first = _page * PageSize;
            for (int i = first; i < Mathf.Min(first + PageSize, _maps.Count); i++)
            {
                string folder = _maps[i];
                var row = WorldPanel.Rect(_content, "Map", 0, 116 - (i - first) * 57, 660, 52);
                _rows.Add(row.gameObject);
                var label = WorldPanel.Label(WorldPanel.Rect(row, "Name", -91, 0, 460, 50), 25);
                label.alignment = TextAnchor.MiddleLeft;
                label.text = Path.GetFileName(folder);
                WorldPanel.Button(row, "maps.share", 235, 0, 182, () => Share(folder));
            }
            _previous.interactable = _page > 0;
            _next.interactable = first + PageSize < _maps.Count;
            _status.text = _maps.Count == 0 ? Loc.T("maps.empty") : $"{_page + 1} / {(_maps.Count + PageSize - 1) / PageSize}";
        }

        private async void Share(string folder)
        {
            if (_busy) return;
            _busy = true;
            int visit = _visit;
            _status.text = Loc.T("maps.packing");
            try
            {
                string root = MapArchive.ShareRoot();
                string zip = await Task.Run(() => MapArchive.Create(folder, root));
                if (!this || visit != _visit) return;
                MapArchive.Share(zip);
                _status.text = string.Empty;
            }
            catch (Exception e)
            {
                if (this && visit == _visit) _status.text = Loc.T("maps.error", e.Message);
            }
            finally { _busy = false; }
        }
    }
}
