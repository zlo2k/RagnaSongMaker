using System.Collections.Generic;
using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Достаёт список аудио через MediaStore (работает на Android 13+, когда прямого
    /// доступа к папкам нет, но разрешение READ_MEDIA_AUDIO выдано).
    /// </summary>
    public static class MediaStoreSongs
    {
        public static List<SongLibrary.Entry> Query()
        {
            var result = new List<SongLibrary.Entry>();

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // без null-аргументов: Unity плохо резолвит перегрузки с null
                var selection = "_data IS NOT NULL";
                var selectionArgs = new string[0];
                var sortOrder = "_data ASC";
                var projection = new[] { "_data", "title" };

                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var mediaClass = new AndroidJavaClass("android.provider.MediaStore$Audio$Media"))
                using (var uri = mediaClass.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
                using (var cursor = resolver.Call<AndroidJavaObject>(
                           "query", uri, projection, selection, selectionArgs, sortOrder))
                {
                    if (cursor == null)
                    {
                        Debug.LogWarning("[Songs] MediaStore: пустой курсор");
                        return result;
                    }

                    // display_name на Horizon OS отсутствует, поэтому берём _data + title
                    var dataIndex = cursor.Call<int>("getColumnIndex", "_data");
                    var titleIndex = cursor.Call<int>("getColumnIndex", "title");

                    while (cursor.Call<bool>("moveToNext"))
                    {
                        var path = dataIndex >= 0 ? cursor.Call<string>("getString", dataIndex) : null;
                        if (string.IsNullOrEmpty(path))
                            continue;

                        var title = titleIndex >= 0 ? cursor.Call<string>("getString", titleIndex) : null;
                        if (string.IsNullOrEmpty(title))
                            title = System.IO.Path.GetFileNameWithoutExtension(path);

                        result.Add(new SongLibrary.Entry
                        {
                            Path = path,
                            Name = title,
                        });
                    }
                }

                Debug.Log("[Songs] MediaStore: " + result.Count + " файл(ов)");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Songs] MediaStore недоступен: " + e.GetType().Name + ": " + e.Message);
            }
#endif

            return result;
        }
    }
}
