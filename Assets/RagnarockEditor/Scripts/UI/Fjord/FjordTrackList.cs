using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class FjordTrackList : MonoBehaviour
{
    public ScrollRect scroll;
    public RectTransform content;
    public Button rowTemplate;
    public Text status;
    public List<string> tracks = new List<string>();
    public UnityEvent<int> onSelected = new UnityEvent<int>();
    public int SelectedIndex { get; private set; } = -1;
    readonly List<Button> rows = new List<Button>();
    static readonly Color Brown = new Color32(89,61,50,255), Jade = new Color32(67,159,145,255);
    void Start() { if (!built) Refresh(); }
    bool built;
    public void SetTracks(IEnumerable<string> names)
    {
        tracks = names == null ? new List<string>() : new List<string>(names);
        Refresh();
    }
    void Refresh()
    {
        built = true;
        foreach (var row in rows) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
        rows.Clear(); SelectedIndex=-1;
        for (int i=0; i<tracks.Count; i++)
        {
            int index=i;
            Button row=Instantiate(rowTemplate,content);
            row.gameObject.SetActive(true);
            row.GetComponentInChildren<Text>().text=tracks[i];
            row.onClick.AddListener(()=>Select(index));
            rows.Add(row);
        }
        status.text=tracks.Count==0 ? "Нет треков" : "Треков: " + tracks.Count;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        scroll.StopMovement(); scroll.verticalNormalizedPosition=1;
    }
    public void Select(int index)
    {
        if(index<0 || index>=rows.Count) return;
        if(SelectedIndex>=0) rows[SelectedIndex].targetGraphic.color=Brown;
        SelectedIndex=index;
        rows[index].targetGraphic.color=Jade;
        status.text=(index+1)+" / "+rows.Count;
        onSelected.Invoke(index);
    }
}
