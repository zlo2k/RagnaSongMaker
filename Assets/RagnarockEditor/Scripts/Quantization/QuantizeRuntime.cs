using UnityEngine;

namespace RagnarockEditor.Quantization
{
    /// <summary>Привязка нот к сетке: переключается каменным чекбоксом.</summary>
    public static class QuantizeRuntime
    {
        private const string Key = "ragna.quantize.snap";

        public static bool SnapEnabled
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
