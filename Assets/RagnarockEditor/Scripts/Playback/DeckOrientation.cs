using RagnarockEditor.VR;
using UnityEngine;

namespace RagnarockEditor.Playback
{
    /// <summary>
    /// Единое направление «по палубе» для всех кружков.
    ///
    /// Раньше каждый диск летел по направлению «камера → свой барабан», поэтому
    /// крайние диски расходились веером. Берём ориентацию палубы (XR Origin)
    /// один раз — тогда все руны и все отлетающие кружки идут параллельно.
    /// </summary>
    public static class DeckOrientation
    {
        private static Transform _origin;

        /// <summary>Горизонтальное направление «вперёд по палубе».</summary>
        public static Vector3 Forward
        {
            get
            {
                if (_origin == null)
                    _origin = ResolveOrigin();

                Vector3 forward;

                if (_origin != null)
                {
                    forward = _origin.forward;
                }
                else if (Camera.main != null)
                {
                    forward = Camera.main.transform.forward;
                }
                else
                {
                    forward = Vector3.forward;
                }

                forward.y = 0f;

                if (forward.sqrMagnitude < 0.0001f)
                    forward = Vector3.forward;

                return forward.normalized;
            }
        }

        private static Transform ResolveOrigin()
        {
            // DrumPositioner знает XR Origin, от которого считаются позиции барабанов.
            var positioner = Object.FindFirstObjectByType<DrumPositioner>();
            if (positioner != null && positioner.XROrigin != null)
                return positioner.XROrigin;

            var manager = Object.FindFirstObjectByType<DrumManager>();
            if (manager != null && manager.XROrigin != null)
                return manager.XROrigin;

            return null;
        }
    }
}
