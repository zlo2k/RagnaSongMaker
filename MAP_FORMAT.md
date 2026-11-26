# Формат карты Ragnarock

Справка по формату кастомной карты — что игра читает, что требует, а что игнорирует.
Собрана и проверена на настоящей карте `divadance.zip` («Diva Dance», BPM 120, Easy, 312 нот,
сделана в Edda 1.0.1) и на исходниках [Edda](https://github.com/PKBeam/Edda) —
`Classes/MapEditor/RagnarockMap.cs` (референсная реализация формата, ей же сделана эта карта).

Дата сверки: 26.11.2026. Подробности правок — в [SESSION_NOTES.md](SESSION_NOTES.md), итерация 5.

---

## 1. Структура карты

Карта — это папка в `CustomSongs` (на PC: `Документы/Ragnarock/CustomSongs/<Имя>/`):

```
<Имя>/
  info.dat        метаданные + список сложностей
  Easy.dat        карта сложности (имя файла задано в info.dat)
  Normal.dat
  Hard.dat
  song.ogg        аудио, ТОЛЬКО Ogg Vorbis (Opus не поддерживается)
  cover.jpg       обложка (квадратная; Edda обрезает до квадрата)
  preview.ogg     превью, необязательно
```

- Сложностей максимум **3** (Easy / Normal / Hard) — в игре три треугольника.
- `preview.ogg` необязателен: без него игра играет первые секунды трека.
- Имена файлов сложностей свободные, но должны совпадать с `_beatmapFilename` в `info.dat`.
- Папку `CustomSongs` игра обходит как набор подпапок (карта = любая папка с `info.dat`).
  Edda для новых карт требует только латиницу — это ограничение Edda, в самом формате
  особых требований к имени папки нет.

---

## 2. `info.dat`

JSON с ведущими подчёркиваниями в ключах. Все ключи ниже, кроме `_songTimeOffset` и `_customData`,
Edda считает **обязательными** и на отсутствии падает с `Incorrect or missing key ...`.

| Ключ | Тип | Что это | Что пишем мы |
|---|---|---|---|
| `_version` | string | версия формата; **игра её не сверяет**, во всех картах `"1"` | `"1"` |
| `_songName` | string | название трека | из сессии |
| `_songSubName` | string | подзаголовок (у всех `""`) | `""` |
| `_songAuthorName` | string | исполнитель | из сессии |
| `_levelAuthorName` | string | автор карты | из сессии |
| `_explicit` | **string** | `"false"` / `"true"` — именно строка, не bool | `"false"` |
| `_beatsPerMinute` | number | BPM, > 0 — **главный параметр тайминга** | из сессии |
| `_shuffle` | number | не используется (у всех 0) | `0` |
| `_shufflePeriod` | number | не используется (у всех 0.5) | `0.5` |
| `_previewStartTime` | number | не используется без `preview.ogg` | 33 % длительности |
| `_previewDuration` | number | то же | 10 c |
| `_songApproximativeDuration` | **int** | длительность, секунды (Edda чинит, если float) | из сессии |
| `_songFilename` | string | имя аудиофайла | `"song.ogg"` |
| `_coverImageFilename` | string | имя обложки | `"cover.jpg"` |
| `_environmentName` | string | окружение | из сессии, по умолчанию `Midgard` |
| `_songTimeOffset` | number | смещение звука; **необязателен** (встроенный редактор его не пишет) | `0` |
| `_customData` | object | редакторские данные, игра не читает | `_contributors` + `_editors` |
| `_difficultyBeatmapSets` | array | наборы характеристик (всегда один `Standard`) | см. ниже |

Окружения (из Edda): `Midgard`, `Alfheim`, `Nidavellir`, `Asgard`, `Muspelheim`, `Helheim`,
`Hellfest`, `Sabaton`, `Empty`, `DarkEmpty`.

### `_difficultyBeatmapSets`

```json
"_difficultyBeatmapSets": [
  {
    "_beatmapCharacteristicName": "Standard",
    "_difficultyBeatmaps": [
      {
        "_difficulty": "Normal",
        "_difficultyRank": 3,
        "_beatmapFilename": "Normal.dat",
        "_noteJumpMovementSpeed": 15.0,
        "_noteJumpStartBeatOffset": 0.0,
        "_customData": { "...": "необязательно, состояние сетки редактора" }
      }
    ]
  }
]
```

| Ключ | Тип | Что это |
|---|---|---|
| `_beatmapCharacteristicName` | string | всегда `Standard` |
| `_difficulty` | string | `Easy` / `Normal` / `Hard` |
| `_difficultyRank` | int | «черепа» сложности, 1–10 (Edda допускает 1–99) |
| `_beatmapFilename` | string | имя файла сложности в этой папке |
| `_noteJumpMovementSpeed` | number | скорость подлёта рун (Note Speed); > 0, 15 — средняя, игра разрешает до 30 |
| `_noteJumpStartBeatOffset` | number | смещение подлёта, обычно 0 |

---

## 3. `<Difficulty>.dat` — карта сложности

```json
{
  "_version": "1",
  "_customData": {
    "_time": 0,
    "_BPMChanges": [],
    "_bookmarks": []
  },
  "_events": [],
  "_notes": [
    { "_time": 9.0, "_lineIndex": 0, "_lineLayer": 1, "_type": 0, "_cutDirection": 1 }
  ],
  "_obstacles": []
}
```

| Ключ | Тип | Что это |
|---|---|---|
| `_version` | string | здесь игра не сверяет его вообще (отмечено в Edda), пишут `"1"` |
| `_customData._time` | number | 0 |
| `_customData._BPMChanges` | array | тайминговые изменения (у большинства карт пусто) |
| `_customData._bookmarks` | array | закладки редактора, `{_time, _name}` |
| `_events` | array | игра не читает, но **массив должен быть** (пустой) |
| `_notes` | array | ноты — единственное, что реально читает игра |
| `_obstacles` | array | игра не читает, но **массив должен быть** (пустой) |

### Нота

| Ключ | Тип | Допустимо | Смысл |
|---|---|---|---|
| `_time` | number | ≥ 0 | время в **битах** от начала песни (не в секундах!) |
| `_lineIndex` | int | 0–3 | барабан, слева направо (0 — крайний левый) |
| `_lineLayer` | int | 0–3 | слой; раскладка одномерная, игра слой не использует. В картах Edda/MMA2 всегда **1** |
| `_type` | int | **только 0** | других типов у Ragnarock нет; иное значение — невалидная карта |
| `_cutDirection` | int | **только 1** | направление удара; иное значение — невалидная карта |

Ноты в списке отсортированы по `_time`; одновременные удары — это несколько нот с одинаковым
`_time` и разными `_lineIndex`. Дубликат «то же время + тот же барабан» — ошибка карты.

---

## 4. Время и сетка

Пересчёт, который делает игра:

```
time_sec = _time * 60 / _beatsPerMinute
```

Проверка на `divadance`: последняя нота `_time = 169.75` при BPM 120 → 84.9 c при длительности
трека 89 c. Если бы `_time` был в секундах, карта вылезла бы за трек вдвое.

**Фаза (первый бит не в нуле).** Почти любой трек начинается не с бита. Если сетка редактора
привязана к t = 0, ноты уезжают от музыки на величину фазы в пределах половины доли —
до 62 мс при BPM 120, на слух это «флама».

Два способа это выразить:

1. **Дробные биты (используем мы).** Сетка строится с фазой `φ` (`BPMDetector.FirstBeatOffset`),
   а `_time` пишется абсолютным: `t * BPM / 60`. Например, при `φ = 0.44` удар в 0.44 c даёт
   `_time = 0.88`. Игра ставит ноту на 0.44 c — точно в музыку. Минус: в Edda такие ноты
   рисуются между линиями сетки (косметика).
2. **`_customData._BPMChanges`** — тайминговое изменение в начале трека, как это делает Edda.
   Формат: `{ "_BPM": 120, "_time": 0, "_beatsPerBar": 4, "_metronomeOffset": 4 }`.
   Мы его не пишем: пустой массив надёжнее и работает независимо от того, учитывает ли игра
   тайминговые изменения.

Остальные величины — `_songApproximativeDuration`, `_previewStartTime`, `_previewDuration` — в
**секундах**. `_noteJumpMovementSpeed` — не время, а безразмерная скорость подлёта рун.

---

## 5. Что игра проверяет, а что игнорирует

Требуется (иначе карта не загрузится / Edda её отвергнет):

- `info.dat`: все ключи из таблицы §2, кроме `_songTimeOffset` и `_customData`;
- `.dat`: массивы `_events`, `_notes`, `_obstacles`;
- ноты: `_lineIndex` 0–3, `_time` ≥ 0, `_type` == 0, `_cutDirection` == 1;
- аудио в Ogg Vorbis.

Игнорируется: `_version` (в `.dat` — точно), `_shuffle`, `_shufflePeriod`, `_previewStartTime`,
`_previewDuration` (без `preview.ogg`), `_customData` целиком, `_events`, `_obstacles`,
`_lineLayer` (принимаются 0–3).

---

## 6. Что пишем мы

Реализация: [Data/InfoDatDTO.cs](Assets/RagnarockEditor/Scripts/Data/InfoDatDTO.cs),
[Data/BeatmapDatDTO.cs](Assets/RagnarockEditor/Scripts/Data/BeatmapDatDTO.cs),
[Serialization/DatSerializer.cs](Assets/RagnarockEditor/Scripts/Serialization/DatSerializer.cs),
[VR/LevelExporter.cs](Assets/RagnarockEditor/Scripts/VR/LevelExporter.cs).

| Решение | Значение | Почему |
|---|---|---|
| `_version` | `"1"` в обоих файлах | как во всех картах экосистемы |
| `_explicit` | `"false"` (строка) | так в формате и в Edda |
| `_lineLayer` | `1` | как в картах Edda/MMA2; игра терпит 0–3 |
| `_noteJumpMovementSpeed` | `15` | средняя скорость по документации Edda |
| `_customData._bookmarks` | `[]` | есть в настоящих картах |
| `_customData._BPMChanges` | `[]` | фазу выражаем дробными битами |
| `preview.ogg` | не делаем | необязателен, игра играет первые секунды |
| сложность | всегда `Normal`, rank 3 | в приложении нет выбора сложности |
| окружение | из сессии, по умолчанию `Midgard` | — |
| zip для шаринга | файлы в корне, имя = имя папки | как «Export Map» в Edda и как ждёт RagnaCustoms; «Извлечь всё» создаёт сразу `<Имя>/` |

Наш экспорт `info.dat` (проверено на данных divadance):

```json
{
  "_version": "1",
  "_songName": "Diva Dance",
  "_songSubName": "",
  "_songAuthorName": "The Fifth Element",
  "_levelAuthorName": "Watsu",
  "_explicit": "false",
  "_beatsPerMinute": 120.0,
  "_songTimeOffset": 0.0,
  "_shuffle": 0.0,
  "_shufflePeriod": 0.5,
  "_previewStartTime": 29.37,
  "_previewDuration": 10.0,
  "_songFilename": "song.ogg",
  "_coverImageFilename": "cover.jpg",
  "_environmentName": "Midgard",
  "_songApproximativeDuration": 89,
  "_customData": {
    "_contributors": [],
    "_editors": { "RagnaSongMaker": { "version": "1.0" }, "_lastEditedBy": "RagnaSongMaker" }
  },
  "_difficultyBeatmapSets": [ { "_beatmapCharacteristicName": "Standard", "_difficultyBeatmaps": [ { "_difficulty": "Normal", "_difficultyRank": 3, "_beatmapFilename": "Normal.dat", "_noteJumpMovementSpeed": 15.0, "_noteJumpStartBeatOffset": 0.0 } ] } ]
}
```

---

## 7. Грабли, на которые уже наступали

- **`[NonSerialized]` в DTO = поле не попадает в JSON.** Newtonsoft его уважает почти как
  `[JsonIgnore]`, но, в отличие от последнего, молча. Из-за этого `_events`, `_obstacles` и
  `_customData._BPMChanges` не писались вообще (итерация 5).
- **`_explicit` — строка `"false"`, а не `false`.** Bool сломает карту.
- **`_songApproximativeDuration` — целое.** Дробное значение Edda чинит сама, игра может споткнуться.
- **`_time` в битах.** Самая частая ошибка при ручной правке JSON.
- **`_type` 1 и `_cutDirection` ≠ 1 — невалидная карта** (не «просто странная»).
- **MP3, переименованный в `song.ogg`, не играет** — нужен настоящий Ogg Vorbis.
- **Без фазы сетки** ноты уезжают от музыки до половины доли (62 мс при 120 BPM).

---

## 8. Как проверить карту

Офлайн-инструмент [plans/tools/map-format-check](plans/tools/map-format-check/Program.cs) читает
папку карты нашими DTO, гоняет `LevelValidator` и структурно сравнивает JSON «как мы запишем»
с настоящим:

```
cd plans/tools/map-format-check
dotnet run "путь/к/распакованной/карте"
```

Ожидаемый вывод на корректной карте: `ИТОГ: OK — наша логика карт совпадает с реальной картой`,
валидатор — 0 ошибок и 0 предупреждений, обязательные ключи на месте.

---

## Ссылки

- [Edda — сайт и документация](https://pkbeam.github.io/Edda/) (`Using Edda` → разделы про
  сложности, превью, Note Speed).
- [Edda — исходники](https://github.com/PKBeam/Edda): `Classes/MapEditor/RagnarockMap.cs`
  (чтение/запись карты и все проверки), `Classes/EddaConstants.cs` (имена окружений и сложностей).
