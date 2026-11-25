using RagnarockEditor.Localization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RagnarockEditor.VR
{
    internal static class WorldPanel
    {
        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x, y);
            return rect;
        }

        public static void Plate(RectTransform rect, Color color)
        {
            rect.gameObject.AddComponent<StonePlate>().Configure(color, 6f);
        }

        public static Text Label(RectTransform rect, int size)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = size * 2 / 3;
            text.resizeTextMaxSize = size;
            return text;
        }

        public static Button Button(Transform parent, string key, float x, float y, float w, UnityAction click)
        {
            var rect = Rect(parent, key, x, y, w, 46);
            Plate(rect, new Color(.39f, .25f, .16f));
            HitBox(rect);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<StonePlate>();
            button.onClick.AddListener(click);
            var label = Label(Rect(rect, "Label", 0, 0, w - 10, 42), 23);
            label.gameObject.AddComponent<LocalizedText>().SetKey(key);
            return button;
        }

        public static void HitBox(RectTransform rect)
        {
            var box = rect.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(rect.rect.width, rect.rect.height, 8f);
        }
    }
}
