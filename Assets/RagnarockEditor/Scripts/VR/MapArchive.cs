using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace RagnarockEditor.VR
{
    internal static class MapArchive
    {
        public static string ShareRoot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var cache = activity.Call<AndroidJavaObject>("getCacheDir");
            return Path.Combine(cache.Call<string>("getAbsolutePath"), "shared-maps");
#else
            return Path.Combine(Application.temporaryCachePath, "shared-maps");
#endif
        }

        public static string Create(string folder, string root)
        {
            if (!File.Exists(Path.Combine(folder, "info.dat")))
                throw new FileNotFoundException("info.dat");
            Directory.CreateDirectory(root);
            // Let receiving apps finish reading even after the chooser closes.
            foreach (var old in Directory.GetDirectories(root))
            {
                if (Directory.GetLastWriteTimeUtc(old) >= DateTime.UtcNow.AddDays(-2)) continue;
                try { Directory.Delete(old, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            var destination = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(destination);
            string path = Path.Combine(destination, Path.GetFileName(folder) + ".zip");
            try
            {
                using var file = File.Create(path);
                using var zip = new ZipArchive(file, ZipArchiveMode.Create);
                foreach (string source in Directory.GetFiles(folder))
                {
                    if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) continue;
                    string name = Path.GetFileName(source);
                    var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                    using var input = File.OpenRead(source);
                    using var output = entry.Open();
                    input.CopyTo(output);
                }
                return path;
            }
            catch
            {
                Directory.Delete(destination, true);
                throw;
            }
        }

        public static void Share(string archive)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var provider = new AndroidJavaClass("org.ragnasongmaker.share.MapProvider");
            provider.CallStatic("share", activity, archive);
#elif UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(archive);
#else
            Application.OpenURL(new Uri(Path.GetDirectoryName(archive)).AbsoluteUri);
#endif
        }
    }
}
