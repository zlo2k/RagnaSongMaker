using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Гоняет Transform за позой контроллера (левая/правая рука) напрямую через UnityEngine.XR.
    /// Аналог HMDPoseDriver, но для XRNode.LeftHand / XRNode.RightHand.
    /// </summary>
    public class XRHandPoseDriver : MonoBehaviour
    {
        public enum Hand
        {
            Left = 0,
            Right = 1,
        }

        [SerializeField] private Hand _hand = Hand.Right;

        private readonly List<InputDevice> _found = new List<InputDevice>();
        private InputDevice _device;
        private bool _loggedState;

        public Hand Side
        {
            get => _hand;
            set => _hand = value;
        }

        public InputDevice Device => _device;

        /// <summary>Курок зажат (для будущего UI/взаимодействия).</summary>
        public bool IsTriggerPressed
        {
            get
            {
                if (!_device.isValid)
                    return false;

                return _device.TryGetFeatureValue(CommonUsages.triggerButton, out var pressed) && pressed;
            }
        }

        private XRNode Node => _hand == Hand.Left ? XRNode.LeftHand : XRNode.RightHand;

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
            _device = InputDevices.GetDeviceAtXRNode(Node);
            if (_device.isValid)
                return;

            _found.Clear();
            InputDevices.GetDevicesAtXRNode(Node, _found);
            if (_found.Count > 0)
            {
                _device = _found[0];
                return;
            }

            // Иногда GetDeviceAtXRNode не находит второй контроллер — ищем по характеристикам
            var hand = _hand == Hand.Left
                ? InputDeviceCharacteristics.Left
                : InputDeviceCharacteristics.Right;

            _found.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                hand | InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller, _found);

            if (_found.Count > 0)
                _device = _found[0];
        }

        private void Apply()
        {
            if (!_device.isValid)
            {
                if (!_loggedState)
                {
                    Debug.LogWarning($"[HMD] {_hand} controller not found");
                    _loggedState = true;
                }
                return;
            }

            if (!_loggedState)
            {
                Debug.Log($"[HMD] {_hand} controller active: {_device.name}");
                _loggedState = true;
            }

            if (_device.TryGetFeatureValue(CommonUsages.devicePosition, out var position))
                transform.localPosition = position;

            if (_device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation))
                transform.localRotation = rotation;
        }
    }
}
