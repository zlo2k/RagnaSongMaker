# ragnaGod — состояние проекта (handoff для новой сессии)

_Обновлено: 21.11.2026, 23:55 (Europe/Minsk). Пишу для того, чтобы новая сессия могла продолжить без раскопок._

---

## 1. Что это за проект

VR-редактор ритм-карт (уровней) для игры Ragnarock на **Meta Quest 3**: в шлеме ставится трек, играются удары по 4 барабанам молотами, из ударов собирается карта (`SongProgress`/`LoopRange` — шкалы трека), затем экспорт.

- Unity **6000.5.0f1** (Unity 6.5), Android, IL2CPP, ARM64, Linear color space, Active Input Handling = **Input System only**.
- URP **17.0.3**, OpenXR **1.17.1**, XRI **3.5.1**, uGUI 2.0.0, Newtonsoft JSON 3.2.1.
- Пакет: `com.DefaultCompany.ragnaGod`, minSdk 26, targetSdk auto (36), `useCustomMainManifest: 0`.
- Устройство: **Quest 3** (`<serial>`, Horizon OS = Android 14). adb уже авторизован (dev mode включён пользователем).

---

## 2. Короткий чеклист «собрать и запустить»

1. Открыть проект в Unity (6000.5.0f1).
2. Меню **`Tools → Ragnarock Editor → Fix VR Project`** — главный «починщик» сцен и настроек (см. §4).
3. **Build** (File → Build Profiles → Android). Сборка идёт ~3–20 мин (Gradle инкрементальный), первый билд был 1 ч 45 мин.
4. `adb install -r` не нужен: Unity ставит сама (Build And Run). **Не включать** `Development Build` + `Wait For Managed Debugger` — иначе приложение висит с «не отвечает» (см. §6.9).
5. При первом входе в «выбор песни» шлем спросит доступ к музыке → **Разрешить** (уже выдано).

Альтернатива меню: batch-режим `-executeMethod RagnarockEditor.EditorTools.VRProjectFixer.FixAll` **не работает** — нет лицензии `com.unity.editor.headless` (Unity возвращает exit code 198).

---

## 3. Что уже работает (проверено в шлеме по логам)

| Возможность | Статус |
|---|---|
| Запуск в VR (не 2D-панель) | ✅ манифест с `com.oculus.intent.category.VR` |
| Трекинг головы (6DoF) | ✅ `HMDPoseDriver` |
| Руки (контроллеры) + молот в руке | ✅ модель `StormMallet_R.fbx`, масштаб 0.478 (длина 0.38 м) |
| Барабаны (4 шт., расстановка вокруг игрока) | ✅ `DrumSceneBootstrap` |
| Регистрация удара молотом | ✅ `DrumHitDetector` (по расстоянию), порог 0.6 м/с |
| Выбор mp3 из памяти шлема | ✅ 5 треков найдено, `Linkin Park Numb.mp3` играет (188 с) |
| Список треков с пагинацией + разрешение | ✅ `READ_MEDIA_AUDIO` выдано |
| Экран «песня выбрана» + HUD записи (прогресс/время/удары) | ⚠️ собрано, но кнопки заменены на Stone UI, **ещё не проверено в шлеме** |
| Stone UI (каменные 3D-кнопки) | ⚠️ собрано фиксером 23:48, **сборки после этого не было** |
| Окружение (снежная долина) | ⚠️ собрано, не проверено в шлеме |
| Скин кнопок из пака GUI Basic Button Pack | ✅ паки импортированы, применяется к uGUI-кнопкам |
| Экспорт карты, калибровка роста, выбор обложки | ❌ не реализовано |

**Последний установленный APK:** 21.11.2026 22:36 (без Stone UI и без окружения).
**Фиксер прогонялся:** 21.11.2026 23:48 — сцены уже содержат новый UI, нужен билд.

---

## 4. `VRProjectFixer` — что он делает

Файл: `Assets/RagnarockEditor/Scripts/EditorTools/VRProjectFixer.cs` (editor-only asmdef `RagnarockEditor.EditorTools`).
Запуск: меню `Tools → Ragnarock Editor → Fix VR Project`, либо автооднократно при открытии проекта, если есть флаг `Library/vr-fix.pending`.

Порядок `FixAll → FixScenes → FixScene(scene)`:

1. **URP** — создаёт `Assets/Settings/URP_Asset.asset` + `URP_Renderer.asset`, назначает в Graphics и во все 6 уровней Quality (MSAA 4, HDR off).
2. **XR-риг** — `XR Origin → Camera Offset → Main Camera`, `XROrigin` (Floor, Y-offset 0), `HMDPoseDriver` на камеру, `UniversalAdditionalCameraData`, `AudioListener`, farClip ≥ 400, clearFlags = Skybox. В `PreviewScene` камера создаётся с нуля.
3. **Руки** — `LeftHand`/`RightHand` под Camera Offset: `XRHandPoseDriver`, `HandUIPointer`, `LineRenderer` (материал `UI_Laser.mat`), визуал-капсула (скрывается, если есть модель). В Record/Preview — молот из FBX + `AimAlignedObject` + `HammerController` (поле `_head` = пустышка `Head`).
4. **Барабаны** — `DrumSceneBootstrap` на DrumsParent (создаёт 4 барабана, расставляет, вешает `DrumHitDetector` на «Head»).
5. **uGUI** — EventSystem → `XRUIInputModule`, World Space канвасам → `TrackedDeviceGraphicRaycaster`; собирает экран выбора песни (список + страницы + кнопка «Разрешить доступ»), HUD записи, панель «песня выбрана».
6. **Stone UI** — 3D-кнопки из `Assets/StoneUI/*.fbx` (см. §7), удаляет заменённые uGUI-кнопки, удаляет `MainMenuCanvas`.
7. **Окружение** — снежная долина (см. §8), удаляет `TestCube_VR_OK` и старый `Ground`.
8. **Скин кнопок** — ищет спрайты `*button*`/`*btn*`, ставит `Image.Type = Sliced` + ColorTint; если текстуры импортированы не как Sprite — переимпортирует.
9. **Материалы** — конвертирует `Standard`/`Default-Material` в `URP/Lit` (иначе magenta), модель молота — в `Materials/Converted/*`.

⚠️ Фиксер **перезаписывает** часть настроек при каждом прогоне: `_aimBlend = 0.5` у `HandUIPointer`, `_blend = 1` у `AimAlignedObject`, `_modelAlignment = (0,0,0)`, `targetLength = 0.38`, радиус удара 0.17. Если крутил в инспекторе — после прогона вернётся.

---

## 5. Ключевые файлы

### Runtime (`Assets/RagnarockEditor/Scripts/VR/`)

