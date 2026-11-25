using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;
using RagnarockEditor.Core;
using RagnarockEditor.Data;
using RagnarockEditor.Quantization;
using RagnarockEditor.VR;
using RagnarockEditor.Playback;
using RagnarockEditor.Recording;
using RagnarockEditor.Audio;

namespace RagnarockEditor.Editor
{
    /// <summary>
    /// Автоматическая настройка проекта при первом открытии в Unity Editor.
    /// Создаёт ScriptableObject ассеты, префабы и базовые сцены.
    /// Вызывается через меню: Tools → Ragnarock Editor → Setup Project
    /// </summary>
    [InitializeOnLoad]
    public static class RagnarockProjectSetup
    {
        private const string AssetRoot = "Assets/RagnarockEditor";
        private const string ScriptableObjPath = AssetRoot + "/ScriptableObjects";
        private const string PrefabPath = AssetRoot + "/Prefabs";
        private const string ScenePath = AssetRoot + "/Scenes";
        private const string MaterialPath = AssetRoot + "/Materials";

        private static bool _isInitialized;

        static RagnarockProjectSetup()
        {
            EditorApplication.update += CheckFirstRun;
        }

        private static void CheckFirstRun()
        {
            if (_isInitialized) return;

            string setupFlag = Application.dataPath + "/RagnarockEditor/.setup_done";
            if (!System.IO.File.Exists(setupFlag))
            {
                // Не запускаем автоматически — только через меню
                Debug.Log("[RagnarockSetup] Проект не настроен. Используйте Tools → Ragnarock Editor → Setup Project");
            }

            _isInitialized = true;
            EditorApplication.update -= CheckFirstRun;
        }

        [MenuItem("Tools/Ragnarock Editor/Setup Project (Full)", priority = 1)]
        public static void FullSetup()
        {
            Debug.Log("=== Ragnarock Editor: Полная настройка проекта ===");
            
            EnsureDirectories();
            // SetupVRSettings(); // Вызывайте ОДИН раз вручную через меню Tools!
            CreateScriptableObjects();
            CreateMaterials();
            CreatePrefabs();
            DeleteOldScenes();
            CreateScenes();
            AddScenesToBuild();

            // Флаг завершения
            string setupFlag = Application.dataPath + "/RagnarockEditor/.setup_done";
            System.IO.File.WriteAllText(setupFlag, "done");

            AssetDatabase.Refresh();
            Debug.Log("=== Настройка завершена! Откройте RecordScene для теста VR. ===");
        }

        private static void DeleteOldScenes()
        {
            if (System.IO.Directory.Exists(ScenePath))
            {
                string[] sceneFiles = System.IO.Directory.GetFiles(ScenePath, "*.unity");
                foreach (var f in sceneFiles)
                {
                    System.IO.File.Delete(f);
                    Debug.Log($"[Setup] Удалена старая сцена: {System.IO.Path.GetFileName(f)}");
                }
            }
        }

        [MenuItem("Tools/Ragnarock Editor/Add Scenes to Build", priority = 6)]
        public static void AddScenesToBuild()
        {
            var scenes = new[]
            {
                $"{ScenePath}/RecordScene.unity",
                $"{ScenePath}/MainMenuScene.unity",
                $"{ScenePath}/SongSelectionScene.unity",
                $"{ScenePath}/PreviewScene.unity"
            };

            var editorScenes = new List<EditorBuildSettingsScene>();
            foreach (var path in scenes)
            {
                if (System.IO.File.Exists(path))
                {
                    editorScenes.Add(new EditorBuildSettingsScene(path, true));
                }
            }

            EditorBuildSettings.scenes = editorScenes.ToArray();
            Debug.Log($"[Setup] Добавлено сцен в билд: {editorScenes.Count}. RecordScene — первая.");
        }

