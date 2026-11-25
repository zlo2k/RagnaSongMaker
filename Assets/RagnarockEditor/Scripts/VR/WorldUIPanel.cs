using System.Collections;
using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Ставит World Space панель перед игроком по фактической позе головы:
    /// высота = высота глаз + смещение (чтобы панель была на уровне лица и сидя, и стоя),
    /// расстояние и разворот лицом к игроку.
    /// </summary>
    public class WorldUIPanel : MonoBehaviour
    {
        [Tooltip("Смещение по высоте относительно глаз, м (0 = на уровне глаз)")]
        [SerializeField] private float _headHeightOffset = 0f;

        [Tooltip("Расстояние от игрока, м")]
        [SerializeField] private float _distance = 1.2f;

        [Tooltip("Повернуть панель лицом к игроку")]
        [SerializeField] private bool _facePlayer = true;

        private void OnEnable()
        {
            StopAllCoroutines();
            StartCoroutine(PlaceDelayed());
        }

        private IEnumerator PlaceDelayed()
        {
            // Ждём, пока HMDPoseDriver применит позу шлема
            yield return null;
            yield return null;
            Place();
        }

        /// <summary>Пересчитать положение панели.</summary>
        public void Place()
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null)
                return;

            var floorY = 0f;
            var rig = GameObject.Find("XR Origin");
            if (rig != null)
                floorY = rig.transform.position.y;

            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            var flatHead = new Vector3(head.position.x, 0f, head.position.z);
            var position = flatHead + forward * _distance;
            position.y = Mathf.Max(head.position.y + _headHeightOffset, floorY + 0.3f);
            transform.position = position;

            if (!_facePlayer)
                return;

            var look = position - head.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        }
    }
}
