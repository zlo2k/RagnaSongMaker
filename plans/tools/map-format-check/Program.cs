using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RagnarockEditor.Data;
using RagnarockEditor.Serialization;

/// <summary>
/// Офлайн-проверка: совпадает ли наша логика карт с реальной картой Ragnarock.
///
/// 1) читает info.dat и все &lt;Difficulty&gt;.dat нашими DTO — проверка, что парсер ничего не теряет;
/// 2) гоняет LevelValidator — проверка, что валидатор не ругается на нормальную карту;
/// 3) собирает "наш экспорт" (те же данные, что пишет LevelExporter) и структурно
///    сравнивает его JSON с реальными файлами: чего не хватает, что лишнее, где другой тип.
///
/// Обязательные ключи взяты из валидатора Edda (Classes/MapEditor/RagnarockMap.cs):
/// именно их отсутствие приводит к "Incorrect or missing key".
/// </summary>
internal static class Program
{
    // Держать в синхроне с DatSerializer.JsonSettings / DatDeserializer.JsonSettings.
    private static readonly JsonSerializerSettings ReadSettings = new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore
    };

    private static readonly JsonSerializerSettings WriteSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include,
        DefaultValueHandling = DefaultValueHandling.Include
    };

    private static readonly string[] RequiredInfoKeys =
    {
        "_version", "_songName", "_songSubName", "_songAuthorName", "_levelAuthorName",
        "_explicit", "_beatsPerMinute", "_shuffle", "_shufflePeriod", "_previewStartTime",
        "_previewDuration", "_songApproximativeDuration", "_songFilename", "_coverImageFilename",
        "_environmentName", "_difficultyBeatmapSets"
    };

    private static readonly string[] RequiredSetKeys = { "_beatmapCharacteristicName", "_difficultyBeatmaps" };

    private static readonly string[] RequiredDifficultyKeys =
    {
        "_difficulty", "_difficultyRank", "_beatmapFilename", "_noteJumpMovementSpeed", "_noteJumpStartBeatOffset"
    };

    private static readonly string[] RequiredMapKeys = { "_events", "_notes", "_obstacles" };

    private static readonly string[] RequiredNoteKeys = { "_time", "_lineIndex", "_lineLayer", "_type", "_cutDirection" };

    private static int _failures;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "-h" || args[0] == "--help")
        {
            Console.WriteLine("usage: MapFormatCheck <папка карты>");
            return 2;
        }

        string folder = args[0];
        if (!Directory.Exists(folder))
        {
            Console.WriteLine($"Нет папки: {folder}");
            return 2;
        }

        Console.WriteLine($"Папка карты: {folder}");

        string infoPath = Path.Combine(folder, "info.dat");
        if (!File.Exists(infoPath))
        {
            Console.WriteLine("info.dat не найден — это не папка карты Ragnarock.");
            return 2;
        }

        string infoText = File.ReadAllText(infoPath);
        var info = JsonConvert.DeserializeObject<InfoDatDTO>(infoText, ReadSettings);

        Console.WriteLine();
        Console.WriteLine("== info.dat ==");
        Console.WriteLine($"  '{info.SongName}' / '{info.SongAuthorName}' | BPM {info.BeatsPerMinute} | " +
                          $"{info.SongApproximativeDuration} c | env '{info.EnvironmentName}' | " +
                          $"explicit '{info.Explicit}' | version '{info.Version}'");
        Console.WriteLine($"  song '{info.SongFilename}', cover '{info.CoverImageFilename}', " +
                          $"preview {info.PreviewStartTime}/{info.PreviewDuration}, offset {info.SongTimeOffset}");

        var difficultyFiles = new List<string>();
        foreach (var set in info.DifficultyBeatmapSets)
        {
            Console.WriteLine($"  set '{set.BeatmapCharacteristicName}': {set.DifficultyBeatmaps.Count} карт(ы)");
            foreach (var d in set.DifficultyBeatmaps)
            {
                Console.WriteLine($"    '{d.Difficulty}' rank {d.DifficultyRank}, '{d.BeatmapFilename}', " +
                                  $"note speed {d.NoteJumpMovementSpeed}, offset {d.NoteJumpStartBeatOffset}");
                if (!string.IsNullOrWhiteSpace(d.BeatmapFilename))
                    difficultyFiles.Add(d.BeatmapFilename);
            }
        }

        if (difficultyFiles.Count == 0)
        {
            Console.WriteLine("! В info.dat нет ссылок на файлы сложностей.");
            return 1;
        }

        // ---- 2. Читаем каждую сложность нашими DTO ----
        var loadedMaps = new Dictionary<string, BeatmapDatDTO>();
        foreach (string file in difficultyFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.Combine(folder, file);
            if (!File.Exists(path))
            {
                Console.WriteLine($"! {file}: файла нет.");
                _failures++;
                continue;
            }

            string mapText = File.ReadAllText(path);
            var map = JsonConvert.DeserializeObject<BeatmapDatDTO>(mapText, ReadSettings);
            loadedMaps[file] = map;

            Console.WriteLine();
            Console.WriteLine($"== {file} ==");
            Console.WriteLine($"  нот: {map.Notes.Count} | events: {map.Events?.Count} | obstacles: {map.Obstacles?.Count} | " +
                              $"version '{map.Version}'");
            if (map.Notes.Count > 0)
            {
                var ordered = map.Notes.OrderBy(n => n.Time).ToList();
                Console.WriteLine($"  время: {ordered[0].Time:0.####} … {ordered[ordered.Count - 1].Time:0.####} бит" +
                                  $" (при {info.BeatsPerMinute} BPM это {ordered[ordered.Count - 1].Time * 60f / info.BeatsPerMinute:0.#} c)");
                Console.WriteLine("  барабаны: " + Describe(map.Notes, n => n.LineIndex));
                Console.WriteLine("  слои:     " + Describe(map.Notes, n => n.LineLayer));
                Console.WriteLine("  типы:     " + Describe(map.Notes, n => n.Type));
                Console.WriteLine("  удары:    " + Describe(map.Notes, n => n.CutDirection));
            }

            // ---- 3. Валидатор на реальной карте ----
            var validation = LevelValidator.ValidateFull(info, map);
            Console.WriteLine($"  LevelValidator: ошибок {validation.Errors.Count}, предупреждений {validation.Warnings.Count}");
            foreach (var e in validation.Errors.Take(5)) Console.WriteLine("    ERR  " + e);
            if (validation.Errors.Count > 5) Console.WriteLine($"    … ещё {validation.Errors.Count - 5}");
            foreach (var w in validation.Warnings.Take(5)) Console.WriteLine("    WARN " + w);
            if (validation.Warnings.Count > 5) Console.WriteLine($"    … ещё {validation.Warnings.Count - 5}");

            if (validation.Errors.Count > 0)
                _failures++;
        }

        // ---- 4. Что мы сами записали бы на месте такого экспорта ----
        var exportInfo = new InfoDatDTO
        {
            SongName = info.SongName,
            SongAuthorName = info.SongAuthorName,
            LevelAuthorName = info.LevelAuthorName,
            BeatsPerMinute = info.BeatsPerMinute,
            SongFilename = info.SongFilename,
            CoverImageFilename = info.CoverImageFilename,
            EnvironmentName = info.EnvironmentName,
            SongApproximativeDuration = info.SongApproximativeDuration,
            PreviewStartTime = info.PreviewStartTime,
            PreviewDuration = info.PreviewDuration
        };
        exportInfo.AddDifficulty("Normal", 3, "Normal.dat");

        Console.WriteLine();
        Console.WriteLine("== сверка схемы: наш экспорт против реальной карты ==");
        CompareJson("info.dat", infoText, JsonConvert.SerializeObject(exportInfo, WriteSettings),
            RequiredInfoKeys, RequiredSetKeys, RequiredDifficultyKeys);

        foreach (var pair in loadedMaps)
        {
            var exportMap = new BeatmapDatDTO();
            foreach (var n in pair.Value.Notes)
                exportMap.Notes.Add(new Note(n.Time, n.LineIndex));

            string realText = File.ReadAllText(Path.Combine(folder, pair.Key));
            CompareJson(pair.Key, realText, JsonConvert.SerializeObject(exportMap, WriteSettings),
                RequiredMapKeys, RequiredNoteKeys);
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "ИТОГ: OK — наша логика карт совпадает с реальной картой."
            : $"ИТОГ: проблем {_failures} — см. выше.");
        return _failures == 0 ? 0 : 1;
    }

    private static string Describe(List<Note> notes, Func<Note, int> selector)
    {
        return string.Join(", ", notes.GroupBy(selector).OrderBy(g => g.Key).Select(g => g.Key + "×" + g.Count()));
    }

    private static void CompareJson(string label, string realText, string oursText, params string[][] requiredGroups)
    {
        JObject real = JObject.Parse(realText);
        JObject ours = JObject.Parse(oursText);

        var realKeys = Flatten(real);
        var ourKeys = Flatten(ours);

        var missing = realKeys.Keys.Except(ourKeys.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var extra = ourKeys.Keys.Except(realKeys.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var typeDiff = realKeys.Keys.Intersect(ourKeys.Keys)
            .Where(k => realKeys[k] != ourKeys[k])
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => $"{k}: у них {realKeys[k]}, у нас {ourKeys[k]}")
            .ToList();

        // Редакторские блоки (сетка/закладки/имя редактора) игра не читает — их отсутствие не проблема.
        bool IsEditorOnly(string key) =>
            key.Contains("._editors", StringComparison.Ordinal) ||
            key.Contains("._difficultyBeatmaps[]._customData", StringComparison.Ordinal);

        var missingEditor = missing.Where(IsEditorOnly).ToList();
        var missingOther = missing.Where(k => !IsEditorOnly(k)).ToList();

        Console.WriteLine($"  [{label}]");
        Console.WriteLine($"    нет у нас (редакторское, необязательно): {(missingEditor.Count == 0 ? "—" : string.Join(", ", missingEditor))}");
        Console.WriteLine($"    нет у нас (прочее): {(missingOther.Count == 0 ? "—" : string.Join(", ", missingOther))}");
        Console.WriteLine($"    лишние у нас: {(extra.Count == 0 ? "—" : string.Join(", ", extra))}");
        Console.WriteLine($"    разный тип: {(typeDiff.Count == 0 ? "—" : string.Join("; ", typeDiff))}");

        // Обязательные ключи: группы заданы именами самого глубокого уровня,
        // поэтому проверяем вхождение по последнему сегменту пути.
        var absentRequired = new List<string>();
        foreach (var group in requiredGroups)
        {
            foreach (string key in group)
            {
                bool present = ourKeys.Keys.Any(k => LastSegment(k) == key);
                bool needed = realKeys.Keys.Any(k => LastSegment(k) == key);
                if (needed && !present)
                    absentRequired.Add(key);
            }
        }

        if (absentRequired.Count > 0)
        {
            Console.WriteLine($"    ОБЯЗАТЕЛЬНЫЕ отсутствуют: {string.Join(", ", absentRequired.Distinct())}");
            _failures++;
        }
        else
        {
            Console.WriteLine("    обязательные ключи: все на месте");
        }

        if (missingOther.Count > 0 || typeDiff.Count > 0)
            _failures++;
    }

    private static string LastSegment(string path)
    {
        int cut = path.LastIndexOf('.');
        return cut < 0 ? path : path.Substring(cut + 1);
    }

    /// <summary>
    /// Плоский список "путь → тип" для сравнения схем. Элементы массивов сворачиваются в "[]",
    /// ключи внутри _editors считаются свободными (там имя редактора).
    /// </summary>
    private static Dictionary<string, string> Flatten(JToken token, string path = "")
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        FlattenInto(token, path, result);
        return result;
    }

    private static void FlattenInto(JToken token, string path, Dictionary<string, string> result)
    {
        if (token is JObject obj)
        {
            if (path.Length > 0) result[path] = "Object";

            foreach (var prop in obj.Properties())
            {
                string name = path.Length == 0 ? prop.Name : path + "." + prop.Name;

                if (prop.Name == "_editors")
                {
                    // Имя редактора — свободный ключ: сравниваем сам блок, а не "Edda" vs "RagnaSongMaker".
                    result[name] = "Object";
                    if (prop.Value is JObject editors)
                    {
                        foreach (var inner in editors.Properties())
                        {
                            if (inner.Name == "_lastEditedBy")
                                result[name + "._lastEditedBy"] = "String";
                            else
                                result[name + ".*"] = "Object";
                        }
                    }
                    continue;
                }

                FlattenInto(prop.Value, name, result);
            }

            return;
        }

        if (token is JArray arr)
        {
            if (path.Length > 0) result[path] = "Array";
            if (arr.Count > 0) FlattenInto(arr[0], path + "[]", result);
            return;
        }

        result[path] = JsonType(token);
    }

    private static string JsonType(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Integer:
            case JTokenType.Float:
                return "Number";
            case JTokenType.String:
                return "String";
            case JTokenType.Boolean:
                return "Boolean";
            case JTokenType.Null:
                return "Null";
            default:
                return token.Type.ToString();
        }
    }
}
