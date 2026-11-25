using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace RagnarockEditor.Audio
{
    /// <summary>
    /// Загрузчик аудиофайлов (.ogg, .mp3, .wav, .m4a).
    /// На Quest 3 использует UnityWebRequestMultimedia для потоковой загрузки.
    /// </summary>
    public class AudioLoader : MonoBehaviour
    {
        /// <summary>Событие: аудио успешно загружено.</summary>
        public event Action<AudioClip> OnAudioLoaded;

        /// <summary>Событие: ошибка загрузки.</summary>
        public event Action<string> OnLoadError;

        /// <summary>Прогресс загрузки (0-1).</summary>
        public float Progress { get; private set; }

        /// <summary>Загруженный AudioClip.</summary>
        public AudioClip LoadedClip { get; private set; }

        /// <summary>
        /// Загружает аудиофайл асинхронно.
        /// </summary>
        /// <param name="filePath">Полный путь к файлу</param>
        public void LoadAudio(string filePath)
        {
            StartCoroutine(LoadAudioCoroutine(filePath));
        }

        private IEnumerator LoadAudioCoroutine(string filePath)
        {
            Progress = 0f;

            if (!File.Exists(filePath))
            {
                OnLoadError?.Invoke($"Файл не найден: {filePath}");
                yield break;
            }

            string extension = Path.GetExtension(filePath).ToLower();
            AudioType audioType = GetAudioType(extension);

            if (audioType == AudioType.UNKNOWN)
            {
                OnLoadError?.Invoke($"Неподдерживаемый формат аудио: {extension}");
                yield break;
            }

            string url = "file://" + filePath;
            Debug.Log($"[AudioLoader] Загрузка: {url} (тип: {audioType})");

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                // Разжатый клип: BPM-анализу нужен PCM через AudioClip.GetData(),
                // а для сжатого mp3/ogg он возвращает false.
                if (www.downloadHandler is DownloadHandlerAudioClip audioHandler)
                {
                    audioHandler.compressed = false;
                    audioHandler.streamAudio = false;
                }

                www.SendWebRequest();

                while (!www.isDone)
                {
                    Progress = www.downloadProgress;
                    yield return null;
                }

                if (www.result == UnityWebRequest.Result.Success)
                {
                    LoadedClip = DownloadHandlerAudioClip.GetContent(www);
                    LoadedClip.name = Path.GetFileNameWithoutExtension(filePath);
                    
                    Debug.Log($"[AudioLoader] Загружено: {LoadedClip.name}, " +
                              $"длительность={LoadedClip.length:F1}с, " +
                              $"частота={LoadedClip.frequency}Гц, " +
                              $"каналы={LoadedClip.channels}");
                    
                    Progress = 1f;
                    OnAudioLoaded?.Invoke(LoadedClip);
                }
                else
                {
                    string error = www.error;
                    Debug.LogError($"[AudioLoader] Ошибка загрузки: {error}");
                    OnLoadError?.Invoke($"Ошибка загрузки аудио: {error}");
                }
            }
        }

        /// <summary>
        /// Определяет AudioType по расширению файла.
        /// </summary>
        private AudioType GetAudioType(string extension)
        {
            switch (extension)
            {
                case ".ogg": return AudioType.OGGVORBIS;
                case ".mp3": return AudioType.MPEG;
                case ".wav": return AudioType.WAV;
                case ".m4a":
                case ".aac": return AudioType.MPEG; // m4a декодируется как MPEG на Android
                default:     return AudioType.UNKNOWN;
            }
        }

        /// <summary>
        /// Освобождает загруженный AudioClip.
        /// </summary>
        public void Unload()
        {
            if (LoadedClip != null)
            {
                Destroy(LoadedClip);
                LoadedClip = null;
            }
            Progress = 0f;
        }
    }
}
