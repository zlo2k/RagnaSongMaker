using RagnarockEditor.Audio;
using RagnarockEditor.Core;
using RagnarockEditor.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.EditorTools
{
    /// <summary>
    /// Единый стиль «каменных плит» для uGUI: палитра, плиты-подложки, плиты-кнопки
    /// и проход по сцене, который заменяет плоские Image-кнопки на каменные.
    ///
    /// Плита — это <see cref="StonePlate"/>: цвет заливки, фаска, шероховатость и
    /// углубление. Спрайтов и текстур нет, поэтому стиль одинаков на любом разрешении
    /// и не тянет за собой импорт ассетов.
    /// </summary>
    public static class StoneUiKit
    {
        // ---------------------------------------------------------------- палитра

        /// <summary>Тёмный камень — подложки панелей и планшетов.</summary>
        public static readonly Color Slab = new Color(0.185f, 0.160f, 0.132f, 1f);

        /// <summary>Внутренняя площадка планшета — чуть светлее рамки.</summary>
        public static readonly Color SlabFace = new Color(0.250f, 0.220f, 0.180f, 1f);

        /// <summary>Тот же камень, но с прозрачностью — панели поверх сцены.</summary>
        public static readonly Color SlabSoft = new Color(0.185f, 0.160f, 0.132f, 0.94f);

        /// <summary>Терракота — выступающие плиты: кнопки, заголовки, поля.</summary>
        public static readonly Color Plate = new Color(0.520f, 0.340f, 0.210f, 1f);

        /// <summary>Терракота посветлее — кнопка под лучом.</summary>
        public static readonly Color PlateHover = new Color(0.660f, 0.450f, 0.270f, 1f);

        /// <summary>Углубление — «выдолбленная» площадка под цифры, полосы, дорожки.</summary>
        public static readonly Color Recess = new Color(0.090f, 0.075f, 0.062f, 1f);

        /// <summary>Слоновая кость — основной цвет надписей на камне.</summary>
        public static readonly Color Ink = new Color(0.980f, 0.940f, 0.850f, 1f);

        /// <summary>Приглушённая кость — подписи второго плана.</summary>
        public static readonly Color InkDim = new Color(0.800f, 0.740f, 0.620f, 1f);

        /// <summary>Оранжевый акцент — заливки шкал.</summary>
        public static readonly Color Accent = new Color(0.950f, 0.550f, 0.220f, 1f);

        /// <summary>
        /// Янтарный камень — тот же материал, что у паков StoneUI и FjordScrollList.
        /// Основные «плиты» интерфейса: планшет BPM, панели.
        /// </summary>
        public static readonly Color Amber = new Color32(207, 135, 59, 255);

        /// <summary>Внутренняя площадка янтарной плиты (светлее рамки).</summary>
        public static readonly Color AmberFace = new Color32(228, 164, 92, 255);

        /// <summary>Тёмно-коричневый камень — плиты-кнопки и строки на янтарной плите.</summary>
        public static readonly Color Burlap = new Color32(89, 61, 50, 255);

        // ---------------------------------------------------------------- фабрика

        /// <summary>Новая плита-подложка на канвасе.</summary>
        public static StonePlate CreatePlate(Transform parent, string name, Vector2 position, Vector2 size,
            Color color, float recess = 0f, float grain = 0.1f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(StonePlate));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var plate = go.GetComponent<StonePlate>();
            plate.Configure(color, BevelFor(size), recess, grain);
            return plate;
        }

        /// <summary>
        /// Добавляет CanvasRenderer, если его нет: без него <see cref="StonePlate"/>
        /// не рисуется (именно из-за этого плиты были невидимы).
        /// </summary>
        public static void EnsureCanvasRenderer(GameObject go)
        {
            if (go.GetComponent<CanvasRenderer>() == null)
                go.AddComponent<CanvasRenderer>();
        }

        /// <summary>
        /// Ремонт всех плит сцены: у старых сцен CanvasRenderer мог не создаться.
        /// Возвращает число исправленных плит.
        /// </summary>
        public static int RepairStonePlates()
        {
            int repaired = 0;

            foreach (var plate in Object.FindObjectsByType<StonePlate>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (plate == null || plate.GetComponent<CanvasRenderer>() != null)
                    continue;

                plate.gameObject.AddComponent<CanvasRenderer>();
                plate.SetAllDirty();
                EditorUtility.SetDirty(plate);
                repaired++;
            }

            return repaired;
        }

        /// <summary>
        /// Приводит GameObject к каменной плите: убирает Image (если был) и ставит StonePlate.
        /// Существующую плиту не пересоздаёт — только перекрашивает и перестраивает фаску.
        /// </summary>
        public static StonePlate EnsurePlate(GameObject go, Color color, float recess = 0f,
            float grain = 0.1f, bool raycastTarget = false)
        {
            var rect = go.GetComponent<RectTransform>();
            Vector2 size = rect != null ? rect.rect.size : Vector2.zero;

            if (size.x < 1f || size.y < 1f)
                size = rect != null ? rect.sizeDelta : Vector2.zero;

            return EnsurePlate(go, size, color, recess, grain, raycastTarget);
        }

        /// <summary>То же, но размер плиты известен заранее (фаска считается по нему).</summary>
        public static StonePlate EnsurePlate(GameObject go, Vector2 size, Color color, float recess = 0f,
            float grain = 0.1f, bool raycastTarget = false)
        {
            var plate = EnsurePlateComponent(go);
            plate.Configure(color, BevelFor(size), recess, grain, size.x > 140f && size.y > 90f);
            plate.raycastTarget = raycastTarget;
            return plate;
        }

        /// <summary>Гарантирует, что на объекте одна графика-плита (Image убирается).</summary>
        public static StonePlate EnsurePlateComponent(GameObject go)
        {
            var plate = go.GetComponent<StonePlate>();
            if (plate != null)
            {
                EnsureCanvasRenderer(go);
                return plate;
            }

            var image = go.GetComponent<Image>();
            if (image != null)
                Object.DestroyImmediate(image);

            EnsureCanvasRenderer(go);
            return go.AddComponent<StonePlate>();
        }

        /// <summary>Фаска под размер плиты: мелкие кнопки — 5 px, крупные панели — 14 px.</summary>
        public static float BevelFor(Vector2 size)
        {
            float min = Mathf.Min(size.x, size.y);
            return Mathf.Clamp(min * 0.16f, 4f, 14f);
        }

        // ---------------------------------------------------------------- подложки

        /// <summary>
        /// Подложка панели: тёмная каменная плита на весь RectTransform.
        /// Если blocker = true, луч руки упирается в неё (модальные экраны).
        /// </summary>
        public static StonePlate EnsureBackdrop(Transform parent, string name, Vector2 size, Color color,
            bool blocker)
        {
            var existing = parent.Find(name);
            GameObject go;

            if (existing == null)
            {
                go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                go.transform.SetAsFirstSibling();
            }
            else
            {
                go = existing.gameObject;
            }

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var plate = EnsurePlate(go, size, color, 0f, 0.10f, blocker);

            if (!blocker)
            {
                var oldBlocker = go.GetComponent<UIRaycastBlocker>();
                if (oldBlocker != null)
                    Object.DestroyImmediate(oldBlocker);

                var oldBox = go.GetComponent<BoxCollider>();
                if (oldBox != null)
                    Object.DestroyImmediate(oldBox);

                return plate;
            }

            if (go.GetComponent<UIRaycastBlocker>() == null)
                go.AddComponent<UIRaycastBlocker>();

            var box = go.GetComponent<BoxCollider>();
            if (box == null)
                box = go.AddComponent<BoxCollider>();

            box.isTrigger = true;
            box.size = new Vector3(size.x, size.y, 20f);
            return plate;
        }

        // ---------------------------------------------------------------- кнопки

        /// <summary>
        /// Каменная кнопка: плита терракоты, надпись слоновой костью, звук нажатия
        /// и хитбокс для луча руки.
        /// </summary>
        public static Button CreatePlateButton(Transform parent, string name, string label,
            Vector2 position, Vector2 size, Color? plateColor = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(StonePlate));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var plate = go.GetComponent<StonePlate>();
            plate.Configure(plateColor ?? Plate, BevelFor(size), 0f, 0.08f, false);
            plate.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = plate;
            StyleTransitions(button);

            var tint = go.AddComponent<UiHoverTint>();
            tint.Bind(plate);

            if (go.GetComponent<SfxButton>() == null)
                go.AddComponent<SfxButton>();

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);

            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 6f);
            labelRect.offsetMax = new Vector2(-10f, -6f);

            var text = labelGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = Mathf.RoundToInt(Mathf.Clamp(size.y * 0.42f, 14f, 40f));
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Ink;
            text.text = label;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var hit = new GameObject("Hit", typeof(BoxCollider));
            hit.transform.SetParent(go.transform, false);
            var box = hit.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x, size.y, 30f);

            return button;
        }

        /// <summary>
        /// Перекрашивает существующую uGUI-кнопку в каменную плиту: Image → StonePlate,
        /// убирает прежний «скин» (обводка/тень от спрайтов), ставит подсветку под лучом.
        /// </summary>
        public static bool StonePlateButton(Button button, Color? plateColor = null)
        {
            if (button == null)
                return false;

            // уже каменная (например, строка списка треков) — цвет не трогаем,
            // но подсветку под лучом всё равно обеспечиваем
            if (button.targetGraphic is StonePlate existingPlate)
            {
                EnsureHoverTint(button, existingPlate);
                return false;
            }

            var go = button.gameObject;
            var rect = (RectTransform)go.transform;
            var size = rect.rect.size;
            if (size.x < 1f || size.y < 1f)
                size = rect.sizeDelta;

            var plate = EnsurePlate(go, plateColor ?? Plate, 0f, 0.08f, true);

            // старый «рельеф» из спрайтов LayerLab больше не нужен
            DestroyChild(go.transform, "SkinOutline");
            DestroyChild(go.transform, "SkinShade");

            button.targetGraphic = plate;
            button.transition = Selectable.Transition.ColorTint;
            StyleTransitions(button);

            var tint = go.GetComponent<UiHoverTint>();
            if (tint == null)
                tint = go.AddComponent<UiHoverTint>();
            tint.Bind(plate);

            if (go.GetComponent<SfxButton>() == null)
                go.AddComponent<SfxButton>();

            // надпись на камне — слоновой костью
            var label = go.transform.Find("Label");
            if (label != null)
            {
                var text = label.GetComponent<Text>();
                if (text != null)
                    text.color = Ink;
            }

            EditorUtility.SetDirty(button);
            return true;
        }

        /// <summary>Подсветка плиты под лучом руки: одна на кнопку, привязана к её графике.</summary>
        public static UiHoverTint EnsureHoverTint(Button button, StonePlate plate)
        {
            var tint = button.GetComponent<UiHoverTint>();
            if (tint == null)
                tint = button.gameObject.AddComponent<UiHoverTint>();

            if (tint.TargetGraphic == null)
                tint.Bind(plate);

            return tint;
        }

        /// <summary>Цветовые состояния кнопки: камень светлеет под лучом и темнеет при нажатии.</summary>
        private static void StyleTransitions(Button button)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.97f, 0.9f, 1f);
            colors.pressedColor = new Color(0.72f, 0.66f, 0.58f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;
        }

        /// <summary>
        /// Проход по всей сцене: каждая uGUI-кнопка становится каменной плитой.
        /// Возвращает число переделанных кнопок.
        /// </summary>
        public static int StyleAllButtonsInScene()
        {
            var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int converted = 0;

            foreach (var button in buttons)
            {
                if (button == null)
                    continue;

                if (StonePlateButton(button))
                    converted++;
            }

            return converted;
        }

        // ---------------------------------------------------------------- контролы

        /// <summary>
        /// Ползунок в виде каменного жёлоба: тёмная дорожка-углубление, оранжевая
        /// заливка-плита и каменная ручка. Используется как индикатор значения
        /// (точная подстройка в VR идёт каменным ползунком на пульте).
        /// </summary>
        public static Slider EnsureStoneSlider(Transform parent, string name, Vector2 position, Vector2 size,
            float minValue, float maxValue, float value)
        {
            var existing = parent.Find(name);
            var slider = existing != null ? existing.GetComponent<Slider>() : null;
            var track = existing != null ? existing.Find("Track") : null;
            bool stone = track != null && track.GetComponent<StonePlate>() != null;

            if (existing == null || slider == null || !stone)
            {
                if (existing != null)
                    Object.DestroyImmediate(existing.gameObject);

                var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
                go.transform.SetParent(parent, false);

                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = size;
                rect.anchoredPosition = position;

                var trackGo = new GameObject("Track", typeof(RectTransform), typeof(CanvasRenderer));
                trackGo.transform.SetParent(go.transform, false);
                var trackRect = (RectTransform)trackGo.transform;
                trackRect.anchorMin = Vector2.zero;
                trackRect.anchorMax = Vector2.one;
                trackRect.offsetMin = Vector2.zero;
                trackRect.offsetMax = Vector2.zero;

                var trackPlate = trackGo.AddComponent<StonePlate>();
                trackPlate.Configure(Recess, 8f, 0f, 0.08f, false);

                var fillArea = new GameObject("Fill Area", typeof(RectTransform));
                fillArea.transform.SetParent(go.transform, false);
                var fillAreaRect = (RectTransform)fillArea.transform;
                fillAreaRect.anchorMin = new Vector2(0f, 0.30f);
                fillAreaRect.anchorMax = new Vector2(1f, 0.70f);
                fillAreaRect.offsetMin = new Vector2(14f, 0f);
                fillAreaRect.offsetMax = new Vector2(-14f, 0f);

                var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer));
                fillGo.transform.SetParent(fillArea.transform, false);
                var fillRect = (RectTransform)fillGo.transform;
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;

                var fillPlate = fillGo.AddComponent<StonePlate>();
                fillPlate.Configure(Accent, 4f, 0f, 0.06f, false);

                var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
                handleArea.transform.SetParent(go.transform, false);
                var handleAreaRect = (RectTransform)handleArea.transform;
                handleAreaRect.anchorMin = Vector2.zero;
                handleAreaRect.anchorMax = Vector2.one;
                handleAreaRect.offsetMin = new Vector2(14f, 0f);
                handleAreaRect.offsetMax = new Vector2(-14f, 0f);

                var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer));
                handleGo.transform.SetParent(handleArea.transform, false);
                var handleRect = (RectTransform)handleGo.transform;
                handleRect.anchorMin = new Vector2(0f, 0f);
                handleRect.anchorMax = new Vector2(0f, 1f);
                handleRect.sizeDelta = new Vector2(34f, 4f);
                handleRect.anchoredPosition = Vector2.zero;

                var handlePlate = handleGo.AddComponent<StonePlate>();
                handlePlate.Configure(Ink, 7f, 0f, 0.05f, false);
                handlePlate.raycastTarget = true;

                slider = go.GetComponent<Slider>();
                slider.fillRect = fillRect;
                slider.handleRect = handleRect;
                slider.targetGraphic = handlePlate;
                slider.direction = Slider.Direction.LeftToRight;
            }
            else
            {
                var rect = (RectTransform)existing;
                rect.sizeDelta = size;
                rect.anchoredPosition = position;
            }

            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.SetValueWithoutNotify(Mathf.Clamp(value, minValue, maxValue));
            return slider;
        }

        /// <summary>Поле ввода на каменной плите: плита терракоты + белая костяная цифра.</summary>
        public static InputField EnsureStoneInputField(Transform parent, string name, Vector2 position,
            Vector2 size, int fontSize = 30)
        {
            var existing = parent.Find(name);
            var input = existing != null ? existing.GetComponent<InputField>() : null;
            bool stone = existing != null && existing.GetComponent<StonePlate>() != null;

            if (existing == null || input == null || !stone)
            {
                if (existing != null)
                    Object.DestroyImmediate(existing.gameObject);

                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(StonePlate), typeof(InputField));
                go.transform.SetParent(parent, false);

                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = size;
                rect.anchoredPosition = position;

                var plate = go.GetComponent<StonePlate>();
                plate.Configure(Plate, BevelFor(size), 6f, 0.06f, false);
                plate.raycastTarget = true;
                plate.SetRecessColor(Recess);

                var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
                textGo.transform.SetParent(go.transform, false);
                var textRect = (RectTransform)textGo.transform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(10f, 2f);
                textRect.offsetMax = new Vector2(-10f, -2f);

                var text = textGo.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = fontSize;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = Ink;
                text.raycastTarget = false;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;

                input = go.GetComponent<InputField>();
                input.targetGraphic = plate;
                input.textComponent = text;
                input.contentType = InputField.ContentType.DecimalNumber;
                input.lineType = InputField.LineType.SingleLine;
                input.characterLimit = 6;
            }
            else
            {
                var rect = (RectTransform)existing;
                rect.sizeDelta = size;
                rect.anchoredPosition = position;
            }

            return input;
        }

        private static void DestroyChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
                Object.DestroyImmediate(child.gameObject);
        }
    }
}