        [MenuItem("Tools/Ragnarock Editor/Setup VR Settings", priority = 0)]
        public static void SetupVRSettings()
        {
            bool hasXR = FindType("Unity.XR.CoreUtils.XROrigin") != null;
            if (hasXR)
            {
                Debug.Log("[Setup] XR пакеты обнаружены. Настройка VR...");
                PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;
                PlayerSettings.MTRendering = true;
                PlayerSettings.SetMobileMTRendering(BuildTargetGroup.Android, true);
                PlayerSettings.colorSpace = ColorSpace.Linear;
                Debug.Log("[Setup] VR настройки применены (Single Pass, Linear).");
            }
            else
            {
                Debug.LogWarning("[Setup] XR пакеты не найдены. Установите XR Plugin Management + XR Interaction Toolkit.");
            }
        }

        [MenuItem("Tools/Ragnarock Editor/Setup ScriptableObjects Only", priority = 2)]
        public static void CreateScriptableObjects()
        {
            EnsureDirectory(ScriptableObjPath);

            // AppConfig
            var appConfig = AssetDatabase.LoadAssetAtPath<AppConfig>($"{ScriptableObjPath}/AppConfig.asset");
            if (appConfig == null)
            {
                appConfig = ScriptableObject.CreateInstance<AppConfig>();
                AssetDatabase.CreateAsset(appConfig, $"{ScriptableObjPath}/AppConfig.asset");
                Debug.Log("[Setup] AppConfig создан");
            }

            // DrumLayoutConfig
            var drumLayout = AssetDatabase.LoadAssetAtPath<DrumLayoutConfig>($"{ScriptableObjPath}/DrumLayoutConfig.asset");
            if (drumLayout == null)
            {
                drumLayout = ScriptableObject.CreateInstance<DrumLayoutConfig>();
                AssetDatabase.CreateAsset(drumLayout, $"{ScriptableObjPath}/DrumLayoutConfig.asset");
                Debug.Log("[Setup] DrumLayoutConfig создан");
            }

            // QuantizeSettings
            var quantSettings = AssetDatabase.LoadAssetAtPath<QuantizeSettings>($"{ScriptableObjPath}/QuantizeSettings.asset");
            if (quantSettings == null)
            {
                quantSettings = ScriptableObject.CreateInstance<QuantizeSettings>();
                AssetDatabase.CreateAsset(quantSettings, $"{ScriptableObjPath}/QuantizeSettings.asset");
                Debug.Log("[Setup] QuantizeSettings создан");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] ScriptableObject ассеты созданы.");
        }

        [MenuItem("Tools/Ragnarock Editor/Setup Materials Only", priority = 3)]
        public static void CreateMaterials()
        {
            EnsureDirectory(MaterialPath);

            // Drum Material
            if (!System.IO.File.Exists($"{MaterialPath}/Drum_Mat.mat"))
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.4f, 0.2f, 0.1f); // Коричневый (дерево)
                mat.name = "Drum_Mat";
                AssetDatabase.CreateAsset(mat, $"{MaterialPath}/Drum_Mat.mat");
            }

