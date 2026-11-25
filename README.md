# RagnaSongMaker

**VR-редактор ритм-карт для [Ragnarock](https://store.steampowered.com/app/1029780/Ragnarock/) на Meta Quest 3.**
Ставите свой трек, играете по четырём барабанам молотами в шлеме — из ударов собирается
готовая карта (`CustomSongs/<Трек>/`), которую можно подкинуть в игру.

*RagnaSongMaker is a VR beatmap editor for Ragnarock running natively on Meta Quest 3
(OpenXR, Unity 6). You play the song on four drums with hammers and the app exports
a ready-to-use custom map.*

---

## Что умеет

| Этап | Что происходит |
|---|---|
| **Выбор трека** | Сканирование памяти шлема (`/sdcard/Music`, `Download`, `Documents`, MediaStore), каменный список с постраничной листанием |
| **Определение BPM** | Автоанализ (onset detection → autocorrelation) + ручная правка: каменный ползунок, Tap Tempo, точное поле |
| **Запись** | Отсчёт 3-2-1, метроном, пауза, перемотка −5 с с удалением ударов, автостоп по концу трека |
| **Квантование** | Удары притягиваются к сетке (1/4 бита по умолчанию), порог настраивается |
| **Предпросмотр** | Руны летят на барабаны, подсветка попадания, скорость 0.5×/1×, перемотка |
| **Экспорт** | `info.dat` + `*.dat` сложности + `song.ogg` (MP3 → OGG на устройстве через libvorbis) |

Дополнительно:

- **8 языков** (en/ru/de/fr/es/it/pl/pt) с автоподхватом языка системы.
- **Каменный интерфейс**: все панели, кнопки, ползунки и шкалы — процедурные каменные плиты
  (`StonePlate`), плюс объёмные плиты из паков Stone UI / StoneControls и подсветка под лучом.
- **Калибровка**: рост игрока → высота барабанов, вытянутая рука → дистанция.
- **Звук**: удары по барабанам, UI-клики, метроном, отсчёт — из пака FjordAudio.
- **Окружение**: снежная долина (Kenney Nature Kit + WinterFjord), панорамный скайбокс Poly Haven.

---

## Быстрый старт

1. **Unity 6000.5.0f1** (Unity 6), модуль **Android Build Support** + OpenXR.
2. Открыть проект, дождаться импорта.
3. Меню **`Tools → Ragnarock Editor → Fix VR Project`** — обязательный шаг: фиксер собирает
   XR-риг, UI, барабаны, окружение и материалы во всех четырёх сценах.
4. **Build And Run** на подключённом Quest 3 (USB, режим разработчика, adb авторизован).
5. В шлеме: «Выбрать песню» → разрешить доступ к музыке → выбрать трек → проверить BPM →
   «Продолжить» → записать → экспорт.

> ℹ️ Batch-режим (`-executeMethod VRProjectFixer.FixAll`) не работает без лицензии
> `com.unity.editor.headless` — используйте пункт меню.

### Что нужно положить вручную

- **`Assets/Layer Lab/GUI-BasicButtonPack/`** — пак из Asset Store (LAYERLAB), в репозиторий не входит.
  Импортируется через *Package Manager → My Assets*. Сцены его больше не используют
  (кнопки заменены каменными плитами), так что без него проект собирается — просто не будет
  демо-сцены пака.
- **Треки** для записи: `.mp3` / `.ogg` в `/sdcard/Music` или `Download` шлема.

---

## Управление

| Действие | Как |
|---|---|
| Наведение | луч из правой/левой руки (aim-поза контроллера) |
| Нажать | триггер, когда луч на кнопке/плите |
| Ударить по барабану | замах молотом сверху-вперёд, удар засчитывается по пересечению плоскости головки |
| Вернуться / выйти | каменная плита `Back` (спрашивает про несохранённый дубль) |

---

## Устройство проекта

```
Assets/
  RagnarockEditor/
    Scripts/
      Audio/          загрузка трека, BPM/onset-детекторы, метроном, SFX
      Core/           AppFlow, SceneLoader, ConfirmDialog, UIRaycastBlocker
      Data/           DTO-модели, RecordSession, DrumLayoutConfig, профиль калибровки
      FileSystem/     поиск треков, экспорт CustomSongs
      Localization/   8 языков, LocalizedText, LanguageSwitcher
      Playback/       предпросмотр: PlaybackController, RuneSpawner, диски, эффекты
      Quantization/   сетка и квантование ударов
      Recording/      RecordManager (FSM), NoteCollector, таймлайн
      Serialization/  .dat сериализация и валидация
      UI/             каменные плиты (StonePlate), BPM-редактор, HUD
      VR/             XR-риг, руки, молоты, барабаны, Stone UI, экраны
      EditorTools/    VRProjectFixer — сборка сцен (Editor)
    Scenes/           MainMenuScene, SongSelectionScene, RecordScene, PreviewScene
    Models/           StoneUI, StoneControls, WinterFjord, Kenney, StormMallet
    Materials/        URP-материалы, конвертация из Standard
    Textures/         скайбокс Poly Haven
  Resources/          локализация, SFX, QuantizeSettings
  Plugins/Android/    libragnaogg.so (arm64) — MP3 → OGG
NativeBuild/          исходники и NDK-сборка libragnaogg.so (libogg + libvorbis)
plans/                UI_UX_DESIGN.md — дизайн интерфейса, решения и грабли
SESSION_NOTES.md      журнал итераций, параметры для подстройки, полезные команды
```

### Пересборка нативного `libragnaogg.so`

```bash
ndk-build NDK_PROJECT_PATH=<abs>/NativeBuild/ogg-android \
          APP_BUILD_SCRIPT=<abs>/NativeBuild/ogg-android/jni/Android.mk \
          NDK_APPLICATION_MK=<abs>/NativeBuild/ogg-android/jni/Application.mk -j8
```

Результат — `NativeBuild/ogg-android/libs/arm64-v8a/libragnaogg.so`, копируется в
`Assets/Plugins/Android/libs/arm64-v8a/`.

---

## Технические детали

- Unity **6000.5.0f1**, URP **17.0.3**, OpenXR **1.17.1**, XRI **3.5.1**, uGUI 2.0.0, Newtonsoft JSON.
- Android, **IL2CPP**, ARM64, minSdk 26, Linear color space, Input System only.
- Барабаны стоят **вертикально** перед игроком (плоскость головки смотрит на игрока),
  удар считается по пересечению плоскости головки вдоль её нормали.
- Интерфейс — world-space канвасы, закреплённые на палубе (`XR Origin`), а не «плавающие»
  за головой; каменные плиты рисуются процедурной геометрией без текстур.
- Метроном, snap и прогресс — объёмные каменные контролы (`StoneControls`) на пульте над барабанами.

---

## Сторонние ассеты и лицензии

| Ассет | Лицензия |
|---|---|
| [Kenney Nature Kit](https://kenney.nl/assets/nature-kit) | CC0 |
| [Poly Haven](https://polyhaven.com/) скайбокс `pizzo_pernice_puresky` | CC0 |
| WinterFjord (дома, ели, камни) | в архиве не указана — уточнить у автора перед публикацией |
| StoneUI Quest, StoneControls, FjordScrollList, FjordAudio, FjordMetronome, StormMallet | в архивах не указана |
| **Layer Lab GUI Basic Button Pack** | Asset Store, **в репозиторий не входит** (нельзя распространять) |

Перед публичным релизом сборки стоит уточнить лицензии на паки из таблицы.

---

## Документация

- [`plans/UI_UX_DESIGN.md`](plans/UI_UX_DESIGN.md) — дизайн интерфейса: экраны, состояния, каменные плиты,
  почему панели стоят там, где стоят, и список уже наступивших грабель.
- [`SESSION_NOTES.md`](SESSION_NOTES.md) — журнал итераций, что проверено в шлеме, параметры для быстрой
  подстройки (дистанция/высота барабанов, порог удара, размеры панелей) и полезные adb-команды.
