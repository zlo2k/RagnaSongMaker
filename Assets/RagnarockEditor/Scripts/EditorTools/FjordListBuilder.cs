using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using RagnarockEditor.Localization;

/// <summary>
/// Собирает каменную панель списка треков (FjordScrollList) в текущей сцене.
/// Адаптер FjordSongListAdapter затем наполняет её реальными файлами.
/// </summary>
public static class FjordListBuilder
{
    static RectTransform Rect(string name, Transform parent)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent, false);
        return r;
    }

    static void Size(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
        r.sizeDelta = new Vector2(w, h);
        r.anchoredPosition = new Vector2(x, y);
    }

    static void Stretch(RectTransform r, float inset = 0)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.one * inset;
        r.offsetMax = -Vector2.one * inset;
    }

    static T Paint<T>(RectTransform r, Color32 color) where T : Graphic
    {
        // без CanvasRenderer графика не рисуется вообще — Unity добавляет его не всегда
        if (r.GetComponent<CanvasRenderer>() == null)
            r.gameObject.AddComponent<CanvasRenderer>();

        var g = r.gameObject.AddComponent<T>();
        g.color = color;
        return g;
    }

    static Text Label(RectTransform r, string s, int px, Color32 color)
    {
        var t = Paint<Text>(r, color);
#if UNITY_2022_2_OR_NEWER
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
        t.text = s;
        t.fontSize = px;
        t.alignment = TextAnchor.MiddleLeft;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 14;
        t.resizeTextMaxSize = px;
        return t;
    }

    [MenuItem("Tools/Fjord/Create Scroll List")]
    public static GameObject Create()
    {
        var existing = Object.FindFirstObjectByType<FjordTrackList>(FindObjectsInactive.Include);
        if (existing != null)
            return existing.gameObject;

        var root = Rect("Fjord Scroll List", null);
        root.sizeDelta = new Vector2(720, 480);
        root.localScale = Vector3.one * .001f;
        root.position = new Vector3(0, 1.4f, 1);

        var canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var xr = System.Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");

        if (xr != null)
            root.gameObject.AddComponent(xr);
        else
            root.gameObject.AddComponent<GraphicRaycaster>();

        var brown = new Color32(89, 61, 50, 255);
        var ivory = new Color32(255, 243, 215, 255);

        var bg = Rect("Stone", root);
        Stretch(bg);
        Paint<StonePlate>(bg, new Color32(207, 135, 59, 255));

        var header = Rect("Title", root);
        Size(header, -10, 180, 620, 54);
        Label(header, Loc.T(LocKeys.SongsTitle), 40, brown);

        var area = Rect("Scroll Area", root);
        Size(area, 0, -7, 660, 266);
        var scroll = area.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 32;

        var view = Rect("Viewport", area);
        Size(view, -21, 0, 610, 266);
        Paint<Image>(view, new Color32(255, 255, 255, 0));
        view.gameObject.AddComponent<RectMask2D>();

        var content = Rect("Content", view);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        content.sizeDelta = Vector2.zero;

        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = view;
        scroll.content = content;

        var rail = Rect("Scrollbar", area);
        Size(rail, 320, 0, 30, 266);
        Paint<StonePlate>(rail, brown);
        var sb = rail.gameObject.AddComponent<Scrollbar>();

        var slide = Rect("Sliding Area", rail);
        Stretch(slide, 2);
        var handle = Rect("Handle", slide);
        Stretch(handle);
        sb.targetGraphic = Paint<StonePlate>(handle, ivory);
        sb.handleRect = handle;
        sb.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = sb;

        var template = Rect("Row Template", root);
        template.gameObject.AddComponent<LayoutElement>().preferredHeight = 59;
        var graphic = Paint<StonePlate>(template, brown);
        var btn = template.gameObject.AddComponent<Button>();
        btn.targetGraphic = graphic;

        var label = Rect("Filename", template);
        Stretch(label, 8);
        label.offsetMin = new Vector2(22, 3);
        Label(label, "", 25, ivory);
        template.gameObject.SetActive(false);

        var foot = Rect("Status", root);
        Size(foot, -10, -187, 620, 36);

        var list = root.gameObject.AddComponent<FjordTrackList>();
        list.status = Label(foot, "", 22, brown);
        list.scroll = scroll;
        list.content = content;
        list.rowTemplate = btn;
        // треки заполняет FjordSongListAdapter после сканирования памяти

        Undo.RegisterCreatedObjectUndo(root.gameObject, "Create Fjord Scroll List");
        return root.gameObject;
    }
}
