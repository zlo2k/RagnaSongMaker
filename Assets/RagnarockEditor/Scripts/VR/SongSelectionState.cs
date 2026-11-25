namespace RagnarockEditor.VR
{
    /// <summary>Выбранный трек, переживает смену сцены.</summary>
    public static class SongSelectionState
    {
        public static string SelectedPath { get; private set; }
        public static string SelectedName { get; private set; }

        /// <summary>Подтверждённый BPM выбранного трека (0 — ещё не определён).</summary>
        public static float SelectedBpm { get; private set; }

        public static void Select(string path, string name)
        {
            SelectedPath = path;
            SelectedName = name;
        }

        /// <summary>Выбирает трек вместе с BPM (после анализа/подтверждения).</summary>
        public static void Select(string path, string name, float bpm)
        {
            Select(path, name);
            SetBpm(bpm);
        }

        /// <summary>Записывает BPM выбранного трека. Некорректные значения игнорируются.</summary>
        public static void SetBpm(float bpm)
        {
            if (float.IsNaN(bpm) || bpm <= 0f)
                return;

            SelectedBpm = bpm;
        }

        public static void Clear()
        {
            SelectedPath = null;
            SelectedName = null;
            SelectedBpm = 0f;
        }
    }
}
