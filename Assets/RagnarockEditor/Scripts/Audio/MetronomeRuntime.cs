using UnityEngine;

namespace RagnarockEditor.Audio
{
    /// <summary>Глобальное включение метронома (каменный переключатель на экране BPM).</summary>
    public static class MetronomeRuntime
    {
        private const string Key = "ragna.metronome.enabled";

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(Key, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