| Файл | Роль |
|---|---|
| `HMDPoseDriver.cs` | поза камеры из `XRNode.Head` через `UnityEngine.XR.InputDevices` |
| `HMDPoseDriverInstaller.cs` | страховка: вешает драйвер на главную камеру каждой сцены |
| `XRHandPoseDriver.cs` | поза контроллера (grip), свойство `IsTriggerPressed` |
| `HandUIPointer.cs` | луч: aim-поза контроллера, Raycast по `StoneButton` и uGUI `Button`, нажатие по триггеру |
| `AimAlignedObject.cs` | разворачивает молот параллельно лучу (`_blend` 0..1) |
| `WorldUIPanel.cs` | ставит World Space панель перед игроком по высоте глаз + смещение |
| `DrumSceneBootstrap.cs` | создаёт барабаны, расставляет, вешает детекторы |
| `DrumHitDetector.cs` | удар по расстоянию: `_hitRadius = 0.17`, `_minSpeed = 0.6` |
| `HammerController.cs` | скорость по **голове** молота (`HeadPosition`), `IsRightHand` для haptic |
| `DrumController.cs` | регистрация удара, подсветка головы, `_minHitVelocity = 0.6`, debounce 0.08 |
| `SongLibrary.cs` | скан папок (`persistentDataPath/Music`, `/sdcard/Music`, `Download`, `Documents`) + MediaStore |
| `MediaStoreSongs.cs` | MediaStore-запрос (`_data` + `title`; `display_name` на Horizon OS **нет**) |
| `MediaPermission.cs` | `READ_MEDIA_AUDIO` (API 33+) / `READ_EXTERNAL_STORAGE` |
| `SongSelectionList.cs` / `SongButton.cs` / `SongSelectionState.cs` | список треков, выбор, хранение пути между сценами |
| `SongPlaybackBootstrap.cs` | грузит трек (UnityWebRequest), **не** играет до `PlayFromStart()` |
| `RecordSessionController.cs` | панель «песня выбрана», HUD записи, прогресс/время/удары, пауза/стоп/рестарт |
| `StoneButton.cs` | 3D-кнопка Stone UI: `JumpScene/Quit/Play/Pause/Stop/Restart`, подсветка |
| `SceneJumpButton.cs` | uGUI-кнопка перехода между сценами |

### Editor

- `Assets/RagnarockEditor/Scripts/EditorTools/VRProjectFixer.cs` — см. §4.
- `Assets/Editor/AddMediaPermissionAndroid.cs` — `IPostGenerateGradleAndroidProject`: вписывает `READ_MEDIA_AUDIO` в сгенерированный `unityLibrary/src/main/AndroidManifest.xml` **после** генерации Gradle-проекта (кастомный главный манифест использовать нельзя, см. §6.7).

### Ассеты, создаваемые фиксером

- `Assets/Settings/URP_Asset.asset`, `URP_Renderer.asset`
- `Assets/RagnarockEditor/Materials/`: `Default_URP.mat`, `UI_Laser.mat`, `Converted/*` (материалы модели), `Env/*` (снег, камень, ель, ствол, дерево, скайбокс)
- `Assets/RagnarockEditor/Models/Env/Cone.asset` (процедурный конус для гор/елей)
- `Assets/RagnarockEditor/Models/StormMallet/StormMallet_R.fbx`

### Внешние ассеты

- `Assets/StoneUI/` — 16 FBX из `StoneUI_Quest.zip` (+ `README_StoneUI.txt`).
- `Assets/Layer Lab/GUI-BasicButtonPack/` — импортирован вручную из кэша Asset Store (`%APPDATA%\Unity\Asset Store-5.x\LAYERLAB\...`), 36 спрайтов с 9-slice.

---

## 6. Грабли, на которые уже наступили (не повторять)

1. **Приложение запускалось как 2D-панель.** Причина: в OpenXR Package Settings выключен feature-group **Meta Quest Support** (`MetaQuestFeature`, `m_enabled: 0`). Симптом в логе: `VolumetricWindowManagerServiceImpl: launch into NEW_VOLUMETRIC_WINDOW`, в манифесте нет `com.oculus.intent.category.VR`. Лечение: включить Meta Quest Support (`Assets/XR/Settings/OpenXR Package Settings.asset`). Лучше проверять APK: `aapt2 dump permissions`/`xmltree`.
2. **Картинка статичная — нет трекинга головы.** `TrackedPoseDriver`, созданный из кода, сериализуется с **пустыми** `m_SingletonActionBindings` → поза не читается. Лечение: свой `HMDPoseDriver` на `InputDevices`, TPD удалён.
3. **Всё розовое.** Материалы URP/Lit, но URP Asset нигде не назначен (`m_CustomRenderPipeline: 0`, все Quality — тоже 0). Лечение: создать URP Asset и назначить в Graphics + Quality (делает фиксер).
4. **15 133 исключения `InvalidOperationException: You are trying to read Input using the UnityEngine.Input class`** — `StandaloneInputModule` при Active Input Handling = Input System only. Лечение: заменён на `XRUIInputModule`.
5. **`DrumHitDetector` не был подключён в префабе барабана вообще** — на «Head» только коллайдер. Плюс `_minHitVelocity` был 1.5 м/с (много). Лечение: детект по расстоянию + порог 0.6.
6. **Скорость молота считалась по рукояти** — при замахе она почти не двигается, удары не проходили порог. Лечение: считать по голове молота.
7. **Кастомный `AndroidManifest.xml` ломает сборку**: Unity при `useCustomMainManifest: 1` не генерирует `<activity>`, а Meta Quest Support дописывает только `<category VR>` → `Missing 'name' key attribute on element activity`. Лечение: `useCustomMainManifest: 0` + build-callback (§5).
8. **`display_name` отсутствует в MediaStore на Horizon OS** → запрос падал. Использовать `_data` + `title` и `getColumnIndex` (не `OrThrow`).
9. **`Development Build` + `Wait For Managed Debugger`** → приложение висит, шлем показывает «не отвечает», в логе `[Wait for debugger to attach]`.
10. **`-batchmode` / `-executeMethod` невозможны** — нет лицензии `com.unity.editor.headless` (exit code 198). Всё делается через открытый редактор.
11. **FBX `pathname` в `.unitypackage` оканчивается `\n00`** — при ручной распаковке чистить первую строку.
12. **`GameObject.Find` не видит выключенные объекты** → фиксер создал дубликат `MainMenuCanvas`. Лечение: `DestroyAllNamed` через `FindObjectsByType(FindObjectsInactive.Include)`.
13. **В StoneUI все подобъекты называются `Mesh`** — `ProgressFill`/`Playhead`/`LoopStart` по имени не найти (см. §9).

---

## 7. Stone UI — факты по геометрии

Реальный масштаб (1 юнит = 1 м), «лицо» — **+Z**, поэтому при расстановке перед игроком поворачиваем на 180° по Y.

| Модель | Габариты (X×Y×Z, м) | Назначение |
|---|---|---|
| Play/Pause/Record/Stop/Replay/Settings/Back/Accept | 0.103 × 0.091 × 0.035 | кнопки-иконки |
| OpenMP3/RecordTake/Retake/ChooseCover/ExportMap/Blank | 0.289 × 0.070 × ~0.03 | плиты меню (подписи запечены в геометрию) |
| SongProgress | 1.279 × 0.054 × 0.04 (рабочая ширина 824 мм) | шкала прогресса, есть ProgressFill + Playhead |
| LoopRange | — | шкала лупа: LoopStart, LoopEnd, RangeFill (исходно 330 мм) |

Текущая раскладка (в `BuildStoneUi`):
- **MainMenu**: OpenMP3 (−0.34, 1.32, 1.15) → SongSelection; Record (0, 1.32, 1.15) → Record; Settings (0.34, 1.32, 1.15); Back (0, 1.12, 1.15) → выход.
- **Record**: Play/Pause/Stop/Replay/Back в ряд на y = 1.18, z = 1.05, x = −0.21…0.35.
- **SongSelection**: OpenMP3 как заголовок (0, 1.78, 1.3), Back (−0.32, 1.02, 1.2).

