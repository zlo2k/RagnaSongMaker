using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Ищет аудиофайлы в памяти шлема.
    /// Основное место — папка приложения (видна по USB: Android/data/&lt;bundle&gt;/files),
    /// плюс попытка прочитать общие папки Music/Download/Documents.
    /// </summary>
    public static class SongLibrary
    {
        public sealed class Entry
        {
            public string Path;
            public string Name;
        }

        // Unity на Android декодирует в рантайме только эти форматы;
        // m4a/aac/flac в список не попадают, чтобы не показывать неиграбельные треки
        private static readonly string[] Extensions = { ".mp3", ".ogg", ".wav", ".aiff", ".aif" };
        private const int MaxFiles = 300;

        public static List<Entry> Scan()
        {
            var results = new List<Entry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in GetRoots())
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    continue;

                var found = 0;
                try
                {
                    foreach (var file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
                    {
                        if (!IsAudio(file))
                            continue;

                        if (!seen.Add(file))
                            continue;

                        results.Add(new Entry
                        {
                            Path = file,
                            Name = Path.GetFileNameWithoutExtension(file),
                        });

                        found++;
                        if (results.Count >= MaxFiles)
                            break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Songs] не удалось прочитать {root}: {e.GetType().Name}");
                }

                Debug.Log($"[Songs] {root}: {found} файл(ов)");
                if (results.Count >= MaxFiles)
                    break;
            }

            foreach (var entry in MediaStoreSongs.Query())
            {
                if (string.IsNullOrEmpty(entry.Path) || !seen.Add(entry.Path))
                    continue;

                results.Add(entry);
                if (results.Count >= MaxFiles)
                    break;
            }

            results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Debug.Log($"[Songs] всего найдено: {results.Count}");
            return results;
        }

        /// <summary>Папка, куда проще всего закинуть музыку по USB.</summary>
        public static string PreferredFolder
        {
            get
            {
                var folder = Path.Combine(Application.persistentDataPath, "Music");
                try
                {
                    if (!Directory.Exists(folder))
                        Directory.CreateDirectory(folder);
                }
                catch
                {
                    // игнорируем
                }

                return folder;
            }
        }

        private static IEnumerable<string> GetRoots()
        {
            yield return PreferredFolder;
            yield return Application.persistentDataPath;

            var storage = "/storage/emulated/0";
            yield return storage + "/Music";
            yield return storage + "/Download";
            yield return storage + "/Documents";
            yield return storage + "/Ragnarock";
        }

        private static bool IsAudio(string path)
        {
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
                return false;

            return Array.IndexOf(Extensions, extension.ToLowerInvariant()) >= 0;
        }
    }
}
