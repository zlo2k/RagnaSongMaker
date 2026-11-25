using System;
using System.Collections.Generic;
using System.IO;
using RagnarockEditor.Audio;
using RagnarockEditor.Core;
using RagnarockEditor.Data;
using RagnarockEditor.Localization;
using RagnarockEditor.UI;
using RagnarockEditor.VR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RagnarockEditor.EditorTools
{
    /// <summary>
    /// Разовый ремонт проекта под VR на Quest 3:
    ///  1. создаёт и назначает URP Asset (сейчас материалы URP/Lit рендерятся Built-in пайплайном => magenta);
    ///  2. в каждую сцену ставит XR Origin + Camera Offset + Tracked Pose Driver (трекинг головы);
    ///  3. в PreviewScene создаёт камеру (сейчас её нет вообще);
    ///  4. EventSystem -> XRUIInputModule, World Space канвасы -> TrackedDeviceGraphicRaycaster.
    /// Запуск из редактора: Tools -> Ragnarock Editor -> Fix VR Project.
    /// Запуск из batchmode: -executeMethod RagnarockEditor.EditorTools.VRProjectFixer.FixAll
    /// </summary>
    public static class VRProjectFixer
    {
        const string SettingsFolder = "Assets/Settings";
        const string RendererPath = "Assets/Settings/URP_Renderer.asset";
        const string PipelinePath = "Assets/Settings/URP_Asset.asset";
        const string ReportPath = "Logs/vr-fix-report.txt";

        static readonly List<string> Log = new List<string>();

        static readonly string[] ScenePaths =
        {
            "Assets/RagnarockEditor/Scenes/RecordScene.unity",
            "Assets/RagnarockEditor/Scenes/MainMenuScene.unity",
            "Assets/RagnarockEditor/Scenes/SongSelectionScene.unity",
            "Assets/RagnarockEditor/Scenes/PreviewScene.unity",
        };

        const string MarkerPath = "Library/vr-fix.pending";

        /// <summary>
        /// Одноразовый прогон при открытии проекта: если есть флаг Library/vr-fix.pending,
        /// дожидаемся конца импорта/компиляции и применяем фикс.
        /// </summary>
        [InitializeOnLoadMethod]
        static void AutoRunOnce()
        {
            if (!File.Exists(MarkerPath))
                return;

            try { File.Delete(MarkerPath); } catch { /* не критично */ }

            var startedAt = EditorApplication.timeSinceStartup;

            void Tick()
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    return;

                // сцены нельзя открывать/сохранять во время Play Mode — ждём выхода
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;

                // даём редактору закончить стартовый импорт
                if (EditorApplication.timeSinceStartup - startedAt < 5.0)
                    return;

                EditorApplication.update -= Tick;
                Debug.Log("[VRFix] auto-run on project open");
                FixAll();
            }

            EditorApplication.update += Tick;
        }

        [MenuItem("Tools/Ragnarock Editor/Fix VR Project", priority = 1)]
        public static void FixAll()
        {
            Log.Clear();
            try
            {
                EnsureFolder(SettingsFolder);
                EnsureRenderPipeline();

                // Новые JSON-переводы должны быть импортированы до сборки сцен:
                // ApplyLoc берёт текст из Loc для предпросмотра прямо в редакторе.
                AssetDatabase.Refresh();
                Loc.Reload();
                ConfigureSfxImporters();

                EnsureBuildSettings();
                FixScenes();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                WriteReport("OK");
                Debug.Log("[VRFix] DONE: " + string.Join(" ; ", Log));
            }
            catch (Exception e)
            {
                WriteReport("FAILED: " + e);
                Debug.LogError("[VRFix] FAILED: " + e);
                throw;
            }
        }

        /// <summary>
        /// Короткие SFX импортируются как PCM/DecompressOnLoad без прелоада-задержек:
        /// иначе на Quest первый удар/клик может звучать с задержкой.
        /// </summary>
        static void ConfigureSfxImporters()
        {
            const string folder = "Assets/Resources/SFX";
            if (!Directory.Exists(folder))
                return;

            int configured = 0;

            foreach (var path in Directory.GetFiles(folder, "*.wav", SearchOption.AllDirectories))
            {
                var assetPath = path.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
                if (importer == null)
                    continue;

                var settings = importer.defaultSampleSettings;
                bool changed = false;

                if (settings.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    changed = true;
                }

                if (settings.compressionFormat != AudioCompressionFormat.PCM)
                {
                    settings.compressionFormat = AudioCompressionFormat.PCM;
                    changed = true;
                }

                if (!settings.preloadAudioData)
                {
                    settings.preloadAudioData = true;
                    changed = true;
                }

                if (!importer.forceToMono)
                {
                    importer.forceToMono = true;
                    changed = true;
                }

                if (!changed)
                    continue;

                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                configured++;
            }

            if (configured > 0)
                Log.Add($"SFX: настроен импорт {configured} файлов");
        }

        /// <summary>
        /// Главное меню должно быть первой сценой: приложение стартует с объяснения
        /// и кнопки «Начать», а не с пустого экрана записи.
        /// </summary>
        static void EnsureBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;

            var names = new[]
            {
                "Assets/RagnarockEditor/Scenes/MainMenuScene.unity",
                "Assets/RagnarockEditor/Scenes/SongSelectionScene.unity",
                "Assets/RagnarockEditor/Scenes/RecordScene.unity",
                "Assets/RagnarockEditor/Scenes/PreviewScene.unity",
            };

            var ordered = new List<EditorBuildSettingsScene>();
            foreach (var path in names)
            {
                if (!File.Exists(path))
                {
                    Log.Add("SKIP build settings missing " + path);
                    continue;
                }

                var existing = Array.Find(scenes, s => s.path == path);
                ordered.Add(existing != null
                    ? existing
                    : new EditorBuildSettingsScene(path, true));
            }

            bool same = ordered.Count == scenes.Length;
            if (same)
            {
                for (int i = 0; i < scenes.Length; i++)
                {
                    if (scenes[i].path != ordered[i].path || scenes[i].enabled != ordered[i].enabled)
                    {
                        same = false;
                        break;
                    }
                }
            }

            if (same)
                return;

            EditorBuildSettings.scenes = ordered.ToArray();
            Log.Add("build settings: MainMenuScene первой сценой");
        }

        // ------------------------------------------------------------------ URP

        static void EnsureRenderPipeline()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                Log.Add("created URP renderer data");
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                Log.Add("created URP asset");
            }

            // мобильный VR: 4x MSAA, без HDR
            pipeline.msaaSampleCount = 4;
            pipeline.supportsHDR = false;
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();

            GraphicsSettings.defaultRenderPipeline = pipeline;

            int previous = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(previous, false);

            Log.Add("URP assigned to Graphics + " + QualitySettings.names.Length + " quality levels");
        }

        // --------------------------------------------------------------- scenes

        static void FixScenes()
        {
            // Грязная текущая сцена не даёт открывать сцены из кода, а в Play Mode это
            // вообще запрещено. Начинаем с пустой сцены, но только если грязная сцена —
            // одна из тех, что фиксер всё равно пересобирает (чужую работу не теряем).
            var active = EditorSceneManager.GetActiveScene();
            bool activeIsTarget = false;

            foreach (var target in ScenePaths)
            {
                if (target == active.path)
                {
                    activeIsTarget = true;
                    break;
                }
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode || (activeIsTarget && active.isDirty))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var path in ScenePaths)
            {
                if (!File.Exists(path))
                {
                    Log.Add("SKIP missing " + path);
                    continue;
                }

                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                FixScene(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, path);
                Log.Add("fixed " + Path.GetFileName(path));
            }
        }

        static void FixScene(Scene scene)
        {
            EnsureInteractionManager();

            var camera = FindMainCamera();
            var origin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();

            if (origin == null)
            {
                var originGo = new GameObject("XR Origin");
                var position = camera != null ? camera.transform.position : Vector3.zero;
                var yaw = camera != null ? camera.transform.eulerAngles.y : 0f;
                originGo.transform.SetPositionAndRotation(
                    new Vector3(position.x, 0f, position.z),
                    Quaternion.Euler(0f, yaw, 0f));
                origin = originGo.AddComponent<XROrigin>();
                Log.Add(scene.name + ": created XR Origin");
            }

            var offset = GetOrCreateCameraOffset(origin);

            if (camera == null)
            {
                var camGo = new GameObject("Main Camera");
                camera = camGo.AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;
                camGo.transform.SetParent(offset, false);
                camGo.transform.localPosition = Vector3.zero;
                camGo.transform.localRotation = Quaternion.identity;
                Log.Add(scene.name + ": created camera");
            }
            else if (camera.transform.parent != offset)
            {
                camera.transform.SetParent(offset, false);
                camera.transform.localPosition = Vector3.zero;
                camera.transform.localRotation = Quaternion.identity;
                Log.Add(scene.name + ": camera re-parented to rig");
            }

            WireOrigin(origin, camera, offset);
            WireCamera(camera);
            WireUi(camera);

            var withHammer = scene.name.Contains("Record") || scene.name.Contains("Preview");

            EnsureHands(scene, offset, withHammer);

            // Барабаны есть во всех сценах: это часть «палубы», а не атрибут режима записи.
            EnsureDrums(scene);

            if (withHammer)
                EnsureSongPlayer(scene);
            if (withHammer)
                EnsureLevelExporter(scene);

            // Stone UI создаётся первым: панели ниже находят каменные кнопки по имени
            // и прячут/показывают их в зависимости от состояния экрана.
            BuildStoneUi(scene);

            if (scene.name.Contains("MainMenu"))
            {
                BuildMainMenu(scene);
                BuildIntroScreen(scene);
            }

            if (scene.name.Contains("SongSelection"))
            {
                BuildSongSelection(scene);
                BuildFjordSongList(scene);
                BuildBpmScreen(scene);
                BuildBpmStoneControls(scene);
                BuildSongSelectionFlow(scene);
            }

            if (withHammer)
                BuildGameplayHud(scene);

            if (scene.name.Contains("Record"))
            {
                BuildSessionUi(scene);
                BuildCalibrationPanel(scene);
                BuildRecordStoneControls(scene);
            }

            if (scene.name.Contains("Preview"))
            {
                BuildExportFolderUi(scene);
                BuildPreviewStoneControls(scene);
            }

            BuildConfirmDialog(scene);
            BuildUIRig(scene);
            BuildEnvironment();
            ApplyButtonSkin();

            // Ремонт плит: у сцен, собранных до 24.11, у StonePlate не было CanvasRenderer,
            // поэтому каменные подложки и кнопки были невидимы (интерфейс выглядел плоским).
            var repaired = StoneUiKit.RepairStonePlates();
            if (repaired > 0)
                Log.Add($"{scene.name}: CanvasRenderer добавлен {repaired} плитам");

            FixBuiltInMaterials();
        }

        // ------------------------------------------------------------------ UI

        static Font GetFont()
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        static GameObject EnsureCanvas(string name, Vector3 position, float scale)
        {
            var go = GameObject.Find(name);
            if (go == null)
                go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            if (go.GetComponent<RectTransform>() == null)
                go.AddComponent<RectTransform>();

            var canvas = go.GetComponent<Canvas>();
            if (canvas == null)
                canvas = go.AddComponent<Canvas>();

            canvas.renderMode = RenderMode.WorldSpace;
            if (canvas.worldCamera == null)
                canvas.worldCamera = Camera.main;

            if (go.GetComponent<CanvasScaler>() == null)
                go.AddComponent<CanvasScaler>();
            if (go.GetComponent<GraphicRaycaster>() == null)
                go.AddComponent<GraphicRaycaster>();

            go.transform.SetPositionAndRotation(position, Quaternion.identity);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        static Text CreateLabel(Transform parent, string name, string content, Vector2 anchoredPosition,
            Vector2 size, int fontSize, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var text = go.GetComponent<Text>();
            text.font = GetFont();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.text = content;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        /// <summary>
        /// Вешает на подпись LocalizedText с нужным ключом и сразу подставляет текст
        /// текущего языка (чтобы в редакторе было видно результат без запуска).
        /// </summary>
        static void ApplyLoc(Text label, string key)
        {
            if (label == null)
                return;

            var localized = label.GetComponent<LocalizedText>();
            if (localized == null)
                localized = label.gameObject.AddComponent<LocalizedText>();

            var so = new SerializedObject(localized);
            var keyProperty = so.FindProperty("_key");
            if (keyProperty != null)
                keyProperty.stringValue = key;
            so.ApplyModifiedPropertiesWithoutUndo();

            label.text = Loc.T(key);
        }

        /// <summary>
        /// Находит/создаёт подпись и приводит её к текущему стилю (кегль, цвет, выравнивание).
        /// Нужно, чтобы повторный прогон фиксера обновлял уже собранные сцены.
        /// </summary>
        static Text EnsureText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize,
            Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var existing = parent.Find(name);
            var text = existing != null ? existing.GetComponent<Text>() : null;

            if (text == null)
            {
                if (existing != null)
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);

                text = CreateLabel(parent, name, string.Empty, position, size, fontSize, alignment);
            }

            var rect = (RectTransform)text.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.gameObject.SetActive(true);
            return text;
        }

        /// <summary>
        /// Каменная плита-подложка: переиспользует объект, если он уже плита,
        /// иначе заменяет плоский Image.
        /// </summary>
        static StonePlate EnsureSlab(Transform parent, string name, Vector2 position, Vector2 size,
            Color color, float recess = 0f, float grain = 0.12f, bool raycastTarget = false)
        {
            var existing = parent.Find(name);
            StonePlate plate;

            if (existing == null)
            {
                plate = StoneUiKit.CreatePlate(parent, name, position, size, color, recess, grain);
            }
            else
            {
                var rect = (RectTransform)existing;
                rect.sizeDelta = size;
                rect.anchoredPosition = position;
                plate = StoneUiKit.EnsurePlate(existing.gameObject, size, color, recess, grain, raycastTarget);
            }

            plate.raycastTarget = raycastTarget;
            return plate;
        }

        /// <summary>
        /// Кнопка-плита (камень): переиспользует объект, если он уже каменный,
        /// иначе заменяет uGUI-кнопку со спрайтом.
        /// </summary>
        static Button EnsurePlateButton(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var existing = parent.Find(name);
            var button = existing != null ? existing.GetComponent<Button>() : null;
            bool stone = button != null && button.targetGraphic is StonePlate;

            if (existing == null || button == null || !stone)
            {
                if (existing != null)
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);

                return StoneUiKit.CreatePlateButton(parent, name, string.Empty, position, size);
            }

            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var hit = button.transform.Find("Hit");
            var box = hit != null ? hit.GetComponent<BoxCollider>() : null;
            if (box != null)
                box.size = new Vector3(size.x, size.y, 30f);

            // подсветка под лучом: у плит, собранных до итерации 3.9, её ещё нет
            if (button.targetGraphic is StonePlate plate)
                StoneUiKit.EnsureHoverTint(button, plate);

            button.gameObject.SetActive(true);
            return button;
        }

        static Button CreateMenuButton(Transform parent, string name, string label, Vector2 anchoredPosition,
            Vector2 size, string sceneName, bool quit)
        {            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = go.GetComponent<Image>();
            image.color = new Color(0.16f, 0.24f, 0.38f, 0.95f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            // звук нажатия по умолчанию (диалоги/экспорт переопределяют его ниже)
            if (go.GetComponent<SfxButton>() == null)
                go.AddComponent<SfxButton>();

            CreateLabel(go.transform, "Label", label, Vector2.zero, size, 30);

            // хитбокс для луча из руки
            var hit = new GameObject("Hit", typeof(BoxCollider));
            hit.transform.SetParent(go.transform, false);
            var box = hit.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x, size.y, 30f);

            if (!string.IsNullOrEmpty(sceneName) || quit)
            {
                var jump = go.AddComponent<SceneJumpButton>();
                var so = new SerializedObject(jump);
                var sceneProperty = so.FindProperty("_sceneName");
                if (sceneProperty != null)
                    sceneProperty.stringValue = sceneName;
                var quitProperty = so.FindProperty("_quit");
                if (quitProperty != null)
                    quitProperty.boolValue = quit;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return button;
        }

        static void AttachPanel(GameObject canvasGo, float headHeightOffset, float distance)
        {
            var panel = canvasGo.GetComponent<WorldUIPanel>();
            if (panel == null)
                panel = canvasGo.AddComponent<WorldUIPanel>();

            var so = new SerializedObject(panel);
            var offsetProperty = so.FindProperty("_headHeightOffset");
            if (offsetProperty != null)
                offsetProperty.floatValue = headHeightOffset;
            var distanceProperty = so.FindProperty("_distance");
            if (distanceProperty != null)
                distanceProperty.floatValue = distance;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Button CreateOrGetButton(Transform parent, string name, string label, Vector2 position,
            Vector2 size, string sceneName = "", bool quit = false)
        {
            var existing = parent.Find(name);
            if (existing != null)
                return existing.GetComponent<Button>();

            return CreateMenuButton(parent, name, label, position, size, sceneName, quit);
        }

        static void BuildMainMenu(Scene scene)
        {
            // меню уже на 3D-плитах Stone UI — uGUI-канвас не пересоздаём
            if (GameObject.Find("StoneUI") != null)
                return;

            var canvas = EnsureCanvas("MainMenuCanvas", new Vector3(0f, 1.45f, 1.4f), 0.002f);
            var root = canvas.transform;

            if (root.Find("Title") == null)
                CreateLabel(root, "Title", "RAGNAROCK EDITOR", new Vector2(0f, 170f), new Vector2(760f, 100f), 56);

            if (root.Find("NewRecordButton") == null)
                CreateMenuButton(root, "NewRecordButton", "\u041D\u043E\u0432\u0430\u044F \u0437\u0430\u043F\u0438\u0441\u044C",
                    new Vector2(0f, 50f), new Vector2(360f, 70f), "RecordScene", false);

            if (root.Find("SongButton") == null)
                CreateMenuButton(root, "SongButton", "\u0412\u044B\u0431\u0440\u0430\u0442\u044C \u043F\u0435\u0441\u043D\u044E",
                    new Vector2(0f, -40f), new Vector2(360f, 70f), "SongSelectionScene", false);

            if (root.Find("QuitButton") == null)
                CreateMenuButton(root, "QuitButton", "\u0412\u044B\u0445\u043E\u0434",
                    new Vector2(0f, -130f), new Vector2(360f, 70f), string.Empty, true);

            AttachPanel(canvas, 0f, 1.2f);
            Log.Add(scene.name + ": меню собрано");
        }

        // ------------------------------------------------------------ интро

        /// <summary>
        /// Стартовый экран: короткое объяснение, переключатель языка и кнопка «Начать».
        /// Именно он встречает игрока при первом запуске (см. IntroScreen).
        /// </summary>
        static void BuildIntroScreen(Scene scene)
        {
            var existing = FindIncludingInactive("IntroCanvas");
            if (existing != null)
                existing.SetActive(true);

            var canvas = EnsureCanvas("IntroCanvas", new Vector3(0f, 1.55f, 1.0f), 0.0013f);
            var root = canvas.transform;
            ((RectTransform)root).sizeDelta = new Vector2(900f, 760f);

            // каменный планшет во весь экран; фон не пропускает луч к кнопкам за панелью
            var introSlab = StoneUiKit.EnsureBackdrop(root, "Background", new Vector2(900f, 760f),
                StoneUiKit.Slab, true);
            introSlab.transform.SetAsFirstSibling();

            var title = root.Find("Title") != null
                ? root.Find("Title").GetComponent<Text>()
                : CreateLabel(root, "Title", string.Empty, new Vector2(0f, 300f), new Vector2(840f, 80f), 46);
            ApplyLoc(title, LocKeys.IntroTitle);

            var body = root.Find("Body") != null
                ? root.Find("Body").GetComponent<Text>()
                : CreateLabel(root, "Body", string.Empty, new Vector2(0f, 45f), new Vector2(800f, 430f), 22,
                    TextAnchor.UpperLeft);
            ApplyLoc(body, LocKeys.IntroBody);

            var languageCaption = root.Find("LanguageCaption") != null
                ? root.Find("LanguageCaption").GetComponent<Text>()
                : CreateLabel(root, "LanguageCaption", string.Empty, new Vector2(0f, -215f), new Vector2(400f, 34f), 20);
            ApplyLoc(languageCaption, LocKeys.IntroLanguage);

            var previous = CreateOrGetButton(root, "LanguagePrev", "◀", new Vector2(-170f, -262f),
                new Vector2(72f, 54f));
            var next = CreateOrGetButton(root, "LanguageNext", "▶", new Vector2(170f, -262f),
                new Vector2(72f, 54f));

            var languageLabel = root.Find("LanguageLabel") != null
                ? root.Find("LanguageLabel").GetComponent<Text>()
                : CreateLabel(root, "LanguageLabel", string.Empty, new Vector2(0f, -262f), new Vector2(240f, 54f), 28);

            var start = CreateOrGetButton(root, "StartButton", string.Empty, new Vector2(0f, -330f),
                new Vector2(340f, 72f));
            var startLabel = start != null ? start.transform.Find("Label") : null;
            var startLocalized = startLabel != null ? startLabel.GetComponent<LocalizedText>() : null;
            if (startLabel != null)
                ApplyLoc(startLabel.GetComponent<Text>(), LocKeys.IntroStart);

            var switcher = canvas.GetComponent<LanguageSwitcher>();
            if (switcher == null)
                switcher = canvas.AddComponent<LanguageSwitcher>();

            var switcherSo = new SerializedObject(switcher);
            switcherSo.FindProperty("_label").objectReferenceValue = languageLabel;
            switcherSo.FindProperty("_previousButton").objectReferenceValue = previous;
            switcherSo.FindProperty("_nextButton").objectReferenceValue = next;
            switcherSo.ApplyModifiedPropertiesWithoutUndo();

            var intro = canvas.GetComponent<IntroScreen>();
            if (intro == null)
                intro = canvas.AddComponent<IntroScreen>();

            var introSo = new SerializedObject(intro);
            introSo.FindProperty("_introRoot").objectReferenceValue = canvas;
            introSo.FindProperty("_menuRoot").objectReferenceValue = FindIncludingInactive("StoneUI");
            introSo.FindProperty("_startButton").objectReferenceValue = start;
            introSo.FindProperty("_startLabel").objectReferenceValue =
                startLocalized != null ? startLocalized : (startLabel != null ? startLabel.GetComponent<LocalizedText>() : null);
            introSo.ApplyModifiedPropertiesWithoutUndo();

            Log.Add(scene.name + ": интро-экран");
        }

        // ------------------------------------------------- выход/подтверждение

        /// <summary>
        /// Модальный диалог подтверждения выхода и предупреждения о несохранённых данных.
        /// Канвас ставится ближе остальных панелей (z = 0.72), поэтому луч руки
        /// упирается в его фон-блокер, а не в каменные кнопки за диалогом.
        /// </summary>
        static void BuildConfirmDialog(Scene scene)
        {
            var existing = FindIncludingInactive("ExitCanvas");
            if (existing != null)
                existing.SetActive(true);

            var canvas = EnsureCanvas("ExitCanvas", new Vector3(0f, 1.5f, 0.72f), 0.0013f);
            var root = canvas.transform;
            ((RectTransform)root).sizeDelta = new Vector2(760f, 470f);

            // Панель — отдельный ребёнок: сам канвас всегда активен, поэтому Awake()
            // компонента диалога гарантированно выполняется при загрузке сцены.
            var panel = root.Find("Panel") as RectTransform;
            if (panel == null)
            {
                var panelGo = new GameObject("Panel", typeof(RectTransform));
                panelGo.transform.SetParent(root, false);
                panel = (RectTransform)panelGo.transform;
                panel.anchorMin = Vector2.zero;
                panel.anchorMax = Vector2.one;
                panel.offsetMin = Vector2.zero;
                panel.offsetMax = Vector2.zero;
            }

            // каменный планшет диалога: он же блокирует луч, чтобы не нажать плиты за ним
            var dialogSlab = StoneUiKit.EnsureBackdrop(panel, "Background", new Vector2(760f, 470f),
                StoneUiKit.Slab, true);
            dialogSlab.transform.SetAsFirstSibling();

            var title = panel.Find("Title") != null
                ? panel.Find("Title").GetComponent<Text>()
                : CreateLabel(panel, "Title", string.Empty, new Vector2(0f, 180f), new Vector2(700f, 60f), 34);

            var message = panel.Find("Message") != null
                ? panel.Find("Message").GetComponent<Text>()
                : CreateLabel(panel, "Message", string.Empty, new Vector2(0f, 70f), new Vector2(700f, 180f), 24);

            var primary = CreateOrGetButton(panel, "PrimaryButton", string.Empty, new Vector2(0f, -70f),
                new Vector2(480f, 62f));
            var secondary = CreateOrGetButton(panel, "SecondaryButton", string.Empty, new Vector2(0f, -140f),
                new Vector2(480f, 56f));
            var cancel = CreateOrGetButton(panel, "CancelButton", string.Empty, new Vector2(0f, -205f),
                new Vector2(320f, 52f));

            SetButtonSound(primary, SfxPlayer.UiSound.Confirm);
            SetButtonSound(secondary, SfxPlayer.UiSound.Confirm);
            SetButtonSound(cancel, SfxPlayer.UiSound.Back);

            var dialog = canvas.GetComponent<ConfirmDialog>();
            if (dialog == null)
                dialog = canvas.AddComponent<ConfirmDialog>();

            var so = new SerializedObject(dialog);
            so.FindProperty("_root").objectReferenceValue = panel.gameObject;
            so.FindProperty("_titleObject").objectReferenceValue = title.gameObject;
            so.FindProperty("_title").objectReferenceValue = title;
            so.FindProperty("_message").objectReferenceValue = message;
            so.FindProperty("_primaryButton").objectReferenceValue = primary;
            so.FindProperty("_primaryLabel").objectReferenceValue = FindButtonLabel(primary);
            so.FindProperty("_secondaryButton").objectReferenceValue = secondary;
            so.FindProperty("_secondaryLabel").objectReferenceValue = FindButtonLabel(secondary);
            so.FindProperty("_cancelButton").objectReferenceValue = cancel;
            so.FindProperty("_cancelLabel").objectReferenceValue = FindButtonLabel(cancel);
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.gameObject.SetActive(false);
            Log.Add(scene.name + ": диалог подтверждения");
        }

        static Text FindButtonLabel(Button button)
        {
            if (button == null)
                return null;

            var label = button.transform.Find("Label");
            return label != null ? label.GetComponent<Text>() : null;
        }

        /// <summary>Задаёт звук нажатия для uGUI-кнопки (Select / Confirm / Back).</summary>
        static void SetButtonSound(Button button, SfxPlayer.UiSound sound)
        {
            if (button == null)
                return;

            var sfx = button.GetComponent<SfxButton>();
            if (sfx == null)
                sfx = button.gameObject.AddComponent<SfxButton>();

            var so = new SerializedObject(sfx);
            var property = so.FindProperty("_sound");
            if (property != null)
                property.enumValueIndex = (int)sound;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void DestroyIfExists(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        static void BuildSongSelection(Scene scene)
        {
            var existingCanvas = FindIncludingInactive("SongSelectionCanvas");
            if (existingCanvas != null)
                existingCanvas.SetActive(true);

            var canvas = EnsureCanvas("SongSelectionCanvas", new Vector3(0f, 1.4f, 1.2f), 0.0014f);
            var canvasRect = (RectTransform)canvas.transform;
            canvasRect.sizeDelta = new Vector2(800f, 700f);
            var root = canvas.transform;

            DestroyIfExists(root, "Hint");
            DestroyIfExists(root, "RecordButton");
            // «Назад» убрана: уход в меню — каменная Back (ScreenRouter)
            DestroyIfExists(root, "BackButton");

            // Тёмная подложка: список читается на снегу и не просвечивает палубой
            EnsureBackdrop(root, new Vector2(800f, 700f), true);

            var title = root.Find("Title") != null
                ? root.Find("Title").GetComponent<Text>()
                : CreateLabel(root, "Title", string.Empty, new Vector2(0f, 300f), new Vector2(760f, 80f), 48);
            ApplyLoc(title, LocKeys.SongsTitle);

            var status = root.Find("Status") != null
                ? root.Find("Status").GetComponent<Text>()
                : CreateLabel(root, "Status", string.Empty, new Vector2(0f, 235f), new Vector2(760f, 70f), 20);

            var container = root.Find("List") as RectTransform;
            if (container == null)
            {
                var listGo = new GameObject("List", typeof(RectTransform));
                listGo.transform.SetParent(root, false);
                container = (RectTransform)listGo.transform;
                container.anchorMin = new Vector2(0.5f, 0.5f);
                container.anchorMax = new Vector2(0.5f, 0.5f);
                container.pivot = new Vector2(0.5f, 0.5f);
                container.sizeDelta = new Vector2(720f, 420f);
                container.anchoredPosition = new Vector2(0f, -10f);
            }

            var templateTransform = container.Find("SongTemplate");
            GameObject template;
            if (templateTransform == null)
            {
                var templateButton = CreateMenuButton(container, "SongTemplate", "Песня",
                    new Vector2(0f, 140f), new Vector2(700f, 62f), string.Empty, false);
                template = templateButton.gameObject;
            }
            else
            {
                template = templateTransform.gameObject;
            }

            template.SetActive(false);

            var pageLabel = root.Find("PageLabel") != null
                ? root.Find("PageLabel").GetComponent<Text>()
                : CreateLabel(root, "PageLabel", "1/1", new Vector2(0f, -255f), new Vector2(160f, 48f), 24);

            var prev = CreateOrGetButton(root, "PrevPage", "◀", new Vector2(-140f, -255f), new Vector2(90f, 54f));
            var next = CreateOrGetButton(root, "NextPage", "▶", new Vector2(140f, -255f), new Vector2(90f, 54f));
            var grant = CreateOrGetButton(root, "GrantButton", string.Empty, new Vector2(300f, -255f),
                new Vector2(250f, 50f));
            ApplyLoc(FindButtonLabel(grant), LocKeys.SongsGrant);

            // Переключение языка доступно и здесь: игрок может сменить его, не возвращаясь в интро
            var previousLanguage = CreateOrGetButton(root, "LanguagePrev", "◀", new Vector2(-150f, -315f),
                new Vector2(64f, 46f));
            var nextLanguage = CreateOrGetButton(root, "LanguageNext", "▶", new Vector2(150f, -315f),
                new Vector2(64f, 46f));

            var languageLabel = root.Find("LanguageLabel") != null
                ? root.Find("LanguageLabel").GetComponent<Text>()
                : CreateLabel(root, "LanguageLabel", string.Empty, new Vector2(0f, -315f), new Vector2(200f, 46f), 22);

            var switcher = canvas.GetComponent<LanguageSwitcher>();
            if (switcher == null)
                switcher = canvas.AddComponent<LanguageSwitcher>();

            var switcherSo = new SerializedObject(switcher);
            switcherSo.FindProperty("_label").objectReferenceValue = languageLabel;
            switcherSo.FindProperty("_previousButton").objectReferenceValue = previousLanguage;
            switcherSo.FindProperty("_nextButton").objectReferenceValue = nextLanguage;
            switcherSo.ApplyModifiedPropertiesWithoutUndo();

            var list = canvas.GetComponent<SongSelectionList>();
            if (list == null)
                list = canvas.AddComponent<SongSelectionList>();

            var so = new SerializedObject(list);
            so.FindProperty("_container").objectReferenceValue = container;
            so.FindProperty("_template").objectReferenceValue = template;
            so.FindProperty("_statusLabel").objectReferenceValue = status;
            so.FindProperty("_pageLabel").objectReferenceValue = pageLabel;
            so.FindProperty("_prevButton").objectReferenceValue = prev;
            so.FindProperty("_nextButton").objectReferenceValue = next;
            so.FindProperty("_grantButton").objectReferenceValue = grant;
            so.ApplyModifiedPropertiesWithoutUndo();

            AttachPanel(canvas, 0f, 1.2f);
            Log.Add(scene.name + ": список песен собран");
        }

        // ---------------------------------------------------------- калибровка

        static GameObject FindIncludingInactive(string name)
        {
            var go = GameObject.Find(name);
            if (go != null)
                return go;

            var transforms = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var transform in transforms)
            {
                if (transform.name == name)
                    return transform.gameObject;
            }

            return null;
        }

        static XRHandPoseDriver FindRightHand()
        {
            var drivers = UnityEngine.Object.FindObjectsByType<XRHandPoseDriver>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var driver in drivers)
            {
                if (driver.Side == XRHandPoseDriver.Hand.Right)
                    return driver;
            }

            return drivers.Length > 0 ? drivers[0] : null;
        }

        static void BuildCalibrationPanel(Scene scene)
        {
            var existing = FindIncludingInactive("CalibrationCanvas");
            Transform root;
            Canvas canvas;

            if (existing != null)
            {
                canvas = existing.GetComponent<Canvas>();
                root = existing.transform;
            }
            else
            {
                var go = new GameObject("CalibrationCanvas", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));

                ((RectTransform)go.transform).sizeDelta = new Vector2(800f, 700f);
                go.transform.position = new Vector3(0f, 1.5f, 1.2f);
                go.transform.localScale = Vector3.one * 0.0014f;

                canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = Camera.main;
                root = go.transform;
                AttachPanel(go, 0f, 1.2f);
            }

            // Калибровка — самостоятельный модальный экран: тёмная подложка и блокировка луча,
            // чтобы случайно не нажать Play/Settings за панелью.
            EnsureBackdrop(root, new Vector2(800f, 700f), true);

            if (root.Find("CalInstruction") == null)
                CreateLabel(root, "CalInstruction", Loc.T(LocKeys.CalibrationTitle),
                    new Vector2(0f, 250f), new Vector2(760f, 140f), 28);
            var progressPlate = EnsureSlab(root, "CalProgressBg", new Vector2(0f, 95f), new Vector2(700f, 32f),
                StoneUiKit.Recess, 0f, 0.06f);

            var fillTransform = progressPlate.transform.Find("CalProgressFill");
            Image fill;

            if (fillTransform == null)
            {
                fill = CreateImage(progressPlate.transform, "CalProgressFill", Vector2.zero, new Vector2(700f, 32f),
                    new Color(0.35f, 0.8f, 0.5f, 1f));

                var fillRect = fill.rectTransform;
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;

                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillAmount = 0f;
            }
            else
            {
                fill = fillTransform.GetComponent<Image>();
            }

            var progressText = root.Find("CalProgressText") != null
                ? root.Find("CalProgressText").GetComponent<Text>()
                : CreateLabel(root, "CalProgressText", string.Empty, new Vector2(0f, 55f), new Vector2(700f, 40f), 22);

            var status = root.Find("CalStatus") != null
                ? root.Find("CalStatus").GetComponent<Text>()
                : CreateLabel(root, "CalStatus", string.Empty, new Vector2(0f, -40f), new Vector2(760f, 140f), 22);

            var start = CreateOrGetButton(root, "CalStartButton", string.Empty, new Vector2(-260f, -210f), new Vector2(240f, 66f));
            ApplyLoc(FindButtonLabel(start), LocKeys.CalibrationStart);

            var skip = CreateOrGetButton(root, "CalSkipButton", string.Empty, new Vector2(0f, -210f), new Vector2(240f, 66f));
            ApplyLoc(FindButtonLabel(skip), LocKeys.CalibrationSkip);

            var save = CreateOrGetButton(root, "CalSaveButton", string.Empty, new Vector2(260f, -210f), new Vector2(240f, 66f));
            ApplyLoc(FindButtonLabel(save), LocKeys.CalibrationSave);

            var manager = UnityEngine.Object.FindFirstObjectByType<DrumCalibrationManager>();
            if (manager == null)
            {
                Log.Add(scene.name + ": DrumCalibrationManager не найден, панель калибровки не подключена");
                return;
            }

            var rightHand = FindRightHand();
            var so = new SerializedObject(manager);
            so.FindProperty("_calibrationCanvas").objectReferenceValue = canvas;
            so.FindProperty("_instructionText").objectReferenceValue = root.Find("CalInstruction").GetComponent<Text>();
            so.FindProperty("_statusText").objectReferenceValue = status;
            so.FindProperty("_startButton").objectReferenceValue = start;
            so.FindProperty("_skipButton").objectReferenceValue = skip;
            so.FindProperty("_saveButton").objectReferenceValue = save;
            so.FindProperty("_progressFill").objectReferenceValue = fill;
            so.FindProperty("_progressText").objectReferenceValue = progressText;
            if (rightHand != null)
                so.FindProperty("_rightHandPoseDriver").objectReferenceValue = rightHand;
            so.ApplyModifiedPropertiesWithoutUndo();

            canvas.gameObject.SetActive(false);
            Log.Add(scene.name + ": панель калибровки собрана");
        }

        // ------------------------------------------------------------ BPM-экран

        static void BuildBpmScreen(Scene scene)
        {
            var selection = FindIncludingInactive("SongSelectionCanvas");
            if (selection != null)
            {
                var legacyPanel = selection.transform.Find("BpmPanel");
                if (legacyPanel != null)
                    UnityEngine.Object.DestroyImmediate(legacyPanel.gameObject);
            }

            var canvasGo = FindIncludingInactive("BpmCanvas");
            Transform root;
            Canvas canvas;

            if (canvasGo == null)
            {
                canvasGo = new GameObject("BpmCanvas", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));

                canvasGo.transform.position = new Vector3(0f, 1.5f, 1.3f);
                canvasGo.transform.localScale = Vector3.one * 0.0011f;

                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = Camera.main;
                root = canvasGo.transform;
            }
            else
            {
                canvas = canvasGo.GetComponent<Canvas>();
                root = canvasGo.transform;
            }

            ((RectTransform)root).sizeDelta = new Vector2(800f, 600f);

            // Плоские элементы прошлых итераций: тёмный Image-фон и uGUI-строка метронома
            DestroyIfExists(root, "Background");
            DestroyIfExists(root, "MetronomeToggle");
            DestroyIfExists(root, "MetronomeVolume");

            // ---- каменный планшет: плита на всю панель, луч упирается в неё
            // янтарный камень — тот же материал, что у каменных кнопок Stone UI и списка треков
            var slab = EnsureSlab(root, "Slab", Vector2.zero, new Vector2(800f, 600f),
                StoneUiKit.Amber, 16f, 0.12f, true);
            // внутренняя площадка светлее рамки: планшет читается как камень с фаской, а не как дыра
            slab.SetRecessColor(StoneUiKit.AmberFace);
            slab.transform.SetAsFirstSibling();

            if (slab.gameObject.GetComponent<UIRaycastBlocker>() == null)
                slab.gameObject.AddComponent<UIRaycastBlocker>();

            var slabBox = slab.gameObject.GetComponent<BoxCollider>();
            if (slabBox == null)
                slabBox = slab.gameObject.AddComponent<BoxCollider>();

            slabBox.isTrigger = true;
            slabBox.size = new Vector3(800f, 600f, 20f);

            // ---- заголовок на плите терракоты
            var titlePlate = EnsureSlab(root, "TitlePlate", new Vector2(0f, 225f), new Vector2(640f, 58f),
                StoneUiKit.Plate, 0f, 0.08f);

            var bpmTitle = EnsureText(root, "BpmTitle", new Vector2(0f, 225f), new Vector2(600f, 50f),
                34, StoneUiKit.Ink);
            ApplyLoc(bpmTitle, LocKeys.BpmTitle);

            // ---- «выдолбленная» площадка под цифру BPM
            var readout = EnsureSlab(root, "Readout", new Vector2(0f, 100f), new Vector2(720f, 170f),
                StoneUiKit.Recess, 0f, 0.08f);

            var bpmValue = EnsureText(root, "BpmValue", new Vector2(0f, 126f), new Vector2(700f, 96f),
                64, StoneUiKit.Ink);

            var confidence = EnsureText(root, "Confidence", new Vector2(0f, 52f), new Vector2(660f, 24f),
                19, StoneUiKit.InkDim);

            // ---- каменный жёлоб BPM: индикатор значения (точная подстройка — каменным ползунком)
            var slider = StoneUiKit.EnsureStoneSlider(root, "BpmSlider", new Vector2(0f, -18f),
                new Vector2(660f, 50f), 40f, 300f, 120f);

            // ---- поле точного BPM на плите терракоты
            var input = StoneUiKit.EnsureStoneInputField(root, "BpmInput", new Vector2(-250f, -96f),
                new Vector2(200f, 58f), 26);

            var tap = EnsurePlateButton(root, "TapButton", new Vector2(235f, -96f), new Vector2(220f, 78f));
            ApplyLoc(FindButtonLabel(tap), LocKeys.BpmTap);

            var tapCount = EnsureText(root, "TapCount", new Vector2(-15f, -80f), new Vector2(280f, 24f),
                20, StoneUiKit.Burlap, TextAnchor.MiddleLeft);

            var tapBpm = EnsureText(root, "TapBpm", new Vector2(-15f, -110f), new Vector2(280f, 26f),
                21, StoneUiKit.Burlap, TextAnchor.MiddleLeft);

            var continueButton = EnsurePlateButton(root, "BpmContinueButton", new Vector2(95f, -200f),
                new Vector2(430f, 76f));
            ApplyLoc(FindButtonLabel(continueButton), LocKeys.BpmContinue);

            // Back на BPM-панели возвращает к списку треков, а не в главное меню
            var bpmBack = EnsurePlateButton(root, "BpmBackButton", new Vector2(-300f, -200f),
                new Vector2(150f, 58f));
            ApplyLoc(FindButtonLabel(bpmBack), LocKeys.BpmBack);

            var status = EnsureText(root, "BpmStatus", new Vector2(0f, 270f), new Vector2(720f, 26f),
                19, StoneUiKit.Burlap);

            // Порядок отрисовки uGUI = порядок детей: плиты снизу, контролы над ними,
            // надписи — поверх всего (иначе плита, созданная позже текста, закроет его).
            slab.transform.SetAsFirstSibling();
            titlePlate.transform.SetAsLastSibling();
            readout.transform.SetAsLastSibling();
            slider.transform.SetAsLastSibling();
            input.transform.SetAsLastSibling();
            tap.transform.SetAsLastSibling();
            continueButton.transform.SetAsLastSibling();
            bpmBack.transform.SetAsLastSibling();

            foreach (var label in new[] { bpmTitle, bpmValue, confidence, tapCount, tapBpm, status })
            {
                if (label != null)
                    label.transform.SetAsLastSibling();
            }

            var editor = canvasGo.GetComponent<BPMEditorUI>();
            if (editor == null)
                editor = canvasGo.AddComponent<BPMEditorUI>();

            var editorSo = new SerializedObject(editor);
            editorSo.FindProperty("_bpmValueText").objectReferenceValue = bpmValue;
            editorSo.FindProperty("_confidenceText").objectReferenceValue = confidence;
            editorSo.FindProperty("_bpmSlider").objectReferenceValue = slider;
            editorSo.FindProperty("_bpmInput").objectReferenceValue = input;
            editorSo.FindProperty("_tapButton").objectReferenceValue = tap;
            editorSo.FindProperty("_tapCountText").objectReferenceValue = tapCount;
            editorSo.FindProperty("_tapBPMText").objectReferenceValue = tapBpm;
            editorSo.FindProperty("_continueButton").objectReferenceValue = continueButton;
            editorSo.ApplyModifiedPropertiesWithoutUndo();

            // ---- метроном: 3D-контролы на каменном пульте (см. BuildBpmStoneControls),
            // здесь только проигрыватель и мост к BPM-редактору.
            DestroyIfExists(root, "MetronomeToggle");
            DestroyIfExists(root, "MetronomeVolume");

            var metronome = canvasGo.GetComponent<MetronomePlayer>();
            if (metronome == null)
                metronome = canvasGo.AddComponent<MetronomePlayer>();

            var preview = canvasGo.GetComponent<MetronomePreview>();
            if (preview == null)
                preview = canvasGo.AddComponent<MetronomePreview>();

            var previewSo = new SerializedObject(preview);
            previewSo.FindProperty("_player").objectReferenceValue = metronome;
            previewSo.FindProperty("_editor").objectReferenceValue = editor;
            previewSo.ApplyModifiedPropertiesWithoutUndo();

            // Оркестратор BPM живёт на всегда активном SongSelectionFlow (см. BuildSongSelectionFlow).
            // На этом канвасе он не получал бы Update, пока канвас выключен,
            // и результат фонового анализа никогда бы не применился.
            var staleFlow = canvasGo.GetComponent<BpmFlowController>();
            if (staleFlow != null)
                UnityEngine.Object.DestroyImmediate(staleFlow);

            canvasGo.SetActive(false);
            Log.Add(scene.name + ": BPM-экран собран");
        }

        /// <summary>
        /// Каменная плита-основание пульта: world-space канвас с плитой, стоит за контролами
        /// (модели пульта повёрнуты к игроку, поэтому плита смещена на -Z локально).
        /// </summary>
        static GameObject CreateStoneBoardSlab(Transform parent, Vector2 sizePx = default,
            Vector3? localPos = null, Color? color = null)
        {
            if (sizePx == default)
                sizePx = new Vector2(920f, 240f);

            var go = new GameObject("Board", typeof(RectTransform), typeof(Canvas), typeof(CanvasRenderer),
                typeof(StonePlate));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = sizePx;
            rect.localScale = Vector3.one * 0.001f;
            rect.localPosition = localPos ?? new Vector3(0f, -0.02f, -0.03f);
            rect.localRotation = Quaternion.identity;

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            Color plateColor = color ?? StoneUiKit.Slab;
            var plate = go.GetComponent<StonePlate>();
            plate.Configure(plateColor, 14f, 14f, 0.12f, true);
            plate.SetRecessColor(color.HasValue ? StoneUiKit.AmberFace : StoneUiKit.SlabFace);
            plate.raycastTarget = false;

            return go;
        }

        /// <summary>
        /// Каменный пульт BPM из StoneControls: ползунок BPM, тумблер метронома,
        /// его громкость, чекбокс привязки к сетке и полоса прогресса анализа.
        /// Пульт стоит под планшетом и включается вместе с BPM-экраном.
        /// </summary>
        static void BuildBpmStoneControls(Scene scene)
        {
            var board = FindIncludingInactive("BpmStoneControls");
            if (board == null)
                board = new GameObject("BpmStoneControls");

            var root = board.transform;

            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);

            // Общая каменная плита под пультом: контролы стоят на ней, а не висят в воздухе
            CreateStoneBoardSlab(root, new Vector2(920f, 240f), new Vector3(0f, -0.02f, -0.03f),
                StoneUiKit.Amber);

            // Верхний ряд: подстройка BPM ползунком, тумблер метронома, привязка к сетке
            var bpm = CreateStoneBar(root, "BpmTune", "Volume_H.fbx",
                new Vector3(0.14f, 0.05f, 0f), 0.46f, false, StoneControl.Kind.Slider);

            var switchCtrl = CreateStoneToggle(root, "MetronomeSwitch", "SwitchOn.fbx", "SwitchOff.fbx",
                new Vector3(-0.16f, 0.05f, 0f), 0.13f);

            var check = CreateStoneToggle(root, "SnapCheck", "CheckOn.fbx", "CheckOff.fbx",
                new Vector3(-0.33f, 0.05f, 0f), 0.11f);

            // Нижний ряд: громкость метронома и прогресс загрузки/анализа
            var volume = CreateStoneBar(root, "MetronomeVolume", "Volume_H.fbx",
                new Vector3(-0.16f, -0.05f, 0f), 0.40f, false, StoneControl.Kind.Slider);

            var progress = CreateStoneBar(root, "AnalysisProgress", "Progress_H.fbx",
                new Vector3(0.22f, -0.05f, 0f), 0.40f, false, StoneControl.Kind.Progress);

            var panel = board.GetComponent<StoneControlsPanel>();
            if (panel == null)
                panel = board.AddComponent<StoneControlsPanel>();

            var so = new SerializedObject(panel);
            so.FindProperty("_bpmSlider").objectReferenceValue = bpm;
            so.FindProperty("_metronomeSwitch").objectReferenceValue = switchCtrl;
            so.FindProperty("_metronomeVolume").objectReferenceValue = volume;
            so.FindProperty("_snapCheck").objectReferenceValue = check;
            so.FindProperty("_analysisProgress").objectReferenceValue = progress;
            so.ApplyModifiedPropertiesWithoutUndo();

            // на экране списка пульт не нужен — его включает BpmFlowController вместе с планшетом
            board.SetActive(false);

            Log.Add(scene.name + ": каменный пульт BPM (BPM, метроном, громкость, snap, прогресс)");
        }

        const string StoneControlsFolder = "Assets/RagnarockEditor/Models/StoneControls/";

        /// <summary>Прогресс записи — каменная полоса Progress_H под HUD.</summary>
        static void BuildRecordStoneControls(Scene scene)
        {
            var board = CreateStoneBoard("RecordStoneControls");
            var root = board.transform;

            // плита под контролами: пульт выглядит как каменная консоль, а не парящие детали
            CreateStoneBoardSlab(root, new Vector2(1080f, 240f), new Vector3(0.05f, -0.01f, -0.03f),
                StoneUiKit.Amber);

            var progress = CreateStoneBar(root, "RecordProgress", "Progress_H.fbx",
                new Vector3(-0.05f, 0f, 0f), 0.70f, false, StoneControl.Kind.Progress);

            var volume = CreateStoneBar(root, "RecordVolume", "Volume_V.fbx",
                new Vector3(0.45f, 0.02f, 0f), 0.30f, true, StoneControl.Kind.Slider);

            var panel = board.GetComponent<StoneControlsPanel>();
            if (panel == null)
                panel = board.AddComponent<StoneControlsPanel>();

            var so = new SerializedObject(panel);
            so.FindProperty("_recordProgress").objectReferenceValue = progress;
            so.FindProperty("_metronomeVolume").objectReferenceValue = volume;
            so.ApplyModifiedPropertiesWithoutUndo();

            Log.Add(scene.name + ": каменный прогресс записи");
        }

        /// <summary>Предпросмотр: перемотка, прогресс и скорость — Seek_H, Progress_V, Seek_V.</summary>
        static void BuildPreviewStoneControls(Scene scene)
        {
            var board = CreateStoneBoard("PreviewStoneControls");
            var root = board.transform;

            // плита под контролами пульта предпросмотра
            CreateStoneBoardSlab(root, new Vector2(1100f, 400f), new Vector3(0f, -0.03f, -0.03f),
                StoneUiKit.Amber);

            var seek = CreateStoneBar(root, "PreviewSeek", "Seek_H.fbx",
                new Vector3(0f, 0.03f, 0f), 0.56f, false, StoneControl.Kind.Slider);

            var progress = CreateStoneBar(root, "PreviewProgress", "Progress_V.fbx",
                new Vector3(-0.42f, 0.02f, 0f), 0.30f, true, StoneControl.Kind.Progress);

            var speed = CreateStoneBar(root, "PreviewSpeed", "Seek_V.fbx",
                new Vector3(0.42f, 0.02f, 0f), 0.30f, true, StoneControl.Kind.Slider);

            var panel = board.GetComponent<StoneControlsPanel>();
            if (panel == null)
                panel = board.AddComponent<StoneControlsPanel>();

            var so = new SerializedObject(panel);
            so.FindProperty("_previewSeek").objectReferenceValue = seek;
            so.FindProperty("_previewProgress").objectReferenceValue = progress;
            so.FindProperty("_previewSpeed").objectReferenceValue = speed;
            so.ApplyModifiedPropertiesWithoutUndo();

            Log.Add(scene.name + ": каменные контролы предпросмотра");
        }

        static GameObject CreateStoneBoard(string name)
        {
            var board = FindIncludingInactive(name);
            if (board == null)
                board = new GameObject(name);

            var root = board.transform;

            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);

            return board;
        }

        static StoneControl CreateStoneBar(Transform parent, string name, string file, Vector3 localPos,
            float targetLength, bool vertical, StoneControl.Kind kind)
        {
            var go = InstantiateStoneModel(parent, name, StoneControlsFolder + file, localPos, targetLength, vertical);
            if (go == null)
                return null;

            AddStoneCollider(go, go);

            var control = go.GetComponent<StoneControl>();
            if (control == null)
                control = go.AddComponent<StoneControl>();

            var so = new SerializedObject(control);
            so.FindProperty("_kind").enumValueIndex = (int)kind;
            so.FindProperty("_vertical").boolValue = vertical;
            // Доска развёрнута к игроку на 180°, поэтому зеркалить ось не нужно:
            // заливка и ручка идут от иконки (локальный min) к максимуму.
            so.FindProperty("_invert").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            return control;
        }

        static StoneControl CreateStoneToggle(Transform parent, string name, string onFile, string offFile,
            Vector3 localPos, float targetLength)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = Quaternion.identity;

            var on = InstantiateStoneModel(root.transform, "On", StoneControlsFolder + onFile,
                Vector3.zero, targetLength, false);
            var off = InstantiateStoneModel(root.transform, "Off", StoneControlsFolder + offFile,
                Vector3.zero, targetLength, false);

            if (on == null || off == null)
                return null;

            AddStoneCollider(root, on);
            off.SetActive(false);

            var control = root.AddComponent<StoneControl>();

            var so = new SerializedObject(control);
            so.FindProperty("_kind").enumValueIndex = (int)StoneControl.Kind.Toggle;
            so.FindProperty("_onState").objectReferenceValue = on;
            so.FindProperty("_offState").objectReferenceValue = off;
            so.ApplyModifiedPropertiesWithoutUndo();

            return control;
        }

        static GameObject InstantiateStoneModel(Transform parent, string name, string path,
            Vector3 localPos, float targetLength, bool vertical)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                Log.Add("нет модели: " + path);
                return null;
            }

            var go = PrefabUtility.InstantiatePrefab(model, parent) as GameObject;
            if (go == null)
                return null;

            if (PrefabUtility.IsPartOfPrefabInstance(go))
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            go.name = name;
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            ConvertMaterials(go);

            var bounds = GetWorldBounds(go);
            float dimension = vertical ? bounds.size.y : bounds.size.x;
            float scale = targetLength / Mathf.Max(0.0001f, dimension);
            go.transform.localScale = Vector3.one * scale;

            return go;
        }

        static void AddStoneCollider(GameObject target, GameObject boundsSource)
        {
            var bounds = GetWorldBounds(boundsSource);

            var box = target.GetComponent<BoxCollider>();
            if (box == null)
                box = target.AddComponent<BoxCollider>();

            box.isTrigger = true;

            var scale = target.transform.lossyScale;
            box.center = target.transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
                bounds.size.x / Mathf.Max(0.0001f, scale.x),
                bounds.size.y / Mathf.Max(0.0001f, scale.y),
                Mathf.Max(0.02f, bounds.size.z / Mathf.Max(0.0001f, scale.z)));
        }

        /// <summary>Ставит доску/пульт под XR Origin на нужное место и разворот.</summary>
        static void PlaceBoard(string name, Vector3 localPos, float yaw, Transform rig)
        {
            var go = FindIncludingInactive(name);
            if (go == null)
                return;

            go.transform.SetParent(rig, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one;
        }

        /// <summary>
        /// Автомат экрана выбора песни: список ↔ BPM. Отдельный объект нужен потому,
        /// что канвасы прячут друг друга, а обработчик Back должен оставаться доступным,
        /// даже когда список скрыт.
        /// </summary>
        static void BuildSongSelectionFlow(Scene scene)
        {
            var flowGo = FindIncludingInactive("SongSelectionFlow");
            if (flowGo == null)
                flowGo = new GameObject("SongSelectionFlow");

            var oldList = FindIncludingInactive("SongSelectionCanvas");
            var fjordList = FindIncludingInactive("Fjord Scroll List");
            var list = fjordList != null ? fjordList : oldList;
            var bpm = FindIncludingInactive("BpmCanvas");

            // В старых сценах остались дубликаты BpmFlowController (на SongSelectionCanvas и на
            // BpmCanvas). SongButton находил первый попавшийся — обычно «пустой», без ссылки на
            // BPM-канвас, из-за чего выбор трека визуально ничего не делал. Оставляем ровно один.
            RemoveStaleBpmFlows(flowGo);

            // ---- автомат «список ↔ BPM»
            var screen = flowGo.GetComponent<SongSelectionScreen>();
            if (screen == null)
                screen = flowGo.AddComponent<SongSelectionScreen>();

            var back = bpm != null ? bpm.transform.Find("BpmBackButton") : null;

            var screenSo = new SerializedObject(screen);
            screenSo.FindProperty("_listRoot").objectReferenceValue = list;
            screenSo.FindProperty("_bpmRoot").objectReferenceValue = bpm;
            screenSo.FindProperty("_bpmBackButton").objectReferenceValue =
                back != null ? back.GetComponent<Button>() : null;
            screenSo.ApplyModifiedPropertiesWithoutUndo();

            // ---- оркестратор BPM на том же активном объекте
            var flow = flowGo.GetComponent<BpmFlowController>();
            if (flow == null)
                flow = flowGo.AddComponent<BpmFlowController>();

            var editor = bpm != null ? bpm.GetComponent<BPMEditorUI>() : null;
            var bpmStatus = bpm != null ? bpm.transform.Find("BpmStatus") : null;
            var continueButton = bpm != null ? bpm.transform.Find("BpmContinueButton") : null;
            var listStatus = list != null ? list.transform.Find("Status") : null;
            var loader = UnityEngine.Object.FindFirstObjectByType<AudioLoader>(FindObjectsInactive.Include);

            // каменный пульт BPM и плита-заголовок списка: пульт виден только на экране BPM,
            // заголовок списка — только на списке треков (иначе он перекрывает планшет BPM)
            var bpmConsole = FindIncludingInactive("BpmStoneControls");
            var listHeader = FindIncludingInactive("OpenMP3Title");

            var flowSo = new SerializedObject(flow);
            flowSo.FindProperty("_audioLoader").objectReferenceValue = loader;
            flowSo.FindProperty("_bpmEditor").objectReferenceValue = editor;
            flowSo.FindProperty("_statusText").objectReferenceValue =
                bpmStatus != null ? bpmStatus.GetComponent<Text>() : null;
            flowSo.FindProperty("_listStatusText").objectReferenceValue =
                listStatus != null ? listStatus.GetComponent<Text>() : null;
            flowSo.FindProperty("_continueButton").objectReferenceValue =
                continueButton != null ? continueButton.GetComponent<Button>() : null;
            flowSo.FindProperty("_bpmEditorRoot").objectReferenceValue = bpm;
            flowSo.FindProperty("_selectionRoot").objectReferenceValue = list;
            flowSo.FindProperty("_bpmConsole").objectReferenceValue = bpmConsole;
            flowSo.FindProperty("_listHeader").objectReferenceValue = listHeader;
            flowSo.ApplyModifiedPropertiesWithoutUndo();

            Log.Add(scene.name + ": SongSelectionFlow + BpmFlow (активный хост)");
        }

        /// <summary>
        /// Каменная панель списка треков из пака FjordScrollList: заголовок, строки,
        /// полоса прокрутки и страницы по 4 трека. Старый uGUI-список прячется.
        /// </summary>
        static void BuildFjordSongList(Scene scene)
        {
            var root = FindIncludingInactive("Fjord Scroll List");
            if (root == null)
                root = FjordListBuilder.Create();

            if (root == null)
            {
                Log.Add(scene.name + ": Fjord Scroll List не создан");
                return;
            }

            root.SetActive(true);

            var fjordCanvas = root.GetComponent<Canvas>();
            if (fjordCanvas != null)
                fjordCanvas.worldCamera = Camera.main;

            var list = root.GetComponent<FjordTrackList>();
            if (list == null)
                list = root.AddComponent<FjordTrackList>();

            var titleTransform = root.transform.Find("Title");
            if (titleTransform != null)
                ApplyLoc(titleTransform.GetComponent<Text>(), LocKeys.SongsTitle);

            var pageUp = CreateOrGetButton(root.transform, "ListPageUp", "▲",
                new Vector2(320f, 205f), new Vector2(40f, 40f));
            var pageDown = CreateOrGetButton(root.transform, "ListPageDown", "▼",
                new Vector2(320f, -205f), new Vector2(40f, 40f));

            SetButtonSound(pageUp, SfxPlayer.UiSound.SeekTick);
            SetButtonSound(pageDown, SfxPlayer.UiSound.SeekTick);

            var oldCanvas = FindIncludingInactive("SongSelectionCanvas");
            if (oldCanvas != null)
                oldCanvas.SetActive(false);

            var adapter = root.GetComponent<FjordSongListAdapter>();
            if (adapter == null)
                adapter = root.AddComponent<FjordSongListAdapter>();

            var so = new SerializedObject(adapter);
            so.FindProperty("_list").objectReferenceValue = list;
            so.FindProperty("_pageUpButton").objectReferenceValue = pageUp;
            so.FindProperty("_pageDownButton").objectReferenceValue = pageDown;
            so.FindProperty("_oldCanvas").objectReferenceValue = oldCanvas;
            so.FindProperty("_perPage").intValue = 4;
            so.ApplyModifiedPropertiesWithoutUndo();

            Log.Add(scene.name + ": каменный список треков (FjordScrollList)");
        }

        /// <summary>Удаляет все BpmFlowController, кроме одного — на <paramref name="keep"/>.</summary>
        static void RemoveStaleBpmFlows(GameObject keep)
        {
            var flows = UnityEngine.Object.FindObjectsByType<BpmFlowController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var flow in flows)
            {
                if (flow == null || flow.gameObject == keep)
                    continue;

                Log.Add("удалён дубликат BpmFlowController на " + flow.gameObject.name);
                UnityEngine.Object.DestroyImmediate(flow);
            }
        }

        static void BuildGameplayHud(Scene scene)
        {
            var canvas = EnsureCanvas("HUDCanvas", new Vector3(0f, 2f, 1.1f), 0.0013f);
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(800f, 700f);

            // «Меню» больше не uGUI-кнопкой: уход в меню — каменная Back через ScreenRouter
            RemoveUguiButtons("MenuButton");

            AttachPanel(canvas, 0.35f, 1.0f);
            Log.Add(scene.name + ": HUD-канвас");
        }

        static RectTransform CreatePanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        static Image CreateImage(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// Экраны записи. Все панели лежат на HUDCanvas и включаются по состоянию
        /// (NoSong / Ready / Countdown / Recording / Paused / Finished) — см. RecordSessionController.
        /// Каменные кнопки Play/Pause/Stop/Replay/Settings тоже прячутся по состоянию.
        /// </summary>
        static void BuildSessionUi(Scene scene)
        {
            var canvasGo = FindIncludingInactive("HUDCanvas");
            if (canvasGo == null)
                return;

            canvasGo.SetActive(true);
            var root = canvasGo.transform;

            // ---------- индикатор метронома (виден во всех состояниях HUD)
            var metronomeIcon = PrepareLabel(root, "MetronomeIcon",
                new Vector2(-330f, 235f), new Vector2(60f, 60f), 44, TextAnchor.MiddleCenter);
            metronomeIcon.text = "●";

            var metronomeLabel = PrepareLabel(root, "MetronomeLabel",
                new Vector2(-330f, 193f), new Vector2(220f, 30f), 16, TextAnchor.MiddleCenter);
            ApplyLoc(metronomeLabel, LocKeys.MetronomeTitle);

            var metronomeIndicator = canvasGo.GetComponent<MetronomeIndicator>();
            if (metronomeIndicator == null)
                metronomeIndicator = canvasGo.AddComponent<MetronomeIndicator>();

            var indicatorSo = new SerializedObject(metronomeIndicator);
            indicatorSo.FindProperty("_icon").objectReferenceValue = metronomeIcon;
            indicatorSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- «песня не выбрана»
            var noSong = PreparePanel(root, "NoSongPanel");
            AddStatePanelBackdrop(noSong);

            var noSongTitle = PrepareLabel(noSong, "NoSongTitle",
                new Vector2(0f, 150f), new Vector2(760f, 60f), 40, TextAnchor.MiddleCenter);
            ApplyLoc(noSongTitle, LocKeys.RecordNoSongTitle);

            var noSongBody = PrepareLabel(noSong, "NoSongBody",
                new Vector2(0f, 55f), new Vector2(740f, 100f), 22, TextAnchor.MiddleCenter);
            ApplyLoc(noSongBody, LocKeys.RecordNoSongBody);

            var chooseSong = PrepareButton(noSong, "ChooseSongButton",
                new Vector2(0f, -60f), new Vector2(380f, 64f));
            ApplyLoc(FindButtonLabel(chooseSong), LocKeys.RecordNoSongChoose);

            var freePlay = PrepareButton(noSong, "FreePlayButton",
                new Vector2(0f, -140f), new Vector2(380f, 60f));
            ApplyLoc(FindButtonLabel(freePlay), LocKeys.RecordNoSongFreeplay);

            // ---------- «готов к записи»
            var ready = PreparePanel(root, "ReadyPanel");
            AddStatePanelBackdrop(ready);

            var readyTitle = PrepareLabel(ready, "ReadyTitle",
                new Vector2(0f, 160f), new Vector2(760f, 60f), 38, TextAnchor.MiddleCenter);
            ApplyLoc(readyTitle, LocKeys.RecordReadyTitle);

            var songLabel = PrepareLabel(ready, "SongLabel",
                new Vector2(0f, 85f), new Vector2(760f, 60f), 30, TextAnchor.MiddleCenter);

            var bpmLabel = PrepareLabel(ready, "BpmLabel",
                new Vector2(0f, 35f), new Vector2(760f, 40f), 22, TextAnchor.MiddleCenter);

            var readyHint = PrepareLabel(ready, "ReadyHint",
                new Vector2(0f, -25f), new Vector2(740f, 60f), 20, TextAnchor.MiddleCenter);
            ApplyLoc(readyHint, LocKeys.RecordReadyHint);

            // Большая явная кнопка: «как начать настукивать» не должно быть загадкой
            var startButton = PrepareButton(ready, "StartButton",
                new Vector2(0f, -100f), new Vector2(420f, 68f));
            ApplyLoc(FindButtonLabel(startButton), LocKeys.RecordReadyStart);

            var anotherSong = PrepareButton(ready, "AnotherButton",
                new Vector2(0f, -185f), new Vector2(360f, 58f));
            ApplyLoc(FindButtonLabel(anotherSong), LocKeys.RecordReadyAnother);

            // ---------- «запись завершена»
            var finished = PreparePanel(root, "FinishedPanel");
            AddStatePanelBackdrop(finished);

            var finishedTitle = PrepareLabel(finished, "FinishedTitle",
                new Vector2(0f, 160f), new Vector2(760f, 60f), 36, TextAnchor.MiddleCenter);
            ApplyLoc(finishedTitle, LocKeys.RecordFinishedTitle);

            var finishedSummary = PrepareLabel(finished, "FinishedSummary",
                new Vector2(0f, 90f), new Vector2(760f, 50f), 24, TextAnchor.MiddleCenter);

            var exportButton = PrepareButton(finished, "ExportButton",
                new Vector2(0f, 10f), new Vector2(360f, 62f));
            ApplyLoc(FindButtonLabel(exportButton), LocKeys.RecordFinishedExport);
            SetButtonSound(exportButton, SfxPlayer.UiSound.Confirm);

            var previewButton = PrepareButton(finished, "PreviewButton",
                new Vector2(0f, -65f), new Vector2(360f, 58f));
            ApplyLoc(FindButtonLabel(previewButton), LocKeys.RecordFinishedPreview);

            var newSongButton = PrepareButton(finished, "NewSongButton",
                new Vector2(0f, -135f), new Vector2(360f, 58f));
            ApplyLoc(FindButtonLabel(newSongButton), LocKeys.RecordFinishedAnother);

            // ---------- HUD записи
            var hud = PreparePanel(root, "HudPanel");

            // жёлоб прогресса — тёмная каменная плита, заливка внутри остаётся акцентом
            var progressPlate = EnsureSlab(hud, "ProgressBg", new Vector2(0f, 250f), new Vector2(720f, 34f),
                StoneUiKit.Recess, 0f, 0.06f);

            if (progressPlate.transform.Find("ProgressFill") == null)
            {
                var fill = CreateImage(progressPlate.transform, "ProgressFill", Vector2.zero, new Vector2(720f, 34f),
                    new Color(1f, 0.55f, 0.2f, 1f));
                var fillRect = (RectTransform)fill.transform;
                fillRect.anchorMin = new Vector2(0f, 0f);
                fillRect.anchorMax = new Vector2(0f, 1f);
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;
            }

            var timeLabel = PrepareLabel(hud, "TimeLabel",
                new Vector2(0f, 200f), new Vector2(720f, 40f), 26, TextAnchor.MiddleCenter);

            var hitsLabel = PrepareLabel(hud, "HitsLabel",
                new Vector2(0f, 152f), new Vector2(720f, 40f), 26, TextAnchor.MiddleCenter);

            var countdownLabel = PrepareLabel(hud, "CountdownLabel",
                new Vector2(0f, 40f), new Vector2(700f, 200f), 96, TextAnchor.MiddleCenter);
            if (countdownLabel != null)
                countdownLabel.color = new Color(1f, 0.82f, 0.45f, 1f);

            // ---------- компонент-автомат
            var controller = canvasGo.GetComponent<RecordSessionController>();
            if (controller == null)
                controller = canvasGo.AddComponent<RecordSessionController>();

            var so = new SerializedObject(controller);
            so.FindProperty("_noSongPanel").objectReferenceValue = noSong.gameObject;
            so.FindProperty("_readyPanel").objectReferenceValue = ready.gameObject;
            so.FindProperty("_finishedPanel").objectReferenceValue = finished.gameObject;
            so.FindProperty("_hudPanel").objectReferenceValue = hud.gameObject;
            so.FindProperty("_songLabel").objectReferenceValue = songLabel;
            so.FindProperty("_bpmLabel").objectReferenceValue = bpmLabel;
            so.FindProperty("_finishedSummary").objectReferenceValue = finishedSummary;
            so.FindProperty("_timeLabel").objectReferenceValue = timeLabel;
            so.FindProperty("_hitsLabel").objectReferenceValue = hitsLabel;
            so.FindProperty("_countdownLabel").objectReferenceValue = countdownLabel;
            so.FindProperty("_progressFill").objectReferenceValue = progressPlate.transform.Find("ProgressFill");
            so.FindProperty("_chooseSongButton").objectReferenceValue = chooseSong;
            so.FindProperty("_startButton").objectReferenceValue = startButton;
            so.FindProperty("_freePlayButton").objectReferenceValue = freePlay;
            so.FindProperty("_anotherSongButton").objectReferenceValue = anotherSong;
            so.FindProperty("_exportButton").objectReferenceValue = exportButton;
            so.FindProperty("_previewButton").objectReferenceValue = previewButton;
            so.FindProperty("_stonePlay").objectReferenceValue = FindStoneButton("Play");
            so.FindProperty("_stonePause").objectReferenceValue = FindStoneButton("Pause");
            so.FindProperty("_stoneStop").objectReferenceValue = FindStoneButton("Stop");
            so.FindProperty("_stoneReplay").objectReferenceValue = FindStoneButton("Replay");
            so.FindProperty("_stoneSettings").objectReferenceValue = FindStoneButton("Settings");
            so.ApplyModifiedPropertiesWithoutUndo();

            Log.Add(scene.name + ": экраны записи (NoSong/Ready/HUD/Finished)");
        }

        static RectTransform PreparePanel(Transform parent, string name)
        {
            var existing = parent.Find(name) as RectTransform;
            if (existing == null)
                return CreatePanel(parent, name);

            existing.gameObject.SetActive(true);
            return existing;
        }

        static Text PrepareLabel(Transform parent, string name, Vector2 position, Vector2 size,
            int fontSize, TextAnchor alignment)
        {
            var existing = parent.Find(name);
            var text = existing != null
                ? existing.GetComponent<Text>()
                : CreateLabel(parent, name, string.Empty, position, size, fontSize, alignment);

            if (text == null)
                return null;

            var rect = (RectTransform)text.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.gameObject.SetActive(true);
            return text;
        }

        static Button PrepareButton(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var button = CreateOrGetButton(parent, name, string.Empty, position, size);
            if (button == null)
                return null;

            button.gameObject.SetActive(true);

            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var hit = button.transform.Find("Hit");
            var box = hit != null ? hit.GetComponent<BoxCollider>() : null;
            if (box != null)
                box.size = new Vector3(size.x, size.y, 30f);

            return button;
        }

        static StoneButton FindStoneButton(string name)
        {
            var stoneUi = FindIncludingInactive("StoneUI");
            if (stoneUi == null)
                return null;

            var child = stoneUi.transform.Find(name);
            return child != null ? child.GetComponent<StoneButton>() : null;
        }

        /// <summary>
        /// Строка «Папка: …» в предпросмотре: переключение готовых папок экспорта
        /// и показ полного пути под кнопкой.
        /// </summary>
        static void BuildExportFolderUi(Scene scene)
        {
            var canvas = FindIncludingInactive("HUDCanvas");
            if (canvas == null)
                return;

            var root = canvas.transform;

            var buttonTransform = root.Find("ExportFolderButton");
            var button = buttonTransform != null
                ? buttonTransform.GetComponent<Button>()
                : CreateMenuButton(root, "ExportFolderButton", string.Empty,
                    new Vector2(0f, -300f), new Vector2(760f, 52f), string.Empty, false);

            var label = FindButtonLabel(button);

            var pathLabel = root.Find("ExportFolderPath") != null
                ? root.Find("ExportFolderPath").GetComponent<Text>()
                : CreateLabel(root, "ExportFolderPath", string.Empty,
                    new Vector2(0f, -342f), new Vector2(900f, 30f), 16);

            var selector = canvas.GetComponent<ExportFolderSelector>();
            if (selector == null)
                selector = canvas.AddComponent<ExportFolderSelector>();

            var so = new SerializedObject(selector);
            so.FindProperty("_button").objectReferenceValue = button;
            so.FindProperty("_label").objectReferenceValue = label;
            so.FindProperty("_pathLabel").objectReferenceValue = pathLabel;
            so.ApplyModifiedPropertiesWithoutUndo();

            SetButtonSound(button, SfxPlayer.UiSound.Select);
            Log.Add(scene.name + ": выбор папки экспорта");
        }

        /// <summary>
        /// Каменная подложка панели (вместо плоского тёмного Image). Если blocker = true,
        /// луч руки упирается в неё и не нажимает элементы за панелью (модальные экраны).
        /// </summary>
        static void EnsureBackdrop(Transform parent, Vector2 size, bool blocker)
        {
            StoneUiKit.EnsureBackdrop(parent, "Backdrop", size, StoneUiKit.Slab, blocker);
        }

        /// <summary>
        /// Подложка под панель состояния записи: каменная плита помягче
        /// (без блокировки луча, чтобы каменные кнопки оставались доступны).
        /// </summary>
        static void AddStatePanelBackdrop(RectTransform panel)
        {
            if (panel == null)
                return;

            var existing = panel.Find("Backdrop");
            if (existing == null)
            {
                var plate = StoneUiKit.CreatePlate(panel, "Backdrop", new Vector2(0f, 30f),
                    new Vector2(800f, 520f), StoneUiKit.SlabSoft, 0f, 0.12f);
                plate.transform.SetAsFirstSibling();
                plate.raycastTarget = false;
                return;
            }

            var rect = (RectTransform)existing;
            rect.anchoredPosition = new Vector2(0f, 30f);
            rect.sizeDelta = new Vector2(800f, 520f);
            rect.SetAsFirstSibling();

            StoneUiKit.EnsurePlate(existing.gameObject, new Vector2(800f, 520f), StoneUiKit.SlabSoft, 0f, 0.12f, false);
        }

        const string HammerPrefabPath = "Assets/RagnarockEditor/Prefabs/VR/Hammer.prefab";
        const string StormMalletPath = "Assets/RagnarockEditor/Models/StormMallet/StormMallet_R.fbx";
        const string DrumPrefabPath = "Assets/RagnarockEditor/Prefabs/VR/Drum.prefab";
        const string DrumConfigPath = "Assets/RagnarockEditor/ScriptableObjects/DrumLayoutConfig.asset";

        static void EnsureHands(Scene scene, Transform offset, bool withHammer)
        {
            var hammerPrefab = withHammer
                ? AssetDatabase.LoadAssetAtPath<GameObject>(HammerPrefabPath)
                : null;

            EnsureHand("LeftHand", XRHandPoseDriver.Hand.Left, false, offset, hammerPrefab);
            EnsureHand("RightHand", XRHandPoseDriver.Hand.Right, true, offset, hammerPrefab);

            Log.Add(scene.name + ": руки" + (hammerPrefab != null ? " + молотки" : ""));
        }

        static void EnsureHand(string handName, XRHandPoseDriver.Hand side, bool isRight,
            Transform offset, GameObject hammerPrefab)
        {
            var existing = offset.Find(handName);
            var handGo = existing != null ? existing.gameObject : new GameObject(handName);

            if (existing == null)
            {
                handGo.transform.SetParent(offset, false);
                handGo.transform.localPosition = Vector3.zero;
                handGo.transform.localRotation = Quaternion.identity;
            }

            var driver = handGo.GetComponent<XRHandPoseDriver>();
            if (driver == null)
                driver = handGo.AddComponent<XRHandPoseDriver>();

            var so = new SerializedObject(driver);
            var handProperty = so.FindProperty("_hand");
            if (handProperty != null)
                handProperty.enumValueIndex = (int)side;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (handGo.transform.childCount == 0)
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visual.name = "Visual";
                var collider = visual.GetComponent<Collider>();
                if (collider != null)
                    UnityEngine.Object.DestroyImmediate(collider);

                visual.transform.SetParent(handGo.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                visual.transform.localScale = new Vector3(0.035f, 0.05f, 0.035f);

                var renderer = visual.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.sharedMaterial = GetUrpFallbackMaterial();
            }

            EnsurePointer(handGo);

            var hammerGo = ResolveHammer(handGo, hammerPrefab, isRight);
            if (hammerGo == null)
                return;

            // молот разворачивается параллельно лучу указателя (aim-поза контроллера)
            var aligner = hammerGo.GetComponent<AimAlignedObject>();
            if (aligner == null)
                aligner = hammerGo.AddComponent<AimAlignedObject>();

            var alignerSo = new SerializedObject(aligner);
            var pointerProperty = alignerSo.FindProperty("_pointer");
            if (pointerProperty != null)
                pointerProperty.objectReferenceValue = handGo.GetComponent<HandUIPointer>();
            var alignmentProperty = alignerSo.FindProperty("_modelAlignment");
            if (alignmentProperty != null)
                alignmentProperty.vector3Value = Vector3.zero; // модель уже смотрит по +Z
            var alignBlend = alignerSo.FindProperty("_blend");
            if (alignBlend != null)
                alignBlend.floatValue = 1f;
            alignerSo.ApplyModifiedPropertiesWithoutUndo();

            var controller = hammerGo.GetComponent<HammerController>();
            if (controller == null)
                controller = hammerGo.AddComponent<HammerController>();

            var hammerSo = new SerializedObject(controller);
            var rightProperty = hammerSo.FindProperty("_isRightHand");
            if (rightProperty != null)
                rightProperty.boolValue = isRight;
            var headProperty = hammerSo.FindProperty("_head");
            if (headProperty != null)
                headProperty.objectReferenceValue = hammerGo.transform.Find("Head");
            hammerSo.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject ResolveHammer(GameObject handGo, GameObject hammerPrefab, bool isRight)
        {
            var malletModel = AssetDatabase.LoadAssetAtPath<GameObject>(StormMalletPath);
            var existing = handGo.transform.Find("Hammer");
            var hammerGo = existing != null ? existing.gameObject : null;

            if (malletModel != null)
            {
                // модель — источник истины: каждый прогон пересоздаём молот из FBX
                if (hammerGo != null)
                    UnityEngine.Object.DestroyImmediate(hammerGo);

                hammerGo = PrefabUtility.InstantiatePrefab(malletModel, handGo.transform) as GameObject;
                if (hammerGo == null)
                    return null;

                // распаковываем, чтобы можно было добавить «Head» и компоненты
                if (PrefabUtility.IsPartOfPrefabInstance(hammerGo))
                    PrefabUtility.UnpackPrefabInstance(hammerGo, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);

                hammerGo.name = "Hammer";
                ApplyMalletTransform(hammerGo, isRight);
                ConvertMaterials(hammerGo);
                HideHandVisual(handGo);
                return hammerGo;
            }

            if (hammerGo == null && hammerPrefab != null)
                hammerGo = PrefabUtility.InstantiatePrefab(hammerPrefab, handGo.transform) as GameObject;

            if (hammerGo == null)
                return null;

            hammerGo.name = "Hammer";
            hammerGo.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            hammerGo.transform.localRotation = Quaternion.identity;
            return hammerGo;
        }

        static void ApplyMalletTransform(GameObject mallet, bool isRight)
        {
            StripHandNodes(mallet);

            mallet.transform.localScale = Vector3.one;
            mallet.transform.localPosition = Vector3.zero;
            mallet.transform.localRotation = Quaternion.identity;

            var size = GetWorldBounds(mallet).size;
            var longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            const float targetLength = 0.38f; // молот в 2 раза меньше прежнего 0.75
            var scale = longest > 0.0001f ? targetLength / longest : 1f;

            // левую руку делаем зеркальной копией правой
            mallet.transform.localScale = new Vector3(isRight ? scale : -scale, scale, scale);

            var scaled = GetLocalBounds(mallet.transform);
            // модель молота вытянута вдоль +Z (рукоять у нуля, голова на +Z)
            var headZ = Mathf.Max(scaled.max.z * 0.8f, 0.1f);

            var head = mallet.transform.Find("Head");
            if (head == null)
            {
                var headGo = new GameObject("Head");
                headGo.transform.SetParent(mallet.transform, false);
                head = headGo.transform;
            }

            head.localPosition = new Vector3(0f, 0f, headZ);
            EditorUtility.SetDirty(mallet);

            Log.Add($"{mallet.name}: size={longest:F2} scale={scale:F3} headZ={headZ:F2}");
        }

        /// <summary>
        /// В FBX отдельными узлами лежат рука (RightHand/RightHand_Mesh) и сам молот
        /// (StormMallet/StormMallet_Mesh) — узел руки удаляем, оставляем только молот.
        /// </summary>
        static void StripHandNodes(GameObject mallet)
        {
            var remove = new System.Collections.Generic.List<GameObject>();
            CollectHandNodes(mallet.transform, remove);

            foreach (var go in remove)
            {
                Log.Add("removed hand node: " + go.name);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static void CollectHandNodes(Transform parent, System.Collections.Generic.List<GameObject> result)
        {
            foreach (Transform child in parent)
            {
                if (child.name.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(child.gameObject);
                    continue; // внутрь не лезем — удалим целиком
                }

                CollectHandNodes(child, result);
            }
        }

        /// <summary>Границы объекта в локальном пространстве (до поворотов).</summary>
        static Bounds GetLocalBounds(Transform root)
        {
            var world = GetWorldBounds(root.gameObject);
            var center = root.InverseTransformPoint(world.center);
            var extents = root.InverseTransformVector(world.extents);
            return new Bounds(center,
                new Vector3(Mathf.Abs(extents.x), Mathf.Abs(extents.y), Mathf.Abs(extents.z)) * 2f);
        }

        static Bounds GetWorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one * 0.5f);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static void HideHandVisual(GameObject handGo)
        {
            var visual = handGo.transform.Find("Visual");
            if (visual != null)
                visual.gameObject.SetActive(false);
        }

        static void ConvertMaterials(GameObject go)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    var converted = ToUrpMaterial(materials[i]);
                    if (converted != null && converted != materials[i])
                    {
                        materials[i] = converted;
                        changed = true;
                    }
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }

        static Material ToUrpMaterial(Material source)
        {
            if (source == null)
                return null;

            if (source.shader != null && source.shader.name.StartsWith("Universal Render Pipeline"))
                return source;

            const string folder = "Assets/RagnarockEditor/Materials/Converted";
            EnsureFolder(folder);

            var path = folder + "/" + Sanitize(source.name) + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                return source;

            var material = new Material(shader) { name = source.name };
            if (source.HasProperty("_Color"))
                material.SetColor("_BaseColor", source.GetColor("_Color"));
            material.SetInt("_Cull", 0); // двусторонний: левая рука — зеркальная копия

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(name.Length);
            foreach (var symbol in name)
                builder.Append(Array.IndexOf(invalid, symbol) >= 0 ? '_' : symbol);
            return builder.Length == 0 ? "Material" : builder.ToString();
        }

        static void EnsurePointer(GameObject handGo)
        {
            var pointer = handGo.GetComponent<HandUIPointer>();
            if (pointer == null)
                pointer = handGo.AddComponent<HandUIPointer>();

            var pointerSo = new SerializedObject(pointer);
            var blendProperty = pointerSo.FindProperty("_aimBlend");
            if (blendProperty != null)
                blendProperty.floatValue = 0.5f;
            pointerSo.ApplyModifiedPropertiesWithoutUndo();

            var line = handGo.GetComponent<LineRenderer>();
            if (line == null)
                line = handGo.AddComponent<LineRenderer>();

            var laser = GetLaserMaterial();
            if (laser != null)
                line.sharedMaterial = laser;

            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = 0.006f;
            line.endWidth = 0.002f;
            line.numCapVertices = 4;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        static Material GetLaserMaterial()
        {
            const string path = "Assets/RagnarockEditor/Materials/UI_Laser.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            material = new Material(shader) { name = "UI_Laser" };
            var color = new Color(1f, 0.55f, 0.25f, 1f);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            AssetDatabase.CreateAsset(material, path);
            Log.Add("created UI_Laser material");
            return material;
        }

        static void EnsureLevelExporter(Scene scene)
        {
            if (UnityEngine.Object.FindFirstObjectByType<LevelExporter>() != null)
                return;

            var go = new GameObject("LevelExporter");
            go.AddComponent<LevelExporter>();
            Log.Add(scene.name + ": LevelExporter");
        }

        static void EnsureSongPlayer(Scene scene)
        {
            if (UnityEngine.Object.FindFirstObjectByType<SongPlaybackBootstrap>() != null)
                return;

            var go = new GameObject("SongPlayer");
            go.AddComponent<AudioSource>();
            go.AddComponent<SongPlaybackBootstrap>();
            Log.Add(scene.name + ": SongPlayer");
        }

        static void EnsureDrums(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DrumPrefabPath);
            var config = AssetDatabase.LoadAssetAtPath<DrumLayoutConfig>(DrumConfigPath);
            var origin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();

            var manager = UnityEngine.Object.FindFirstObjectByType<DrumManager>(FindObjectsInactive.Include);

            if (manager == null)
            {
                var root = new GameObject("Drums");
                var drumsParent = new GameObject("DrumsParent");
                drumsParent.transform.SetParent(root.transform, false);
                manager = root.AddComponent<DrumManager>();

                var managerSo = new SerializedObject(manager);
                managerSo.FindProperty("_drumPrefab").objectReferenceValue = prefab;
                managerSo.FindProperty("_drumsParent").objectReferenceValue = drumsParent.transform;
                managerSo.ApplyModifiedPropertiesWithoutUndo();

                Log.Add(scene.name + ": создан DrumManager + DrumsParent");
            }
            else
            {
                var managerSo = new SerializedObject(manager);
                var prefabProperty = managerSo.FindProperty("_drumPrefab");
                if (prefabProperty != null && prefabProperty.objectReferenceValue == null && prefab != null)
                {
                    prefabProperty.objectReferenceValue = prefab;
                    managerSo.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            var positioner = manager.GetComponent<DrumPositioner>();
            if (positioner == null)
                positioner = UnityEngine.Object.FindFirstObjectByType<DrumPositioner>(FindObjectsInactive.Include);
            if (positioner == null)
                positioner = manager.gameObject.AddComponent<DrumPositioner>();

            var positionerSo = new SerializedObject(positioner);
            var configProperty = positionerSo.FindProperty("_config");
            if (configProperty != null && configProperty.objectReferenceValue == null && config != null)
                configProperty.objectReferenceValue = config;

            var originProperty = positionerSo.FindProperty("_xrOrigin");
            if (originProperty != null && originProperty.objectReferenceValue == null && origin != null)
                originProperty.objectReferenceValue = origin.transform;

            positionerSo.ApplyModifiedPropertiesWithoutUndo();

            if (manager.GetComponent<DrumSceneBootstrap>() == null)
            {
                manager.gameObject.AddComponent<DrumSceneBootstrap>();
                Log.Add(scene.name + ": добавлен DrumSceneBootstrap");
            }
        }

        /// <summary>
        /// Под URP материалы со встроенным Standard-шейдером (Ground/TestCube) рендерятся magenta.
        /// </summary>
        // ------------------------------------------------------------ Stone UI

        const string StoneUiFolder = "Assets/StoneUI/";

        static void BuildStoneUi(Scene scene)
        {
            var isMain = scene.name.Contains("MainMenu");
            var isRecord = scene.name.Contains("Record");
            var isSong = scene.name.Contains("SongSelection");
            var isPreview = scene.name.Contains("Preview");
            if (!isMain && !isRecord && !isSong && !isPreview)
                return;

            var existing = FindIncludingInactive("StoneUI");
            var root = existing != null ? existing.transform : new GameObject("StoneUI").transform;

            if (isMain)
            {
                CreateStoneOrUpdate(root, "OpenMP3", "OpenMP3.fbx", new Vector3(-0.34f, 1.32f, 1.20f),
                    StoneButton.Action.JumpScene, "SongSelectionScene");
                CreateStoneOrUpdate(root, "Record", "Record.fbx", new Vector3(0f, 1.32f, 1.20f),
                    StoneButton.Action.NewLevel, string.Empty);
                CreateStoneOrUpdate(root, "Settings", "Settings.fbx", new Vector3(0.34f, 1.32f, 1.20f),
                    StoneButton.Action.Help, string.Empty);
                CreateStoneOrUpdate(root, "Back", "Back.fbx", new Vector3(0f, 1.12f, 1.20f),
                    StoneButton.Action.Quit, string.Empty);
            }
            else if (isRecord)
            {
                const float y = 1.18f;
                const float z = 1.20f;
                CreateStoneOrUpdate(root, "Play", "Play.fbx", new Vector3(-0.21f, y, z), StoneButton.Action.Play, string.Empty);
                CreateStoneOrUpdate(root, "Pause", "Pause.fbx", new Vector3(-0.07f, y, z), StoneButton.Action.Pause, string.Empty);
                CreateStoneOrUpdate(root, "Stop", "Stop.fbx", new Vector3(0.07f, y, z), StoneButton.Action.Stop, string.Empty);
                CreateStoneOrUpdate(root, "Replay", "Replay.fbx", new Vector3(0.21f, y, z), StoneButton.Action.Restart, string.Empty);
                // Back идёт через ScreenRouter: он спросит про несохранённый дубль
                CreateStoneOrUpdate(root, "Back", "Back.fbx", new Vector3(0.35f, y, z), StoneButton.Action.Back, string.Empty);
                CreateStoneOrUpdate(root, "Settings", "Settings.fbx", new Vector3(-0.35f, y, z), StoneButton.Action.Calibrate, string.Empty);
                Log.Add(scene.name + ": Stone UI записи");
            }
            else if (isSong)
            {
                CreateStoneOrUpdate(root, "OpenMP3Title", "OpenMP3.fbx", new Vector3(0f, 1.78f, 1.26f),
                    StoneButton.Action.JumpScene, string.Empty);
                // Back вынесен за пределы панели: она стала плотной и иначе закрыла бы плиту
                CreateStoneOrUpdate(root, "Back", "Back.fbx", new Vector3(-0.62f, 1.30f, 1.20f),
                    StoneButton.Action.Back, string.Empty);
                Log.Add(scene.name + ": Stone UI выбора песни");
            }
            else if (isPreview)
            {
                const float y = 1.18f;
                const float z = 1.20f;
                CreateStoneOrUpdate(root, "Back", "Back.fbx", new Vector3(-0.38f, y, z), StoneButton.Action.Back, string.Empty);
                CreateStoneOrUpdate(root, "Play", "Play.fbx", new Vector3(-0.24f, y, z), StoneButton.Action.Play, string.Empty);
                CreateStoneOrUpdate(root, "Pause", "Pause.fbx", new Vector3(-0.10f, y, z), StoneButton.Action.Pause, string.Empty);
                CreateStoneOrUpdate(root, "Stop", "Stop.fbx", new Vector3(0.04f, y, z), StoneButton.Action.Stop, string.Empty);
                // «Повторить»: Stop + Play с начала в предпросмотре
                CreateStoneOrUpdate(root, "Replay", "Replay.fbx", new Vector3(0.18f, y, z), StoneButton.Action.Restart, string.Empty);
                CreateStoneOrUpdate(root, "ExportMap", "ExportMap.fbx", new Vector3(0.38f, 1.02f, z), StoneButton.Action.Export, string.Empty);
                Log.Add(scene.name + ": Stone UI предпросмотра");
            }

            // uGUI-кнопки, которые заменили каменными
            if (isMain)
            {
                RemoveUguiButtons("NewRecordButton", "SongButton", "QuitButton");
                DestroyAllNamed("MainMenuCanvas");
            }
            else if (isRecord)
            {
                RemoveUguiButtons("StartButton", "AnotherButton", "CancelButton",
                    "PauseButton", "StopButton", "MenuButton");
            }
            else if (isSong)
            {
                RemoveUguiButtons("BackButton");
            }
            else if (isPreview)
            {
                RemoveUguiButtons("MenuButton");
            }
        }

        /// <summary>
        /// Ставит каменную кнопку, а если такая уже есть — обновляет действие и позицию.
        /// Без этого повторный прогон фиксера не менял бы поведение уже собранных сцен.
        /// </summary>
        static GameObject CreateStoneOrUpdate(Transform parent, string name, string fileName, Vector3 position,
            StoneButton.Action action, string sceneName)
        {
            var existing = parent.Find(name);
            if (existing == null)
                return CreateStone(parent, name, fileName, position, action, sceneName);

            var button = existing.GetComponent<StoneButton>();
            if (button == null)
                button = existing.gameObject.AddComponent<StoneButton>();

            var so = new SerializedObject(button);
            var actionProperty = so.FindProperty("_action");
            if (actionProperty != null)
                actionProperty.enumValueIndex = (int)action;

            var sceneProperty = so.FindProperty("_sceneName");
            if (sceneProperty != null)
                sceneProperty.stringValue = sceneName ?? string.Empty;

            so.ApplyModifiedPropertiesWithoutUndo();

            existing.localPosition = position;
            existing.localRotation = Quaternion.Euler(0f, 180f, 0f);
            existing.localScale = Vector3.one;

            return existing.gameObject;
        }

        static GameObject CreateStone(Transform parent, string name, string fileName, Vector3 position,
            StoneButton.Action action, string sceneName)
        {
            var path = StoneUiFolder + fileName;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                Log.Add("нет модели " + path);
                return null;
            }

            var go = PrefabUtility.InstantiatePrefab(model, parent) as GameObject;
            if (go == null)
                return null;

            if (PrefabUtility.IsPartOfPrefabInstance(go))
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);

            go.name = name;
            go.transform.localPosition = position;
            // модель смотрит в +Z, игрок стоит лицом к +Z — разворачиваем к игроку
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = Vector3.one;

            ConvertMaterials(go);

            var bounds = GetWorldBounds(go);
            var lossy = go.transform.lossyScale;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(
                bounds.size.x / Mathf.Max(Mathf.Abs(lossy.x), 0.0001f),
                bounds.size.y / Mathf.Max(Mathf.Abs(lossy.y), 0.0001f),
                Mathf.Max(bounds.size.z / Mathf.Max(Mathf.Abs(lossy.z), 0.0001f), 0.02f));
            box.center = go.transform.InverseTransformPoint(bounds.center);

            var button = go.AddComponent<StoneButton>();
            var so = new SerializedObject(button);
            var actionProperty = so.FindProperty("_action");
            if (actionProperty != null)
                actionProperty.enumValueIndex = (int)action;
            var sceneProperty = so.FindProperty("_sceneName");
            if (sceneProperty != null)
                sceneProperty.stringValue = sceneName ?? string.Empty;
            so.ApplyModifiedPropertiesWithoutUndo();

            return go;
        }

        static void RemoveUguiButtons(params string[] names)
        {
            foreach (var name in names)
            {
                var go = GameObject.Find(name);
                if (go == null)
                    continue;

                UnityEngine.Object.DestroyImmediate(go);
                Log.Add("убрана uGUI-кнопка " + name);
            }
        }

        static void HideCanvas(string name)
        {
            var go = GameObject.Find(name);
            if (go != null && go.activeSelf)
                go.SetActive(false);
        }

        /// <summary>Удаляет все объекты с таким именем, включая выключенные (GameObject.Find их не видит).</summary>
        static void DestroyAllNamed(string name)
        {
            var transforms = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var transform in transforms)
            {
                if (transform.name != name)
                    continue;

                UnityEngine.Object.DestroyImmediate(transform.gameObject);
                Log.Add("удалён " + name);
            }
        }

        // ------------------------------------------------- UI на палубе

        /// <summary>
        /// Интерфейс крепится к палубе (XR Origin), а не «плавает» за головой:
        /// меню всегда на одном месте — над барабанами, в той же системе координат,
        /// что и расстановка барабанов (DrumPositioner считает её от XR Origin).
        /// </summary>
        static void BuildUIRig(Scene scene)
        {
            var origin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();

            var rig = FindIncludingInactive("UIRig");
            if (rig == null)
                rig = new GameObject("UIRig");

            if (origin != null)
                rig.transform.SetParent(origin.transform, false);

            rig.transform.localPosition = Vector3.zero;
            rig.transform.localRotation = Quaternion.identity;
            rig.transform.localScale = Vector3.one;

            // Follow-риг из прошлых итераций больше не нужен: панели не должны ездить за головой
            var follower = rig.GetComponent<PlayerSpaceFollower>();
            if (follower != null)
                UnityEngine.Object.DestroyImmediate(follower);

            PlacePanel("HUDCanvas", new Vector3(0f, 1.75f, 1.20f), 0.0011f, rig.transform);
            PlacePanel("Fjord Scroll List", new Vector3(0f, 1.45f, 1.20f), 0.0011f, rig.transform);
            PlacePanel("SongSelectionCanvas", new Vector3(0f, 1.45f, 1.20f), 0.0011f, rig.transform);
            PlacePanel("BpmCanvas", new Vector3(0f, 1.66f, 1.20f), 0.0011f, rig.transform);
            PlacePanel("CalibrationCanvas", new Vector3(0f, 1.50f, 1.10f), 0.0011f, rig.transform);
            PlacePanel("IntroCanvas", new Vector3(0f, 1.50f, 1.20f), 0.0011f, rig.transform);
            PlacePanel("ExitCanvas", new Vector3(0f, 1.45f, 1.06f), 0.0011f, rig.transform);
            PlaceBoard("BpmStoneControls", new Vector3(0f, 1.20f, 1.19f), 180f, rig.transform);
            PlaceBoard("RecordStoneControls", new Vector3(0f, 1.20f, 1.19f), 180f, rig.transform);
            PlaceBoard("PreviewStoneControls", new Vector3(0f, 1.20f, 1.19f), 180f, rig.transform);

            PlaceAtRig("StoneUI", rig.transform);
            PlaceAtRig("SongSelectionFlow", rig.transform);

            Log.Add(scene.name + ": UI закреплён на палубе" + (origin != null ? " (" + origin.name + ")" : string.Empty));
        }

        static void PlacePanel(string name, Vector3 localPosition, float scale, Transform rig)
        {
            var go = FindIncludingInactive(name);
            if (go == null)
                return;

            go.transform.SetParent(rig, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * scale;

            // У RectTransform позицию задаёт anchoredPosition, а не localPosition:
            // без этого сцены сохраняли старое смещение панели (она «не переезжала»).
            if (go.transform is RectTransform rect)
                rect.anchoredPosition = new Vector2(localPosition.x, localPosition.y);

            var panel = go.GetComponent<WorldUIPanel>();
            if (panel != null)
                panel.enabled = false;
        }

        static void PlaceAtRig(string name, Transform rig)
        {
            var go = FindIncludingInactive(name);
            if (go == null)
                return;

            go.transform.SetParent(rig, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }

        // ------------------------------------------------------------ окружение

        static void BuildEnvironment()
        {
            // уже собранное окружение не пересобираем (иначе удалим свой же Ground)
            var existingEnvironment = GameObject.Find("Environment");
            if (existingEnvironment != null && (existingEnvironment.transform.Find("Village/Longhall") == null
                || existingEnvironment.transform.Find("Deck") != null
                || existingEnvironment.transform.Find("Mountains") != null))
            {
                UnityEngine.Object.DestroyImmediate(existingEnvironment);
                existingEnvironment = null;
                Log.Add("окружение пересобирается (в старой версии нет деревни)");
            }

            if (existingEnvironment != null)
            {
                RemoveIfExists("TestCube_VR_OK");
                ConfigureLighting();
                return;
            }

            RemoveIfExists("TestCube_VR_OK");
            RemoveIfExists("Ground");

            ConfigureLighting();

            var root = new GameObject("Environment").transform;

            var snow = GetEnvMaterial("Env_Snow", new Color(0.86f, 0.90f, 0.96f), 0.15f);
            var pine = GetEnvMaterial("Env_Pine", new Color(0.16f, 0.27f, 0.31f), 0.05f);
            var trunk = GetEnvMaterial("Env_Trunk", new Color(0.28f, 0.21f, 0.16f), 0.05f);

            // земля
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            DestroyCollider(ground);
            ground.transform.SetParent(root, false);
            ground.transform.localPosition = new Vector3(0f, 0f, 0f);
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            ground.GetComponent<Renderer>().sharedMaterial = snow;

            // деревянный настил под игроком (палуба)
            var cone = EnsureConeMesh();
            var random = new System.Random(20261121);

            var trees = new GameObject("Trees").transform;
            trees.SetParent(root, false);

            var treeModels = new[]
            {
                "tree_pineTallA", "tree_pineTallB", "tree_pineTallC",
                "tree_pineDefaultA", "tree_pineDefaultB", "tree_pineRoundA", "tree_cone",
            };

            var fjordSpruces = new[] { "SpruceTall", "SpruceBroad", "SpruceSpire", "SpruceYoung" };

            for (int i = 0; i < 70; i++)
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var distance = 8f + (float)random.NextDouble() * 34f;
                if (distance < 12f && Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, 90f)) < 35f)
                    distance += 12f;

                var position = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                var height = 4.5f + (float)random.NextDouble() * 5.5f;
                var model = treeModels[random.Next(treeModels.Length)];
                var yaw = (float)random.NextDouble() * 360f;

                var fjordTree = CreateFjordProp(trees, fjordSpruces[random.Next(fjordSpruces.Length)], position, yaw,
                    0.9f + (float)random.NextDouble() * 0.45f);
                if (fjordTree != null)
                    continue;

                var kenneyTree = CreateKenneyProp(trees, model, position, height, yaw);
                if (kenneyTree != null)
                    continue;

                var fallback = new GameObject("Tree_" + i).transform;
                fallback.SetParent(trees, false);
                fallback.localPosition = position;
                fallback.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);

                var trunkGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                trunkGo.name = "Trunk";
                DestroyCollider(trunkGo);
                trunkGo.transform.SetParent(fallback, false);
                trunkGo.transform.localPosition = new Vector3(0f, height * 0.15f, 0f);
                trunkGo.transform.localScale = new Vector3(0.22f, height * 0.15f, 0.22f);
                trunkGo.GetComponent<Renderer>().sharedMaterial = trunk;

                CreateCone(fallback, "Crown", cone, new Vector3(0f, height * 0.2f, 0f),
                    height * 0.55f, height * 0.85f, pine, random);
            }

            var propsRoot = new GameObject("Props").transform;
            propsRoot.SetParent(root, false);
            var propModels = new[] { "rock_largeA", "rock_largeB", "rock_smallA", "rock_tallA", "log", "stump_round", "plant_bush" };
            var fjordRocks = new[] { "Boulder", "RockCluster", "StandingStone", "Slab" };

            for (int i = 0; i < 26; i++)
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var distance = 10f + (float)random.NextDouble() * 28f;
                if (Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, 90f)) < 40f)
                    distance += 10f;
                var position = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                var height = 0.5f + (float)random.NextDouble() * 1.2f;
                CreateKenneyProp(propsRoot, propModels[random.Next(propModels.Length)], position, height,
                    (float)random.NextDouble() * 360f);
            }

            var village = new GameObject("Village").transform;
            village.SetParent(root, false);
            CreateHouse(village, "Longhall", new Vector3(-15f, 0f, 13f), 65f, 9f);
            CreateHouse(village, "HearthCabin", new Vector3(16f, 0f, 15f), -70f, 7f);
            CreateHouse(village, "RaisedStorehouse", new Vector3(-19f, 0f, -11f), 140f, 6f);
            CreateHouse(village, "Longhall", new Vector3(14f, 0f, -17f), -140f, 8f);

            CreateSnowfall();

            Log.Add("окружение собрано (снежная долина: земля, горы, ели, настил)");
        }

        static void DestroyCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
        }

        static void CreateCone(Transform parent, string name, Mesh mesh, Vector3 position,
            float width, float height, Material material, System.Random random)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            go.transform.localScale = new Vector3(width, height, width);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        const string FjordFolder = "Assets/RagnarockEditor/Models/Environment/WinterFjord/";

        static void SetPaletteImporter(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                return;

            if (importer.filterMode == FilterMode.Point && !importer.mipmapEnabled)
                return;

            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static Material GetFjordMaterial()
        {
            const string path = "Assets/RagnarockEditor/Materials/Env/Env_Fjord.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            EnsureFolder("Assets/RagnarockEditor/Materials/Env");

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                return null;

            SetPaletteImporter(FjordFolder + "FjordPalette.png");

            material = new Material(shader) { name = "Env_Fjord" };
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.05f);

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(FjordFolder + "FjordPalette.png");
            if (texture != null)
                material.SetTexture("_BaseMap", texture);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static GameObject CreateFjordProp(Transform parent, string modelName, Vector3 position, float yaw, float scale)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FjordFolder + modelName + ".fbx");
            if (model == null)
                return null;

            var go = PrefabUtility.InstantiatePrefab(model, parent) as GameObject;
            if (go == null)
                return null;

            if (PrefabUtility.IsPartOfPrefabInstance(go))
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);

            go.name = modelName;

            var material = GetFjordMaterial();
            if (material != null)
            {
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterial = material;
            }

            go.transform.localScale = Vector3.one * Mathf.Max(0.05f, scale);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }

        static void CreateHouse(Transform parent, string modelName, Vector3 position, float yaw, float fallbackLength)
        {
            if (CreateFjordProp(parent, modelName, position, yaw, 1f) != null)
                return;

            CreateLonghouse(parent, position, yaw, fallbackLength);
        }

        const string KenneyFolder = "Assets/RagnarockEditor/Models/Environment/Kenney/";

        static GameObject CreateKenneyProp(Transform parent, string modelName, Vector3 position,
            float targetHeight, float yaw)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(KenneyFolder + modelName + ".fbx");
            if (model == null)
                return null;

            var go = PrefabUtility.InstantiatePrefab(model, parent) as GameObject;
            if (go == null)
                return null;

            if (PrefabUtility.IsPartOfPrefabInstance(go))
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);

            go.name = modelName;

            var material = GetKenneyMaterial();
            if (material != null)
            {
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterial = material;
            }

            var size = GetWorldBounds(go).size;
            var longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            var scale = longest > 0.0001f ? targetHeight / longest : 1f;
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // сажаем модель на землю по фактической нижней границе
            var bounds = GetWorldBounds(go);
            var correction = parent.position.y - bounds.min.y;
            go.transform.localPosition += new Vector3(0f, correction, 0f);

            return go;
        }

        static Material GetKenneyMaterial()
        {
            const string path = "Assets/RagnarockEditor/Materials/Env/Env_Kenney.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            EnsureFolder("Assets/RagnarockEditor/Materials/Env");

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                return null;

            material = new Material(shader) { name = "Env_Kenney" };
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.05f);

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(KenneyFolder + "colormap.png");
            if (texture != null)
                material.SetTexture("_BaseMap", texture);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void CreateLonghouse(Transform parent, Vector3 position, float yaw, float length)
        {
            var house = new GameObject("Longhouse");
            house.transform.SetParent(parent, false);
            house.transform.localPosition = position;
            house.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var wall = GetEnvMaterial("Env_HouseWall", new Color(0.31f, 0.22f, 0.16f), 0.05f);
            var roof = GetEnvMaterial("Env_HouseRoof", new Color(0.88f, 0.91f, 0.96f), 0.1f);
            var door = GetEnvMaterial("Env_HouseDoor", new Color(0.15f, 0.11f, 0.08f), 0.05f);

            const float width = 4.5f;
            const float wallHeight = 3f;

            var walls = GameObject.CreatePrimitive(PrimitiveType.Cube);
            walls.name = "Walls";
            DestroyCollider(walls);
            walls.transform.SetParent(house.transform, false);
            walls.transform.localPosition = new Vector3(0f, wallHeight * 0.5f, 0f);
            walls.transform.localScale = new Vector3(width, wallHeight, length);
            walls.GetComponent<Renderer>().sharedMaterial = wall;

            for (int side = -1; side <= 1; side += 2)
            {
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = side < 0 ? "RoofLeft" : "RoofRight";
                DestroyCollider(slab);
                slab.transform.SetParent(house.transform, false);
                slab.transform.localPosition = new Vector3(side * width * 0.25f, wallHeight + 0.6f, 0f);
                slab.transform.localRotation = Quaternion.Euler(0f, 0f, side * 32f);
                slab.transform.localScale = new Vector3(width * 0.62f, 0.35f, length + 0.6f);
                slab.GetComponent<Renderer>().sharedMaterial = roof;
            }

            var entry = GameObject.CreatePrimitive(PrimitiveType.Cube);
            entry.name = "Door";
            DestroyCollider(entry);
            entry.transform.SetParent(house.transform, false);
            entry.transform.localPosition = new Vector3(0f, 0.9f, length * 0.5f + 0.03f);
            entry.transform.localScale = new Vector3(1.1f, 1.8f, 0.12f);
            entry.GetComponent<Renderer>().sharedMaterial = door;
        }

        static void CreateSnowfall()
        {
            if (GameObject.Find("Snowfall") != null)
                return;

            var rig = GameObject.Find("XR Origin");
            var go = new GameObject("Snowfall");
            if (rig != null)
                go.transform.SetParent(rig.transform, false);
            go.transform.localPosition = new Vector3(0f, 4.5f, 0f);

            var system = go.AddComponent<ParticleSystem>();

            var main = system.main;
            main.startLifetime = 8f;
            main.startSpeed = 0.5f;
            main.startSize = 0.035f;
            main.startColor = new Color(1f, 1f, 1f, 0.9f);
            main.gravityModifier = 0.06f;
            main.maxParticles = 3000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;

            var emission = system.emission;
            emission.rateOverTime = 150f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(16f, 0.2f, 16f);

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            var material = GetSnowflakeMaterial();
            if (material != null)
                particleRenderer.sharedMaterial = material;
        }

        static Material GetSnowflakeMaterial()
        {
            const string path = "Assets/RagnarockEditor/Materials/Env/Env_Snowflake.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            EnsureFolder("Assets/RagnarockEditor/Materials/Env");

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            material = new Material(shader) { name = "Env_Snowflake" };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void RemoveIfExists(string name)
        {
            var go = GameObject.Find(name);
            if (go == null)
                return;

            UnityEngine.Object.DestroyImmediate(go);
            Log.Add("удалён " + name);
        }

        static void ConfigureLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.58f, 0.65f, 0.78f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.016f;
            RenderSettings.fogColor = new Color(0.78f, 0.84f, 0.93f);

            var skybox = GetSkyboxMaterial();
            if (skybox != null)
                RenderSettings.skybox = skybox;

            var light = UnityEngine.Object.FindFirstObjectByType<Light>();
            if (light != null && light.type == LightType.Directional)
            {
                light.color = new Color(0.92f, 0.95f, 1f);
                light.intensity = 1.15f;
                light.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
                light.shadows = LightShadows.Soft;
            }
        }

        static Material GetSkyboxMaterial()
        {
            const string panoramaPath = "Assets/RagnarockEditor/Textures/Sky/pizzo_pernice_puresky_4k.jpg";
            const string materialPath = "Assets/RagnarockEditor/Materials/Env/Env_Skybox.mat";

            EnsureFolder("Assets/RagnarockEditor/Materials/Env");

            var panorama = AssetDatabase.LoadAssetAtPath<Texture2D>(panoramaPath);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (existing != null)
            {
                if (panorama == null || existing.shader.name == "Skybox/Panoramic")
                    return existing;

                AssetDatabase.DeleteAsset(materialPath);
                existing = null;
            }

            if (panorama != null && AssetImporter.GetAtPath(panoramaPath) is TextureImporter importer)
            {
                if (importer.maxTextureSize < 4096 || importer.wrapMode != TextureWrapMode.Clamp)
                {
                    importer.maxTextureSize = 4096;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.SaveAndReimport();
                }
            }

            var panoramicShader = panorama != null ? Shader.Find("Skybox/Panoramic") : null;
            if (panoramicShader != null)
            {
                var panoramic = new Material(panoramicShader) { name = "Env_Skybox" };
                panoramic.SetTexture("_MainTex", panorama);
                panoramic.SetFloat("_Mapping", 0f);
                panoramic.SetFloat("_ImageType", 0f);
                panoramic.SetFloat("_Exposure", 1f);
                AssetDatabase.CreateAsset(panoramic, materialPath);
                Log.Add("скайбокс: панорама pizzo_pernice_puresky");
                return panoramic;
            }

            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
                return null;

            var procedural = new Material(shader) { name = "Env_Skybox" };
            procedural.SetColor("_SkyTint", new Color(0.52f, 0.63f, 0.86f));
            procedural.SetColor("_GroundColor", new Color(0.82f, 0.87f, 0.94f));
            procedural.SetFloat("_AtmosphereThickness", 0.75f);
            procedural.SetFloat("_Exposure", 1.15f);
            AssetDatabase.CreateAsset(procedural, materialPath);
            return procedural;
        }
        static Material GetEnvMaterial(string name, Color color, float smoothness)
        {
            const string folder = "Assets/RagnarockEditor/Materials/Env";
            EnsureFolder(folder);

            var path = folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                return null;

            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Mesh EnsureConeMesh()
        {
            const string path = "Assets/RagnarockEditor/Models/Env/Cone.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
                return mesh;

            EnsureFolder("Assets/RagnarockEditor/Models/Env");

            const int segments = 8;
            var vertices = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();

            vertices.Add(new Vector3(0f, 1f, 0f));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 1f));

            for (int i = 0; i <= segments; i++)
            {
                var angle = i / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * 0.5f, 0f, Mathf.Sin(angle) * 0.5f));
                normals.Add(new Vector3(Mathf.Cos(angle), 0.5f, Mathf.Sin(angle)).normalized);
                uvs.Add(new Vector2(i / (float)segments, 0f));
            }

            for (int i = 1; i <= segments; i++)
            {
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(i + 1);
            }

            var baseCenter = vertices.Count;
            vertices.Add(Vector3.zero);
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 1; i <= segments; i++)
            {
                triangles.Add(baseCenter);
                triangles.Add(i + 1);
                triangles.Add(i);
            }

            mesh = new Mesh { name = "Cone" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ------------------------------------------------------------ скин кнопок

        /// <summary>
        /// Если в проекте импортирован паК со спрайтами кнопок (например, GUI Basic Button Pack),
        /// натягиваем их на все uGUI-кнопки: 9-slice + состояния hover/pressed.
        /// </summary>
        /// <summary>
        /// Все uGUI-кнопки становятся каменными плитами: Image со спрайтом заменяется
        /// на <see cref="StonePlate"/>, надпись — слоновой костью, под лучом плита светлеет.
        /// Спрайтовый «скин» LayerLab больше не используется: интерфейс целиком каменный.
        /// </summary>
        static void ApplyButtonSkin()
        {
            var converted = StoneUiKit.StyleAllButtonsInScene();
            Log.Add($"каменные кнопки: переделано плит — {converted}");
        }

        static void FixBuiltInMaterials()
        {
            var fallback = GetUrpFallbackMaterial();
            if (fallback == null)
                return;

            var replaced = 0;
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    var shaderName = material != null && material.shader != null ? material.shader.name : null;

                    if (shaderName == null || shaderName == "Standard" || shaderName == "Default-Material" ||
                        shaderName == "Hidden/InternalErrorShader")
                    {
                        materials[i] = fallback;
                        changed = true;
                        replaced++;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                    EditorUtility.SetDirty(renderer);
                }
            }

            if (replaced > 0)
                Log.Add("replaced " + replaced + " built-in material slot(s) with URP/Lit");
        }

        static Material GetUrpFallbackMaterial()
        {
            const string path = "Assets/RagnarockEditor/Materials/Default_URP.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Log.Add("WARN: URP/Lit shader not found");
                return null;
            }

            material = new Material(shader) { name = "Default_URP" };
            AssetDatabase.CreateAsset(material, path);
            Log.Add("created Default_URP material");
            return material;
        }

        static void EnsureInteractionManager()
        {
            if (UnityEngine.Object.FindFirstObjectByType<XRInteractionManager>() != null)
                return;

            var go = new GameObject("XR Interaction Manager");
            go.AddComponent<XRInteractionManager>();
            Log.Add("created XR Interaction Manager");
        }

        static Camera FindMainCamera()
        {
            var camera = Camera.main;
            if (camera != null)
                return camera;

            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            return cameras.Length > 0 ? cameras[0] : null;
        }

        static Transform GetOrCreateCameraOffset(XROrigin origin)
        {
            var so = new SerializedObject(origin);
            var property = so.FindProperty("m_CameraFloorOffsetObject");
            var go = property != null ? property.objectReferenceValue as GameObject : null;

            if (go == null)
            {
                go = new GameObject("Camera Offset");
                go.transform.SetParent(origin.transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                if (property != null)
                {
                    property.objectReferenceValue = go;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            return go.transform;
        }

        static void WireOrigin(XROrigin origin, Camera camera, Transform offset)
        {
            var so = new SerializedObject(origin);

            var cameraProperty = so.FindProperty("m_Camera");
            if (cameraProperty != null && cameraProperty.objectReferenceValue == null)
                cameraProperty.objectReferenceValue = camera;

            var offsetProperty = so.FindProperty("m_CameraFloorOffsetObject");
            if (offsetProperty != null && offsetProperty.objectReferenceValue == null)
                offsetProperty.objectReferenceValue = offset.gameObject;

            so.ApplyModifiedPropertiesWithoutUndo();

            // Floor-режим: рост берётся из позы шлема, смещение камеры по Y не нужно
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 0f;
            EditorUtility.SetDirty(origin);
        }

        static void WireCamera(Camera camera)
        {
            var go = camera.gameObject;

            if (go.GetComponent<AudioListener>() == null)
                go.AddComponent<AudioListener>();

            if (go.GetComponent<UniversalAdditionalCameraData>() == null)
                go.AddComponent<UniversalAdditionalCameraData>();

            // чтобы были видны небо и дальние горы
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, 400f);

            var brokenDriver = go.GetComponent<TrackedPoseDriver>();
            if (brokenDriver != null)
            {
                // Экшены, созданные из кода, сериализуются без биндингов => поза не читается.
                UnityEngine.Object.DestroyImmediate(brokenDriver);
                Log.Add("removed binding-less TrackedPoseDriver from " + go.name);
            }

            if (go.GetComponent<HMDPoseDriver>() == null)
                go.AddComponent<HMDPoseDriver>();

            EditorUtility.SetDirty(go);
        }

        static void WireUi(Camera camera)
        {
            var eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var go = new GameObject("EventSystem");
                eventSystem = go.AddComponent<EventSystem>();
            }

            var hasXrModule = false;
            foreach (var module in eventSystem.GetComponents<BaseInputModule>())
            {
                if (module is XRUIInputModule)
                {
                    hasXrModule = true;
                    continue;
                }

                // StandaloneInputModule падает на активном Input System
                UnityEngine.Object.DestroyImmediate(module);
            }

            if (!hasXrModule)
                eventSystem.gameObject.AddComponent<XRUIInputModule>();

            EditorUtility.SetDirty(eventSystem);

            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.renderMode != RenderMode.WorldSpace)
                    continue;

                if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                    canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

                if (canvas.worldCamera == null)
                    canvas.worldCamera = camera;

                EditorUtility.SetDirty(canvas);
            }
        }

        // --------------------------------------------------------------- helpers

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        static void WriteReport(string status)
        {
            try
            {
                var directory = Path.GetDirectoryName(ReportPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var lines = new List<string> { status, DateTime.Now.ToString("u") };
                lines.AddRange(Log);
                File.WriteAllLines(ReportPath, lines);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VRFix] report write failed: " + e.Message);
            }
        }
    }
}

