using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.VR
{
    /// <summary>Кнопка одного трека в списке выбора песни.</summary>
    [RequireComponent(typeof(Button))]
    public class SongButton : MonoBehaviour
    {
        [SerializeField] private Text _label;

        private string _path;
        private string _name;

        public void Bind(string songName, string path)
        {
            _name = songName;
            _path = path;

            if (_label == null)
                _label = GetComponentInChildren<Text>(true);

            if (_label != null)
                _label.text = songName;
        }

        private void Start()
        {
            var button = GetComponent<Button>();
            if (button != null)
                button.onClick.AddListener(OnClick);
        }

        private void OnClick()
        {
            if (string.IsNullOrEmpty(_path))
                return;

            // M1: если в сцене есть оркестратор BPM — сначала анализ и подтверждение BPM.
            // В старых сценах могло остаться несколько компонентов: берём только тот,
            // у которого реально назначен BPM-канвас.
            var flows = FindObjectsByType<BpmFlowController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            BpmFlowController flow = null;
            foreach (var candidate in flows)
            {
                if (candidate != null && candidate.IsWiredToBpmCanvas)
                {
                    flow = candidate;
                    break;
                }
            }

            if (flow == null && flows.Length > 0)
                flow = flows[0];

            if (flow != null)
            {
                SongSelectionState.Select(_path, _name);
                Debug.Log("[Songs] выбран (этап BPM): " + _path);
                flow.StartAnalysis(_path, _name);
                return;
            }

            // прежнее поведение: сразу в запись, без BPM-этапа
            SongSelectionState.Select(_path, _name);
            Debug.Log("[Songs] выбран: " + _path);

            Core.SceneLoader.Instance.LoadScene("RecordScene");
        }
    }
}
