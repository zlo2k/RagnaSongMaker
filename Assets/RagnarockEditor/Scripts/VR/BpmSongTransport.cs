using RagnarockEditor.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.VR
{
    public class BpmSongTransport : MonoBehaviour
    {
        private AudioSource _source;
        private Slider _seek;
        private Button _play;
        private LocalizedText _label;
        private Text _clock;
        private bool _playing;
        private int _sample;
        private int _lastSecond = -1;

        public void SetClip(AudioClip clip)
        {
            if (!_source) Build();
            _source.Stop();
            _source.clip = clip;
            _playing = false;
            _sample = 0;
            _lastSecond = -1;
            _seek.SetValueWithoutNotify(0);
            _seek.interactable = _play.interactable = clip != null;
            Refresh();
        }

        private void Build()
        {
            var row = WorldPanel.Rect(transform, "Song transport", 0, -267, 740, 48);
            row.localPosition += Vector3.back * 18;
            _source = row.gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0;
            _play = WorldPanel.Button(row, "transport.play", -280, 0, 172, Toggle);
            _label = _play.GetComponentInChildren<LocalizedText>();
            var track = WorldPanel.Rect(row, "Song position", 10, 0, 365, 44);
            WorldPanel.Plate(track, new Color(.22f, .16f, .12f));
            WorldPanel.HitBox(track);
            var fill = WorldPanel.Rect(track, "Fill", 0, 0, 0, -26);
            WorldPanel.Plate(fill, new Color(.95f, .55f, .22f));
            var handle = WorldPanel.Rect(track, "Handle", 0, 0, 14, -8);
            WorldPanel.Plate(handle, new Color(.98f, .92f, .78f));
            _seek = track.gameObject.AddComponent<Slider>();
            _seek.fillRect = fill;
            _seek.handleRect = handle;
            _seek.onValueChanged.AddListener(Seek);
            _clock = WorldPanel.Label(WorldPanel.Rect(row, "Time", 284, 0, 165, 44), 22);
        }

        private void Toggle()
        {
            if (!_source.clip) return;
            if (_playing)
            {
                _sample = _source.timeSamples;
                _source.Pause();
            }
            else
            {
                _source.Play();
                _source.timeSamples = _sample;
            }
            _playing = !_playing;
            Refresh();
        }

        private void Seek(float fraction)
        {
            var clip = _source.clip;
            if (!clip) return;
            _sample = (int)(fraction * (double)(clip.samples - 1));
            if (_playing) _source.timeSamples = _sample;
            Refresh();
        }

        private void Update()
        {
            if (!_source || !_playing) return;
            if (!_source.isPlaying) _playing = false;
            _sample = _playing ? _source.timeSamples : 0;
            _seek.SetValueWithoutNotify(_sample / (float)Mathf.Max(1, _source.clip.samples - 1));
            Refresh();
        }

        private void Refresh()
        {
            string key = _playing ? "transport.pause" : "transport.play";
            if (_label.Key != key) _label.SetKey(key);
            var clip = _source.clip;
            int second = clip ? _sample / clip.frequency : 0;
            if (_lastSecond == second) return;
            _lastSecond = second;
            int total = clip ? Mathf.FloorToInt(clip.length) : 0;
            _clock.text = $"{second / 60}:{second % 60:00} / {total / 60}:{total % 60:00}";
        }

        private void OnDisable()
        {
            if (_source) SetClip(null);
        }
    }
}
