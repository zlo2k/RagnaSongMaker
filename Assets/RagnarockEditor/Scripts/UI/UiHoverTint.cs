using UnityEngine;
using UnityEngine.UI;

namespace RagnarockEditor.UI
{
    /// <summary>
    /// Подсветка каменной плиты под лучом руки. У uGUI-кнопок нет EventSystem-наведения
    /// (луч бьёт Physics.Raycast'ом), поэтому подсветку включает <c>HandUIPointer</c>:
    /// плита под лучом светлеет, как объёмная каменная кнопка.
    /// </summary>
    [DisallowMultipleComponent]
    public class UiHoverTint : MonoBehaviour
    {
        [SerializeField] private Graphic _graphic;
        [SerializeField] private Color _highlight = new Color(1f, 0.86f, 0.62f, 1f);

        [Tooltip("Насколько сильно светлеет плита под лучом, 0..1")]
        [SerializeField, Range(0.1f, 1f)] private float _strength = 0.55f;

        private Color _base = Color.white;
        private bool _captured;
        private bool _hovered;

        /// <summary>Плита, у которой меняется цвет.</summary>
        public Graphic TargetGraphic => ResolveGraphic();

        private Graphic ResolveGraphic()
        {
            if (_graphic == null)
                _graphic = GetComponent<Graphic>();

            return _graphic;
        }

        /// <summary>Назначить графику и запомнить её цвет как базовый.</summary>
        public void Bind(Graphic graphic)
        {
            _graphic = graphic;
            _captured = false;
            CaptureBase();
        }

        /// <summary>Пересчитать базовый цвет (например, после перекраски плиты).</summary>
        public void CaptureBase()
        {
            var graphic = ResolveGraphic();
            if (graphic == null)
                return;

            // вызывается до подсветки: текущий цвет плиты и есть базовый
            _base = graphic.color;
            _captured = true;

            if (_hovered)
                Apply(true);
        }

        /// <summary>Наведение лучом: включить/выключить подсветку.</summary>
        public void SetHovered(bool value)
        {
            if (_hovered == value)
                return;

            _hovered = value;

            if (!_captured)
            {
                _base = ResolveGraphic() != null ? ResolveGraphic().color : Color.white;
                _captured = true;
            }

            Apply(_hovered);
        }

        private void Apply(bool hovered)
        {
            var graphic = ResolveGraphic();
            if (graphic == null)
                return;

            graphic.color = hovered ? Color.Lerp(_base, _highlight, _strength) : _base;
        }

        private void OnDisable()
        {
            _hovered = false;

            var graphic = ResolveGraphic();
            if (graphic != null && _captured)
                graphic.color = _base;
        }
    }
}