            // Drum Head Material
            if (!System.IO.File.Exists($"{MaterialPath}/DrumHead_Mat.mat"))
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.9f, 0.85f, 0.7f); // Кожаный
                mat.name = "DrumHead_Mat";
                AssetDatabase.CreateAsset(mat, $"{MaterialPath}/DrumHead_Mat.mat");
            }

            // Drum Hit Flash Material
            if (!System.IO.File.Exists($"{MaterialPath}/DrumHitFlash_Mat.mat"))
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(1f, 0.8f, 0f); // Жёлтая вспышка
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1f, 0.6f, 0f) * 2f);
                mat.name = "DrumHitFlash_Mat";
                AssetDatabase.CreateAsset(mat, $"{MaterialPath}/DrumHitFlash_Mat.mat");
            }

            // Hammer Material
            if (!System.IO.File.Exists($"{MaterialPath}/Hammer_Mat.mat"))
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.3f, 0.25f, 0.2f);
                mat.name = "Hammer_Mat";
                AssetDatabase.CreateAsset(mat, $"{MaterialPath}/Hammer_Mat.mat");
            }

            // Rune Materials (4 цвета)
            Color[] runeColors = {
                new Color(1f, 0.2f, 0.2f),   // Красный
                new Color(0.2f, 0.6f, 1f),   // Синий
                new Color(0.2f, 1f, 0.3f),   // Зелёный
                new Color(1f, 0.8f, 0f)      // Жёлтый
            };

            for (int i = 0; i < 4; i++)
            {
                string runePath = $"{MaterialPath}/Rune_{i}_Mat.mat";
                if (!System.IO.File.Exists(runePath))
                {
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    mat.color = runeColors[i];
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", runeColors[i] * 1.5f);
                    mat.name = $"Rune_{i}_Mat";
                    AssetDatabase.CreateAsset(mat, runePath);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Материалы созданы.");
        }

        [MenuItem("Tools/Ragnarock Editor/Setup Prefabs Only", priority = 4)]
        public static void CreatePrefabs()
        {
            EnsureDirectory(PrefabPath + "/VR");
            EnsureDirectory(PrefabPath + "/UI");

            CreateDrumPrefab();
            CreateHammerPrefab();
            CreateRunePrefab();
            // CreateXR_RigPrefab(); // Требует XR Interaction Toolkit

            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Префабы созданы.");
        }

        private static void CreateDrumPrefab()
        {
            string path = $"{PrefabPath}/VR/Drum.prefab";
            if (System.IO.File.Exists(path)) return;

            GameObject drum = new GameObject("Drum");

            // Цилиндр (корпус барабана)
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(drum.transform);
            body.transform.localPosition = new Vector3(0f, -0.075f, 0f);
            body.transform.localScale = new Vector3(0.35f, 0.15f, 0.35f);

            var bodyRenderer = body.GetComponent<MeshRenderer>();
            var drumMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialPath}/Drum_Mat.mat");
            if (drumMat != null) bodyRenderer.material = drumMat;

            // Верхняя поверхность (головка барабана) — тонкий цилиндр
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            head.name = "Head";
            head.transform.SetParent(drum.transform);
            head.transform.localPosition = new Vector3(0, 0.08f, 0);
            head.transform.localScale = new Vector3(0.34f, 0.01f, 0.34f);

            var headRenderer = head.GetComponent<MeshRenderer>();
            var headMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialPath}/DrumHead_Mat.mat");
            if (headMat != null) headRenderer.material = headMat;

            // Коллайдер-триггер на головке
            var collider = head.GetComponent<CapsuleCollider>();
            if (collider != null)
            {
                collider.isTrigger = true;
                collider.radius = 0.5f;
                collider.height = 2f;
            }

            // Добавляем компоненты
            drum.AddComponent<DrumController>();
            // DrumSceneBootstrap installs the detector on Head.

            // Настраиваем DrumController
            var drumCtrl = drum.GetComponent<DrumController>();
            var hitFlashMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialPath}/DrumHitFlash_Mat.mat");
            
            // Сериализованные поля задаются через SerializedObject
            var so = new SerializedObject(drumCtrl);
            so.FindProperty("_drumHeadRenderer").objectReferenceValue = headRenderer;
            so.FindProperty("_hoopMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialPath}/Hammer_Mat.mat");
            so.FindProperty("_defaultMaterial").objectReferenceValue = headMat;
            so.FindProperty("_hitMaterial").objectReferenceValue = hitFlashMat;
            so.FindProperty("_minHitVelocity").floatValue = 1.5f;
            so.FindProperty("_debounceTime").floatValue = 0.08f;
            so.ApplyModifiedProperties();

            PrefabUtility.SaveAsPrefabAsset(drum, path);
            Object.DestroyImmediate(drum);
            Debug.Log($"[Setup] Drum.prefab создан: {path}");
        }

        private static void CreateHammerPrefab()
        {
            string path = $"{PrefabPath}/VR/Hammer.prefab";
            if (System.IO.File.Exists(path)) return;

            GameObject hammer = new GameObject("Hammer");

            // Рукоятка
            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            handle.name = "Handle";
            handle.transform.SetParent(hammer.transform);
            handle.transform.localPosition = Vector3.zero;
            handle.transform.localScale = new Vector3(0.04f, 0.2f, 0.04f);

            // Головка молота
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            head.transform.SetParent(hammer.transform);
            head.transform.localPosition = new Vector3(0, 0.25f, 0);
            head.transform.localScale = new Vector3(0.1f, 0.08f, 0.12f);

            var hammerMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialPath}/Hammer_Mat.mat");
            if (hammerMat != null)
            {
                handle.GetComponent<MeshRenderer>().material = hammerMat;
                head.GetComponent<MeshRenderer>().material = hammerMat;
            }

            // Коллайдер для детекции ударов
            var headCollider = head.GetComponent<BoxCollider>();
            if (headCollider != null)
            {
                headCollider.isTrigger = true;
            }

            hammer.AddComponent<Rigidbody>().isKinematic = true;
            hammer.AddComponent<HammerController>();

            PrefabUtility.SaveAsPrefabAsset(hammer, path);
            Object.DestroyImmediate(hammer);
            Debug.Log($"[Setup] Hammer.prefab создан: {path}");
        }

        private static void CreateRunePrefab()
        {
            string path = $"{PrefabPath}/VR/Rune.prefab";
            if (System.IO.File.Exists(path)) return;

            GameObject rune = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rune.name = "Rune";
            rune.transform.localScale = Vector3.one * 0.08f;

            var renderer = rune.GetComponent<MeshRenderer>();
            var runeMat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialPath}/Rune_0_Mat.mat");
            if (runeMat != null) renderer.material = runeMat;

            rune.AddComponent<RuneVisual>();

            PrefabUtility.SaveAsPrefabAsset(rune, path);
            Object.DestroyImmediate(rune);
            Debug.Log($"[Setup] Rune.prefab создан: {path}");
        }

        private static void CreateXR_RigPrefab()
        {
            string path = $"{PrefabPath}/VR/XR_Rig.prefab";
            if (System.IO.File.Exists(path)) return;

            var xrOriginComp = AddComponentByTypeName(new GameObject("XR Origin"), "Unity.XR.CoreUtils.XROrigin");
            if (xrOriginComp != null)
            {
                var xrOrigin = xrOriginComp.gameObject;

                GameObject cameraOffset = new GameObject("Camera Offset");
                cameraOffset.transform.SetParent(xrOrigin.transform);
                cameraOffset.transform.localPosition = Vector3.zero;

                GameObject mainCamera = new GameObject("Main Camera");
                mainCamera.transform.SetParent(cameraOffset.transform);
                mainCamera.transform.localPosition = Vector3.zero;
                mainCamera.AddComponent<Camera>();
                mainCamera.AddComponent<AudioListener>();
                mainCamera.tag = "MainCamera";

                var so = new SerializedObject(xrOriginComp);
                var ofsProp = so.FindProperty("m_CameraFloorOffsetObject");
                if (ofsProp != null) ofsProp.objectReferenceValue = cameraOffset;
                var camProp = so.FindProperty("m_Camera");
                if (camProp != null) camProp.objectReferenceValue = mainCamera.GetComponent<Camera>();
                so.ApplyModifiedProperties();

                PrefabUtility.SaveAsPrefabAsset(xrOrigin, path);
                Object.DestroyImmediate(xrOrigin);
                Debug.Log($"[Setup] XR_Rig.prefab создан: {path}");
            }
            else
            {
                Debug.LogWarning("[Setup] XR_Rig пропущен — XR Interaction Toolkit не найден");
            }
        }

        [MenuItem("Tools/Ragnarock Editor/Setup Scenes Only", priority = 5)]
        public static void CreateScenes()
        {
            EnsureDirectory(ScenePath);

            CreateMainMenuScene();
            CreateSongSelectionScene();
            CreateRecordScene();
            CreatePreviewScene();

            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Сцены созданы.");
        }

        #region Scene Creation

        private static void CreateMainMenuScene()
        {
            string path = $"{ScenePath}/MainMenuScene.unity";
            if (System.IO.File.Exists(path)) return;

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // EventSystem
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // Main Camera (для редактора)
            var camera = new GameObject("Main Camera");
            camera.AddComponent<Camera>();
            camera.AddComponent<AudioListener>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 1.6f, -2f);

            // Directional Light
            var light = new GameObject("Directional Light");
            var lightComp = light.AddComponent<Light>();
            lightComp.type = LightType.Directional;
            lightComp.intensity = 1f;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            // UI Canvas (World Space)
            var canvasGO = new GameObject("MainMenuCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera.GetComponent<Camera>();
            canvasGO.transform.position = new Vector3(0, 1.5f, 1.5f);
            canvasGO.transform.localScale = Vector3.one * 0.002f;

            var canvasRect = canvasGO.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(800, 600);

            canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            canvasGO.AddComponent<UI.MainMenuUI>();

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[Setup] MainMenuScene создана: {path}");
        }

        private static void CreateSongSelectionScene()
        {
            string path = $"{ScenePath}/SongSelectionScene.unity";
            if (System.IO.File.Exists(path)) return;

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // EventSystem
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // Camera
            var camera = new GameObject("Main Camera");
            camera.AddComponent<Camera>();
            camera.AddComponent<AudioListener>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 1.6f, -2f);

            // Light
            var light = new GameObject("Directional Light");
            var lightComp = light.AddComponent<Light>();
            lightComp.type = LightType.Directional;
            lightComp.intensity = 1f;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            // Canvas
            var canvasGO = new GameObject("SongSelectionCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera.GetComponent<Camera>();
            canvasGO.transform.position = new Vector3(0, 1.5f, 1.5f);
            canvasGO.transform.localScale = Vector3.one * 0.002f;
            canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            canvasGO.AddComponent<UI.SongSelectionUI>();
            canvasGO.AddComponent<UI.BPMEditorUI>();

            // AudioLoader
            var audioLoaderGO = new GameObject("AudioLoader");
            audioLoaderGO.AddComponent<AudioLoader>();

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[Setup] SongSelectionScene создана: {path}");
        }

        private static void CreateRecordScene()
        {
            string path = $"{ScenePath}/RecordScene.unity";
            if (System.IO.File.Exists(path)) return;

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // EventSystem
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // Light
            var light = new GameObject("Directional Light");
            var lightComp = light.AddComponent<Light>();
            lightComp.type = LightType.Directional;
            lightComp.intensity = 1f;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            // Ground plane
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(2, 1, 2);

            var xrOriginGO = new GameObject("XR Origin");
            var xrOriginComp = AddComponentByTypeName(xrOriginGO, "Unity.XR.CoreUtils.XROrigin");

            var mainCam = new GameObject("Main Camera");
            mainCam.tag = "MainCamera";
            var cam = mainCam.AddComponent<Camera>();
            cam.stereoTargetEye = StereoTargetEyeMask.Both;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            mainCam.AddComponent<AudioListener>();

            // ТЕСТ: куб перед камерой чтобы убедиться что рендеринг работает
            var testCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            testCube.name = "TestCube_VR_OK";
            testCube.transform.position = new Vector3(0, 1.6f, 2f);
            testCube.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
            var cubeRenderer = testCube.GetComponent<MeshRenderer>();
            cubeRenderer.sharedMaterial.color = Color.red;

            if (xrOriginComp != null)
            {
                var cameraOffset = new GameObject("Camera Offset");
                cameraOffset.transform.SetParent(xrOriginGO.transform);
                cameraOffset.transform.localPosition = Vector3.zero;
                mainCam.transform.SetParent(cameraOffset.transform);
                mainCam.transform.localPosition = Vector3.zero;

                var soXRO = new SerializedObject(xrOriginComp);
                var cfProp = soXRO.FindProperty("m_CameraFloorOffsetObject");
                if (cfProp != null) cfProp.objectReferenceValue = cameraOffset;
                var cmProp = soXRO.FindProperty("m_Camera");
                if (cmProp != null) cmProp.objectReferenceValue = cam;
                var trackingProp = soXRO.FindProperty("m_TrackingOriginMode");
                if (trackingProp != null) trackingProp.enumValueIndex = 2;
                soXRO.ApplyModifiedProperties();
                Debug.Log("[Setup] RecordScene: XR Origin с трекингом");
            }
            else
            {
                mainCam.transform.position = new Vector3(0, 1.6f, 0);
                Debug.LogWarning("[Setup] RecordScene: XR Origin не найден, базовая VR-камера");
            }

            // Drums Parent
            var drumsParent = new GameObject("DrumsParent");

            var drumManager = drumsParent.AddComponent<DrumManager>();
            var drumPositioner = drumsParent.AddComponent<DrumPositioner>();
            var drumCalibration = drumsParent.AddComponent<DrumCalibrationManager>();

            // Ссылка на префаб барабана
            var drumPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabPath}/VR/Drum.prefab");
            var soDrumMgr = new SerializedObject(drumManager);
            soDrumMgr.FindProperty("_drumPrefab").objectReferenceValue = drumPrefab;
            soDrumMgr.FindProperty("_drumsParent").objectReferenceValue = drumsParent.transform;
            soDrumMgr.ApplyModifiedProperties();

            var soDrumPos = new SerializedObject(drumPositioner);
            var drumLayout = AssetDatabase.LoadAssetAtPath<DrumLayoutConfig>($"{ScriptableObjPath}/DrumLayoutConfig.asset");
            soDrumPos.FindProperty("_config").objectReferenceValue = drumLayout;
            soDrumPos.ApplyModifiedProperties();

            // Record System
            var recordGO = new GameObject("RecordSystem");
            var recordManager = recordGO.AddComponent<RecordManager>();
            var noteCollector = recordGO.AddComponent<NoteCollector>();

            // Timeline Controller (Canvas)
            var timelineCanvas = new GameObject("TimelineCanvas");
            var tlCanvas = timelineCanvas.AddComponent<Canvas>();
            tlCanvas.renderMode = RenderMode.WorldSpace;
            timelineCanvas.transform.position = new Vector3(0, 2.2f, 2f);
            timelineCanvas.transform.localScale = Vector3.one * 0.0015f;
            timelineCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
            timelineCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            timelineCanvas.AddComponent<TimelineController>();
            timelineCanvas.AddComponent<UI.RecordHUD>();

            // Metronome
            var metronomeGO = new GameObject("Metronome");
            metronomeGO.AddComponent<MetronomePlayer>();
            metronomeGO.AddComponent<MetronomeVisualizer>();

            // Связываем компоненты
            var soRecordMgr = new SerializedObject(recordManager);
            soRecordMgr.FindProperty("_drumManager").objectReferenceValue = drumManager;
            soRecordMgr.FindProperty("_metronomePlayer").objectReferenceValue = metronomeGO.GetComponent<MetronomePlayer>();
            soRecordMgr.FindProperty("_metronomeVisualizer").objectReferenceValue = metronomeGO.GetComponent<MetronomeVisualizer>();
            soRecordMgr.FindProperty("_audioSource").objectReferenceValue = recordGO.AddComponent<AudioSource>();
            soRecordMgr.ApplyModifiedProperties();

            var soNoteCollector = new SerializedObject(noteCollector);
            soNoteCollector.FindProperty("_recordManager").objectReferenceValue = recordManager;
            soNoteCollector.FindProperty("_drumManager").objectReferenceValue = drumManager;
            soNoteCollector.ApplyModifiedProperties();

            var soTimeline = new SerializedObject(timelineCanvas.GetComponent<TimelineController>());
            soTimeline.FindProperty("_recordManager").objectReferenceValue = recordManager;
            soTimeline.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[Setup] RecordScene создана: {path}");
        }

        private static void CreatePreviewScene()
        {
            string path = $"{ScenePath}/PreviewScene.unity";
            if (System.IO.File.Exists(path)) return;

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // EventSystem
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // Light
            var light = new GameObject("Directional Light");
            var lightComp = light.AddComponent<Light>();
            lightComp.type = LightType.Directional;
            lightComp.intensity = 1f;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            // Ground
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(2, 1, 2);

            // Drums Parent
            var drumsParent = new GameObject("DrumsParent");
            var drumManager = drumsParent.AddComponent<DrumManager>();
            var drumPositioner = drumsParent.AddComponent<DrumPositioner>();

            var drumPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabPath}/VR/Drum.prefab");
            var soDrumMgr = new SerializedObject(drumManager);
            soDrumMgr.FindProperty("_drumPrefab").objectReferenceValue = drumPrefab;
            soDrumMgr.FindProperty("_drumsParent").objectReferenceValue = drumsParent.transform;
            soDrumMgr.ApplyModifiedProperties();

            var drumLayout = AssetDatabase.LoadAssetAtPath<DrumLayoutConfig>($"{ScriptableObjPath}/DrumLayoutConfig.asset");
            var soDrumPos = new SerializedObject(drumPositioner);
            soDrumPos.FindProperty("_config").objectReferenceValue = drumLayout;
            soDrumPos.ApplyModifiedProperties();

            // Playback System
            var playbackGO = new GameObject("PlaybackSystem");
            playbackGO.AddComponent<PlaybackController>();
            playbackGO.AddComponent<RuneSpawner>();
            playbackGO.AddComponent<DrumHighlight>();

            var runePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabPath}/VR/Rune.prefab");
            var soSpawner = new SerializedObject(playbackGO.GetComponent<RuneSpawner>());
            soSpawner.FindProperty("_runePrefab").objectReferenceValue = runePrefab;
            soSpawner.ApplyModifiedProperties();

            var soHighlight = new SerializedObject(playbackGO.GetComponent<DrumHighlight>());
            soHighlight.FindProperty("_drumManager").objectReferenceValue = drumManager;
            soHighlight.ApplyModifiedProperties();

            // Preview UI Canvas
            var previewCanvas = new GameObject("PreviewCanvas");
            var pvCanvas = previewCanvas.AddComponent<Canvas>();
            pvCanvas.renderMode = RenderMode.WorldSpace;
            previewCanvas.transform.position = new Vector3(0, 2.2f, 2f);
            previewCanvas.transform.localScale = Vector3.one * 0.0015f;
            previewCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
            previewCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var previewUI = previewCanvas.AddComponent<UI.PreviewUI>();

            // CustomSongsExporter
            var exporterGO = new GameObject("CustomSongsExporter");
            exporterGO.AddComponent<FileSystem.CustomSongsExporter>();

            var soPreviewUI = new SerializedObject(previewUI);
            soPreviewUI.FindProperty("_playbackController").objectReferenceValue = playbackGO.GetComponent<PlaybackController>();
            soPreviewUI.FindProperty("_exporter").objectReferenceValue = exporterGO.GetComponent<FileSystem.CustomSongsExporter>();
            soPreviewUI.ApplyModifiedProperties();

            var soPlaybackCtrl = new SerializedObject(playbackGO.GetComponent<PlaybackController>());
            soPlaybackCtrl.FindProperty("_runeSpawner").objectReferenceValue = playbackGO.GetComponent<RuneSpawner>();
            soPlaybackCtrl.FindProperty("_drumManager").objectReferenceValue = drumManager;
            soPlaybackCtrl.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[Setup] PreviewScene создана: {path}");
        }

        #endregion

        [MenuItem("Tools/Ragnarock Editor/Setup XR Settings", priority = 0)]
        public static void SetupXRSettings()
        {
#if HAS_XR_PACKAGES
            var xrSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(
                BuildTargetGroup.Android);

            if (xrSettings == null)
            {
                Debug.LogWarning("[Setup] XR Settings не найдены. Создайте через Edit → Project Settings → XR Plugin Management.");
                return;
            }

            var xrManagerSettings = xrSettings.Manager;
            if (xrManagerSettings == null)
            {
                xrManagerSettings = ScriptableObject.CreateInstance<XRManagerSettings>();
                xrSettings.Manager = xrManagerSettings;
            }

            // Включаем Oculus loader
#if USE_XR_MANAGEMENT
            var oculusLoader = ScriptableObject.CreateInstance<Unity.XR.Oculus.OculusLoader>();
            xrManagerSettings.TryAddLoader(oculusLoader);
#endif

            Debug.Log("[Setup] XR Settings настроены (проверьте вручную в Project Settings).");
#endif
        }

        #region Helpers

        private static void EnsureDirectories()
        {
            EnsureDirectory(AssetRoot);
            EnsureDirectory(ScriptableObjPath);
            EnsureDirectory(PrefabPath + "/VR");
            EnsureDirectory(PrefabPath + "/UI");
            EnsureDirectory(ScenePath);
            EnsureDirectory(MaterialPath);
            EnsureDirectory(AssetRoot + "/Audio/SFX");
            EnsureDirectory(AssetRoot + "/Textures");
        }

        private static void EnsureDirectory(string path)
        {
            if (!System.IO.Directory.Exists(path))
            {
                System.IO.Directory.CreateDirectory(path);
                Debug.Log($"[Setup] Создана папка: {path}");
            }
        }

        #endregion

        #region Reflection Helpers (для XR-пакетов без прямых ссылок)

        private static Component AddComponentByTypeName(GameObject go, string typeName)
        {
            var type = FindType(typeName);
            if (type != null)
            {
                return go.AddComponent(type);
            }
            Debug.LogWarning($"[Setup] Тип не найден: {typeName}");
            return null;
        }

        private static Component GetComponentByTypeName(GameObject go, string typeName)
        {
            var type = FindType(typeName);
            if (type != null)
            {
                return go.GetComponent(type);
            }
            return null;
        }

        private static System.Type FindType(string typeName)
        {
            // Пробуем assembly-qualified имена для XRI Toolkit
            string[] assemblies = {
                "Unity.XR.Interaction.Toolkit",
                "UnityEngine.XR.Interaction.Toolkit",
                "Unity.XR.Interaction.Toolkit.Runtime"
            };

            foreach (var asm in assemblies)
            {
                var t = System.Type.GetType($"{typeName}, {asm}");
                if (t != null) return t;
            }

            // Ищем по всем загруженным сборкам с вариантами имени
            string[] typeNames = {
                typeName,
                "Unity.XR.CoreUtils.XROrigin",
                "Unity.XR.Interaction.Toolkit.XROrigin",
                "UnityEngine.XR.Interaction.Toolkit.XROrigin"
            };

            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var tn in typeNames)
                {
                    var t = asm.GetType(tn);
                    if (t != null)
                    {
                        Debug.Log($"[Setup] Найден тип: {t.FullName} в сборке {asm.GetName().Name}");
                        return t;
                    }
                }
            }

            // Отладка: выводим все XR-сборки
            Debug.LogWarning("[Setup] Поиск XR-типов. Загруженные XR-сборки:");
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (name.Contains("XR") || name.Contains("Interaction") || name.Contains("Toolkit"))
                {
                    Debug.LogWarning($"  Сборка: {name}");
                    // Выводим типы, содержащие "Origin"
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name.Contains("Origin"))
                            Debug.LogWarning($"    Тип: {t.FullName}");
                    }
                }
            }

            return null;
        }

        #endregion
    }
}
