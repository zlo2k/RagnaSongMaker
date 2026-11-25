using System;
using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace RagnarockEditor.EditorTools
{
    /// <summary>
    /// Добавляет разрешения на чтение музыки в сгенерированный unityLibrary/AndroidManifest.xml.
    ///
    /// Так надёжнее, чем «кастомный главный манифест»: Unity в этом случае генерирует
    /// манифест как обычно (активити, VR-категория Quest, meta-data), а мы только
    /// дописываем строки с разрешениями уже после генерации Gradle-проекта.
    /// </summary>
    public class AddMediaPermissionAndroid : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1;

        private const string Insert =
            "  <uses-permission android:name=\"android.permission.READ_MEDIA_AUDIO\" />\n" +
            "  <uses-permission android:name=\"android.permission.READ_EXTERNAL_STORAGE\" android:maxSdkVersion=\"32\" />\n";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            try
            {
                var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
                if (!File.Exists(manifestPath))
                {
                    Debug.LogWarning("[Android] манифест не найден: " + manifestPath);
                    return;
                }

                var text = File.ReadAllText(manifestPath);
                if (text.Contains("READ_MEDIA_AUDIO"))
                    return;

                var manifestStart = text.IndexOf("<manifest", StringComparison.Ordinal);
                if (manifestStart < 0)
                {
                    Debug.LogWarning("[Android] не найден тег <manifest>");
                    return;
                }

                var tagEnd = text.IndexOf('>', manifestStart);
                if (tagEnd < 0)
                    return;

                text = text.Insert(tagEnd + 1, "\n" + Insert);
                File.WriteAllText(manifestPath, text);
                Debug.Log("[Android] добавлены разрешения READ_MEDIA_AUDIO в " + manifestPath);
            }
            catch (Exception e)
            {
                Debug.LogError("[Android] не удалось добавить разрешения: " + e);
            }
        }
    }
}
