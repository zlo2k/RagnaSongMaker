using System;
using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Оживляет объёмный каменный контрол из StoneControls: двигает заливку и ручку
    /// по значению 0..1, принимает клик по точке попадания луча.
    /// </summary>
    public class StoneControl : MonoBehaviour
    {
        public enum Kind
        {
            Slider,
            Progress,
            Toggle
        }

        [SerializeField] private Kind _kind = Kind.Slider;
        [SerializeField] private bool _vertical;
        [SerializeField] private bool _invert;
        [SerializeField] private float _value;
        [SerializeField] private GameObject _onState;
        [SerializeField] private GameObject _offState;

        public event Action<float> ValueChanged;

        public float Value => _value;
        public Kind ControlKind => _kind;

        private Transform _fill;
        private Transform _handle;
        private Vector3 _fullScale;
        private int _scaleAxis;
        private float _start, _end, _handleOffset;
        private Vector3 Axis => _vertical ? Vector3.up : Vector3.right;
        public bool IsDragging { get; private set; }

        public bool BeginDrag()
        {
            if (_kind != Kind.Slider || IsDragging || _fill == null) return false;
            IsDragging = true;
            return true;
        }

        public void EndDrag() => IsDragging = false;
        private void OnDisable() => EndDrag();

        private bool _on = true;

        private void Awake()
        {
            Build();
            Apply();
        }

        public void SetValue(float value)
        {
            _value = Mathf.Clamp01(value);
            Apply();
            ValueChanged?.Invoke(_value);
        }

        public void SetProgress(float value) => SetValue(value);

        /// <summary>Обновить значение без события (синхронизация с плеером, чтобы не было петли).</summary>
        public void SetValueSilent(float value)
        {
            _value = Mathf.Clamp01(value);
            Apply();
        }

        public void SetOn(bool on)
        {
            _on = on;

            if (_onState != null)
                _onState.SetActive(on);

            if (_offState != null)
                _offState.SetActive(!on);
        }

        public bool IsOn => _on;

        /// <summary>Клик лучом: для слайдера — по координате, для тумблера — переключение.</summary>
        public void PressAt(Vector3 worldPoint)
        {
            if (_kind == Kind.Toggle)
            {
                SetOn(!_on);
                ValueChanged?.Invoke(_on ? 1f : 0f);
                return;
            }

            if (_kind != Kind.Slider)
                return;

            float position = Vector3.Dot(transform.InverseTransformPoint(worldPoint), Axis);
            float fraction = Mathf.InverseLerp(_start, _end, position);
            float value = _invert ? 1f - fraction : fraction;
            if (!Mathf.Approximately(_value, value)) SetValue(value);
        }

        private void Build()
        {
            if (_kind == Kind.Toggle) return;
            _fill = FindDeep(transform, "Fill");
            _handle = FindDeep(transform, "Handle");
            var rail = FindDeep(transform, "Rail");
            if (!_fill || !rail) { _fill = null; return; }
            var fillMesh = _fill.GetComponent<MeshFilter>();
            var railMesh = rail.GetComponent<MeshFilter>();
            if (!fillMesh || !railMesh) { _fill = null; return; }

            // FBX axes may be rotated or reflected relative to the board.
            // Work in board space for input and mesh-local space for scaling.
            var axis = Axis;
            var toBoard = transform.worldToLocalMatrix * rail.localToWorldMatrix;
            Bounds bounds = railMesh.sharedMesh.bounds;
            float middle = Vector3.Dot(toBoard.MultiplyPoint3x4(bounds.center), axis);
            float half = 0f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 extent = Vector3.zero;
                extent[i] = bounds.extents[i];
                half += Mathf.Abs(Vector3.Dot(toBoard.MultiplyVector(extent), axis));
            }

            var fillToBoard = transform.worldToLocalMatrix * _fill.localToWorldMatrix;
            var size = fillMesh.sharedMesh.bounds.size;
            float longest = 0f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 edge = Vector3.zero;
                edge[i] = size[i];
                float length = Mathf.Abs(Vector3.Dot(fillToBoard.MultiplyVector(edge), axis));
                if (length > longest) { longest = length; _scaleAxis = i; }
            }
            if (half <= 0f || longest <= 0f) { _fill = null; return; }
            // The exported Fill pivot is the empty end of the rail.
            float pivot = Vector3.Dot(transform.InverseTransformPoint(_fill.position), axis);
            _start = middle + (pivot < middle ? -half : half);
            _end = 2f * middle - _start;
            _fullScale = _fill.localScale;
            _fullScale[_scaleAxis] *= 2f * half / longest;
            if (_handle)
            {
                var mesh = _handle.GetComponent<MeshFilter>();
                if (mesh) _handleOffset = Vector3.Dot(transform.InverseTransformVector(
                    _handle.TransformVector(mesh.sharedMesh.bounds.center)), axis);
            }
        }

        private void MoveAlongRail(Transform part, float coordinate)
        {
            var local = transform.InverseTransformPoint(part.position);
            local += Axis * (coordinate - Vector3.Dot(local, Axis));
            part.position = transform.TransformPoint(local);
        }

        private void Apply()
        {
            if (!_fill) return;
            float fraction = Mathf.Clamp01(_value);
            var scale = _fullScale;
            scale[_scaleAxis] *= fraction;
            _fill.localScale = scale;
            MoveAlongRail(_fill, _invert ? Mathf.Lerp(_end, _start, fraction) : _start);
            _fill.gameObject.SetActive(fraction > 0f);
            if (_handle) MoveAlongRail(_handle, Mathf.Lerp(_start, _end, _invert ? 1f-fraction : fraction) - _handleOffset);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null)
                return null;

            var children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != root && children[i].name == name)
                    return children[i];
            }

            return null;
        }

    }
}
