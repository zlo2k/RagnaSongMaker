using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Загружает выбранный трек в AudioSource, но НЕ запускает его:
    /// старт делает UI записи (RecordSessionController) по кнопке «Начать».
    /// </summary>
    public class SongPlaybackBootstrap : MonoBehaviour
    {
        private AudioSource _source;

        public bool HasSong => !string.IsNullOrEmpty(SongSelectionState.SelectedPath);
        public bool IsReady { get; private set; }
        public bool IsPlaying => _source != null && _source.isPlaying;

        public float Position => _source != null && _source.clip != null ? _source.time : 0f;
        public float Length => _source != null && _source.clip != null ? _source.clip.length : 0f;

        private IEnumerator Start()
        {
            IsReady = false;

            var path = SongSelectionState.SelectedPath;
            if (string.IsNullOrEmpty(path))
            {
                Debug.Log("[Song] трек не выбран — тишина");
                yield break;
            }

            _source = GetComponent<AudioSource>();
            if (_source == null)
                _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            using (var request = UnityWebRequestMultimedia.GetAudioClip("file://" + path, GetAudioType(path)))
            {
                // Разжатый клип: нужен не только для проигрывания, но и чтобы работал
                // AudioClip.GetData() — иначе mp3 нельзя перекодировать в ogg при экспорте.
                if (request.downloadHandler is DownloadHandlerAudioClip audioHandler)
                {
                    audioHandler.compressed = false;
                    audioHandler.streamAudio = false;
                }

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("[Song] не загрузилось: " + request.error + " (" + path + ")");
                    yield break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null)
                {
                    Debug.LogError("[Song] AudioClip пустой: " + path);
                    yield break;
                }

                _source.clip = clip;
                IsReady = true;
                Debug.Log($"[Song] загружен \"{SongSelectionState.SelectedName}\", {clip.length:F1} с (ожидает старта)");
            }
        }

        public void PlayFromStart()
        {
            if (!IsReady || _source == null)
                return;

            _source.Stop();
            _source.time = 0f;
            _source.Play();
            Debug.Log("[Song] старт воспроизведения");
        }

        public void Pause()
        {
            if (_source != null)
                _source.Pause();
        }

        public void Resume()
        {
            if (_source != null)
                _source.UnPause();
        }

        public void StopPlayback()
        {
            if (_source == null)
                return;

            _source.Stop();
            _source.time = 0f;
        }

        /// <summary>
        /// Ставит позицию воспроизведения (перемотка назад при записи).
        /// Не меняет состояние play/pause.
        /// </summary>
        public void Seek(float seconds)
        {
            if (_source == null || _source.clip == null)
                return;

            _source.time = Mathf.Clamp(seconds, 0f, _source.clip.length);
        }

        private static AudioType GetAudioType(string path)
        {
            var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".ogg": return AudioType.OGGVORBIS;
                case ".wav": return AudioType.WAV;
                case ".mp3": return AudioType.MPEG;
                default: return AudioType.UNKNOWN;
            }
        }
    }
}