---

## 8. Окружение (первая версия, процедурная — АКТУАЛЬНО см. «Итерации 2.2–2.4»)

`BuildEnvironment()` создаёт объект `Environment`:
- земля: Plane 400×400 м, `Env_Snow`;
- настил `Deck` 4.2×3.2 м, `Env_Wood`;
- 10 гор-конусов на 55–100 м;
- 70 елей (ствол + конус) в радиусе 8–42 м, впереди поляна;
- скайбокс `Skybox/Procedural` + экспоненциальный туман (0.016) + Directional Light (холодный, 1.15, soft shadows).

Производительность ещё не мерили на устройстве; если просядет FPS — уменьшать число елей/тени.

---

## 9. Открытые задачи (в порядке приоритета)

1. **Собрать APK и проверить** Stone UI (наведение/подсветка/клик), экран «песня выбрана», HUD, окружение, новый размер молота.
2. **Подключить шкалы `SongProgress` / `LoopRange`**: подобъекты безымянные — вычислить ProgressFill/Playhead по bounds (например, самый маленький меш = playhead, полоса = fill) или попросить переименовать при экспорте. Нужно для HUD записи вместо uGUI-прогресса.
3. **`Blank`** — world-space текст под имя песни/локализацию.
4. **Поток приложения**: экран экспорта (`ExportMap`, `ChooseCover`, `Retake`, `Accept`), калибровка роста (`DrumCalibrationManager` с null-ссылками, не используется), `RecordManager`/`NoteCollector` → сохранение `.dat`.
5. **BPM-анализ** выбранного трека (`BPMDetector`, `OnsetDetector` есть, не подключены к потоку).
6. **Мелочи**: `Assets/XR 1` — пустая папка-мусор; `README_StoneUI.txt` лежит в `Assets/StoneUI`; часть uGUI-кнопок осталась только в списке треков (это ок).

---

## 10. Полезные команды

```powershell
$adb = 'C:\Users\79282\AppData\Local\Android\Sdk\platform-tools\adb.exe'
$aapt2 = 'F:\unity\hub\6000.5.0f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\build-tools\36.0.0\aapt2.exe'

& $adb devices -l
& $adb logcat -c; & $adb shell am start -n com.DefaultCompany.ragnaGod/com.unity3d.player.UnityPlayerGameActivity
& $adb logcat -d | Select-String '\[Songs\]|\[UI\]|\[Song\]|\[Drums\]|\[HMD\]|\[Record\]|\[StoneUI\]'

# проверка APK
& $aapt2 dump permissions F:\work\unity\ragnaGod\main.apk
& $aapt2 dump badging F:\work\unity\ragnaGod\main.apk | Select-String 'launchable'
& $aapt2 dump xmltree F:\work\unity\ragnaGod\main.apk --file AndroidManifest.xml | Select-String 'category'

# музыка в шлем
& $adb shell mkdir -p /sdcard/Music
& $adb push .\track.mp3 /sdcard/Music/
& $adb push .\track.mp3 /storage/emulated/0/Android/data/com.DefaultCompany.ragnaGod/files/Music/
```

В шлеме сейчас лежат тестовые файлы: `/sdcard/Music/test-mp3.mp3`, `test-music.wav`, `Linkin Park Numb.mp3`; в папке приложения — `test-tone.wav`.

Отчёты фиксера: `Logs/vr-fix-report.txt`. Логи запусков: `Logs/quest-*.txt`.

---

## 11. Параметры для быстрой подстройки

