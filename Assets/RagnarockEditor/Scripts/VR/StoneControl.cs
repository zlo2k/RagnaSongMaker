using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Оживляет объёмный каменный контрол из StoneControls: двигает заливку и ручку
    /// по значению 0..1, принимает клик по точке попадания луча.
    ///
    /// Штатные узлы Fill/Handle из FBX скрываются, а поверх рисуются свои заливка
    /// и ручка — так не зависим от их пивотов и масштаба модели.
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

        private Transform _fillOverlay;
        private Transform _handleOverlay;
        private Material _fillMaterial;
        private Material _handleMaterial;
        private float _min;
        private float _length;
        private float _crossA;
        private float _crossB;
        private float _lossyScale = 1f;
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

            Vector3 local = transform.InverseTransformPoint(worldPoint);
            float position = _vertical ? local.y : local.x;
            float value = Mathf.InverseLerp(_min, _min + _length, position);

            if (_invert)
                value = 1f - value;

            SetValue(value);        }

        // ------------------------------------------------------------ построение

        private void Build()
        {
            if (_kind == Kind.Toggle)
                return;

            Transform fill = FindDeep(transform, "Fill");
            Transform handle = FindDeep(transform, "Handle");

            if (fill != null)
            {
                var renderer = fill.GetComponentInChildren<Renderer>();
                if (renderer != null)
                    _fillMaterial = renderer.sharedMaterial;

                fill.gameObject.SetActive(false);
            }

            if (handle != null)
            {
                var renderer = handle.GetComponentInChildren<Renderer>();
                if (renderer != null)
                    _handleMaterial = renderer.sharedMaterial;

                handle.gameObject.SetActive(false);
            }

            if (_fillMaterial == null)
                _fillMaterial = CreateMaterial(new Color(0.26f, 0.62f, 0.57f, 1f));

            if (_handleMaterial == null)
                _handleMaterial = CreateMaterial(new Color(0.98f, 0.95f, 0.88f, 1f));

            if (!TryGetLocalAxisBounds(fill, handle, out _min, out _length, out _crossA, out _crossB))
                return;

            _lossyScale = Mathf.Max(0.0001f, transform.lossyScale.x);

            _fillOverlay = CreateOverlay(PrimitiveType.Cube, "FillOverlay", _fillMaterial);
            _handleOverlay = _kind == Kind.Slider
                ? CreateOverlay(PrimitiveType.Sphere, "HandleOverlay", _handleMaterial)
                : null;
        }

        /// <summary>Границы модели в локальных координатах без скрытых Fill/Handle.</summary>
        private bool TryGetLocalAxisBounds(Transform fill, Transform handle,
            out float min, out float length, out float crossA, out float crossB)
        {
            min = 0f;
            length = 0f;
            crossA = 0f;
            crossB = 0f;

            var renderers = GetComponentsInChildren<Renderer>(true);
            bool has = false;
            Bounds world = default;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (fill != null && renderer.transform.IsChildOf(fill))
                    continue;

                if (handle != null && renderer.transform.IsChildOf(handle))
                    continue;

                if (!has)
                {
                    world = renderer.bounds;
                    has = true;
                }
                else
                {
                    world.Encapsulate(renderer.bounds);
                }
            }

            if (!has)
                return false;

            Vector3 a = transform.InverseTransformPoint(world.min);
            Vector3 b = transform.InverseTransformPoint(world.max);

            Vector3 localMin = Vector3.Min(a, b);
            Vector3 localMax = Vector3.Max(a, b);

            float axisMin = _vertical ? localMin.y : localMin.x;
            float axisMax = _vertical ? localMax.y : localMax.x;

            float margin = Mathf.Max(0.005f, (axisMax - axisMin) * 0.06f);
            min = axisMin + margin;
            length = Mathf.Max(0.01f, axisMax - axisMin - margin * 2f);

            crossA = (localMin.y + localMax.y) * 0.5f;
            crossB = localMax.z + 0.005f;

            if (_vertical)
                crossA = (localMin.x + localMax.x) * 0.5f;

            return true;
        }

        private Transform CreateOverlay(PrimitiveType type, string name, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;

            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            go.transform.SetParent(transform, false);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return go.transform;
        }

        private void Apply()
        {
            if (_kind == Kind.Toggle || _fillOverlay == null)
                return;

            float fill = Mathf.Clamp01(_value) * _length;
            float thickness = 0.02f / _lossyScale;
            float cross = 0.02f / _lossyScale;
            float visual = _invert ? _length - fill : fill;

            if (_vertical)
            {
                _fillOverlay.localScale = new Vector3(cross, Mathf.Max(0.0001f, fill), thickness);
                _fillOverlay.localPosition = new Vector3(_crossA, _min + visual + fill * 0.5f, _crossB);
            }
            else
            {
                _fillOverlay.localScale = new Vector3(Mathf.Max(0.0001f, fill), cross, thickness);
                _fillOverlay.localPosition = new Vector3(_min + visual + fill * 0.5f, _crossA, _crossB);
            }

            _fillOverlay.gameObject.SetActive(fill > 0.001f);

            if (_handleOverlay == null)
                return;

            float handleSize = 0.045f / _lossyScale;
            _handleOverlay.localScale = Vector3.one * handleSize;

            float handleOffset = (_invert ? 1f - _value : _value) * _length;

            if (_vertical)
                _handleOverlay.localPosition = new Vector3(_crossA, _min + handleOffset, _crossB);
            else
                _handleOverlay.localPosition = new Vector3(_min + handleOffset, _crossA, _crossB);
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

        private static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Standard");

            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            return material;
        }
    }
}
