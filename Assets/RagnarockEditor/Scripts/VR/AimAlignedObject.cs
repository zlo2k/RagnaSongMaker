using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Ставит объект (молот) параллельно лучу указателя: каждый LateUpdate берёт
    /// aim-позу контроллера и разворачивает объект по ней.
    /// Модель молота «смотрит» вверх по +Y, поэтому доворачиваем её на 90° вокруг X,
    /// чтобы голова молота шла вперёд по лучу.
    /// </summary>
    public class AimAlignedObject : MonoBehaviour
    {
        [SerializeField] private HandUIPointer _pointer;

        [Tooltip("Доворот модели: +Y модели -> вперёд луча")]
        [SerializeField] private Vector3 _modelAlignment = new Vector3(90f, 0f, 0f);

        [Tooltip("0 = по рукоятке, 1 = параллельно лучу, 0.5 = половина")]
        [Range(0f, 1f)]
        [SerializeField] private float _blend = 1f;

        private void LateUpdate()
        {
            if (_pointer == null)
                return;

            if (!_pointer.TryGetAimRotation(out var aimRotation))
                return;

            var modelOffset = Quaternion.Euler(_modelAlignment);
            var aimTarget = aimRotation * modelOffset;
            var handTarget = _pointer.transform.rotation * modelOffset;

            transform.rotation = _blend >= 0.999f
                ? aimTarget
                : Quaternion.Slerp(handTarget, aimTarget, _blend);
        }
    }
}