| Что | Где | Сейчас |
|---|---|---|
| Наклон луча/молота | `HandUIPointer._aimBlend`, `AimAlignedObject._blend` | 0.5 / 1.0 |
| Длина молота | `VRProjectFixer` (`targetLength`) | 0.38 м |
| Радиус и порог удара | `DrumHitDetector._hitRadius`, `_minSpeed`; `DrumController._minHitVelocity` | 0.17 / 0.6 / 0.6 |
| Расстановка барабанов | `Assets/RagnarockEditor/ScriptableObjects/DrumLayoutConfig.asset` | дистанция 0.55, высота 0.72, ширина 1.05, дуга 0.25 |
| Позиции 3D-кнопок | `VRProjectFixer.BuildStoneUi` | y≈1.18…1.32, z≈1.05…1.15 |
| Высота панелей uGUI | `WorldUIPanel._headHeightOffset`, `_distance` | 0 / 1.2 (меню), +0.35 / 1.0 (HUD) |
| Цвет кнопок пака | `VRProjectFixer.TryFindButtonSprites` (список `preferences`) | `Button_Round_Navy` первый |
```

---

# Итерация 2 (24.11.2026): M1–M6 реализованы

Ниже — дополнение к §3/§9: функциональность собрана, часть ещё не проверена в шлеме.

## Что добавилось

| Блок | Файлы | Состояние |
|---|---|---|
| **M1 BPM** | `VR/BpmFlowController.cs` (new), `UI/BPMEditorUI.cs`, `VR/SongButton.cs`, `VR/SongSelectionState.cs`, `Audio/BPMDetector.cs`, `Audio/OnsetDetector.cs` | анализ в фоновом потоке, редактор BPM (слайдер/ввод/Tap), `OnConfirmed` |
| **M2 Калибровка** | `VR/DrumCalibrationManager.cs`, `Data/DrumCalibrationProfile.cs`, `VR/DrumPositioner.cs` | рост→высота, триггер→дистанция, профиль в PlayerPrefs |
| **M3 Запись** | `Recording/RecordManager.cs`, `NoteCollector.cs`, `TimelineController.cs`, `MetronomeVisualizer.cs`, `UI/RecordHUD.cs`, `VR/RecordSessionController.cs`, `VR/SongPlaybackBootstrap.cs`, `Data/RecordSession.cs` | FSM Countdown→Recording⇄Paused→Finished, rewind с удалением ударов |
| **M4+M6 Экспорт** | `VR/OggEncoder.cs` (new), `VR/LevelExporter.cs` (new), `Assets/Plugins/Android/libs/arm64-v8a/libragnaogg.so` | квантование→валидация→`CustomSongs/<Song>/`, конвертация в ogg |
| **M5 Предпросмотр** | `Playback/PlaybackController.cs`, `RuneSpawner.cs`, `RuneVisual.cs` | пул рун, синхронизация с аудио, подсветка барабана |

## Ключевые технические решения

- **FFT**: `OnsetDetector` переведён на radix-2 (было 109 с CPU на 3-мин трек → 0.26 с).
- **OGG на устройстве**: собственный `libragnaogg.so` (863 КБ, arm64, NDK r27c) из libogg+libvorbis, обёртка `ragna_encode_ogg`. Исходники и сборка: `NativeBuild/` (`jni/Android.mk`, `jni/Application.mk`, `jni/ragna_ogg.c`). Пересборка: `ndk-build NDK_PROJECT_PATH=<abs> APP_BUILD_SCRIPT=<abs>/jni/Android.mk NDK_APPLICATION_MK=<abs>/jni/Application.mk -j8`.
  Нюанс: `ogg/config_types.h` пришлось положить руками (его генерирует configure).
- **mp3 → ogg**: `SongPlaybackBootstrap` теперь грузит клип разжатым (`DownloadHandlerAudioClip.compressed = false`), иначе `AudioClip.GetData()` возвращал false и конвертация была невозможна. Цена — ~30–60 МБ RAM на трек.
- **`LevelValidator.IsValid`** был полем, которое никогда не становилось `true` — исправлено на `Errors.Count == 0`.
- **`RecordManager.State`** — это тип enum, свойство называется `CurrentState` (C# не даёт члену класса называться как вложенный тип).

## Что нужно проверить в шлеме

1. Выбор трека → BPM (слайдер/Tap/ввод) → «Продолжить» → запись.
2. Запись: отсчёт 3-2-1, метроном, пауза, перемотка (−5 с убирает удары), автостоп по концу трека.
3. Settings (каменная кнопка) → калибровка: рост → триггер → «Сохранить»; повторный вход — профиль применяется сам.
4. Preview: руны падают на барабаны, Play/Pause/Stop, скорость 0.5x/1x.
5. ExportMap → папка `persistentDataPath/CustomSongs/<Song>/`, в логе `ogg: True`.

## Известные ограничения

- BPM-детектор путает half/double tempo (90→178.5, 150→75) — обязательна ручная правка в редакторе.
- Скорость 0.5x в предпросмотре сделана через `AudioSource.pitch` (звук ниже, без time-stretch).
- `RuneSpawner._spawnAdvanceTime` в сцене = 2 с (код-дефолт 1.5) — правится в инспекторе.
- Калибровка пишет в общий `DrumLayoutConfig.asset`: в Play Mode редактора ассет пометится dirty.
- `FindFirstObjectByType` в Unity 6.5 помечен `[Obsolete]` (только предупреждения).

## Итерация 2.1 — UI всегда перед игроком

- Новый `VR/PlayerSpaceFollower.cs` + объект `UIRig` в каждой сцене: все UI-корни (`HUDCanvas`, `SongSelectionCanvas`, `CalibrationCanvas`, `StoneUI`) стали детьми рига, `WorldUIPanel` на них отключён.
- Паттерн **lazy follow**: риг ставится в «ноги игрока» (`y = headY - 1.6`), поворот только по yaw; догоняет игрока, только если тот ушёл > `_moveThreshold` (0.4 м) или повернулся > `_rotationThreshold` (35°), со сглаживанием `_smoothSpeed` (5/с). Жёсткой привязки к голове нет — это соответствует гайдлайну Meta (не делать head-locked панели) и повторяет идею XRI `LazyFollow`.
- Тюнинг: `PlayerSpaceFollower` на объекте `UIRig` в инспекторе (`_referenceEyeHeight`, `_rotationThreshold`, `_moveThreshold`, `_smoothSpeed`, `_snapDelay`). `Recenter()` можно вызвать из кода, если понадобится кнопка «вернуть меню».

## Итерация 2.2 — окружение на ассетах + снег

- Скачаны **CC0-ассеты Kenney** (прямая ссылка через `kenney.nl/download-link`, без Asset Store): из Nature Kit взяты 16 моделей — `tree_pineTallA/B/C`, `tree_pineDefaultA/B`, `tree_pineRoundA`, `tree_cone`, `tree_simple`, камни `rock_largeA/B`, `rock_smallA/B`, `rock_tallA`, `log`, `stump_round`, `plant_bush` + общая текстура `colormap.png`. Лежат в `Assets/RagnarockEditor/Models/Environment/Kenney/`, лицензия CC0 (можно использовать и в публичной сборке).
- В фиксере: `CreateKenneyProp()` — ставит модель, натягивает общий URP-материал `Env_Kenney` с `colormap`, нормализует по высоте и сажает на землю по bounds. Если модели нет — откат на процедурную ёлку-конус.
- **Домики** — процедурные длинные дома (`CreateLonghouse`): сруб 4.5×3 м, двускатная снежная крыша, дверь; 4 дома расставлены вокруг поляны (деревня).
- **Снегопад** — `CreateSnowfall()`: ParticleSystem на `XR Origin` (следует за игроком), box 16×16 м, 150 частиц/с, lifetime 8 с, лёгкий боковой снос, мир-пространство.
- Старые сцены пересоберутся автоматически: если в `Environment` нет `Village`, он удаляется и строится заново.

## Итерация 2.3 — WinterFjord (дома, ели, камни)

- Из `WinterFjord.zip` разложены 12 FBX + палитра `FjordPalette.png` (144×8, Point-фильтр, без mipmap) в `Assets/RagnarockEditor/Models/Environment/WinterFjord/`.
- Модели **в реальном масштабе, пивот на земле** (Longhall 5.85×6.03×9.39 м, SpruceTall 6.3 м, Boulder 1.69 м) — нормализация не нужна.
- Фиксер: `CreateFjordProp()` + материал `Env_Fjord` (`URP/Lit`, `_BaseMap` = палитра). Дома деревни теперь настоящие модели (`Longhall`, `HearthCabin`, `RaisedStorehouse`), процедурный длинный дом остался как фолбэк. Ели — `SpruceTall/Broad/Spire/Young` (масштаб 0.9–1.35), камни — `Boulder/RockCluster/StandingStone/Slab`; если чего-то нет — откат на Kenney, затем на конусы.
- Пересборка окружения: триггер теперь `Village/Longhall` (раньше просто `Village`), поэтому старые сцены с процедурной деревней пересоберутся автоматически.
- Лицензия WinterFjord не указана в архиве — если планируется публикация, уточнить у автора модели.

## Итерация 2.4 — разбор скриншота из шлема и правки UI/окружения

### Как получить картинку из шлема (приём)
`adb exec-out screencap` для VR отдаёт **0 байт** — VR-слой не отдаётся композитором. Работает системный скриншот (кнопка Oculus + триггер) → файл в `/sdcard/Oculus/Screenshots/<package>-YYYYMMDD-HHMMSS.jpg`, дальше:
```powershell
& $adb pull '/sdcard/Oculus/Screenshots/com.DefaultCompany.ragnaGod-20261124-151627.jpg' Logs\headset-shot.jpg
```

### Что нашлось на скриншоте и как исправлено
1. **BPM-панель и список песен висели одновременно.** `BpmPanel` был дочерним к `SongSelectionCanvas`, без фона; список не скрывался.
   → BPM вынесен в **отдельный канвас `BpmCanvas`** (800×600, scale 0.0014) с тёмным фоном (alpha 0.96); `_selectionRoot` теперь = весь `SongSelectionCanvas`, он скрывается на время анализа. Старый `BpmPanel` фиксер удаляет.
2. **UI был слишком крупным** (800 px × 0.002 = 1.6 м на 1.2 м дистанции).
   → масштабы: выбор песни `0.002 → 0.0014`, HUD `0.0016 → 0.0013`, калибровка `0.002 → 0.0014`. `BpmCanvas` в списке для `UIRig`.
3. **Кнопки выглядели плоскими** — у LayerLab спрайт `Button_Round_Navy` сам по себе плоская заливка.
   → `AddButtonDepth()`: сверху обводка из их же `Button_Round_Line` (белая, 55 %), снизу затемнение 20 % (`SkinShade`). Дочерние Image с `raycastTarget = false`, клоны в списке наследуют.
4. **Огромное бревно у ног** — Kenney-пропсы масштабировались по высоте; у бревна Y мал → диаметр раздувался.
   → масштаб по **наибольшему** измерению, минимальная дистанция 10 м, передний сектор ±40° свободен.
5. **Вертикальная оранжевая полоса** — луч левой руки при ненайденном контроллере.
   → `HandUIPointer` выключает `LineRenderer`, если устройство не валидно; `XRHandPoseDriver` дополнительно ищет контроллер по `InputDeviceCharacteristics.Left/Right | HeldInHand | Controller`.
6. **«Треугольники» на фоне и коричневый квадрат на полу.**
   → удалены процедурные горы-конусы (`Mountains`) и деревянный настил (`Deck`); неиспользуемые материалы `Env_Mountain`/`Env_Wood` убраны из кода. Guard пересборки теперь реагирует и на остатки `Deck`/`Mountains`.

### Скайбокс
- Панорама **Poly Haven** (CC0) `pizzo_pernice_puresky`: скачан tonemapped JPG 8192×4096 (19.9 МБ), локально уменьшен до **4096×2048 и 180 КБ** (`Assets/RagnarockEditor/Textures/Sky/pizzo_pernice_puresky_4k.jpg`).
- `GetSkyboxMaterial()`: `Skybox/Panoramic` (`_MainTex`, mapping = Latitude-Longitude, exposure 1), импорт с `maxTextureSize 4096` и `wrapMode Clamp`; если панорамы нет — процедурное небо. Старый процедурный `Env_Skybox.mat` удаляется и пересоздаётся.
- Альтернативы под тот же шейдер: `snow_field_puresky`, `alps_field`, `winter_evening`, `snowy_hillside`.

### Состояние на конец итерации
- Все правки проверены только **парсером Roslyn** (`csc` из dotnet SDK, фильтр `CS1xxx`) — Unity не запускался. Логика в шлеме не проверялась.
- Следующий шаг: `Tools → Ragnarock Editor → Fix VR Project` → Build → скриншот (Oculus + триггер) → `adb pull` → визуальная проверка.

### Мелочи на будущее
- В коде остались два `0.002f` (старый `MainMenuCanvas`, который всё равно удаляется) — не влияет.
- Материалы `Env_Mountain.mat` / `Env_Wood.mat` остались файлами на диске, но кодом не используются.

---

# Итерация 3 (24.11.2026): интерфейс, локализация, защита несохранённых данных

Полный разбор и обоснование — `plans/UI_UX_DESIGN.md`. Ниже — короткая сводка.

## Что изменилось в поведении

| Было | Стало |
|---|---|
| Старт с `RecordScene` | Старт с `MainMenuScene` → интро-экран с объяснением и кнопкой «Начать» |
| Только русский | 8 языков (en/ru/de/fr/es/it/pl/pt), автоподхват языка системы, выбор в интро и на выборе песни, `PlayerPrefs["ragna.locale"]` |
| Все каменные кнопки всегда видны | Видимость по состоянию: NoSong / Ready / Countdown / Recording / Paused / Finished |
| В `NoSong` автостарт «свободной игры» | В `NoSong` только «Выбрать песню» / «Игра без песни», кнопки записи скрыты |
| Выход без подтверждения | Диалог «Выйти?»; при несохранённом дубле — «Выйти без сохранения / Экспортировать и выйти / Отмена» |
| Барабаны только в Record/Preview | Барабаны во всех 4 сценах |
| Back с BPM уводил в меню | Возврат к списку треков |
| Play доступен до загрузки трека | Показывается «Загрузка трека…», запись не стартует |
| Модалки не блокировали луч | `UIRaycastBlocker` останавливает луч руки на фоне модалки |

## Новые и переработанные файлы

- `Scripts/Localization/`: `Language.cs`, `Loc.cs`, `LocKeys.cs`, `LocalizedText.cs`, `LanguageSwitcher.cs`.
- `Scripts/Core/`: `AppFlow.cs`, `ConfirmDialog.cs`, `UIRaycastBlocker.cs`.
- `Scripts/VR/`: `IntroScreen.cs`, `SongSelectionScreen.cs`; переписаны `RecordSessionController.cs`, `StoneButton.cs` (+`ScreenRouter`), `BpmFlowController.cs` (`Cancel`), `SongSelectionList.cs`, `HandUIPointer.cs`.
- `Assets/Resources/Localization/{en,ru,de,fr,es,it,pl,pt}.json` — 106 ключей в каждом.
- `EditorTools/VRProjectFixer.cs`: `BuildIntroScreen`, `BuildConfirmDialog`, `BuildSongSelectionFlow`, новые панели записи, `EnsureDrums` для всех сцен, `ApplyLoc` для подписей, `EnsureBuildSettings`, идемпотентные каменные кнопки.
- `ProjectSettings/EditorBuildSettings.asset`: `MainMenuScene` теперь первая сцена.

## Что нужно сделать руками

1. Открыть проект в Unity 6000.5.0f1.
2. **Tools → Ragnarock Editor → Fix VR Project** (обязательно: сцены пересобирает фиксер).
3. Build And Run на шлем; чек-лист — в `plans/UI_UX_DESIGN.md` §9.

## Ограничения

- Unity в этой сессии не запускалась: код проверен компилятором Roslyn (0 ошибок), логика в шлеме не проверялась.
- Нет CJK-языков: `LegacyRuntime.ttf` без иероглифов, нужен TMP-шрифт.
- Экспорт синхронный (блокирует кадр на время OGG-кодирования).
- Системное закрытие приложения / «Домой» не перехватывается.

### Итерация 3.1 — после первой сборки

1. **«Выбрал песню — ничего не происходит».** Причин было две: (а) в сцене два
   `BpmFlowController`, и `SongButton` находил старый на `SongSelectionCanvas` без ссылки
   на BPM-канвас; (б) второй лежал на выключенном `BpmCanvas`, поэтому его `Update()` /
   `PollAnalysis()` не работали. Теперь дубликаты удаляются фиксером, единственный
   оркестратор живёт на активном `SongSelectionFlow`; BPM-экран открывается сразу по
   тапу трека («Загрузка…» → «Анализ…», «Продолжить» выключена до результата).
2. **Меню больше не плавает.** `PlayerSpaceFollower` удаляется, `UIRig` — ребёнок
   `XR Origin`; панели на фиксированных локальных координатах над барабанами.
3. **Кнопка «Начать запись»** в состоянии Ready (ключ `record.ready.start`).
4. **Читаемость:** тёмные подложки у списка песен / BPM / панелей записи; каменная Back
   на выборе песни вынесена за край панели (x = −0.62).
5. `AudioLoader` грузит клип разжатым (`compressed = false`) — иначе BPM-анализ
   не получал PCM и откатывался к 120 BPM.

После этих правок нужно снова прогнать `Tools → Ragnarock Editor → Fix VR Project`
и пересобрать APK.

### Итерация 3.2 — звук (FjordAudio)

- `FjordAudio.zip` распакован в `Assets/Resources/SFX/`: 12 ударов
  (`Drums/Drum{1..4}_{1..3}.wav`) и 8 UI-звуков (`UI/Hover,Select,Confirm,Back,Pause,RecordStart,CountIn,SeekTick`).
  `FjordDemo.wav` в проект не копировался.
- `Audio/SfxPlayer.cs` — ленивый синглтон: грузит клипы из Resources, 8 голосов,
  UI 2D + удары 3D (случайная вариация, громкость от скорости). Прогрев —
  `RuntimeInitializeOnLoadMethod` до первой сцены.
- `Audio/SfxButton.cs` — звук клика для uGUI-кнопок; фиксер вешает его на все кнопки,
  диалогам ставит Confirm/Back, экспорту — Confirm.
- Хуки: `DrumController` (удар), `HandUIPointer` (наведение + клик по uGUI),
  `StoneButton` (нажатие по действию), `RecordManager` (отсчёт CountIn, начало RecordStart),
  `AppFlow` (успех/ошибка экспорта).
- Фиксер настраивает импорт: PCM, DecompressOnLoad, preload, mono — иначе на Quest
  первый звук запаздывает.

Снова прогнать фиксер → Build.

### Итерация 3.3 — все руны на одном барабане

- Причина: в `Drum.prefab` `_drumIndex = 0`, `DrumManager.CreateDrums()` индекс не
  переопределял → все удары писались как барабан 0 → все ноты `_lineIndex = 0` →
  `RuneSpawner` вёл все руны к первому барабану.
- `DrumController.SetDrumIndex()`; индекс задаётся в `CreateDrums`, в событии
  `OnAnyDrumHit` передаётся индекс по позиции в массиве; `PositionDrums` и
  `DrumSceneBootstrap.AssignDrumIndices` переприсваивают 0..3 при расстановке.
- `RecordSession.ClearHits()` чистит и `QuantizedNotes` — новый дубль больше не
  показывает старую карту в предпросмотре.
- В лог добавлено распределение ударов по барабанам.
- Старые дубли не восстановить — нужен новый. Пересборки APK достаточно,
  фиксер для этого фикса не обязателен.

### Итерация 3.4 — барабаны ближе, кружки как в игре

- `DrumLayoutConfig.asset`: `DistanceFromPlayer` 0.55 → 0.45 м, `TotalWidth` 1.07 м
  (шаг 0.357 при диаметре 0.35 → зазор ~7 мм, ряд почти впритык), `ArcRadius` 0.25 → 0
  (дуга раздвигала крайние барабаны вбок и ряд выглядел разреженным).
- `Playback/DiscVisual.cs` — общий меш/материал диска (32 сегмента, URP/Lit,
  двусторонний, эмиссия), размеры в метрах, палитра по барабанам.
- `RuneVisual` заменяет сферу из `Rune.prefab` на диск 0.32 м, убирает коллайдер;
  размер постоянный, появление и исчезновение — alpha-фейд по **0.4 с**
  (материал URP/Lit Transparent).
- `RuneSpawner`: руны летят к игроку в плоскости над барабанами
  (`_approachDistance = 2.2`, `_hoverHeight = 0.1`) вместо падения сверху.
- `Playback/HitDiscEffect.cs` — пул кружков, улетающих от игрока после удара
  (спин, замедление, постоянный размер; появление 0.4 с, исчезновение ~0.4 с,
  полёт 0.9 с); вызывается из `DrumController.RegisterHit`.
- **Антидребезг удара**: `DrumHitDetector` считает удар как **пересечение плоскости**
  сверху вниз одной точкой `HeadPosition` (геометрия/«две грани» не важны). После удара
  молот «разряжен» до подъёма на `_rearmHeight = 0.09`; это убивает дубли от отскока
  и проскальзывания сквозь барабан. Плюс: только сверху (vy ≤ −0.35), только ближайший
  барабан, кулдаун 0.12 с, ниже −0.12 м не считается. Каждый удар пишет в лог
  `[Hit] drum=… vy=… speed=…`.
- `Playback/DeckOrientation.cs` — единое направление «по палубе» (XR Origin),
  поэтому все руны и все отлетающие кружки летят **параллельно**, без веера.
- Радиус зоны удара уменьшен 0.22 → 0.17 м: при шаге 0.357 зоны соседних барабанов
  (0.17 × 2 = 0.34) больше не перекрываются, один замах не засчитывается двум барабанам.
- Пересборки APK достаточно, фиксер не нужен.

### Итерация 3.5 — папка экспорта, сетка, повтор

- Фейд кружков увеличен до **1 с** в обе стороны (предпросмотр и удар), полёт кружка
  после удара 2.4 с; прозрачность URP/Lit выставляется и через Blend/ZWrite напрямую,
  в меш добавлены UV — на случай fallback-шейдера.
- `Core/ExportSettings` + `VR/ExportFolderSelector`: 3 папки экспорта
  (`persistentDataPath/CustomSongs`, `/sdcard/Music/RagnarockMaps`,
  `/sdcard/Download/RagnarockMaps`), переключение кнопкой «Папка: …» в предпросмотре,
  путь под кнопкой, выбор в PlayerPrefs. `SongFileManager.GetCustomSongsPath()`
  теперь возвращает выбранную папку.
- Починен «молчащий» экспорт: `PreviewUI.OnSave` идёт через `AppFlow.SaveThen`
  (квантование + диалог с результатом/путём), `ExportCurrentSession` подхватывает
  `QuantizeSettings` из Resources.
- Сетка: `Assets/Resources/QuantizeSettings.asset` с `SnapThreshold 0.5`,
  `SnapDivision 4` — каждый удар притягивается к ближайшему делению (1/4 бита).
- В предпросмотре добавлена каменная кнопка Replay (Stop + Play с начала).
- **Нужен прогон фиксера**: он создаёт кнопку выбора папки и Replay в PreviewScene.

### Итерация 3.6 — метроном

- `MetronomePlayer`: клипы из `Resources/SFX/Metronome/Tick` и `TickAccent`
  (если файлов нет — синтезированный клик), громкость в `PlayerPrefs`
  (`ragna.metronome.volume`), событие `OnTick(accent)`.
- Экран BPM: кнопка «Включить/Выключить метроном» + ползунок громкости,
  `MetronomePreview` тикает с текущим BPM и останавливается при уходе с экрана.
- Экран записи: иконка `●` + подпись «Метроном» (`MetronomeIndicator`), пульсирует
  на тиках; `RecordManager.TickMetronome` тикает по времени песни (один тик на бит,
  акцент каждые 4), а не своим циклом — без дрейфа.
- `FjordMetronome.zip` в корне **не найден** (проверено `F:\work\unity` и проект).
  Когда появится — распаковать `Tick`/`TickAccent` в `Assets/Resources/SFX/Metronome/`,
  код уже готов.
- **Нужен прогон фиксера** (кнопка/слайдер/иконка создаются им) и пересборка APK.

### Итерация 3.7 — UI-паки

- Звук метронома: `FjordMetronome.zip` распакован, `Tick.wav` → `Resources/SFX/Metronome/Tick.wav`,
  `Downbeat.wav` → `TickAccent.wav`. Синтезированный клик больше не используется.
- `FjordScrollList.zip`: панель списка песен (процедурный `StonePlate`) интегрирована
  в `SongSelectionScene`; адаптер `FjordSongListAdapter` сканирует треки, листает по 4,
  добавляет строкам BoxCollider для луча руки; старый uGUI-список скрыт.
  Модели `Panel/Row/RowSelected/Thumb` скопированы, но пока не задействованы.
- `StoneControls.zip`: 10 FBX скопированы в `Assets/RagnarockEditor/Models/StoneControls/`.
  Во FBX есть узлы `Fill`/`Handle`; предложена раскладка пульта (см. UI_UX_DESIGN §17.2),
  реализация контролов — следующий шаг после подтверждения раскладки.
- **Нужен прогон фиксера** (панель списка создаётся им) и пересборка APK.

### Итерация 3.8 — единая плоскость и StoneControls

- Все основные панели выровнены: z 1.20, scale 0.0011; калибровка 1.10,
  диалог 1.06 (модальный), Stone UI 1.20, каменные контролы 1.19.
- `StoneControl` оживляет FBX через узлы `Fill`/`Handle` (свои заливка и ручка).
- `StoneControlsPanel`:
  - BPM: тумблер метронома, `Volume_H`, чекбокс snap, `Progress_H` анализа;
  - Record: `Progress_H` записи, `Volume_V` метронома;
  - Preview: `Seek_H` перемотка, `Progress_V` прогресс, `Seek_V` скорость.
- `MetronomeRuntime.Enabled` и `QuantizeRuntime.SnapEnabled` — общие переключатели
  (PlayerPrefs); выключенный метроном не тикает при записи.
- `HandUIPointer` поддерживает `StoneControl.PressAt(point)`.
- **Нужен прогон фиксера** и пересборка APK.
---

# Итерация 3.9 (24.11.2026): «весь интерфейс из каменных плит» — исправлен корень «плоскости»

## Почему интерфейс был плоским

`StonePlate` (процедурная плита для uGUI) создавался через `AddComponent<StonePlate>()`
на пустом `RectTransform`. На таких объектах **не появлялся `CanvasRenderer`**, поэтому
плиты не рисовались вообще: на экране оставались только тексты и 3D-плиты из `StoneUI`.
Так было и со списком треков (`Fjord Scroll List` → `Stone`), и с BPM-панелью, и со всеми
подложками. Лечение:

- у `StonePlate` теперь `[RequireComponent(typeof(CanvasRenderer))]` + подстраховка в `Awake()`;
- все фабрики (`StoneUiKit.CreatePlate/CreatePlateButton/EnsureStoneSlider/EnsureStoneInputField`,
  `FjordListBuilder.Paint`, `CreateStoneBoardSlab`) создают `CanvasRenderer` явно;
- `StoneUiKit.RepairStonePlates()` в конце `FixScene` добивает старые сцены (лог:
  `CanvasRenderer добавлен N плитам`).

Проверено кадром из Play Mode (см. ниже): плиты рисуются, фон списка и планшет BPM
появились.

## Что сделано по виду

- `StonePlate` умеет: фаску (`bevel`), утопленную площадку (`recess` + цвет),
  шероховатость грани (`grain`), подсветку/тень фаски и скол на правой грани.
- Палитра (`StoneUiKit`): `Amber` (207,135,59 — материал паков StoneUI/FjordScrollList),
  `AmberFace`, `Plate` (терракота кнопок), `Burlap` (89,61,50 — строки/подписи),
  `Recess`, `Ink`, `InkDim`, `Accent`.
- **Все uGUI-кнопки** — каменные плиты: `Image` со спрайтом заменяется на `StonePlate`
  (`StoneUiKit.StyleAllButtonsInScene`), хитбокс `Hit` сохраняется, спрайтовый скин
  LayerLab и `AddButtonDepth` удалены.
- `UiHoverTint` — плита под лучом светлеет (у uGUI нет EventSystem-наведения, подсветку
  включает `HandUIPointer`).
- Подложки интро, диалога выхода, панелей записи (NoSong/Ready/Finished), калибровки,
  старого списка — каменные плиты; полосы прогресса — каменные жёлоба (заливка акцентом).

## Экран BPM

- Планшет 800×600 (scale 0.0011, y = 1.50, z = 1.20): янтарная плита с фаской и светлой
  внутренней площадкой → плита заголовка → тёмное «выдолбленное» окно с `128 BPM`
  и уверенностью → каменный жёлоб-индикатор BPM → поле точного BPM, Tap Tempo,
  «Назад», «Продолжить» — всё плитами; подписи слоновой костью/тёмно-коричневым.
- Порядок детей задаётся явно (`Slab` первым, подписи последними), иначе плиты,
  созданные позже текста, его перекрывают.
- Каменный пульт `BpmStoneControls` (y = 1.02, z = 1.19) на янтарной плите-основании:
  `BpmTune` (Volume_H, 40–300 BPM), тумблер метронома, его громкость, snap,
  прогресс загрузки — 5 контролов в два ряда.
- Пульт и плита-заголовок списка включаются вместе с BPM-экраном:
  `BpmFlowController._bpmConsole` / `_listHeader` (`SetBpmScreensVisible`).
- Ползунок BPM теперь **работает в шлеме**: uGUI-слайдер луч не тянет (только клик по
  `Button`), поэтому BPM правится 3D-контролом `BpmTune`; `BPMEditorUI.SetBpmFromControl`
  двигает индикатор и пишет значение в сессию, Tap Tempo и результат анализа двигают
  сам ползунок (`StoneControlsPanel.UpdateBpmSlider`).

## Прогон фиксера и сборка

- `FixScenes` начинает с пустой сцены, если текущая грязная/идёт Play Mode — иначе
  `EditorSceneManager.OpenScene` падал «This cannot be used during play mode».
- Фиксер прогнан, сцены пересобраны, APK собран и **установлен на Quest 3**
  (`main.apk`, финальная сборка 23:45:50, 43.7 МБ; в отчёте 1 ГБ — это распакованный объём).
- Отчёт: `Logs/vr-fix-report.txt` (OK, 23:45:19Z).

## Как я снимал кадры без шлема (приём на будущее)

Play Mode + временный `AgentCaptureRunner` на сцене `SongSelectionScene`:
камера ставится в `XR Origin` (0, 1.62, −0.55) и смотрит на планшет, `HMDPoseDriver`
выключается, руки скрываются, BPM-экран открывается вручную, демо-результат
`128 BPM / 78%`, кадр снимается **через RenderTexture** (`camera.targetTexture` +
`ReadPixels`) — `ScreenCapture` в runtime-сборке недоступен, `camera.Render()` под URP
не работает. Кадры: `Logs/preview-list.png`, `Logs/preview-bpm.png`.

Запуск действий в открытом редакторе: `plans/tools/unity-drive.ps1` —
пишет маркер `Library/*.pending`, активирует окно Unity и шлёт `Ctrl+R`
(Assets → Refresh: подхватывает скрипты и запускает `[InitializeOnLoadMethod]`).
Если скрипты не менялись, доменной перезагрузки не будет — помогает переключение
Play Mode (`Ctrl+P` дважды).

## Что осталось проверить в шлеме

1. Список треков и BPM-экран: цвет/читаемость плит, подсветка под лучом, нажатия.
2. Экран записи: плиты состояний NoSong/Ready/Finished, каменный жёлоб прогресса.
3. Калибровка и диалог выхода (модальные плиты блокируют луч).
4. Пульт BPM в шлеме: `BpmTune` тянется лучом и меняет BPM.

Итог по сценам: кнопок без плиты нет; `Image` остались только там, где это
заливка шкалы (`CalProgressFill`, `ProgressFill`) или прозрачная маска списка (`Viewport`).
---
---

# Итерация 4 (25.11.2026): эксперимент с барабанами откатан

Пробовал переставить барабаны «лицом к игроку» (поворот −90° по X), увеличить их,
перекрасить и перевести полёт рун/кружков в плоскость головок. По просьбе
пользователя **все правки по барабанам и полёту кружков откатаны** к состоянию
итерации 3.9:

- `DrumPositioner` — прежний поворот `Quaternion.Euler(DrumTiltAngle, 0, 0)`;
  `DrumLayoutConfig.asset` — 0.45 м / 0.72 м / 1.07 м / дуга 0 / наклон 10° / диаметр 0.35.
- `DrumHitDetector` — прежний детект по горизонтальной плоскости сверху вниз.
- `RuneSpawner` / `RuneVisual` / `HitDiscEffect` — руны и кружки снова летят
  в горизонтальной плоскости над барабанами, диски вращаются вокруг Y.
- `DrumController` / `DrumManager` / `DrumSceneBootstrap` — без обода, масштаба
  и нормалей головки.
- Материалы `Drum_Mat` (0.4, 0.2, 0.1) и `DrumHead_Mat` (0.9, 0.85, 0.7) — исходные.

## Что из этой итерации осталось (не связано с барабанами)

- **`PlacePanel` пишет `anchoredPosition`.** У RectTransform позицию задаёт именно он;
  раньше фиксер писал только `localPosition`, и панели «не переезжали» по высоте
  (в сцене оставалось старое значение). Теперь планшет BPM и пульты стоят там,
  куда их ставит фиксер.
- **`AutoRunOnce` не запускает фиксер в Play Mode** — раньше падал на `OpenScene`.
- README, `.gitignore`/`.gitattributes`, вендорные исходники libogg/libvorbis в `NativeBuild/`,
  репозиторий на GitHub (`zlo2k/RagnaSongMaker`).
---
---

# Итерация 5 (26.11.2026): логика карт сверена с настоящей картой Ragnarock

Проверял на `divadance.zip` с корня воркспейса — карта «Diva Dance» (BPM 120, сложность Easy,
312 нот, сделана в Edda 1.0.1, лежит как `divadance/{info.dat, Easy.dat, song.ogg, cover.jpg, preview.ogg}`).
Эталон формата — исходники Edda (`Classes/MapEditor/RagnarockMap.cs`), потому что карта сделана им
и это та реализация, по которой карты реально играются.

## Что было не так

1. **`_events` и `_obstacles` не попадали в файл вообще.** В DTO они были помечены `[NonSerialized]`,
   а Newtonsoft такие поля не сериализует (как `[JsonIgnore]`). В `*.dat` оставались только
   `_version`, `_customData`, `_notes`. Edda требует все три массива и падает с
   «Incorrect or missing key _events»; реальная карта их содержит (пустыми). Атрибуты убраны.
2. **`_customData._BPMChanges` терялся** по той же причине. Заодно добавлен `_bookmarks` —
   в настоящих картах он есть.
3. **`_lineLayer` писали 0.** Игра слой не использует (принимает 0..3), но Edda и MMA2 всегда пишут
   1 — теперь пишем 1, чтобы файл был неотличим от карт экосистемы.
4. **`LevelValidator` ругался на любую настоящую карту:** требовал `_lineLayer == 0` →
   на divadance получалось 312 ошибок. Теперь 0..3 допустимо, `!= 1` — предупреждение.
   `_type` и `_cutDirection` наоборот ужесточены до ровно 0 и 1 (игра других не принимает,
   Edda на них падает) — раньше `_type = 1` считался валидным, а `_cutDirection != 1` — только предупреждением.
5. **`info.dat`:** `_version` «2.0.0» → «1»; добавлен `_explicit` — и это СТРОКА `"false"`,
   а не bool (так в формате и в Edda); добавлен `_customData` с `_editors` (кто сделал карту);
   `_noteJumpMovementSpeed` 14 → 15 (средняя скорость по документации Edda).
6. **Обложка:** `_coverImageFilename` больше не становится пустым при сбое генерации — всегда
   `cover.jpg` (карта без обложки в игре выглядит битой).
7. **MP3-трек не перекодировался.** `LevelExporter.ExportCurrentSession` не передавал клип, а
   `ResolveClip()` смотрел только поля инспектора → экспорт молча копировал MP3 под именем
   `song.ogg`, который Ragnarock (Ogg Vorbis) не проиграет. Теперь клип берётся ещё и из
   `SongPlaybackBootstrap` (он специально грузится `compressed = false` для `GetData()`).

## Фаза сетки: первый бит не в 0

`BPMDetector` давно считает `FirstBeatOffset` (`FindFirstBeat`), но результат никто не читал.
Сетка квантования была привязана к t = 0, поэтому у трека с интро (а так начинается почти любая
музыка — об этом прямо пишет документация Edda) все ноты систематически уезжали от музыки
до половины доли: **62 мс при 120 BPM**, на слух — «флама» при игре.

- `SongData.FirstBeatSeconds` — фаза из анализа; пишется в `BpmFlowController.SaveSongToSession`
  (и в старом `SongSelectionUI`).
- `BeatQuantizer` считает `_time` как АБСОЛЮТНЫЕ биты (`t * BPM / 60`), а фазу использует только
  в `SnapToGrid`. Раньше `SecondsToBeats` вычитал фазу — с ненулевой фазой экспорт уехал бы назад.
- Фаза передаётся во все три места квантования (`LevelExporter`, `PlaybackController`, `PreviewUI`),
  и метроном в `RecordManager` тикает с той же фазой (иначе он щёлкает мимо музыки и тянет запись за собой).
- Проверка арифметики (BPM 120, фаза 0.44): удар 0.44 с без фазы прилипает к 0.5 с (60 мс),
  с фазой остаётся 0.44 с, `_time` = 0.88 бита, обратный пересчёт игры даёт те же 0.44 с.

## Инструмент проверки

`plans/tools/map-format-check` — консольный .NET-проект: читает папку карты нашими DTO,
гоняет `LevelValidator` и структурно сравнивает JSON «как мы запишем» с настоящим файлом
(чего не хватает, что лишнее, где другой тип, все ли обязательные ключи на месте).

```
cd plans/tools/map-format-check
dotnet run "путь/к/распакованной/карте"
```

Результат на divadance: 312 нот (0×86, 1×74, 2×69, 3×83), время 9…169.75 бита (84.9 с),
все `_lineLayer = 1`, `_type = 0`, `_cutDirection = 1`; **валидатор — 0 ошибок, 0 предупреждений**;
`.dat` — схема совпадает полностью; `info.dat` — расхождения только в редакторских блоках
(`_editors`, `_customData` сложностей), игра их не читает.

Сам формат (поля, типы, тайминг, грабли) вынесен в [MAP_FORMAT.md](MAP_FORMAT.md) в корне проекта —
это справка, по которой можно править карты руками и проверять новые.

## Что сознательно не менял

- **`preview.ogg` не делаем.** По документации Edda превью необязательно: без него игра играет
  первые секунды трека.
- **Zip для шаринга — плоский** (файлы в корне), как «Export Map» в Edda и как ждёт RagnaCustoms.
  Имя архива = имя папки карты, поэтому «Извлечь всё» создаёт сразу `CustomSongs/<Имя>/`.
  Архив `divadance.zip` пришёл с вложенной папкой — для игры это неважно, распаковку она не делает.
- **Папка карты** по-прежнему из названия трека (может быть с пробелами/кириллицей). Edda для новых
  карт требует только латиницу, но игра читает любую папку в `CustomSongs`.
- **`_BPMChanges` не пишем** (пустой массив): фазу выражаем дробными битами в `_time`, это работает
  независимо от того, учитывает ли игра тайминговые изменения.

## Что нужно

Пересобрать APK — правки только в C#. Фиксер сцен не нужен; сцены в репозитории (4 .unity)
изменены ещё до этой итерации.

