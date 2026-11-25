using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Гоняет Transform за позой шлема напрямую через UnityEngine.XR (без Input System и
    /// без InputAction-ассетов). Нужен потому, что TrackedPoseDriver с экшенами,
    /// созданными из кода, сериализуется без биндингов (m_SingletonActionBindings: [])
    /// и позу не читает.
    /// Вешается на камеру, которая лежит под Camera Offset / XR Origin.
    /// </summary>
    public class HMDPoseDriver : MonoBehaviour
    {
        [Tooltip("Отслеживать положение головы")]
        [SerializeField] private bool _trackPosition = true;

        [Tooltip("Отслеживать поворот головы")]
        [SerializeField] private bool _trackRotation = true;

        private readonly List<InputDevice> _found = new List<InputDevice>();
        private InputDevice _device;
        private bool _loggedState;

        private void OnEnable()
        {
            Application.onBeforeRender += Apply;
            RefreshDevice();
            Apply();
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= Apply;
        }

        private void Update()
        {
            if (!_device.isValid)
                RefreshDevice();

            Apply();
        }

        private void RefreshDevice()
        {
            _device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (_device.isValid)
                return;

            _found.Clear();
            InputDevices.GetDevicesAtXRNode(XRNode.Head, _found);
            if (_found.Count > 0)
                _device = _found[0];
        }

        private void Apply()
        {
            if (!_device.isValid)
            {
                if (!_loggedState)
                {
                    Debug.LogWarning("[HMD] head device not found (XR not started?)");
                    _loggedState = true;
                }
                return;
            }

            if (!_loggedState)
            {
                Debug.Log("[HMD] head tracking active: " + _device.name);
                _loggedState = true;
            }

            if (_trackPosition && _device.TryGetFeatureValue(CommonUsages.devicePosition, out var position))
                transform.localPosition = position;

            if (_trackRotation && _device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation))
                transform.localRotation = rotation;
        }
    }
}
