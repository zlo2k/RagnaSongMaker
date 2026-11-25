using System.Collections.Generic;
using RagnarockEditor.Audio;
using RagnarockEditor.Core;
using RagnarockEditor.UI;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Луч из руки: рисует линию и по нажатию триггера «нажимает» uGUI-кнопку, в которую попал.
    /// Направление берётся из aim-позы контроллера (pointer pose), как в системном интерфейсе Quest —
    /// grip-поза смотрит вверх вдоль рукоятки и для луча не годится.
    /// </summary>
    public class HandUIPointer : MonoBehaviour
    {
        [SerializeField] private float _maxDistance = 6f;
        [SerializeField] private float _lineWidth = 0.006f;
        [SerializeField] private Color _idleColor = new Color(1f, 0.55f, 0.25f, 1f);
        [SerializeField] private Color _hoverColor = new Color(0.35f, 1f, 0.45f, 1f);

        [Tooltip("Запасной наклон луча, если aim-поза недоступна")]
        [SerializeField] private float _fallbackPitch = -25f;

        [Tooltip("0 = по grip-позе (вдоль рукоятки), 1 = по aim-позе (как системный луч Quest), 0.5 = половина")]
        [Range(0f, 1f)]
        [SerializeField] private float _aimBlend = 0.5f;

        // UnityEngine.XR.InputDevice (не InputSystem.InputDevice — из-за using они конфликтуют)
        private readonly List<UnityEngine.XR.InputDevice> _found = new List<UnityEngine.XR.InputDevice>();
        private UnityEngine.XR.InputDevice _device;
        private XRNode _node = XRNode.RightHand;
        private LineRenderer _line;
        private MaterialPropertyBlock _block;
        private bool _wasPressed;
        private bool _logged;
        private StoneButton _hoveredStone;
        private Button _hoveredButton;
        private UiHoverTint _hoveredTint;
        private StoneControl _hoveredControl;
        private StoneControl _pressedControl;
        private Vector3 _pressedControlPoint;

        private void Awake()
        {
            var poseDriver = GetComponent<XRHandPoseDriver>();
            if (poseDriver != null)
                _node = poseDriver.Side == XRHandPoseDriver.Hand.Left ? XRNode.LeftHand : XRNode.RightHand;

            _line = GetComponent<LineRenderer>();
            if (_line == null)
                _line = gameObject.AddComponent<LineRenderer>();

            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.startWidth = _lineWidth;
            _line.endWidth = _lineWidth * 0.35f;
            _line.numCapVertices = 4;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            _block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            RefreshDevice();
        }

        private void Update()
        {
            if (!_device.isValid)
                RefreshDevice();

            // контроллер не найден — не рисуем «висящий» луч и не ждём клика
            if (!_device.isValid)
            {
                if (_line != null && _line.enabled)
                    _line.enabled = false;
                return;
            }

            if (_line != null && !_line.enabled)
                _line.enabled = true;

            var origin = transform.position;
            var direction = transform.forward;

            if (TryGetAimPose(out var aimOrigin, out var aimDirection, out _))
            {
                origin = aimOrigin;
                direction = aimDirection;
            }
            else
            {
                direction = transform.rotation * Quaternion.Euler(_fallbackPitch, 0f, 0f) * Vector3.forward;
            }

            var end = origin + direction * _maxDistance;

            Button target = null;
            StoneButton stoneTarget = null;
            StoneControl controlTarget = null;
            var hits = Physics.RaycastAll(origin, direction, _maxDistance, ~0, QueryTriggerInteraction.Collide);
            if (hits.Length > 1)
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                // Модальный диалог или фон панели: луч упирается в блокер и не жмёт кнопки за ним
                if (hits[i].collider.GetComponentInParent<UIRaycastBlocker>() != null)
                {
                    end = hits[i].point;
                    break;
                }

                // Объёмные каменные контролы (слайдеры/тумблеры/прогресс)
                var control = hits[i].collider.GetComponentInParent<StoneControl>();
                if (control != null)
                {
                    controlTarget = control;
                    _pressedControlPoint = hits[i].point;
                    end = hits[i].point;
                    break;
                }

                // 3D-кнопки Stone UI
                var stone = hits[i].collider.GetComponentInParent<StoneButton>();
                if (stone != null)
                {
                    stoneTarget = stone;
                    end = hits[i].point;
                    break;
                }

                var button = hits[i].collider.GetComponentInParent<Button>();
                if (button == null || !button.IsInteractable())
                    continue;

                target = button;
                end = hits[i].point;
                break;
            }

            UpdateLine(origin, end, stoneTarget != null || target != null || controlTarget != null);

            if (_hoveredControl != controlTarget)
            {
                _hoveredControl = controlTarget;

                if (_hoveredControl != null)
                    SfxPlayer.Instance.PlayHover();
            }

            if (_hoveredStone != stoneTarget)
            {
                if (_hoveredStone != null)
                    _hoveredStone.SetHighlighted(false);

                _hoveredStone = stoneTarget;

                if (_hoveredStone != null)
                {
                    _hoveredStone.SetHighlighted(true);
                    SfxPlayer.Instance.PlayHover();
                }
            }

            if (_hoveredButton != target)
            {
                if (_hoveredTint != null)
                    _hoveredTint.SetHovered(false);

                _hoveredButton = target;
                _hoveredTint = target != null ? target.GetComponentInParent<UiHoverTint>() : null;

                if (_hoveredTint != null)
                    _hoveredTint.SetHovered(true);

                if (_hoveredButton != null)
                    SfxPlayer.Instance.PlayHover();
            }

            var pressed = IsTriggerPressed();
            if (pressed && !_wasPressed)
            {
                if (controlTarget != null)
                {
                    Debug.Log("[UI] control " + controlTarget.name);
                    SfxPlayer.Instance.PlayUi(SfxPlayer.UiSound.Select);
                    controlTarget.PressAt(_pressedControlPoint);
                }
                else if (stoneTarget != null)
                {
                    Debug.Log("[UI] click " + stoneTarget.name);
                    // звук нажатия играет сама каменная кнопка (у неё разный звук по действию)
                    stoneTarget.Press();
                }
                else if (target != null)
                {
                    Debug.Log("[UI] click " + target.name);

                    var sfx = target.GetComponentInParent<SfxButton>();
                    SfxPlayer.Instance.PlayUi(sfx != null ? sfx.Sound : SfxPlayer.UiSound.Select);

                    target.onClick.Invoke();
                }
            }

            _wasPressed = pressed;
        }

        /// <summary>Aim-поворот контроллера (для выравнивания молота по лучу).</summary>
        public bool TryGetAimRotation(out Quaternion rotation)
        {
            if (TryGetAimPose(out _, out _, out rotation))
                return true;

            rotation = transform.rotation * Quaternion.Euler(_fallbackPitch, 0f, 0f);
            return false;
        }

        /// <summary>
        /// Aim-поза контроллера из Input System (та же, что использует Quest для луча).
        /// Позы приходят в tracking space — переводим в мир через родителя руки (Camera Offset).
        /// </summary>
        private bool TryGetAimPose(out Vector3 origin, out Vector3 direction, out Quaternion rotation)
        {
            origin = default;
            direction = default;
            rotation = default;

            var device = _node == XRNode.LeftHand ? XRController.leftHand : XRController.rightHand;
            if (device == null)
                return false;

            var pointer = device.TryGetChildControl<PoseControl>("pointer");
            if (pointer == null)
                return false;

            var parent = transform.parent != null ? transform.parent : transform;
            var localPosition = pointer.position.ReadValue();
            var localRotation = pointer.rotation.ReadValue();

            origin = parent.TransformPoint(localPosition);

            // aim-поза — «эталон» системного луча Quest; _aimBlend позволяет взять половину
            // угла между ней и grip-позой (по рукоятке контроллера)
            var aimRotation = parent.rotation * localRotation;
            rotation = _aimBlend >= 0.999f
                ? aimRotation
                : Quaternion.Slerp(transform.rotation, aimRotation, _aimBlend);

            direction = rotation * Vector3.forward;
            return true;
        }

        private void UpdateLine(Vector3 origin, Vector3 end, bool hovering)
        {
            if (_line == null)
                return;

            _line.SetPosition(0, origin);
            _line.SetPosition(1, end);

            var color = hovering ? _hoverColor : _idleColor;
            _line.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", color);
            _block.SetColor("_Color", color);
            _line.SetPropertyBlock(_block);
        }

        private bool IsTriggerPressed()
        {
            if (!_device.isValid)
                return false;

            if (_device.TryGetFeatureValue(CommonUsages.triggerButton, out var button) && button)
                return true;

            return _device.TryGetFeatureValue(CommonUsages.trigger, out var value) && value > 0.55f;
        }

        private void RefreshDevice()
        {
            _device = InputDevices.GetDeviceAtXRNode(_node);
            if (_device.isValid)
            {
                if (!_logged)
                {
                    Debug.Log($"[UI] pointer device {_node}: {_device.name}");
                    _logged = true;
                }
                return;
            }

            _found.Clear();
            InputDevices.GetDevicesAtXRNode(_node, _found);
            if (_found.Count > 0)
                _device = _found[0];
        }
    }
}
