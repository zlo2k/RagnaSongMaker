using UnityEngine;
using RagnarockEditor.VR;

namespace RagnarockEditor.Playback
{
    /// <summary>
    /// Подсвечивает барабаны при предпросмотре (какая руна на какой барабан летит).
    /// </summary>
    public class DrumHighlight : MonoBehaviour
    {
        [SerializeField] private DrumManager _drumManager;
        [SerializeField] private Color _highlightColor = Color.yellow;
        [SerializeField] private float _highlightDuration = 0.3f;

        /// <summary>
        /// Подсвечивает указанный барабан.
        /// </summary>
        public void HighlightDrum(int drumIndex)
        {
            if (_drumManager != null)
            {
                _drumManager.HighlightDrum(drumIndex, true);
                StartCoroutine(ClearHighlightAfterDelay(drumIndex));
            }
        }

        private System.Collections.IEnumerator ClearHighlightAfterDelay(int drumIndex)
        {
            yield return new WaitForSeconds(_highlightDuration);
            if (_drumManager != null)
            {
                _drumManager.HighlightDrum(drumIndex, false);
            }
        }
    }
}
